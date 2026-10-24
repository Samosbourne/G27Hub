/* G27 Hub: substitui o g27log.c do gcampa.
 *
 * Log: as linhas vao para uma fila e uma thread propria as entrega pelo pipe
 * \\.\pipe\G27Hub_Log (formato "NIVEL|mensagem\n"). Sem o app aberto, cai para
 * %LOCALAPPDATA%\G27Hub\logs\ffb-<pid>.log. Fila cheia = linha descartada (e contada),
 * nunca espera: quem chama e a thread do jogo.
 *
 * Memoria compartilhada: Local\G27Hub_Shared, layout em G27HubShared (espelhado em
 * src/G27Hub.Core/FfbShared.cs).
 */
#include "g27log.h"
#include "g27cfg.h"

#include <stdarg.h>
#include <stdio.h>
#include <string.h>

#define QCAP 256
#define QLINE 600

static char g_q[QCAP][QLINE];
static int g_head, g_count;
static LONG g_dropped;
static CRITICAL_SECTION g_cs;
static HANDLE g_event;
static INIT_ONCE g_once = INIT_ONCE_STATIC_INIT;

static void write_fallback(HANDLE *file, const char *line) {
    if (*file == INVALID_HANDLE_VALUE) {
        WCHAR path[MAX_PATH];
        DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH);
        if (n == 0 || n > MAX_PATH - 64) return;
        wcscat(path, L"\\G27Hub");
        CreateDirectoryW(path, NULL);
        wcscat(path, L"\\logs");
        CreateDirectoryW(path, NULL);
        swprintf(path + wcslen(path), MAX_PATH - wcslen(path), L"\\ffb-%lu.log", GetCurrentProcessId());
        *file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                            OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (*file == INVALID_HANDLE_VALUE) return;
    }

    /* "NIVEL|msg\n" -> "HH:MM:SS  NIVEL  FFB        msg\r\n", igual ao log do app */
    const char *bar = strchr(line, '|');
    if (!bar) return;
    SYSTEMTIME st;
    GetLocalTime(&st);
    char out[QLINE + 64];
    int len = snprintf(out, sizeof(out), "%02u:%02u:%02u  %-5.*s  FFB        %s", st.wHour, st.wMinute,
                       st.wSecond, (int)(bar - line), line, bar + 1);
    if (len <= 0) return;
    if (len >= (int)sizeof(out)) len = (int)sizeof(out) - 1;
    if (out[len - 1] == '\n') { out[len - 1] = '\r'; out[len++] = '\n'; out[len] = 0; }
    DWORD w;
    WriteFile(*file, out, (DWORD)strlen(out), &w, NULL);
}

static DWORD WINAPI writer_thread(LPVOID p) {
    (void)p;
    HANDLE pipe = INVALID_HANDLE_VALUE, file = INVALID_HANDLE_VALUE;
    DWORD lastTry = 0;
    char line[QLINE];

    for (;;) {
        WaitForSingleObject(g_event, INFINITE);
        for (;;) {
            EnterCriticalSection(&g_cs);
            if (g_count == 0) {
                LeaveCriticalSection(&g_cs);
                break;
            }
            int tail = (g_head - g_count + QCAP) % QCAP;
            memcpy(line, g_q[tail], QLINE);
            g_count--;
            LeaveCriticalSection(&g_cs);

            /* ponytail: tenta reconectar o pipe no maximo a cada 2 s; o app pode abrir depois do jogo */
            if (pipe == INVALID_HANDLE_VALUE && GetTickCount() - lastTry > 2000) {
                lastTry = GetTickCount();
                pipe = CreateFileW(L"\\\\.\\pipe\\G27Hub_Log", GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
            }
            DWORD w;
            if (pipe != INVALID_HANDLE_VALUE && !WriteFile(pipe, line, (DWORD)strlen(line), &w, NULL)) {
                CloseHandle(pipe);
                pipe = INVALID_HANDLE_VALUE;
            }
            if (pipe == INVALID_HANDLE_VALUE) {
                write_fallback(&file, line);
            }
        }
    }
}

static BOOL CALLBACK init_logger(PINIT_ONCE once, PVOID param, PVOID *ctx) {
    (void)once;
    (void)param;
    (void)ctx;
    InitializeCriticalSection(&g_cs);
    g_event = CreateEventW(NULL, FALSE, FALSE, NULL);

    /* A thread vive ate o fim do processo: fixa a DLL na memoria para o COM nunca
       descarrega-la por baixo da thread (DllCanUnloadNow). */
    HMODULE self;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                       (LPCWSTR)(void *)writer_thread, &self);
    HANDLE t = CreateThread(NULL, 0, writer_thread, NULL, 0, NULL);
    if (t) CloseHandle(t);
    return TRUE;
}

static void vlog(const char *nivel, const char *fmt, va_list args) {
    InitOnceExecuteOnce(&g_once, init_logger, NULL, NULL);

    char msg[QLINE];
    int n = snprintf(msg, sizeof(msg), "%s|", nivel);
    vsnprintf(msg + n, sizeof(msg) - n - 1, fmt, args);
    msg[sizeof(msg) - 2] = 0;
    strcat(msg, "\n");

    EnterCriticalSection(&g_cs);
    if (g_count == QCAP) {
        g_dropped++;
    } else {
        if (g_dropped && g_count < QCAP - 1) {
            snprintf(g_q[g_head], QLINE, "AVISO|%ld linhas de log descartadas (fila cheia)\n", g_dropped);
            g_head = (g_head + 1) % QCAP;
            g_count++;
            g_dropped = 0;
        }
        memcpy(g_q[g_head], msg, QLINE);
        g_head = (g_head + 1) % QCAP;
        g_count++;
    }
    LeaveCriticalSection(&g_cs);
    SetEvent(g_event);
}

void g27_log(int level, const char *fmt, ...) {
    /* nivel configurado relido no maximo 1x por segundo */
    static DWORD configured = 1, readTick = 0;
    DWORD now = GetTickCount();
    if (readTick == 0 || now - readTick > 1000) {
        configured = g27_cfg_get(L"Log", 1);
        readTick = now ? now : 1;
    }
    if ((DWORD)level > configured) {
        return;
    }
    va_list args;
    va_start(args, fmt);
    vlog(level >= 2 ? "DEBUG" : "INFO", fmt, args);
    va_end(args);
}

void hub_log(const char *nivel, const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vlog(nivel, fmt, args);
    va_end(args);
}

/* ---------------- memoria compartilhada ---------------- */

typedef struct {
    DWORD magic;   /* 'G27H' */
    DWORD version; /* 2 */
    DWORD pid;     /* processo do jogo que esta mandando forca */
    LONG force;    /* -10000..10000, antes do corte */
    DWORD clips;   /* ticks com a forca saturada, acumulado */
    DWORD tick;    /* GetTickCount da ultima atualizacao */
    /* Fase 10: vibracao extra escrita pelo app a partir da telemetria do jogo (VibrationMixer.cs) */
    DWORD vibTick;            /* GetTickCount da ultima escrita do app; parado > 250 ms = sem vibracao */
    LONG engAmp, engHz100;    /* motor: amplitude 0..10000 e frequencia em centesimos de Hz */
    LONG roadAmp, roadHz100;  /* pista/zebra/ABS */
    LONG kickAmp;             /* tranco (troca de marcha, batida), com sinal */
    DWORD kickUntil;          /* GetTickCount ate quando o tranco vale */
} G27HubShared;

static G27HubShared *hub_map(void) {
    static G27HubShared *shared = NULL;
    static BOOL tried = FALSE;
    if (!shared && !tried) {
        tried = TRUE;
        /* o app pode ter criado antes (vibracao); CreateFileMapping abre o que ja existe */
        HANDLE map = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, 64, L"Local\\G27Hub_Shared");
        if (!map) return NULL;
        shared = (G27HubShared *)MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, 64);
        if (!shared) return NULL;
        shared->magic = 0x48373247; /* "G27H" */
        shared->version = 2;
    }
    return shared;
}

void hub_publish(LONG force, BOOL clipped) {
    G27HubShared *shared = hub_map();
    if (!shared) return;
    shared->pid = GetCurrentProcessId();
    shared->force = force;
    if (clipped) shared->clips++;
    shared->tick = GetTickCount();
}

/* seno aproximado (parabola corrigida, erro < 0,1%) de uma fase em voltas 0..1; sem a biblioteca de matematica */
static double wave(double turns) {
    double x = turns < 0.5 ? 4.0 * turns - 1.0 : 3.0 - 4.0 * turns; /* triangulo -1..1 */
    x = x * 0.5 + 0.5;                                               /* 0..1 -> meia onda */
    double y = 4.0 * x * (1.0 - x);                                  /* parabola 0..1..0 */
    y = 0.225 * (y * y - y) + y;
    return turns < 0.5 ? y : -y;
}

LONG hub_vibration(double dt) {
    static double phEng, phRoad; /* em voltas, 0..1 */
    G27HubShared *s = hub_map();
    DWORD now = GetTickCount();
    if (!s || now - s->vibTick > 250) return 0;
    phEng += s->engHz100 / 100.0 * dt;
    phRoad += s->roadHz100 / 100.0 * dt;
    if (phEng >= 1.0) phEng -= 1.0;
    if (phRoad >= 1.0) phRoad -= 1.0;
    double f = s->engAmp * wave(phEng) + s->roadAmp * wave(phRoad);
    if ((LONG)(s->kickUntil - now) > 0) f += s->kickAmp;
    return (LONG)f;
}
