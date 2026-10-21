/* DirectInput effect driver do G27. Baseado em gcampa/g27-ffb-driverless (MIT, commit 415c871),
   ver ../LICENSE-gcampa.txt. Mudancas do G27 Hub: log pelo pipe do app (hub.c), forca publicada
   na memoria compartilhada, CLSID e configuracao proprios. */
#include <windows.h>
#ifndef DIRECTINPUT_VERSION
#define DIRECTINPUT_VERSION 0x0800
#endif
#include <dinput.h>
#include <dinputd.h>
#include <hidsdi.h>
#include <hidpi.h>

#include <stdlib.h>
#include <string.h>

#include "g27ids.h"
#include "g27hid.h"
#include "g27cfg.h"
#include "g27log.h"
#include "effects.h"

typedef struct Driver {
    IDirectInputEffectDriver iface; /* MUST be the first member */
    LONG ref;
    CRITICAL_SECTION cs;

    /* device state */
    BOOL started;
    DWORD extId;
    WCHAR path[512];
    HANDLE hWrite, hRead;
    HANDLE stopEvent, mixerThread, readerThread;

    /* FF state */
    DWORD deviceGain; /* 0..10000 */
    BOOL paused, actuatorsOn;
    LONGLONG pauseStartQpc;
    Effect effects[MAX_EFFECTS];
    LONG rawX, xMin, xMax;
    /* Set once the reader thread has delivered a real sample. Before that rawX is still
       0, which is a valid raw value meaning "full left lock" - not "unknown". */
    LONG haveSample;
    PHIDP_PREPARSED_DATA pp;

    /* config */
    DWORD invert, masterGain, logLevel;
} Driver;

static LONG g_objects = 0;
static LONG g_locks = 0;
static HMODULE g_module = NULL;

/* ---- forward decls: IDirectInputEffectDriver methods ---- */
static HRESULT STDMETHODCALLTYPE Driver_QueryInterface(IDirectInputEffectDriver *This, REFIID riid, void **ppv);
static ULONG STDMETHODCALLTYPE Driver_AddRef(IDirectInputEffectDriver *This);
static ULONG STDMETHODCALLTYPE Driver_Release(IDirectInputEffectDriver *This);
static HRESULT STDMETHODCALLTYPE Driver_DeviceID(IDirectInputEffectDriver *This, DWORD a, DWORD b, DWORD c, DWORD d, LPVOID e);
static HRESULT STDMETHODCALLTYPE Driver_GetVersions(IDirectInputEffectDriver *This, LPDIDRIVERVERSIONS a);
static HRESULT STDMETHODCALLTYPE Driver_Escape(IDirectInputEffectDriver *This, DWORD a, DWORD b, LPDIEFFESCAPE c);
static HRESULT STDMETHODCALLTYPE Driver_SetGain(IDirectInputEffectDriver *This, DWORD a, DWORD b);
static HRESULT STDMETHODCALLTYPE Driver_SendForceFeedbackCommand(IDirectInputEffectDriver *This, DWORD a, DWORD b);
static HRESULT STDMETHODCALLTYPE Driver_GetForceFeedbackState(IDirectInputEffectDriver *This, DWORD a, LPDIDEVICESTATE b);
static HRESULT STDMETHODCALLTYPE Driver_DownloadEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b, LPDWORD c, LPCDIEFFECT d, DWORD e);
static HRESULT STDMETHODCALLTYPE Driver_DestroyEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b);
static HRESULT STDMETHODCALLTYPE Driver_StartEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b, DWORD c, DWORD d);
static HRESULT STDMETHODCALLTYPE Driver_StopEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b);
static HRESULT STDMETHODCALLTYPE Driver_GetEffectStatus(IDirectInputEffectDriver *This, DWORD a, DWORD b, LPDWORD c);

static IDirectInputEffectDriverVtbl g_driverVtbl = {
    Driver_QueryInterface, Driver_AddRef, Driver_Release,
    Driver_DeviceID, Driver_GetVersions, Driver_Escape, Driver_SetGain,
    Driver_SendForceFeedbackCommand, Driver_GetForceFeedbackState,
    Driver_DownloadEffect, Driver_DestroyEffect, Driver_StartEffect,
    Driver_StopEffect, Driver_GetEffectStatus
};

static DWORD WINAPI MixerThreadProc(LPVOID param);
static DWORD WINAPI ReaderThreadProc(LPVOID param);
static void driver_stop_device(Driver *drv);

/* ================= IDirectInputEffectDriver ================= */

static HRESULT STDMETHODCALLTYPE Driver_QueryInterface(IDirectInputEffectDriver *This, REFIID riid, void **ppv) {
    if (!ppv) {
        return E_POINTER;
    }
    if (IsEqualGUID(riid, &IID_IUnknown_G27) || IsEqualGUID(riid, &IID_IDirectInputEffectDriver_G27)) {
        *ppv = This;
        Driver_AddRef(This);
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE Driver_AddRef(IDirectInputEffectDriver *This) {
    Driver *drv = (Driver *)This;
    return (ULONG)InterlockedIncrement(&drv->ref);
}

static ULONG STDMETHODCALLTYPE Driver_Release(IDirectInputEffectDriver *This) {
    Driver *drv = (Driver *)This;
    LONG r = InterlockedDecrement(&drv->ref);
    if (r == 0) {
        driver_stop_device(drv);
        DeleteCriticalSection(&drv->cs);
        free(drv);
        InterlockedDecrement(&g_objects);
    }
    return (ULONG)r;
}

static HRESULT STDMETHODCALLTYPE Driver_DeviceID(IDirectInputEffectDriver *This, DWORD a, DWORD b, DWORD c, DWORD d, LPVOID e) {
    Driver *drv = (Driver *)This;
    (void)a;
    (void)b;

    if (c != 0) {
        LPDIHIDFFINITINFO info = (LPDIHIDFFINITINFO)e;
        if (!info || info->dwSize < sizeof(DIHIDFFINITINFO) || !info->pwszDeviceInterface) {
            return DIERR_INVALIDPARAM;
        }
        if (drv->started) {
            return DI_OK;
        }

        drv->logLevel = g27_cfg_get(L"Log", 0);
        drv->invert = g27_cfg_get(L"Invert", 0);
        drv->masterGain = g27_cfg_get(L"MasterGain", 100);

        wcsncpy(drv->path, info->pwszDeviceInterface, 511);
        drv->path[511] = 0;
        drv->extId = d;

        drv->hWrite = g27_open(drv->path, FALSE);
        if (drv->hWrite == INVALID_HANDLE_VALUE) {
            DWORD err = GetLastError();
            hub_log("ERRO", "Nao consegui abrir o G27 para FFB (erro %lu) path=%ls",
                    (unsigned long)err, drv->path);
            return E_FAIL;
        }

        WCHAR exe[MAX_PATH] = L"?";
        GetModuleFileNameW(NULL, exe, MAX_PATH);
        const WCHAR *exeName = wcsrchr(exe, L'\\') ? wcsrchr(exe, L'\\') + 1 : exe;
        hub_log("OK", "g27ffb.dll (%s) carregada no %ls (pid %lu)", sizeof(void *) == 8 ? "x64" : "x86",
                exeName, (unsigned long)GetCurrentProcessId());

        drv->stopEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
        drv->mixerThread = CreateThread(NULL, 0, MixerThreadProc, drv, 0, NULL);
        drv->readerThread = CreateThread(NULL, 0, ReaderThreadProc, drv, 0, NULL);
        drv->started = TRUE;

        g27_log(1, "DeviceID begin ext=%u path=%ls", (unsigned)d, drv->path);
        return DI_OK;
    }

    driver_stop_device(drv);
    return DI_OK;
}

static void driver_stop_device(Driver *drv) {
    if (!drv->started) {
        return;
    }

    if (drv->stopEvent) {
        SetEvent(drv->stopEvent);
    }
    if (drv->mixerThread) {
        WaitForSingleObject(drv->mixerThread, 2000);
        CloseHandle(drv->mixerThread);
        drv->mixerThread = NULL;
    }
    if (drv->readerThread) {
        WaitForSingleObject(drv->readerThread, 2000);
        CloseHandle(drv->readerThread);
        drv->readerThread = NULL;
    }
    if (drv->stopEvent) {
        CloseHandle(drv->stopEvent);
        drv->stopEvent = NULL;
    }
    if (drv->hWrite && drv->hWrite != INVALID_HANDLE_VALUE) {
        CloseHandle(drv->hWrite);
        drv->hWrite = NULL;
    }

    EnterCriticalSection(&drv->cs);
    memset(drv->effects, 0, sizeof(drv->effects));
    LeaveCriticalSection(&drv->cs);

    drv->started = FALSE;
    g27_log(1, "DeviceID end ext=%u", (unsigned)drv->extId);
}

static HRESULT STDMETHODCALLTYPE Driver_GetVersions(IDirectInputEffectDriver *This, LPDIDRIVERVERSIONS a) {
    (void)This;
    if (!a || a->dwSize < sizeof(DIDRIVERVERSIONS)) {
        return DIERR_INVALIDPARAM;
    }
    a->dwFirmwareRevision = 1;
    a->dwHardwareRevision = 1;
    a->dwFFDriverVersion = 0x00010000;
    return DI_OK;
}

static HRESULT STDMETHODCALLTYPE Driver_Escape(IDirectInputEffectDriver *This, DWORD a, DWORD b, LPDIEFFESCAPE c) {
    (void)This;
    (void)c;
    g27_log(1, "Escape id=%u cmd=%u not supported", (unsigned)a, (unsigned)b);
    return DIERR_UNSUPPORTED;
}

static HRESULT STDMETHODCALLTYPE Driver_SetGain(IDirectInputEffectDriver *This, DWORD a, DWORD b) {
    Driver *drv = (Driver *)This;
    (void)a;
    drv->deviceGain = (b > 10000) ? 10000 : b;
    g27_log(1, "SetGain gain=%u", (unsigned)drv->deviceGain);
    return DI_OK;
}

static HRESULT STDMETHODCALLTYPE Driver_SendForceFeedbackCommand(IDirectInputEffectDriver *This, DWORD a, DWORD b) {
    Driver *drv = (Driver *)This;
    (void)a;

    g27_log(1, "SendForceFeedbackCommand flags=%08X", (unsigned)b);

    EnterCriticalSection(&drv->cs);

    if (b & DISFFC_RESET) {
        memset(drv->effects, 0, sizeof(drv->effects));
        drv->paused = FALSE;
    }
    if (b & DISFFC_STOPALL) {
        for (int i = 0; i < MAX_EFFECTS; i++) {
            drv->effects[i].playing = FALSE;
        }
    }
    if (b & DISFFC_PAUSE) {
        if (!drv->paused) {
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            drv->pauseStartQpc = now.QuadPart;
            drv->paused = TRUE;
        }
    }
    if (b & DISFFC_CONTINUE) {
        if (drv->paused) {
            LARGE_INTEGER now;
            QueryPerformanceCounter(&now);
            LONGLONG delta = now.QuadPart - drv->pauseStartQpc;
            for (int i = 0; i < MAX_EFFECTS; i++) {
                if (drv->effects[i].used && drv->effects[i].playing) {
                    drv->effects[i].pausedAccum += delta;
                }
            }
            drv->paused = FALSE;
        }
    }
    if (b & DISFFC_SETACTUATORSON) {
        drv->actuatorsOn = TRUE;
    }
    if (b & DISFFC_SETACTUATORSOFF) {
        drv->actuatorsOn = FALSE;
    }

    LeaveCriticalSection(&drv->cs);
    return DI_OK;
}

static HRESULT STDMETHODCALLTYPE Driver_GetForceFeedbackState(IDirectInputEffectDriver *This, DWORD a, LPDIDEVICESTATE b) {
    Driver *drv = (Driver *)This;
    (void)a;
    if (!b || b->dwSize < sizeof(DIDEVICESTATE)) {
        return DIERR_INVALIDPARAM;
    }

    EnterCriticalSection(&drv->cs);
    int used = 0, playing = 0;
    for (int i = 0; i < MAX_EFFECTS; i++) {
        if (drv->effects[i].used) {
            used++;
            if (drv->effects[i].playing) {
                playing++;
            }
        }
    }
    DWORD state = DIGFFS_POWERON;
    state |= drv->actuatorsOn ? DIGFFS_ACTUATORSON : DIGFFS_ACTUATORSOFF;
    if (used == 0) state |= DIGFFS_EMPTY;
    if (playing == 0) state |= DIGFFS_STOPPED;
    if (drv->paused) state |= DIGFFS_PAUSED;
    b->dwState = state;
    b->dwLoad = (DWORD)(used * 100 / MAX_EFFECTS);
    LeaveCriticalSection(&drv->cs);

    return DI_OK;
}

static HRESULT STDMETHODCALLTYPE Driver_DownloadEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b, LPDWORD c, LPCDIEFFECT d, DWORD e) {
    Driver *drv = (Driver *)This;
    (void)a;
    if (!c) {
        return DIERR_INVALIDPARAM;
    }

    EnterCriticalSection(&drv->cs);
    HRESULT hr = effects_download(drv->effects, b, c, d, e);
    LeaveCriticalSection(&drv->cs);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Driver_DestroyEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b) {
    Driver *drv = (Driver *)This;
    (void)a;
    EnterCriticalSection(&drv->cs);
    HRESULT hr = effects_destroy(drv->effects, b);
    LeaveCriticalSection(&drv->cs);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Driver_StartEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b, DWORD c, DWORD d) {
    Driver *drv = (Driver *)This;
    (void)a;
    EnterCriticalSection(&drv->cs);
    HRESULT hr = effects_start(drv->effects, b, c, d);
    LeaveCriticalSection(&drv->cs);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Driver_StopEffect(IDirectInputEffectDriver *This, DWORD a, DWORD b) {
    Driver *drv = (Driver *)This;
    (void)a;
    EnterCriticalSection(&drv->cs);
    HRESULT hr = effects_stop(drv->effects, b);
    LeaveCriticalSection(&drv->cs);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Driver_GetEffectStatus(IDirectInputEffectDriver *This, DWORD a, DWORD b, LPDWORD c) {
    Driver *drv = (Driver *)This;
    (void)a;
    EnterCriticalSection(&drv->cs);
    HRESULT hr = effects_status(drv->effects, b, c);
    LeaveCriticalSection(&drv->cs);
    return hr;
}

/* ================= Reader thread (wheel position) ================= */

static DWORD WINAPI ReaderThreadProc(LPVOID param) {
    Driver *drv = (Driver *)param;

    drv->hRead = g27_open(drv->path, TRUE);
    if (drv->hRead == INVALID_HANDLE_VALUE) {
        g27_log(1, "Reader: open failed err=%lu", (unsigned long)GetLastError());
        drv->hRead = NULL;
        return 0;
    }

    if (!HidD_GetPreparsedData(drv->hRead, &drv->pp)) {
        g27_log(1, "Reader: HidD_GetPreparsedData failed");
        CloseHandle(drv->hRead);
        drv->hRead = NULL;
        return 0;
    }

    HIDP_CAPS caps;
    HidP_GetCaps(drv->pp, &caps);

    HIDP_VALUE_CAPS valueCaps;
    USHORT n = 1;
    if (HidP_GetSpecificValueCaps(HidP_Input, 0x01, 0, 0x30, &valueCaps, &n, drv->pp) == HIDP_STATUS_SUCCESS && n >= 1) {
        drv->xMin = valueCaps.LogicalMin;
        drv->xMax = valueCaps.LogicalMax;
        if (drv->xMax <= drv->xMin) {
            drv->xMin = 0;
            drv->xMax = (1 << valueCaps.BitSize) - 1;
        }
    } else {
        drv->xMin = 0;
        drv->xMax = 1023;
    }

    BYTE *buf = (BYTE *)malloc(caps.InputReportByteLength);
    if (!buf) {
        HidD_FreePreparsedData(drv->pp);
        drv->pp = NULL;
        CloseHandle(drv->hRead);
        drv->hRead = NULL;
        return 0;
    }

    /* Seed the position before the read loop starts. The wheel only sends input reports
       when something changes, so a stationary wheel would leave us with no position at
       all - and if it is stationary away from centre, a spring effect started by a game
       would produce no centring force until the player first moved it. HidD_GetInputReport
       asks for the current state instead of waiting for a change. Not every device
       supports it, so failure here is not fatal: we simply fall back to learning the
       position from the first report that arrives. */
    {
        BYTE *seed = (BYTE *)calloc(1, caps.InputReportByteLength);
        if (seed) {
            seed[0] = 0;
            if (HidD_GetInputReport(drv->hRead, seed, caps.InputReportByteLength)) {
                ULONG raw = 0;
                if (HidP_GetUsageValue(HidP_Input, 0x01, 0, 0x30, &raw, drv->pp, (PCHAR)seed,
                                        caps.InputReportByteLength) == HIDP_STATUS_SUCCESS) {
                    InterlockedExchange(&drv->rawX, (LONG)raw);
                    InterlockedExchange(&drv->haveSample, 1);
                    g27_log(1, "Reader: seeded position rawX=%lu", (unsigned long)raw);
                }
            } else {
                g27_log(1, "Reader: HidD_GetInputReport unavailable err=%lu; will wait for a report",
                        (unsigned long)GetLastError());
            }
            free(seed);
        }
    }

    OVERLAPPED ov;
    memset(&ov, 0, sizeof(ov));
    ov.hEvent = CreateEventW(NULL, TRUE, FALSE, NULL);

    HANDLE waitHandles[2];
    waitHandles[0] = ov.hEvent;
    waitHandles[1] = drv->stopEvent;

    for (;;) {
        ResetEvent(ov.hEvent);
        BOOL ok = ReadFile(drv->hRead, buf, caps.InputReportByteLength, NULL, &ov);
        if (!ok && GetLastError() != ERROR_IO_PENDING) {
            break;
        }

        DWORD wr = WaitForMultipleObjects(2, waitHandles, FALSE, INFINITE);
        if (wr == WAIT_OBJECT_0 + 1) {
            CancelIoEx(drv->hRead, &ov);
            break;
        }
        if (wr != WAIT_OBJECT_0) {
            break;
        }

        DWORD got = 0;
        if (!GetOverlappedResult(drv->hRead, &ov, &got, FALSE)) {
            continue;
        }

        ULONG raw = 0;
        if (HidP_GetUsageValue(HidP_Input, 0x01, 0, 0x30, &raw, drv->pp, (PCHAR)buf,
                                caps.InputReportByteLength) == HIDP_STATUS_SUCCESS) {
            InterlockedExchange(&drv->rawX, (LONG)raw);
            InterlockedExchange(&drv->haveSample, 1);
        }
    }

    CloseHandle(ov.hEvent);
    free(buf);
    HidD_FreePreparsedData(drv->pp);
    drv->pp = NULL;
    CloseHandle(drv->hRead);
    drv->hRead = NULL;
    return 0;
}

/* ================= Mixer thread ================= */

static DWORD WINAPI MixerThreadProc(LPVOID param) {
    Driver *drv = (Driver *)param;

    HANDLE timer = CreateWaitableTimerExW(NULL, NULL, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    if (!timer) {
        timer = CreateWaitableTimerW(NULL, FALSE, NULL);
    }
    LARGE_INTEGER due;
    due.QuadPart = -20000; /* 2 ms, relative */
    SetWaitableTimer(timer, &due, 2, NULL, NULL, FALSE);

    g27_send(drv->hWrite, CMD_AUTOCENTER_OFF);

    LARGE_INTEGER freq;
    QueryPerformanceFrequency(&freq);

    BYTE lastLV = 0x80;
    DWORD lastSendTick = GetTickCount();
    DWORD lastLogTick = 0;
    DWORD lastCfgTick = 0;
    LONG condGain[4] = {100, 100, 100, 100}; /* % por tipo: mola, amortecedor, inercia, atrito */
    LONG centerSpring = 0;

    HANDLE waitHandles[2];
    waitHandles[0] = drv->stopEvent;
    waitHandles[1] = timer;

    /* State for position/velocity derivatives (fixed 2 ms tick assumed). */
    const double dt = 0.002;
    LONG prevPos = 0;
    double filtVel = 0.0;
    double prevFiltVel = 0.0;
    BOOL firstTick = TRUE;

    for (;;) {
        DWORD wr = WaitForMultipleObjects(2, waitHandles, FALSE, INFINITE);
        if (wr == WAIT_OBJECT_0) {
            break;
        }

        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);

        /* Until the reader thread has produced its first sample there is no position
           information at all. rawX is still 0, and 0 is a legitimate raw reading that
           means "full left lock" - so using it would make a spring effect command full
           force to the right the instant a game starts one, and would feed the
           derivative a huge fictional jump on the following tick. Report a centred,
           stationary wheel until a real sample exists, and only then start
           differentiating, so the baseline comes from a real position. */
        BOOL haveSample = (InterlockedCompareExchange(&drv->haveSample, 0, 0) != 0);
        LONG rawX = InterlockedCompareExchange(&drv->rawX, 0, 0);
        LONG xMin = drv->xMin;
        LONG xMax = drv->xMax;

        LONG pos = 0;
        LONG velN = 0;
        LONG accN = 0;

        if (haveSample) {
            if (xMax > xMin) {
                pos = (LONG)(((LONGLONG)(rawX - xMin) * 20000) / (xMax - xMin)) - 10000;
            }

            double rawVel = firstTick ? 0.0 : ((double)(pos - prevPos) / dt);
            filtVel = firstTick ? rawVel : (filtVel + 0.2 * (rawVel - filtVel));
            double acc = firstTick ? 0.0 : ((filtVel - prevFiltVel) / dt);
            prevPos = pos;
            prevFiltVel = filtVel;
            firstTick = FALSE;

            velN = (LONG)(filtVel * 10000.0 / VEL_FULLSCALE);
            if (velN > 10000) velN = 10000;
            if (velN < -10000) velN = -10000;
            accN = (LONG)(acc * 10000.0 / ACC_FULLSCALE);
            if (accN > 10000) accN = 10000;
            if (accN < -10000) accN = -10000;
        }

        /* G27 Hub: os ajustes da aba Forca valem com o jogo aberto (relidos 1x por segundo) */
        if (GetTickCount() - lastCfgTick >= 1000) {
            lastCfgTick = GetTickCount();
            drv->invert = g27_cfg_get(L"Invert", 0);
            drv->masterGain = g27_cfg_get(L"MasterGain", 100);
            condGain[0] = (LONG)g27_cfg_get(L"SpringGain", 100);
            condGain[1] = (LONG)g27_cfg_get(L"DamperGain", 100);
            centerSpring = (LONG)g27_cfg_get(L"CenterSpring", 0);
        }

        EnterCriticalSection(&drv->cs);
        LONG fDi = effects_mix_force(drv->effects, now.QuadPart, freq.QuadPart);
        LONG fCond = effects_mix_cond(drv->effects, now.QuadPart, freq.QuadPart, pos, velN, accN, condGain);
        LeaveCriticalSection(&drv->cs);

        /* "Forca central": mola fixa somada ao que o jogo pede */
        if (haveSample && centerSpring > 0) {
            fCond += (LONG)(-(LONGLONG)pos * centerSpring / 100);
        }

        LONGLONG F = (LONGLONG)fDi * (drv->invert ? -1 : 1) + fCond;
        F = F * (LONGLONG)drv->deviceGain / 10000;
        F = F * (LONGLONG)drv->masterGain / 100;

        if (drv->paused || !drv->actuatorsOn) {
            F = 0;
        }
        hub_publish((LONG)(F > 100000 ? 100000 : F < -100000 ? -100000 : F), F > 10000 || F < -10000);
        if (F > 10000) F = 10000;
        if (F < -10000) F = -10000;

        LONG step = (LONG)(F * 127 / 10000);
        LONG lv = G27_LV_POSITIVE_IS_RIGHT ? (0x80 + step) : (0x80 - step);
        if (lv < 0) lv = 0;
        if (lv > 255) lv = 255;
        BYTE LV = (BYTE)lv;

        DWORD nowTick = GetTickCount();
        if (nowTick - lastLogTick >= 100) {
            g27_log(2, "mix fDi=%ld fCond=%ld F=%lld gain=%u master=%u paused=%d act=%d LV=%02X "
                       "rawX=%ld xMin=%ld xMax=%ld pos=%ld velN=%ld accN=%ld",
                    (long)fDi, (long)fCond, (long long)F, (unsigned)drv->deviceGain,
                    (unsigned)drv->masterGain, (int)drv->paused, (int)drv->actuatorsOn, (unsigned)LV,
                    (long)rawX, (long)xMin, (long)xMax, (long)pos, (long)velN, (long)accN);
            lastLogTick = nowTick;
        }
        if (LV == 0x80 && lastLV != 0x80) {
            g27_send(drv->hWrite, CMD_STOP);
        } else if (LV != 0x80 && (LV != lastLV || (nowTick - lastSendTick) >= 100)) {
            BYTE cmd[7] = {0x11, 0x08, LV, 0x80, 0x00, 0x00, 0x00};
            g27_send(drv->hWrite, cmd);
            lastSendTick = nowTick;
        }
        lastLV = LV;
    }

    /* Hand the wheel back with a modest centering spring, so it doesn't sit limp
       after the game exits. */
    g27_send(drv->hWrite, CMD_STOP);
    g27_autocenter_apply(drv->hWrite, 40);

    CancelWaitableTimer(timer);
    CloseHandle(timer);
    return 0;
}

/* ================= IClassFactory statica ================= */

static HRESULT STDMETHODCALLTYPE Factory_QueryInterface(IClassFactory *This, REFIID riid, void **ppv);
static ULONG STDMETHODCALLTYPE Factory_AddRef(IClassFactory *This);
static ULONG STDMETHODCALLTYPE Factory_Release(IClassFactory *This);
static HRESULT STDMETHODCALLTYPE Factory_CreateInstance(IClassFactory *This, IUnknown *outer, REFIID riid, void **ppv);
static HRESULT STDMETHODCALLTYPE Factory_LockServer(IClassFactory *This, WINBOOL fLock);

static IClassFactoryVtbl g_factoryVtbl = {
    Factory_QueryInterface, Factory_AddRef, Factory_Release,
    Factory_CreateInstance, Factory_LockServer
};

static IClassFactory g_factory = {&g_factoryVtbl};

static HRESULT STDMETHODCALLTYPE Factory_QueryInterface(IClassFactory *This, REFIID riid, void **ppv) {
    (void)This;
    if (!ppv) {
        return E_POINTER;
    }
    if (IsEqualGUID(riid, &IID_IUnknown_G27) || IsEqualGUID(riid, &IID_IClassFactory_G27)) {
        *ppv = &g_factory;
        return S_OK;
    }
    *ppv = NULL;
    return E_NOINTERFACE;
}

static ULONG STDMETHODCALLTYPE Factory_AddRef(IClassFactory *This) {
    (void)This;
    return 2;
}

static ULONG STDMETHODCALLTYPE Factory_Release(IClassFactory *This) {
    (void)This;
    return 1;
}

static HRESULT STDMETHODCALLTYPE Factory_CreateInstance(IClassFactory *This, IUnknown *outer, REFIID riid, void **ppv) {
    (void)This;
    if (!ppv) {
        return E_POINTER;
    }
    *ppv = NULL;
    if (outer) {
        return CLASS_E_NOAGGREGATION;
    }

    Driver *drv = (Driver *)calloc(1, sizeof(Driver));
    if (!drv) {
        return E_OUTOFMEMORY;
    }

    drv->iface.lpVtbl = &g_driverVtbl;
    drv->ref = 1;
    InitializeCriticalSection(&drv->cs);
    drv->deviceGain = 10000;
    drv->actuatorsOn = TRUE;

    InterlockedIncrement(&g_objects);

    HRESULT hr = Driver_QueryInterface(&drv->iface, riid, ppv);
    Driver_Release(&drv->iface);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Factory_LockServer(IClassFactory *This, WINBOOL fLock) {
    (void)This;
    if (fLock) {
        InterlockedIncrement(&g_locks);
    } else {
        InterlockedDecrement(&g_locks);
    }
    return S_OK;
}

/* ================= DllMain + export ================= */

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = hinst;
        DisableThreadLibraryCalls(hinst);
    }
    return TRUE;
}

HRESULT WINAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID *ppv) {
    if (!ppv) {
        return E_POINTER;
    }
    *ppv = NULL;
    if (!IsEqualGUID(rclsid, &CLSID_G27FFB)) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    return Factory_QueryInterface(&g_factory, riid, ppv);
}

HRESULT WINAPI DllCanUnloadNow(void) {
    return (g_objects == 0 && g_locks == 0) ? S_OK : S_FALSE;
}
