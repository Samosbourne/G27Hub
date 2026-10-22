// G27 Hub: plugin de telemetria do Euro Truck Simulator 2 / American Truck Simulator.
// Manda RPM, RPM máxima do caminhão, marcha e combustível para o app (UDP 127.0.0.1:4445, ~60 Hz),
// no mesmo pacote do mod do BeamNG com a assinatura "G27T". Usa o SCS SDK (MIT, ver ../scssdk/LICENSE-scssdk.txt).
// Instalado pelo app em <jogo>\bin\win_x64\plugins\; desfazer = apagar o arquivo.
#include <winsock2.h>
#include <string.h>
#include "scssdk_telemetry.h"
#include "eurotrucks2/scssdk_eut2.h"
#include "common/scssdk_telemetry_common_configs.h"
#include "common/scssdk_telemetry_truck_common_channels.h"

#pragma pack(push, 1)
struct Packet
{
    char magic[4];
    float rpm, maxRpm, idleRpm;
    int gear, shiftLight;
    float fuel; // 0–1
};
#pragma pack(pop)
static_assert(sizeof(Packet) == 28, "o app lê 28 bytes");

static SOCKET sock = INVALID_SOCKET;
static sockaddr_in to;
static bool paused = true;
static float rpm, maxRpm, fuel, fuelCapacity;
static int gear;
static ULONGLONG lastSend;

static SCSAPI_VOID on_frame_end(const scs_event_t, const void *const, const scs_context_t)
{
    ULONGLONG now = GetTickCount64();
    if (paused || sock == INVALID_SOCKET || now - lastSend < 15) return; // pausado = o app apaga os LEDs em 1 s
    lastSend = now;
    Packet p = {{'G', '2', '7', 'T'}, rpm, maxRpm, 0, gear, 0, fuelCapacity > 0 ? fuel / fuelCapacity : 0};
    sendto(sock, reinterpret_cast<const char *>(&p), sizeof p, 0, reinterpret_cast<const sockaddr *>(&to), sizeof to);
}

static SCSAPI_VOID on_pause(const scs_event_t event, const void *const, const scs_context_t)
{
    paused = event == SCS_TELEMETRY_EVENT_paused;
}

static SCSAPI_VOID on_configuration(const scs_event_t, const void *const event_info, const scs_context_t)
{
    const auto *info = static_cast<const scs_telemetry_configuration_t *>(event_info);
    if (strcmp(info->id, SCS_TELEMETRY_CONFIG_truck) != 0) return;
    for (const scs_named_value_t *a = info->attributes; a->name; ++a)
    {
        if (strcmp(a->name, SCS_TELEMETRY_CONFIG_ATTRIBUTE_rpm_limit) == 0) maxRpm = a->value.value_float.value;
        else if (strcmp(a->name, SCS_TELEMETRY_CONFIG_ATTRIBUTE_fuel_capacity) == 0) fuelCapacity = a->value.value_float.value;
    }
}

static SCSAPI_VOID store_float(const scs_string_t, const scs_u32_t, const scs_value_t *const value, const scs_context_t context)
{
    *static_cast<float *>(context) = value->value_float.value;
}

static SCSAPI_VOID store_s32(const scs_string_t, const scs_u32_t, const scs_value_t *const value, const scs_context_t context)
{
    *static_cast<int *>(context) = value->value_s32.value;
}

SCSAPI_RESULT scs_telemetry_init(const scs_u32_t version, const scs_telemetry_init_params_t *const params)
{
    if (version != SCS_TELEMETRY_VERSION_1_01) return SCS_RESULT_unsupported;
    const auto *p = static_cast<const scs_telemetry_init_params_v101_t *>(params);

    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) return SCS_RESULT_generic_error;
    sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    to = {};
    to.sin_family = AF_INET;
    to.sin_port = htons(4445);
    to.sin_addr.s_addr = htonl(INADDR_LOOPBACK);

    bool ok =
        p->register_for_event(SCS_TELEMETRY_EVENT_frame_end, on_frame_end, nullptr) == SCS_RESULT_ok &&
        p->register_for_event(SCS_TELEMETRY_EVENT_paused, on_pause, nullptr) == SCS_RESULT_ok &&
        p->register_for_event(SCS_TELEMETRY_EVENT_started, on_pause, nullptr) == SCS_RESULT_ok &&
        p->register_for_event(SCS_TELEMETRY_EVENT_configuration, on_configuration, nullptr) == SCS_RESULT_ok;
    // canais que faltarem (versão futura do jogo) só ficam em zero
    p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_engine_rpm, SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none, store_float, &rpm);
    p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_engine_gear, SCS_U32_NIL, SCS_VALUE_TYPE_s32, SCS_TELEMETRY_CHANNEL_FLAG_none, store_s32, &gear);
    p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_fuel, SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none, store_float, &fuel);

    p->common.log(ok ? SCS_LOG_TYPE_message : SCS_LOG_TYPE_error,
        ok ? "G27 Hub: telemetria para os LEDs do G27 ativa (127.0.0.1:4445)" : "G27 Hub: falha ao registrar eventos");
    paused = true;
    return ok ? SCS_RESULT_ok : SCS_RESULT_generic_error;
}

SCSAPI_VOID scs_telemetry_shutdown(void)
{
    if (sock != INVALID_SOCKET) closesocket(sock);
    sock = INVALID_SOCKET;
    WSACleanup();
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID) { return TRUE; }
