namespace G27Hub.Core;

/// <summary>
/// Números dos botões do G27 (1-based, como no joy.cpl), vindos do mapa em vigor (<see cref="Map"/>, AppConfig.Map).
/// O padrão é o do G27 do John; o assistente "Configurar volante" grava o de outros volantes.
/// </summary>
public static class G27Buttons
{
    public static InputMap Map { get; set; } = new();
    public static int[] ShifterBlack => Map.ShifterBlack; // losango: baixo, esquerda, direita, cima
    public static int PaddleRight => Map.PaddleRight;
    public static int PaddleLeft => Map.PaddleLeft;
    public static int[] ShifterRed => Map.ShifterRed;   // da esquerda para a direita
    public static int[] Gears => Map.Gears;
    public static int[] WheelLeft => Map.WheelLeft;    // lateral esquerda, de cima para baixo
    public static int[] WheelRight => Map.WheelRight;  // lateral direita, de cima para baixo
    public static int Reverse => Map.Reverse;
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
    public double? Angle => Axes.TryGetValue(G27Buttons.Map.Steer, out var x) ? AngleFrom(x.Normalized, Range) : null;

    public double Throttle => Pedal(G27Buttons.Map.Throttle);
    public double Brake => Pedal(G27Buttons.Map.Brake);
    public double Clutch => Pedal(G27Buttons.Map.Clutch);

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

    /// <summary>0..1 pisado. O G27 manda 255 com o pedal solto (alguns volantes: 0, ver InputMap.Rising).</summary>
    double Pedal(string axis) => Axes.TryGetValue(axis, out var a) ? PedalCalibration.Apply(axis, a, G27Buttons.Map.RisesWhenPressed(axis)) : 0;

    /// <summary>Ângulo em graus (negativo = esquerda) a partir da posição 0..1 do eixo.</summary>
    public static double AngleFrom(double normalized, int range) => (normalized - 0.5) * range;
}
