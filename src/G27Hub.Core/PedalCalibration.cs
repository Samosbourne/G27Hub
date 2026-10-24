using G27Hub.Logging;
using Microsoft.Win32;

namespace G27Hub.Core;

/// <summary>Faixa bruta de um pedal: Pressed = valor no fundo, Released = valor solto (o G27 manda 255 solto).</summary>
public sealed record PedalRange(int Pressed, int Released);

/// <summary>
/// Fase 9: calibração dos pedais gravada do jeito do próprio Windows (calibração do usuário do DirectInput, em HKCU, sem admin):
/// todos os jogos DirectInput passam a ler o pedal de 0 a 100% na faixa medida. Desfazer = apagar as chaves.
/// Instâncias DirectInput dos eixos do G27 nativo medidas no PC do John em 22/10/2026 (g27fftest axes): X 0, Y 1, Z 2, Rz 5.
/// </summary>
public static class PedalCalibration
{
    public const string Key = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\DirectInput\VID_046D&PID_C29B\Calibration\0\Type\Axes";
    // instância DirectInput = posição do eixo no DIJOYSTATE (por isso Rz é 5 mesmo com só 4 eixos); Rx/Ry/Slider/Dial
    // só aparecem em volantes com outro mapa (InputMap)
    static readonly Dictionary<string, int> Instance = new() { ["Y"] = 1, ["Z"] = 2, ["Rx"] = 3, ["Ry"] = 4, ["Rz"] = 5, ["Slider"] = 6, ["Dial"] = 7 };

    /// <summary>Calibração em vigor (o app também usa: desenho dos pedais e volante virtual). Eixo → faixa.</summary>
    public static IReadOnlyDictionary<string, PedalRange> Current { get; private set; } = Load();

    static Dictionary<string, PedalRange> Load()
    {
        var r = new Dictionary<string, PedalRange>();
        foreach (var (axis, n) in Instance)
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Key}\{n}");
            if (k?.GetValue("Calibration") is byte[] { Length: 12 } b)
                r[axis] = new(BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 8));
        }
        return r;
    }

    /// <summary>
    /// Grava a faixa medida (com uma folga: o fundo vira 100% e o pedal solto vira 0% mesmo se tremer um pouco).
    /// O Windows guarda mínimo e máximo; o sentido (pedal que sobe ou desce ao pisar) fica no InputMap.
    /// </summary>
    public static PedalRange Save(string axis, int pressed, int released)
    {
        int lo = Math.Min(pressed, released) + 2, hi = Math.Max(pressed, released) - 3;
        using var k = Registry.CurrentUser.CreateSubKey($@"{Key}\{Instance[axis]}");
        k.SetValue("Calibration", BitConverter.GetBytes(lo).Concat(BitConverter.GetBytes((lo + hi) / 2))
            .Concat(BitConverter.GetBytes(hi)).ToArray(), RegistryValueKind.Binary);
        Current = Load();
        var range = pressed < released ? new PedalRange(lo, hi) : new PedalRange(hi, lo);
        AppLog.Ok("HID", $"Pedal {axis} calibrado: fundo {range.Pressed}, solto {range.Released} (bruto 0–255)");
        return range;
    }

    public static void Clear()
    {
        foreach (var n in Instance.Values) Registry.CurrentUser.DeleteSubKeyTree($@"{Key}\{n}", throwOnMissingSubKey: false);
        Current = Load();
        AppLog.Info("HID", "Calibração dos pedais removida (de volta à faixa de fábrica)");
    }

    /// <summary>0..1 pisado, com a calibração se houver. rising = o valor sobe ao pisar (InputMap.Rising).</summary>
    public static double Apply(string axis, AxisValue a, bool rising = false) => Apply(Current.GetValueOrDefault(axis), a, rising);

    /// <summary>A faixa vem do Windows como mínimo (Pressed) e máximo (Released); o sentido decide qual ponta é o fundo.</summary>
    public static double Apply(PedalRange? r, AxisValue a, bool rising = false)
    {
        if (r == null || r.Released <= r.Pressed) return rising ? a.Normalized : 1 - a.Normalized;
        var f = (double)(a.Value - r.Pressed) / (r.Released - r.Pressed); // 0 no mínimo, 1 no máximo
        return Math.Clamp(rising ? f : 1 - f, 0, 1);
    }
}
