namespace G27Hub.Core;

/// <summary>
/// Relatórios de saída de 7 bytes do G27 (sem o report ID). Fatos do protocolo levantados
/// a partir do hid-lg4ff; status de validação em docs/protocolo-g27.md.
/// </summary>
public static class G27Commands
{
    public const int VendorId = 0x046D;
    public const int PidCompat = 0xC294; // como o G27 aparece ao ligar (modo Driving Force EX)
    public const int PidNative = 0xC29B; // G27 em modo nativo
    public const int MinRange = 40, MaxRange = 900;

    public static byte[] RevertOnReset => [0xF8, 0x0A, 0, 0, 0, 0, 0];
    public static byte[] NativeMode => [0xF8, 0x09, 0x04, 0x01, 0, 0, 0];

    public static byte[] Range(int degrees)
    {
        var d = Math.Clamp(degrees, MinRange, MaxRange);
        return [0xF8, 0x81, (byte)d, (byte)(d >> 8), 0, 0, 0];
    }

    /// <summary>Bits 0–4 = os 5 LEDs (ordem física a confirmar no volante).</summary>
    public static byte[] Leds(int mask) => [0xF8, 0x12, (byte)(mask & 0x1F), 0, 0, 0, 0];

    public static byte[] AutocenterOff => [0xF5, 0, 0, 0, 0, 0, 0];

    /// <summary>Mola de autocentro, 0–100%. 0 desliga. Retorna os relatórios a enviar em ordem.</summary>
    public static byte[][] Autocenter(int percent)
    {
        var magnitude = Math.Clamp(percent, 0, 100) * 0xFFFF / 100;
        if (magnitude == 0) return [AutocenterOff];

        // Curva do G27: inclinação até 2/3 da força, mais íngreme depois; "a" vai pela metade em volantes não-MOMO.
        const int knee = 0xAAAA;
        long a = magnitude <= knee ? 0x0C * magnitude : 0x0C * knee + 0x06L * (magnitude - knee);
        long b = magnitude <= knee ? 0x80 * magnitude : 0x80 * knee + 0xFFL * (magnitude - knee);
        a >>= 1;
        byte k = (byte)(a / knee), force = (byte)(b / knee);

        return [[0xFE, 0x0D, k, k, force, 0, 0], [0x14, 0, 0, 0, 0, 0, 0]];
    }
}
