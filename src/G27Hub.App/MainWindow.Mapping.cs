using System.Windows;
using System.Windows.Threading;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// "Configurar volante": o usuário mexe cada controle (volante, pedais, marchas, botões) e o app aprende em qual eixo/botão
/// ele aparece (InputMapLearner). Para G27 com outro mapa (o de um colega do John, 23/10/2026: embreagem e marchas em
/// outros lugares). Muda o que o app lê (pedais, marchas, atribuições, volante virtual); os jogos do Modo Padrão leem direto.
/// </summary>
public partial class MainWindow
{
    InputMapLearner? _learner;
    readonly DispatcherTimer _mapTimer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    string _mapLast = "";

    void MapWheel_Click(object sender, RoutedEventArgs e)
    {
        DevicePopup.IsOpen = false;
        if (!_dev.IsConnected || _dev.State is not { } s)
        {
            ShowToast("G27", "Warn", T("G27 não conectado"), T("Ligue o volante para configurar."));
            return;
        }
        _learner = new InputMapLearner(_config.Map);
        _learner.Begin(s);
        _mapLast = "";
        MapSave.Visibility = Visibility.Collapsed;
        MapSkip.Visibility = Visibility.Visible;
        MapOverlay.Visibility = Visibility.Visible;
        _mapTimer.Tick -= MapTimer_Tick;
        _mapTimer.Tick += MapTimer_Tick;
        _mapTimer.Start();
        AppLog.Info("HID", "Configurar volante: começou");
        ShowMapStep();
    }

    void MapTimer_Tick(object? sender, EventArgs e) => MapTick();

    void MapTick()
    {
        AnimateMapDemo(Environment.TickCount64 % 2400 / 2400.0);
        if (_learner == null || _learner.Done || _dev.State is not { } s) return;
        var step = _learner.Step;
        if (_learner.Feed(s))
        {
            _mapLast = T(InputMapLearner.Steps[step].Text) + ": ✓ " + Learned(step);
            ShowMapStep();
        }
        MapBar.Width = ((FrameworkElement)MapBar.Parent).ActualWidth * Math.Min(_learner.Progress(s), 1);
    }

    /// <summary>O que o passo aprendeu, para mostrar ("eixo Z", "botão 13").</summary>
    string Learned(int step)
    {
        var (id, _, kind) = InputMapLearner.Steps[step];
        var m = _learner!.Map;
        if (kind == InputMapLearner.Kind.Axis)
            return T($"eixo {id switch { "steerL" or "steerR" => m.Steer, "throttle" => m.Throttle, "brake" => m.Brake, _ => m.Clutch }}");
        int b = id switch
        {
            "reverse" => m.Reverse, "paddleL" => m.PaddleLeft, "paddleR" => m.PaddleRight,
            _ when id.StartsWith("gear") => m.Gears[id[4] - '1'],
            _ when id.StartsWith("wheelL") => m.WheelLeft[id[6] - '0'],
            _ when id.StartsWith("wheelR") => m.WheelRight[id[6] - '0'],
            _ when id.StartsWith("red") => m.ShifterRed[id[3] - '0'],
            _ => m.ShifterBlack[id[5] - '0'],
        };
        return T($"botão {b}");
    }

    /// <summary>
    /// Mostra o que fazer no passo atual, num ciclo de 2,4 s (t = 0..1): volante girando até o fim, pedal afundando,
    /// manopla entrando na marcha, botão piscando. Usa os desenhos próprios do app.
    /// </summary>
    void AnimateMapDemo(double t)
    {
        var id = _learner is { Done: false } l ? InputMapLearner.Steps[l.Step].Id : "";
        static double Ease(double x) => x * x * (3 - 2 * x);
        double f = t < 0.35 ? Ease(t / 0.35) : t < 0.65 ? 1 : t < 0.9 ? 1 - Ease((t - 0.65) / 0.25) : 0;
        bool blink = t * 4 % 1 < 0.5;

        bool wheel = id.StartsWith("steer") || id.StartsWith("paddle") || id.StartsWith("wheel");
        bool pedals = id is "throttle" or "brake" or "clutch";
        bool shifter = id.StartsWith("gear") || id is "reverse" || id.StartsWith("red") || id.StartsWith("black");
        MapDemoWheel.Visibility = wheel ? Visibility.Visible : Visibility.Collapsed;
        if (!wheel) MapDemoArrow.Visibility = Visibility.Collapsed;
        MapDemoPedals.Visibility = pedals ? Visibility.Visible : Visibility.Collapsed;
        MapDemoShifter.Visibility = shifter ? Visibility.Visible : Visibility.Collapsed;

        if (wheel)
        {
            MapDemoWheel.Angle = id == "steerL" ? -270 * f : id == "steerR" ? 270 * f : 0;
            // seta grande do lado para onde girar (girado 270°, o desenho sozinho confunde)
            MapDemoArrow.Visibility = id.StartsWith("steer") ? Visibility.Visible : Visibility.Collapsed;
            MapDemoArrow.Text = id == "steerL" ? "⟲" : "⟳"; // anti-horário = esquerda
            MapDemoArrow.HorizontalAlignment = id == "steerL" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            MapDemoArrow.Margin = new Thickness(60, 0, 60, 0);
            MapDemoWheel.Demo(id switch
            {
                "paddleL" => 0, "paddleR" => 1,
                _ when id.StartsWith("wheelL") => 2 + id[6] - '0',
                _ when id.StartsWith("wheelR") => 5 + id[6] - '0',
                _ => -1,
            }, blink);
        }
        if (pedals)
        {
            int p = id switch { "clutch" => 0, "brake" => 1, _ => 2 };
            for (int i = 0; i < 3; i++) MapDemoPedals.SetDepth(i, i == p ? f : 0);
        }
        if (shifter)
        {
            var gear = id is "reverse" ? "R" : id.StartsWith("gear") ? id[4..] : null;
            MapDemoShifter.ShowKnob(gear, f);
            if (gear == null) MapDemoShifter.SetGear("N");
            int button = id.StartsWith("red") ? id[3] - '0' : id.StartsWith("black") ? 5 + id[5] - '0' : -1;
            MapDemoShifter.SetPressed(blink && button >= 0 ? [button] : []);
        }
    }

    void ShowMapStep()
    {
        var l = _learner!;
        MapLast.Text = _mapLast;
        if (l.Done)
        {
            MapCount.Text = T("Pronto");
            MapStep.Text = T("Tudo aprendido. Clique SALVAR para o app passar a usar este mapa.");
            MapBar.Width = 0;
            MapSkip.Visibility = Visibility.Collapsed;
            MapSave.Visibility = Visibility.Visible;
            return;
        }
        var (_, text, kind) = InputMapLearner.Steps[l.Step];
        MapCount.Text = T($"Passo {l.Step + 1} de {InputMapLearner.Steps.Length}");
        MapStep.Text = T(text);
        MapHint.Text = kind == InputMapLearner.Kind.Axis
            ? T("Vai até o fim e volta: o passo avança sozinho.")
            : T("O passo avança sozinho quando o app vê o botão. Não tem esse controle? Clique PULAR.");
    }

    void MapSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_learner == null || _dev.State is not { } s) return;
        var step = _learner.Step;
        _learner.Skip(s);
        _mapLast = T(InputMapLearner.Steps[step].Text) + T($": pulado (fica {Learned(step)})");
        ShowMapStep();
    }

    void MapSave_Click(object sender, RoutedEventArgs e) => SaveMap(_learner!.Map, T("Volante configurado"));

    void MapDefault_Click(object sender, RoutedEventArgs e) => SaveMap(new InputMap(), T("Mapa de fábrica restaurado"));

    void MapCancel_Click(object sender, RoutedEventArgs e)
    {
        CloseMap();
        AppLog.Info("HID", "Configurar volante: cancelado");
    }

    void SaveMap(InputMap map, string title)
    {
        _config.Map = G27Buttons.Map = map;
        _config.Save();
        foreach (var w in new[] { MainWheel, AssignWheel, LedWheel }) w.Remap();
        CloseMap();
        RefreshAssignmentsUi();
        UpdateCalibStatus();
        AppLog.Ok("HID", $"Mapa do volante salvo{(map.IsDefault ? " (padrão)" : "")}: {map.Describe()}");
        ShowToast("G27", "Ok", title, T("Pedais, marchas e botões do app seguem este mapa. Se calibrou os pedais antes, calibre de novo."));
    }

    /// <summary>
    /// Pedal do mapa que não existe ou que, solto, fica no meio do curso (o G27 manda o pedal solto numa ponta):
    /// sinal de volante com outro mapa, como a embreagem do colega do John parada em 50%. Null = parece certo.
    /// </summary>
    string? MapSuspicion(G27State s)
    {
        var m = G27Buttons.Map;
        foreach (var (axis, name) in new[] { (m.Throttle, T("acelerador")), (m.Brake, T("freio")), (m.Clutch, T("embreagem")) })
        {
            if (!s.Axes.TryGetValue(axis, out var a)) return T($"O {name} não aparece no eixo {axis}.");
            if (a.Normalized is > 0.25 and < 0.75) return T($"O {name} (eixo {axis}) está no meio do curso com o pedal solto.");
        }
        // pedal que aparece pisado com o pé fora: o eixo sobe ao pisar e o mapa não sabe (ex.: mapa de antes da 1.1.1)
        foreach (var (value, name) in new[] { (s.Throttle, T("acelerador")), (s.Brake, T("freio")), (s.Clutch, T("embreagem")) })
            if (value > 0.9) return T($"O {name} aparece pisado até o fundo com o pedal solto.");
        return null;
    }

    /// <summary>Ao conectar: se o mapa parece errado para este volante, oferece o "Configurar volante".</summary>
    void CheckMap()
    {
        if (_dev.State is not { } s || MapSuspicion(s) is not { } why) return;
        AppLog.Aviso("HID", $"O mapa dos pedais parece não bater com este volante: {why}");
        ShowToast("G27", "Warn", T("Este G27 parece ter outro mapa"), why + T(" Configure uma vez para o app ler pedais, marchas e botões certo."),
            TimeSpan.FromSeconds(20), (T("Configurar"), () => MapWheel_Click(this, new RoutedEventArgs())), (T("Agora não"), null));
    }

    void CloseMap()
    {
        _learner = null;
        _mapTimer.Stop();
        MapOverlay.Visibility = Visibility.Collapsed;
    }
}
