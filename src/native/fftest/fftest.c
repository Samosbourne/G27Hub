#include <windows.h>
#ifndef DIRECTINPUT_VERSION
#define DIRECTINPUT_VERSION 0x0800
#endif
#include <dinput.h>

#include <shellapi.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#include "g27ids.h"

static void print_hr(const char *what, HRESULT hr) {
    wprintf(L"%hs: 0x%08lX\n", what, (unsigned long)hr);
}

static WINBOOL CALLBACK enum_print_cb(LPCDIDEVICEINSTANCEW inst, LPVOID ref) {
    (void)ref;
    wprintf(L"  %ls  guidProduct=%08lX  guidFFDriver=%08lX\n",
            inst->tszProductName,
            (unsigned long)inst->guidProduct.Data1,
            (unsigned long)inst->guidFFDriver.Data1);
    return DIENUM_CONTINUE;
}

static void cmd_list(IDirectInput8W *di) {
    wprintf(L"-- ATTACHEDONLY --\n");
    IDirectInput8_EnumDevices(di, DI8DEVCLASS_GAMECTRL, enum_print_cb, NULL, DIEDFL_ATTACHEDONLY);
    wprintf(L"-- ATTACHEDONLY | FORCEFEEDBACK --\n");
    IDirectInput8_EnumDevices(di, DI8DEVCLASS_GAMECTRL, enum_print_cb, NULL,
                               DIEDFL_ATTACHEDONLY | DIEDFL_FORCEFEEDBACK);
}

typedef struct {
    GUID instance;
    BOOL found;
} FindCtx;

/* G27 Hub: "g27fftest <teste> vjoy" testa o volante virtual (Compatibilidade Total) em vez do G27 */
static DWORD g_product = 0xC29B046D;

static WINBOOL CALLBACK find_g27_cb(LPCDIDEVICEINSTANCEW inst, LPVOID ref) {
    FindCtx *ctx = (FindCtx *)ref;
    if (inst->guidProduct.Data1 == g_product) {
        ctx->instance = inst->guidInstance;
        ctx->found = TRUE;
        return DIENUM_STOP;
    }
    return DIENUM_CONTINUE;
}

static void run_const_test(IDirectInputDevice8W *dev) {
    DWORD axes[1] = {0};
    LONG dirs[1] = {1};
    DICONSTANTFORCE cf;
    cf.lMagnitude = 5000;

    DIEFFECT eff;
    memset(&eff, 0, sizeof(eff));
    eff.dwSize = sizeof(DIEFFECT);
    eff.dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS;
    eff.dwDuration = INFINITE;
    eff.dwSamplePeriod = 0;
    eff.dwGain = 10000;
    eff.dwTriggerButton = DIEB_NOTRIGGER;
    eff.dwTriggerRepeatInterval = 0;
    eff.cAxes = 1;
    eff.rgdwAxes = axes;
    eff.rglDirection = dirs;
    eff.lpEnvelope = NULL;
    eff.cbTypeSpecificParams = sizeof(cf);
    eff.lpvTypeSpecificParams = &cf;
    eff.dwStartDelay = 0;

    IDirectInputEffect *effect = NULL;
    HRESULT hr = IDirectInputDevice8_CreateEffect(dev, &GUID_ConstantForce_G27, &eff, &effect, NULL);
    print_hr("CreateEffect(const +5000)", hr);
    if (FAILED(hr) || !effect) {
        return;
    }

    hr = IDirectInputEffect_Start(effect, 1, 0);
    print_hr("Start(+5000)", hr);
    Sleep(1500);
    hr = IDirectInputEffect_Stop(effect);
    print_hr("Stop", hr);

    Sleep(300);

    cf.lMagnitude = -5000;
    hr = IDirectInputEffect_SetParameters(effect, &eff, DIEP_TYPESPECIFICPARAMS | DIEP_START);
    print_hr("SetParameters(-5000, START)", hr);
    Sleep(1500);
    hr = IDirectInputEffect_Stop(effect);
    print_hr("Stop", hr);

    IDirectInputEffect_Release(effect);
}

/* G27 Hub: "texture" imita o FFB de um jogo como o BeamNG: atualiza a força constante o mais rápido possível
   (vibração de 25 Hz, como piso/zebra) por 3 s e mede quantas atualizações por segundo o caminho aguenta. */
static void run_texture_test(IDirectInputDevice8W *dev) {
    DWORD axes[1] = {0};
    LONG dirs[1] = {1};
    DICONSTANTFORCE cf = {0};
    DIEFFECT eff;
    memset(&eff, 0, sizeof(eff));
    eff.dwSize = sizeof(DIEFFECT);
    eff.dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS;
    eff.dwDuration = INFINITE;
    eff.dwGain = 10000;
    eff.dwTriggerButton = DIEB_NOTRIGGER;
    eff.cAxes = 1;
    eff.rgdwAxes = axes;
    eff.rglDirection = dirs;
    eff.cbTypeSpecificParams = sizeof(cf);
    eff.lpvTypeSpecificParams = &cf;

    IDirectInputEffect *effect = NULL;
    HRESULT hr = IDirectInputDevice8_CreateEffect(dev, &GUID_ConstantForce_G27, &eff, &effect, NULL);
    print_hr("CreateEffect(texture)", hr);
    if (FAILED(hr) || !effect) return;
    IDirectInputEffect_Start(effect, 1, 0);

    LARGE_INTEGER f, t0, a, b;
    QueryPerformanceFrequency(&f);
    QueryPerformanceCounter(&t0);
    double worst = 0, total = 0;
    int calls = 0;
    for (;;) {
        QueryPerformanceCounter(&a);
        double t = (double)(a.QuadPart - t0.QuadPart) / f.QuadPart;
        if (t > 3.0) break;
        cf.lMagnitude = (LONG)(4000.0 * sin(2.0 * 3.14159265358979 * 25.0 * t));
        IDirectInputEffect_SetParameters(effect, &eff, DIEP_TYPESPECIFICPARAMS);
        QueryPerformanceCounter(&b);
        double ms = (double)(b.QuadPart - a.QuadPart) * 1000.0 / f.QuadPart;
        total += ms;
        if (ms > worst) worst = ms;
        calls++;
    }
    wprintf(L"texture: %d atualizacoes em 3 s = %d por segundo; SetParameters media %.3f ms, pior %.2f ms\n",
            calls, calls / 3, total / calls, worst);
    IDirectInputEffect_Stop(effect);
    IDirectInputEffect_Release(effect);
}

static void run_sine_test(IDirectInputDevice8W *dev) {
    DWORD axes[1] = {0};
    LONG dirs[1] = {1};
    DIPERIODIC pf;
    pf.dwMagnitude = 4000;
    pf.lOffset = 0;
    pf.dwPhase = 0;
    pf.dwPeriod = 200000;

    DIEFFECT eff;
    memset(&eff, 0, sizeof(eff));
    eff.dwSize = sizeof(DIEFFECT);
    eff.dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS;
    eff.dwDuration = 2000000;
    eff.dwSamplePeriod = 0;
    eff.dwGain = 10000;
    eff.dwTriggerButton = DIEB_NOTRIGGER;
    eff.dwTriggerRepeatInterval = 0;
    eff.cAxes = 1;
    eff.rgdwAxes = axes;
    eff.rglDirection = dirs;
    eff.lpEnvelope = NULL;
    eff.cbTypeSpecificParams = sizeof(pf);
    eff.lpvTypeSpecificParams = &pf;
    eff.dwStartDelay = 0;

    IDirectInputEffect *effect = NULL;
    HRESULT hr = IDirectInputDevice8_CreateEffect(dev, &GUID_Sine_G27, &eff, &effect, NULL);
    print_hr("CreateEffect(sine)", hr);
    if (FAILED(hr) || !effect) {
        return;
    }

    hr = IDirectInputEffect_Start(effect, 1, 0);
    print_hr("Start(sine)", hr);
    Sleep(2200);
    hr = IDirectInputEffect_Stop(effect);
    print_hr("Stop", hr);

    IDirectInputEffect_Release(effect);
}

static void run_cond_test(IDirectInputDevice8W *dev, const GUID *typeGuid, const char *label) {
    DWORD axes[1] = {0};
    LONG dirs[1] = {1};
    DICONDITION cond;
    cond.lOffset = 0;
    cond.lPositiveCoefficient = 8000;
    cond.lNegativeCoefficient = 8000;
    cond.dwPositiveSaturation = 10000;
    cond.dwNegativeSaturation = 10000;
    cond.lDeadBand = 0;

    DIEFFECT eff;
    memset(&eff, 0, sizeof(eff));
    eff.dwSize = sizeof(DIEFFECT);
    eff.dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS;
    eff.dwDuration = INFINITE;
    eff.dwSamplePeriod = 0;
    eff.dwGain = 10000;
    eff.dwTriggerButton = DIEB_NOTRIGGER;
    eff.dwTriggerRepeatInterval = 0;
    eff.cAxes = 1;
    eff.rgdwAxes = axes;
    eff.rglDirection = dirs;
    eff.lpEnvelope = NULL;
    eff.cbTypeSpecificParams = sizeof(cond);
    eff.lpvTypeSpecificParams = &cond;
    eff.dwStartDelay = 0;

    IDirectInputEffect *effect = NULL;
    char msg[64];
    snprintf(msg, sizeof(msg), "CreateEffect(%s)", label);
    HRESULT hr = IDirectInputDevice8_CreateEffect(dev, typeGuid, &eff, &effect, NULL);
    print_hr(msg, hr);
    if (FAILED(hr) || !effect) {
        return;
    }

    snprintf(msg, sizeof(msg), "Start(%s)", label);
    hr = IDirectInputEffect_Start(effect, 1, 0);
    print_hr(msg, hr);

    wprintf(L"%hs: 6 seconds, interact with the wheel...\n", label);
    Sleep(6000);

    hr = IDirectInputEffect_Stop(effect);
    print_hr("Stop", hr);

    IDirectInputEffect_Release(effect);
}

int main(void) {
    int argc = 0;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    const WCHAR *cmd = (argc >= 2) ? argv[1] : L"list";
    if (argc >= 3 && wcscmp(argv[2], L"vjoy") == 0) g_product = 0xBEAD1234;

    HWND hwnd = CreateWindowExW(0, L"STATIC", L"fftest", WS_OVERLAPPEDWINDOW,
                                 0, 0, 100, 100, NULL, NULL, GetModuleHandleW(NULL), NULL);

    IDirectInput8W *di = NULL;
    HRESULT hr = DirectInput8Create(GetModuleHandleW(NULL), DIRECTINPUT_VERSION,
                                     &IID_IDirectInput8W_G27, (void **)&di, NULL);
    print_hr("DirectInput8Create", hr);
    if (FAILED(hr) || !di) {
        if (hwnd) DestroyWindow(hwnd);
        if (argv) LocalFree(argv);
        return 1;
    }

    if (wcscmp(cmd, L"list") == 0) {
        cmd_list(di);
        IDirectInput8_Release(di);
        if (hwnd) DestroyWindow(hwnd);
        if (argv) LocalFree(argv);
        return 0;
    }

    FindCtx ctx;
    memset(&ctx, 0, sizeof(ctx));
    IDirectInput8_EnumDevices(di, DI8DEVCLASS_GAMECTRL, find_g27_cb, &ctx,
                               DIEDFL_ATTACHEDONLY | DIEDFL_FORCEFEEDBACK);
    if (!ctx.found) {
        wprintf(L"No force-feedback-capable native G27 found (run g27ctl native + register first).\n");
        IDirectInput8_Release(di);
        if (hwnd) DestroyWindow(hwnd);
        if (argv) LocalFree(argv);
        return 2;
    }

    IDirectInputDevice8W *dev = NULL;
    hr = IDirectInput8_CreateDevice(di, &ctx.instance, &dev, NULL);
    print_hr("CreateDevice", hr);
    if (FAILED(hr) || !dev) {
        IDirectInput8_Release(di);
        if (hwnd) DestroyWindow(hwnd);
        if (argv) LocalFree(argv);
        return 3;
    }

    DIOBJECTDATAFORMAT of[1];
    of[0].pguid = &GUID_XAxis_G27;
    of[0].dwOfs = 0;
    of[0].dwType = DIDFT_AXIS | DIDFT_ANYINSTANCE;
    of[0].dwFlags = 0;

    DIDATAFORMAT fmt;
    fmt.dwSize = sizeof(DIDATAFORMAT);
    fmt.dwObjSize = sizeof(DIOBJECTDATAFORMAT);
    fmt.dwFlags = DIDF_ABSAXIS;
    fmt.dwDataSize = 4;
    fmt.dwNumObjs = 1;
    fmt.rgodf = of;

    hr = IDirectInputDevice8_SetDataFormat(dev, &fmt);
    print_hr("SetDataFormat", hr);

    hr = IDirectInputDevice8_SetCooperativeLevel(dev, hwnd, DISCL_EXCLUSIVE | DISCL_BACKGROUND);
    print_hr("SetCooperativeLevel", hr);

    DIPROPDWORD dp;
    dp.diph.dwSize = sizeof(DIPROPDWORD);
    dp.diph.dwHeaderSize = sizeof(DIPROPHEADER);
    dp.diph.dwObj = 0;
    dp.diph.dwHow = DIPH_DEVICE;
    dp.dwData = DIPROPAUTOCENTER_OFF;
    hr = IDirectInputDevice8_SetProperty(dev, DIPROP_AUTOCENTER, &dp.diph);
    print_hr("SetProperty(AUTOCENTER_OFF)", hr);

    hr = IDirectInputDevice8_Acquire(dev);
    print_hr("Acquire", hr);

    BOOL doConst = (wcscmp(cmd, L"const") == 0) || (wcscmp(cmd, L"all") == 0);
    BOOL doSine = (wcscmp(cmd, L"sine") == 0) || (wcscmp(cmd, L"all") == 0);
    BOOL doSpring = (wcscmp(cmd, L"spring") == 0) || (wcscmp(cmd, L"all") == 0);
    BOOL doDamper = (wcscmp(cmd, L"damper") == 0) || (wcscmp(cmd, L"all") == 0);

    if (doConst) {
        run_const_test(dev);
    }
    if (wcscmp(cmd, L"texture") == 0) {
        run_texture_test(dev);
    }
    if (doSine) {
        run_sine_test(dev);
    }
    if (doSpring) {
        wprintf(L"spring: move the wheel, it should return to center.\n");
        run_cond_test(dev, &GUID_Spring_G27, "spring");
    }
    if (doDamper) {
        wprintf(L"damper: turn the wheel, it should resist in proportion to speed.\n");
        run_cond_test(dev, &GUID_Damper_G27, "damper");
    }

    IDirectInputDevice8_Unacquire(dev);
    IDirectInputDevice8_Release(dev);
    IDirectInput8_Release(di);
    if (hwnd) DestroyWindow(hwnd);
    if (argv) LocalFree(argv);
    return 0;
}
