namespace G27Hub.Core;

public sealed record AxisValue(int Value, int Min, int Max)
{
    public double Normalized => Max == Min ? 0 : (double)(Value - Min) / (Max - Min);
}

/// <summary>
/// Estado lido do volante. Eixos por nome de usage HID ("X" = volante); qual eixo é
/// acelerador/freio/embreagem e quais botões são as marchas é confirmado no teste da Fase 1.
/// </summary>
public sealed record G27State(IReadOnlyDictionary<string, AxisValue> Axes, IReadOnlyList<int> Buttons, int Hat, int Range)
{
    public double? Angle => Axes.TryGetValue("X", out var x) ? AngleFrom(x.Normalized, Range) : null;

    /// <summary>Ângulo em graus (negativo = esquerda) a partir da posição 0..1 do eixo.</summary>
    public static double AngleFrom(double normalized, int range) => (normalized - 0.5) * range;
}
