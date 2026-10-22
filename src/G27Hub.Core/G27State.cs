namespace G27Hub.Core;

/// <summary>
/// Números dos botões do G27 nativo (1-based, como no joy.cpl). Vermelhos do câmbio e marchas 1–3 confirmados
/// pelo John em 21/10/2026; o resto segue a sequência do relatório (22 botões + o 23º separado = ré).
/// </summary>
public static class G27Buttons
{
    public static readonly int[] ShifterBlack = [1, 2, 3, 4]; // losango: baixo, esquerda, direita, cima
    public const int PaddleRight = 5, PaddleLeft = 6;
    public static readonly int[] ShifterRed = [12, 9, 10, 11]; // da esquerda para a direita
    public static readonly int[] Gears = [13, 14, 15, 16, 17, 18];
    // Vermelhos do volante confirmados pelo John em 21/10/2026; borboletas 5/6 ainda pela sequência
    public static readonly int[] WheelLeft = [8, 21, 22];  // lateral esquerda, de cima para baixo
    public static readonly int[] WheelRight = [7, 19, 20]; // lateral direita, de cima para baixo
    public const int Reverse = 23;
}

public sealed record AxisValue(int Value, int Min, int Max)
{
    public double Normalized => Max == Min ? 0 : (double)(Value - Min) / (Max - Min);
}

/// <summary>
/// Estado lido do volante. Eixos por nome de usage HID ("X" = volante).
/// </summary>
public sealed record G27State(IReadOnlyDictionary<string, AxisValue> Axes, IReadOnlyList<int> Buttons, int Hat, int Range)
{
    public double? Angle => Axes.TryGetValue("X", out var x) ? AngleFrom(x.Normalized, Range) : null;

    // ponytail: mapeamento do G27 nativo conhecido de outros drivers, ainda não confirmado no volante do John
    // (docs/testes/fase4.md). Se estiver trocado, é só mudar aqui.
    public double Throttle => Pedal("Z");
    public double Brake => Pedal("Rz");
    public double Clutch => Pedal("Y");

    /// <summary>"1".."6", "R" ou "N".</summary>
    public string Gear
    {
        get
        {
            for (int g = 0; g < G27Buttons.Gears.Length; g++)
                if (Buttons.Contains(G27Buttons.Gears[g])) return (g + 1).ToString();
            return Buttons.Contains(G27Buttons.Reverse) ? "R" : "N";
        }
    }

    /// <summary>0..1 pisado. O G27 manda 255 com o pedal solto.</summary>
    double Pedal(string axis) => Axes.TryGetValue(axis, out var a) ? PedalCalibration.Apply(axis, a) : 0;

    /// <summary>Ângulo em graus (negativo = esquerda) a partir da posição 0..1 do eixo.</summary>
    public static double AngleFrom(double normalized, int range) => (normalized - 0.5) * range;
}
