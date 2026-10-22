namespace G27Hub.Core;

/// <summary>
/// "Girar o G27 pelo mouse" (aba Volante): controlador PD que empurra o volante real até o ângulo pedido,
/// com força constante direta (G27Device.SetForce, positivo = esquerda). Só brincadeira: força limitada.
/// </summary>
public sealed class WheelSpinner
{
    public double Kp = 1.0;       // % de força por grau de erro
    public double Kd = 0.06;     // % por grau/s (freia antes de chegar)
    public double MaxForce = 50;  // %
    public double MinPush = 18;   // % para vencer o atrito e a zona morta do G27 (medido em 22/10: com menos, parava a 5–10° do alvo)
    public double Tolerance = 1.5; // graus

    double _vel, _last = double.NaN;

    public double Velocity => _vel;

    /// <summary>Força para mandar ao G27 (-100..100, positivo = esquerda) dado o ângulo atual, o alvo e o tempo desde a última chamada.</summary>
    public int Step(double angle, double target, double dt)
    {
        if (!double.IsNaN(_last) && dt > 0)
            _vel += ((angle - _last) / dt - _vel) * Math.Min(1, dt / 0.02); // filtro de ~20 ms
        _last = angle;

        double err = target - angle;
        double right = Kp * err - Kd * _vel;
        if (Math.Abs(err) > Tolerance) right += Math.Sign(err) * MinPush;
        else if (Math.Abs(_vel) < 20) right = 0; // chegou e parou: solta o volante
        return (int)Math.Round(-Math.Clamp(right, -MaxForce, MaxForce));
    }

    public void Reset()
    {
        _vel = 0;
        _last = double.NaN;
    }
}
