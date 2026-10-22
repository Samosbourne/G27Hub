using System.Diagnostics;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>
/// "Testar volante" (pedido do John, como o LGS fazia): devagar até o fim à esquerda, até o fim à direita, centro;
/// depois de uma vez à direita, de uma vez à esquerda, centro. Usa o WheelSpinner (força limitada) em cada passo.
/// </summary>
public static class WheelTest
{
    public sealed record Step(string Name, double Target, bool Quick);

    /// <summary>Passos para a rotação atual (para 5° antes do batente, para não bater).</summary>
    public static Step[] Steps(int range)
    {
        double m = range / 2.0 - 5;
        return
        [
            new(Lang.T("Girando devagar até o fim à esquerda"), -m, false),
            new(Lang.T("Girando devagar até o fim à direita"), m, false),
            new(Lang.T("Voltando ao centro"), 0, false),
            new(Lang.T("De uma vez para a direita"), m, true),
            new(Lang.T("De uma vez para a esquerda"), -m, true),
            new(Lang.T("Voltando ao centro"), 0, false),
        ];
    }

    public const double SweepSpeed = 270; // °/s nos passos devagar
    const double Arrived = 4;             // graus
    const int Settle = 150, StepTimeout = 6000; // ms

    /// <summary>
    /// Roda a sequência na thread de quem chamar (bloqueia ~10–15 s). status recebe o nome de cada passo.
    /// Retorna false se foi cancelado ou o volante desconectou. Sempre termina com a força zerada.
    /// </summary>
    public static bool Run(G27Device dev, Func<bool> cancelled, Action<string>? status = null)
    {
        var spinner = new WheelSpinner();
        var sw = Stopwatch.StartNew();
        double last = 0;
        int lastForce = int.MinValue;
        try
        {
            dev.SetAutocenter(0);
            foreach (var step in Steps(dev.Range))
            {
                status?.Invoke(step.Name);
                spinner.MaxForce = step.Quick ? 80 : 50;
                double start = dev.State?.Angle ?? 0, t0 = sw.Elapsed.TotalSeconds;
                long arrivedAt = -1, began = sw.ElapsedMilliseconds;
                while (true)
                {
                    if (cancelled() || !dev.IsConnected) return false;
                    var t = sw.Elapsed.TotalSeconds;
                    var angle = dev.State?.Angle ?? 0;
                    // devagar: o alvo anda até o destino; de uma vez: o alvo já é o destino
                    var target = step.Quick ? step.Target
                        : start + Math.Clamp(step.Target - start, -SweepSpeed * (t - t0), SweepSpeed * (t - t0));
                    var f = spinner.Step(angle, target, t - last);
                    last = t;
                    if (f != lastForce) { dev.SetForce(f); lastForce = f; }

                    bool there = target == step.Target && Math.Abs(angle - step.Target) < Arrived;
                    if (!there) arrivedAt = -1;
                    else if (arrivedAt < 0) arrivedAt = sw.ElapsedMilliseconds;
                    if ((there && sw.ElapsedMilliseconds - arrivedAt > Settle) || sw.ElapsedMilliseconds - began > StepTimeout) break;
                    Thread.Sleep(1);
                }
            }
            AppLog.Ok("FFB", "Teste do volante concluído");
            return true;
        }
        finally
        {
            try { dev.SetForce(0); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }
    }
}
