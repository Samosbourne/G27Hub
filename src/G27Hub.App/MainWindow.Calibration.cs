using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Fase 9: assistente de calibração dos pedais (aba Pedais). 1) pés fora dos pedais: mede o "solto";
/// 2) pisar cada um até o fundo: mede o "fundo"; grava na calibração do Windows (PedalCalibration).
/// </summary>
public partial class MainWindow
{
    static (string Axis, string Name)[] CalibPedals =>
        [(G27Buttons.Map.Clutch, T("Embreagem")), (G27Buttons.Map.Brake, T("Freio")), (G27Buttons.Map.Throttle, T("Acelerador"))];
    int _calibStep; // 0 = fechado, 1 = soltos, 2 = pisando
    readonly Dictionary<string, (int Released, int Pressed)> _calib = [];
    readonly List<(Border Fill, TextBlock Text)> _calibRows = [];

    void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (!_dev.IsConnected) { ShowToast("G27", "Warn", T("G27 não conectado"), T("Ligue o volante para calibrar os pedais.")); return; }
        _calib.Clear();
        CalibRows.Children.Clear();
        _calibRows.Clear();
        foreach (var (_, name) in CalibPedals)
        {
            var track = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 2) };
            track.Children.Add(new Border { Background = Draw.Hex("#232B3A"), CornerRadius = new CornerRadius(4) });
            var fill = new Border { Background = (Brush)FindResource("Accent"), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Children.Add(fill);
            var text = new TextBlock { Style = (Style)FindResource("Hint") };
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new TextBlock { Text = name.ToUpperInvariant(), Style = (Style)FindResource("SettingLabel") });
            row.Children.Add(track);
            row.Children.Add(text);
            CalibRows.Children.Add(row);
            _calibRows.Add((fill, text));
        }
        _calibStep = 1;
        CalibStep.Text = T("1. Tire os pés de todos os pedais e clique PRÓXIMO.");
        CalibNext.Content = T("Próximo");
        CalibOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>Chamado a cada quadro com o assistente aberto: barras com o valor bruto e o que já foi medido.</summary>
    void UpdateCalibration()
    {
        if (_calibStep == 0 || _dev.State is not { } s) return;
        for (int i = 0; i < CalibPedals.Length; i++)
        {
            var axis = CalibPedals[i].Axis;
            if (!s.Axes.TryGetValue(axis, out var a)) continue;
            if (_calibStep == 2 && _calib.TryGetValue(axis, out var c) && a.Value < c.Pressed) _calib[axis] = c with { Pressed = a.Value };
            var (fill, text) = _calibRows[i];
            fill.Width = ((FrameworkElement)fill.Parent).ActualWidth * (1 - a.Normalized); // bruto, sem calibração
            text.Text = _calibStep == 1 ? T($"agora {a.Value} (solto = 255 num pedal novo)")
                : T($"agora {a.Value} · solto {_calib[axis].Released} · fundo {_calib[axis].Pressed}") +
                  (_calib[axis].Released - _calib[axis].Pressed < 40 ? T("  ← pise até o fundo") : "  ✓");
        }
    }

    void CalibNext_Click(object sender, RoutedEventArgs e)
    {
        if (_dev.State is not { } s) return;
        if (_calibStep == 1)
        {
            foreach (var (axis, _) in CalibPedals)
                if (s.Axes.TryGetValue(axis, out var a)) _calib[axis] = (a.Value, a.Value);
            _calibStep = 2;
            CalibStep.Text = T("2. Pise cada pedal até o fundo, um de cada vez, e solte. Depois clique SALVAR.");
            CalibNext.Content = T("Salvar");
            return;
        }

        var saved = new List<string>();
        foreach (var (axis, name) in CalibPedals)
        {
            if (!_calib.TryGetValue(axis, out var c) || c.Released - c.Pressed < 40) continue; // não foi pisado: mantém como estava
            var r = PedalCalibration.Save(axis, c.Pressed, c.Released);
            saved.Add(T($"{name} {r.Pressed}–{r.Released}"));
        }
        _calibStep = 0;
        CalibOverlay.Visibility = Visibility.Collapsed;
        UpdateCalibStatus();
        ShowToast("G27", saved.Count > 0 ? "Ok" : "Warn", saved.Count > 0 ? T("Pedais calibrados") : T("Nada foi calibrado"),
            saved.Count > 0 ? T("Vale para todos os jogos (reabra o jogo se ele estiver aberto). ") + string.Join(", ", saved) : T("Nenhum pedal foi pisado até o fundo."));
    }

    void CalibCancel_Click(object sender, RoutedEventArgs e)
    {
        _calibStep = 0;
        CalibOverlay.Visibility = Visibility.Collapsed;
    }

    void CalibClear_Click(object sender, RoutedEventArgs e)
    {
        PedalCalibration.Clear();
        UpdateCalibStatus();
    }

    void UpdateCalibStatus()
    {
        var c = PedalCalibration.Current;
        CalibStatus.Text = c.Count == 0 ? T("Sem calibração (faixa de fábrica).")
            : T("Calibrados: ") + string.Join(", ", CalibPedals.Where(p => c.ContainsKey(p.Axis)).Select(p => T($"{p.Name.ToLowerInvariant()} {c[p.Axis].Pressed}–{c[p.Axis].Released}")));
        CalibClearButton.Visibility = c.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
