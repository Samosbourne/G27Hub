using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Fase 5b: atribuições (teclas, macros, mídia, programa, site, perfil, shift), pedal como tecla,
/// câmbio sequencial e atalho de rotação.</summary>
public partial class MainWindow : IAssignmentHost
{
    ButtonEngine? _engine;
    volatile bool _windowActive;
    int _selButton = G27Buttons.WheelRight[0];
    bool _recording;
    readonly Stopwatch _recClock = new();
    readonly List<KeyCaptureBox> _captures = [];

    // ================= motor =================

    void StartAssignments()
    {
        _engine = new ButtonEngine(new SendInputSink(), this);
        Activated += (_, _) => { _windowActive = true; _engine.ReleaseAll(); };
        Deactivated += (_, _) => _windowActive = false;
        _dev.StateChanged += s =>
        {
            // ponytail: o perfil é lido na thread do volante enquanto a tela edita; troca atômica de referência basta aqui
            // com o G27 Hub em foco as atribuições ficam pausadas (não digitar no próprio app); o atalho de rotação não
            try { _engine.Process(s, _config.Active, _config, paused: _windowActive); }
            catch (Exception ex) { AppLog.Erro("PERFIL", "Atribuição falhou", ex); }
        };
    }

    void IAssignmentHost.SwitchProfile(string profileId) => Dispatcher.BeginInvoke(() =>
    {
        if (_config.Profiles.FirstOrDefault(p => p.Id == profileId) is { } p) ActivateProfile(p, auto: false);
    });

    void IAssignmentHost.CycleRange() => Dispatcher.BeginInvoke(async () =>
    {
        var steps = _config.RotationSteps;
        var next = steps.FirstOrDefault(r => r > W.Range, steps[0]);
        W.Range = next;
        _config.Save();
        LoadSettingsIntoUi();
        ApplyWheel(force: false);
        AppLog.Ok("HID", $"Atalho do volante: rotação {next}°");
        ShowToast("G27", "Accent", T($"Rotação: {next}°"), T("Atalho: os dois vermelhos de cima do volante."));
        if (!_dev.IsConnected) return;
        var count = Array.IndexOf(steps, next) + 1; // LEDs acesos = posição na lista
        HoldLeds(1200); // a telemetria volta sozinha depois
        _dev.SetLeds((1 << count) - 1);
        await Task.Delay(1200);
    });

    // ================= nomes dos botões =================

    static IEnumerable<(string Group, int Button, string Name)> AllButtons()
    {
        string[] pos = [T("em cima"), T("no meio"), T("embaixo")];
        for (int i = 0; i < 3; i++) yield return (T("VOLANTE"), G27Buttons.WheelLeft[i], T($"Esquerda, {pos[i]}"));
        for (int i = 0; i < 3; i++) yield return (T("VOLANTE"), G27Buttons.WheelRight[i], T($"Direita, {pos[i]}"));
        yield return (T("VOLANTE"), G27Buttons.PaddleLeft, T("Borboleta esquerda"));
        yield return (T("VOLANTE"), G27Buttons.PaddleRight, T("Borboleta direita"));
        for (int i = 0; i < 4; i++) yield return (T("BASE DO CÂMBIO"), G27Buttons.ShifterRed[i], T($"Vermelho {i + 1}"));
        string[] black = [T("baixo"), T("esquerda"), T("direita"), T("cima")];
        for (int i = 0; i < 4; i++) yield return (T("BASE DO CÂMBIO"), G27Buttons.ShifterBlack[i], T($"Preto de {black[i]}"));
        yield return (T("DIRECIONAL"), ButtonEngine.HatUp, T("Cima"));
        yield return (T("DIRECIONAL"), ButtonEngine.HatRight, T("Direita"));
        yield return (T("DIRECIONAL"), ButtonEngine.HatDown, T("Baixo"));
        yield return (T("DIRECIONAL"), ButtonEngine.HatLeft, T("Esquerda"));
        for (int g = 0; g < 6; g++) yield return (T("MARCHAS"), G27Buttons.Gears[g], T($"{g + 1}ª marcha"));
        yield return (T("MARCHAS"), G27Buttons.Reverse, T("Ré"));
    }

    static string ButtonName(int b) => AllButtons().FirstOrDefault(x => x.Button == b) is { Name: not null } x ? T($"{x.Group.ToLowerInvariant()} · {x.Name}") : T($"botão {b}");

    // ================= aba Atribuições =================

    Dictionary<int, ButtonAction> EditMap => LayerShift.IsChecked == true ? _config.Active.ShiftActions : _config.Active.Actions;

    void RefreshAssignmentsUi()
    {
        var p = _config.Active;
        AssignOnCheck.IsChecked = p.AssignmentsOn;
        LayerShift.IsEnabled = p.ShiftButton != 0;
        if (p.ShiftButton == 0) LayerNormal.IsChecked = true;
        RotationShortcutCheck.IsChecked = _config.RotationShortcut;
        AssignWheel.Highlight(_selButton);
        BuildEditor();
        BuildAssignList();
        BuildPedalKeys();
        BuildSequential();
    }

    void SelectButton(int b)
    {
        _selButton = b;
        AssignWheel.Highlight(b);
        LastButton.Text = T($"Editando: {ButtonName(b)} (botão {b})");
        BuildEditor();
        BuildAssignList();
    }

    void SaveAssignments()
    {
        _config.Save();
        BuildAssignList();
    }

    void AssignOn_Click(object sender, RoutedEventArgs e)
    {
        _config.Active.AssignmentsOn = AssignOnCheck.IsChecked == true;
        _engine?.ReleaseAll();
        _config.Save();
        AppLog.Info("PERFIL", $"Atribuições {(_config.Active.AssignmentsOn ? "ligadas" : "desligadas")} em \"{_config.Active.Name}\"");
    }

    void Layer_Click(object sender, RoutedEventArgs e)
    {
        BuildEditor();
        BuildAssignList();
    }

    void RotationShortcut_Click(object sender, RoutedEventArgs e)
    {
        _config.RotationShortcut = RotationShortcutCheck.IsChecked == true;
        _config.Save();
    }

    void BuildAssignList()
    {
        AssignList.Children.Clear();
        var p = _config.Active;
        var map = EditMap;
        string? group = null;
        foreach (var (g, b, name) in AllButtons())
        {
            if (g != group)
            {
                group = g;
                AssignList.Children.Add(new TextBlock { Text = g, Style = (Style)FindResource("SettingLabel"), Margin = new Thickness(22, 12, 22, 4) });
            }
            var sel = b == _selButton;
            var action = b == p.ShiftButton ? "SHIFT" : map.TryGetValue(b, out var a) ? a.Summary(_config) : T("Padrão");
            var row = new Grid();
            row.Children.Add(new TextBlock { Text = name, FontSize = 12.5 });
            row.Children.Add(new TextBlock { Text = action, FontSize = 12, Foreground = action == T("Padrão") ? (Brush)FindResource("TextDim") : (Brush)FindResource("Accent"), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis });
            var border = new Border
            {
                Padding = new Thickness(sel ? 19 : 22, 7, 22, 7), Child = row, Cursor = Cursors.Hand,
                Background = sel ? Draw.Hex("#232B3A") : Brushes.Transparent,
                BorderBrush = (Brush)FindResource("Accent"), BorderThickness = new Thickness(sel ? 3 : 0, 0, 0, 0),
            };
            border.MouseLeftButtonUp += (_, _) => SelectButton(b);
            AssignList.Children.Add(border);
        }
    }

    void BuildEditor()
    {
        AssignEditor.Children.Clear();
        _captures.RemoveAll(c => !c.IsLoaded);
        var p = _config.Active;
        var map = EditMap;
        var b = _selButton;
        map.TryGetValue(b, out var action);
        action ??= new ButtonAction();

        AssignEditor.Children.Add(new TextBlock { Text = $"{ButtonName(b).ToUpperInvariant()} " + T($"(BOTÃO {b})") + (LayerShift.IsChecked == true ? T(" · CAMADA SHIFT") : ""), Style = (Style)FindResource("SettingLabel"), Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) });

        var isShift = new CheckBox { Content = T("Este botão é o Shift (segure para a camada 2)"), IsChecked = p.ShiftButton == b, Margin = new Thickness(0, 0, 0, 12), IsEnabled = LayerShift.IsChecked != true };
        isShift.Click += (_, _) =>
        {
            p.ShiftButton = isShift.IsChecked == true ? b : 0;
            LayerShift.IsEnabled = p.ShiftButton != 0;
            SaveAssignments();
            BuildEditor();
        };
        AssignEditor.Children.Add(isShift);
        if (p.ShiftButton == b && LayerShift.IsChecked != true) return; // o shift não tem ação própria

        (ActionKind Kind, string Label)[] kinds =
        [
            (ActionKind.None, T("Padrão (só o jogo vê o botão)")), (ActionKind.Key, T("Tecla ou combinação")), (ActionKind.Macro, "Macro"),
            (ActionKind.Media, T("Mídia / volume")), (ActionKind.Program, T("Abrir programa")), (ActionKind.Website, T("Abrir site")), (ActionKind.Profile, T("Trocar para o perfil")),
        ];
        foreach (var (kind, label) in kinds)
        {
            var r = new RadioButton { Content = label, GroupName = "AssignKind", IsChecked = action.Kind == kind, Margin = new Thickness(0, 0, 0, 8) };
            r.Click += (_, _) =>
            {
                action.Kind = kind;
                if (kind == ActionKind.None) map.Remove(b); else map[b] = action;
                SaveAssignments();
                BuildEditor();
            };
            AssignEditor.Children.Add(r);
        }

        var param = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        AssignEditor.Children.Add(param);
        switch (action.Kind)
        {
            case ActionKind.Key:
                var cap = new KeyCaptureBox { Value = action.Keys };
                cap.Changed += k => { action.Keys = k; SaveAssignments(); };
                _captures.Add(cap);
                param.Children.Add(cap);
                param.Children.Add(new TextBlock { Text = T("A tecla fica apertada enquanto você segura o botão."), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0) });
                break;

            case ActionKind.Macro:
                foreach (var (mode, label) in new[] { (MacroMode.Once, T("Toca uma vez")), (MacroMode.RepeatWhileHeld, T("Repete enquanto segura")), (MacroMode.Toggle, T("Liga / desliga (repete até apertar de novo)")) })
                {
                    var r = new RadioButton { Content = label, GroupName = "MacroMode", IsChecked = action.Mode == mode, Margin = new Thickness(0, 0, 0, 6) };
                    r.Click += (_, _) => { action.Mode = mode; SaveAssignments(); };
                    param.Children.Add(r);
                }
                var steps = new TextBlock { Style = (Style)FindResource("Hint"), Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 8), Text = MacroText(action) };
                param.Children.Add(steps);
                var rec = new Button { Style = (Style)FindResource(_recording ? "FlatButton" : "AccentButton"), Content = _recording ? T("Parar gravação") : T("Gravar (digite as teclas)") };
                rec.Click += (_, _) => ToggleRecording(action, steps, rec);
                param.Children.Add(rec);
                param.Children.Add(new TextBlock { Text = T("Grava as teclas e o tempo entre elas. Clique em PARAR com o mouse."), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0) });
                break;

            case ActionKind.Media:
                foreach (var m in KeyNames.Media)
                {
                    var r = new RadioButton { Content = T(m), GroupName = "Media", IsChecked = action.Media == m, Margin = new Thickness(0, 0, 0, 6) };
                    r.Click += (_, _) => { action.Media = m; SaveAssignments(); };
                    param.Children.Add(r);
                }
                break;

            case ActionKind.Program:
                param.Children.Add(new TextBlock { Text = action.Path.Length > 0 ? action.Path : T("Nenhum programa escolhido"), Style = (Style)FindResource("Hint"), Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) });
                var pick = new Button { Style = (Style)FindResource("FlatButton"), Content = T("Escolher programa…") };
                pick.Click += (_, _) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = T("Programas (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|Todos|*.*") };
                    if (dlg.ShowDialog(this) != true) return;
                    action.Path = dlg.FileName;
                    SaveAssignments();
                    BuildEditor();
                };
                param.Children.Add(pick);
                break;

            case ActionKind.Website:
                var url = new TextBox { Text = action.Url, FontSize = 12.5, Padding = new Thickness(6), Background = Draw.Hex("#232B3A"), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = Draw.Hex("#2E3A4F") };
                url.TextChanged += (_, _) => { action.Url = url.Text.Trim(); _config.Save(); };
                url.LostFocus += (_, _) => BuildAssignList();
                param.Children.Add(url);
                param.Children.Add(new TextBlock { Text = T("Ex.: youtube.com ou https://…"), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0) });
                break;

            case ActionKind.Profile:
                foreach (var prof in _config.Profiles)
                {
                    var r = new RadioButton { Content = prof.Name, GroupName = "TargetProfile", IsChecked = action.ProfileId == prof.Id, Margin = new Thickness(0, 0, 0, 6) };
                    r.Click += (_, _) => { action.ProfileId = prof.Id; SaveAssignments(); };
                    param.Children.Add(r);
                }
                break;
        }
    }

    static string MacroText(ButtonAction a) =>
        a.Steps.Count == 0 ? T("Macro vazia.")
        : string.Join("  ", a.Steps.Take(24).Select(s => $"{(s.DelayMs > 0 ? $"({s.DelayMs} ms) " : "")}{KeyNames.Display(KeyNames.Name(s.Vk))}{(s.Down ? "↓" : "↑")}")) + (a.Steps.Count > 24 ? "  …" : "");

    // ---- gravação de macro: a janela inteira escuta o teclado ----

    ButtonAction? _recAction;
    TextBlock? _recText;

    void ToggleRecording(ButtonAction action, TextBlock text, Button button)
    {
        _recording = !_recording;
        if (_recording)
        {
            _recAction = action;
            _recText = text;
            action.Steps.Clear();
            _recClock.Restart();
            text.Text = T("Gravando… digite as teclas.");
            PreviewKeyDown += RecordKey;
            PreviewKeyUp += RecordKey;
        }
        else
        {
            PreviewKeyDown -= RecordKey;
            PreviewKeyUp -= RecordKey;
            SaveAssignments();
            AppLog.Ok("PERFIL", $"Macro gravada: {action.Steps.Count(s => s.Down)} teclas");
        }
        button.Content = _recording ? T("Parar gravação") : T("Gravar (digite as teclas)");
        button.Style = (Style)FindResource(_recording ? "FlatButton" : "AccentButton");
    }

    void RecordKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.IsRepeat || _recAction == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;
        var delay = _recAction.Steps.Count == 0 ? 0 : (int)Math.Min(_recClock.ElapsedMilliseconds, 5000);
        _recClock.Restart();
        _recAction.Steps.Add(new MacroStep { Vk = vk, Down = e.IsDown, DelayMs = delay });
        if (_recText != null) _recText.Text = MacroText(_recAction);
    }

    bool CapturingKeys => _recording || _captures.Any(c => c.Capturing);

    // ================= pedal como tecla e câmbio sequencial =================

    void BuildPedalKeys()
    {
        PedalKeysPanel.Children.Clear();
        var p = _config.Active;
        string[] names = [T("EMBREAGEM"), T("FREIO"), T("ACELERADOR")];
        for (int i = 0; i < 3; i++)
        {
            var pk = p.Pedals[i];
            var on = new CheckBox { Content = T($"{names[i]} COMO TECLA"), IsChecked = pk.On, Margin = new Thickness(0, 0, 0, 8) };
            var slider = new SettingSlider { Label = T("APERTA A TECLA A PARTIR DE (%)"), Minimum = 5, Maximum = 95, Value = pk.Threshold };
            var cap = new KeyCaptureBox { Value = pk.Keys, Margin = new Thickness(0, 0, 0, 18) };
            on.Click += (_, _) => { pk.On = on.IsChecked == true; _config.Save(); };
            slider.ValueChanged += v => { pk.Threshold = (int)Math.Round(v); _config.Save(); };
            cap.Changed += k => { pk.Keys = k; _config.Save(); };
            _captures.Add(cap);
            PedalKeysPanel.Children.Add(on);
            PedalKeysPanel.Children.Add(slider);
            PedalKeysPanel.Children.Add(cap);
        }
    }

    void BuildSequential()
    {
        SequentialPanel.Children.Clear();
        var seq = _config.Active.Sequential;
        var on = new CheckBox { Content = T("Ligado neste perfil"), IsChecked = seq.On, Margin = new Thickness(0, 0, 0, 12) };
        on.Click += (_, _) => { seq.On = on.IsChecked == true; _config.Save(); _engine?.ReleaseAll(); };
        SequentialPanel.Children.Add(on);
        foreach (var (label, get, set) in new (string, Func<string>, Action<string>)[] { (T("TECLA PARA SUBIR MARCHA"), () => seq.Up, v => seq.Up = v), (T("TECLA PARA DESCER MARCHA"), () => seq.Down, v => seq.Down = v) })
        {
            SequentialPanel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("SettingLabel"), Margin = new Thickness(0, 0, 0, 4) });
            var cap = new KeyCaptureBox { Value = get(), Margin = new Thickness(0, 0, 0, 12) };
            cap.Changed += k => { set(k); _config.Save(); };
            _captures.Add(cap);
            SequentialPanel.Children.Add(cap);
        }
    }
}
