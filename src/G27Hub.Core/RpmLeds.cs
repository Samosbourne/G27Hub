namespace G27Hub.Core;

public enum LedMode
{
    /// <summary>A barra enche com a RPM; no ponto de troca (LED 5) pisca inteira.</summary>
    Progressive,
    /// <summary>Apagados; só piscam todos no ponto de troca.</summary>
    ShiftPoint,
    /// <summary>A barra enche sem piscar; pisca só no corte de giro (≥ 99%).</summary>
    Limiter,
}

/// <summary>Aba LEDs (global, em config.json).</summary>
public sealed class LedSettings
{
    public bool On { get; set; } = true;
    public LedMode Mode { get; set; }
    /// <summary>% da RPM máxima em que cada LED acende (LED 1 = par de fora … LED 5 = par vermelho do meio).</summary>
    public int[] Thresholds { get; set; } = [75, 80, 85, 90, 95];
    /// <summary>"Permitir que os jogos controlem os LEDs": usa a luz de troca / barra de giro do próprio jogo quando ele manda.</summary>
    public bool GameControls { get; set; } = true;
    public bool PitLimiterBlink { get; set; } = true;
    public bool LowFuelBlink { get; set; }
}

public static class RpmLeds
{
    public const int All = 0x1F;
    public const float LowFuel = 0.05f;

    /// <summary>Máscara dos 5 LEDs (bit 0 = par de fora … bit 4 = par do meio) para o quadro de telemetria.</summary>
    public static int Mask(TelemetryFrame f, LedSettings s, long nowMs)
    {
        if (f.PitLimiter && s.PitLimiterBlink) return nowMs / 250 % 2 == 0 ? 0b10101 : 0b01010;

        double pct = Percent(f, s);
        int lit = s.Thresholds.Count(t => pct >= t);
        bool shift = lit == 5 || s.GameControls && f.ShiftLight;
        bool blink = nowMs / 80 % 2 == 0; // ~6 piscadas por segundo
        int bar = (1 << lit) - 1;
        int mask = s.Mode switch
        {
            LedMode.Progressive => shift ? (blink ? All : 0) : bar,
            LedMode.ShiftPoint => shift && blink ? All : 0,
            _ => pct >= 99 ? (blink ? All : 0) : shift ? All : bar,
        };
        if (s.LowFuelBlink && f.Fuel is > 0 and < LowFuel && nowMs / 500 % 2 == 0) mask |= 1;
        return mask;
    }

    /// <summary>% da RPM máxima; usa a barra do jogo (F1) se ele mandar e for permitido, ou se não houver RPM máxima.</summary>
    public static double Percent(TelemetryFrame f, LedSettings s) =>
        f.GamePercent is { } g && (s.GameControls || f.MaxRpm <= 0) ? g * 100
        : f.MaxRpm > 0 ? f.Rpm / f.MaxRpm * 100 : 0;
}
