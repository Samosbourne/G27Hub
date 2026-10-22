using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;

namespace G27Hub.App;

/// <summary>
/// Janela principal: telas do G HUB ligadas ao G27 (G27Device) a 60 fps, ajustes aplicados no volante,
/// console de diagnóstico e notificações.
/// </summary>
public partial class MainWindow : Window
{
    readonly G27Device _dev = new();
    readonly DispatcherTimer _applyTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    bool _loading = true, _quitting, _testsRan;
    double _shownAngle;

    // peças desenhadas uma vez e atualizadas a cada quadro
    readonly List<(double T, Line Line)> _arc = [];
    (Rectangle[] Segs, TextBlock Value)[] _pedalMeters = [], _testMeters = [];
    readonly List<Border> _ledBar = [];
    readonly List<(Ellipse Dot, TextBlock Text)> _buttonDots = [];
    TextBlock _hatText = null!;
    readonly Polyline _forceLine = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    readonly Queue<double> _force = new();
    int _forcePid;
    string _forceGame = "";
    IReadOnlyList<int> _prevButtons = [];

    // console
    readonly ObservableCollection<LogLine> _log = [];
    readonly List<LogLine> _pausedLines = [];
    readonly ICollectionView _logView;
    bool _paused;
    string _origin = "";
    static readonly string[] Origins = ["", "HID", "MODO", "FFB", "LED", "PERFIL", "APP"];
    DateTime _lastErrorToast;

    public System.Drawing.Icon TrayIcon { get; }

    public MainWindow(bool connect)
    {
        InitializeComponent();
        (Icon, TrayIcon) = MakeIcons();

        BuildArc();
        _pedalMeters = BuildMeters(PedalMeters, 24, 7);
        _testMeters = BuildMeters(TestMeters, 16, 6);
        BuildLedBar();
        BuildForceGraph();
        BuildButtonGrid();
        BuildQuickTests([]);

        foreach (var l in AppLog.Memory.Snapshot()) _log.Add(l);
        _logView = CollectionViewSource.GetDefaultView(_log);
        _logView.Filter = o => ConsoleFilter((LogLine)o);
        ConsoleList.ItemsSource = _logView;
        AppLog.Memory.Added += l => Dispatcher.BeginInvoke(() => OnLog(l));
        VerboseCheck.IsChecked = AppLog.Verbose;

        LoadSettingsIntoUi();
        foreach (var s in new[] { RangeSlider, AutocenterSlider, GainSlider, MinForceSlider, SpringSlider, DamperSlider, CenterSlider })
            s.ValueChanged += _ => SettingChanged();
        foreach (var c in new[] { AutocenterCheck, CenterCheck, InvertCheck, AllowGameCheck })
            c.Click += (_, _) => SettingChanged();
        FfbEnabled.Click += (_, _) => ToggleFfb();
        AssignWheel.HotspotClicked += SelectButton;
        _applyTimer.Tick += (_, _) => { _applyTimer.Stop(); ApplySettings(); };
        _toastTimer.Tick += (_, _) => HideToast();
        StateChanged += (_, _) =>
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); // WindowChrome passa da tela ao maximizar
            MaxIcon.Text = WindowState == WindowState.Maximized ? "" : "";
        };
        PreviewKeyDown += OnKey;
        _loading = false;

        ShowScreen(HomeScreen);
        _dev.SetRange(W.Range); // aplicada ao conectar
        UpdateProfileHeader();
        if (!connect) return;
        StartProfileWatcher();
        StartAssignments();
        _dev.ConnectionChanged += c => Dispatcher.BeginInvoke(() => OnConnection(c));
        _dev.Start();
        CompositionTarget.Rendering += OnFrame;
    }

    // ================= conexão =================

    /// <summary>Com "--ffbtest": roda o minijogo assim que o volante conectar (para testes automáticos).</summary>
    public bool AutoFfbTest { get; set; }

    void OnConnection(bool connected)
    {
        if (connected)
        {
            if (AutoFfbTest)
            {
                AutoFfbTest = false;
                ShowScreen(WheelScreen);
                ShowSection("forca");
                Dispatcher.BeginInvoke(() => StartFfbTest_Click(this, new RoutedEventArgs()), DispatcherPriority.Background);
            }
            ApplyWheel(force: true);
            if (HomeScreen.Visibility == Visibility.Visible) ShowScreen(DevicesScreen);
            UpdateDeviceStatus();
            ShowToast("G27", "Ok", "G27 conectado", _dev.IsNative ? $"Modo nativo, {_dev.Range}°. Pode abrir o jogo." : "Conectado, mas fora do modo nativo. Use \"Reaplicar\" na aba Teste.");
        }
        else
        {
            ShowScreen(HomeScreen);
            ShowToast("G27", "Warn", "G27 desconectado", "Ao reconectar, o modo nativo e a rotação voltam sozinhos.");
        }
    }

    void UpdateDeviceStatus() => DeviceStatus.Text =
        $"{(_dev.IsNative ? "Modo nativo" : "Modo compatível (C294)")} · {_dev.Range}° · " +
        $"{(FfbRegistration.IsRegistered() ? "Force feedback ativo" : "Force feedback desligado")} · Modo Padrão";

    // ================= quadro a quadro (60 fps) =================

    void OnFrame(object? sender, EventArgs e)
    {
        if (FfbTestOverlay.Visibility == Visibility.Visible) { StepFfbTest(); return; }
        if (!IsVisible || WheelScreen.Visibility != Visibility.Visible) return;
        var s = _dev.State;
        if (s == null || !_dev.IsConnected) return;

        _shownAngle += ((s.Angle ?? 0) - _shownAngle) * 0.5; // suaviza entre os relatórios do volante
        double[] pedals = [s.Clutch, s.Brake, s.Throttle];

        switch (CurrentSection)
        {
            case "volante":
                MainWheel.Angle = _shownAngle;
                MainWheel.SetPressed(s.Buttons);
                AngleText.Text = $"{_shownAngle:+0;-0;0}°";
                UpdateArc((int)RangeSlider.Value, _shownAngle);
                break;
            case "pedais":
                UpdateMeters(_pedalMeters, pedals);
                for (int i = 0; i < 3; i++) Pedals.SetPressed(i, pedals[i] > 0.05);
                break;
            case "forca":
                UpdateForce();
                break;
            case "cambio":
                Shifter.SetGear(s.Gear);
                Shifter.SetPressed(ShifterPressed(s));
                GearText.Text = s.Gear;
                break;
            case "atribuicoes":
                AssignWheel.SetPressed(s.Buttons);
                var pressed = ButtonEngine.Pressed(s).Except(_prevButtons).FirstOrDefault();
                if (pressed != 0 && !CapturingKeys) SelectButton(pressed); // apertar no volante escolhe o botão
                break;
            case "teste":
                TestAngle.Text = $"{_shownAngle:+0.0;-0.0;0.0}°";
                TestMin.Text = $"-{W.Range / 2}°";
                TestMax.Text = $"+{W.Range / 2}°";
                if (s.Axes.TryGetValue("X", out var x))
                {
                    TestRaw.Text = $"bruto {x.Value} / {x.Max}";
                    TestMarker.Margin = new Thickness(x.Normalized * Math.Max(TestTrack.ActualWidth - 10, 0), 0, 0, 0);
                }
                UpdateMeters(_testMeters, pedals);
                TestShifter.SetGear(s.Gear);
                TestShifter.SetPressed(ShifterPressed(s));
                for (int b = 0; b < _buttonDots.Count; b++)
                {
                    var on = s.Buttons.Contains(b + 1);
                    _buttonDots[b].Dot.Fill = on ? (Brush)FindResource("Accent") : Draw.Hex("#262626");
                    _buttonDots[b].Text.Foreground = on ? Brushes.White : (Brush)FindResource("TextDim");
                }
                _hatText.Text = s.Hat < 0 ? "Direcional: solto" : $"Direcional: {new[] { "cima", "cima-direita", "direita", "baixo-direita", "baixo", "baixo-esquerda", "esquerda", "cima-esquerda" }[s.Hat % 8]}";
                break;
        }
        _prevButtons = ButtonEngine.Pressed(s).ToArray();
    }

    /// <summary>Índices do desenho do câmbio: 0–3 vermelhos, 4 direcional, 5–8 pretos.</summary>
    static int[] ShifterPressed(G27State s)
    {
        var p = new List<int>();
        for (int i = 0; i < 4; i++) if (s.Buttons.Contains(G27Buttons.ShifterRed[i])) p.Add(i);
        if (s.Hat >= 0) p.Add(4);
        for (int i = 0; i < 4; i++) if (s.Buttons.Contains(G27Buttons.ShifterBlack[i])) p.Add(5 + i);
        return p.ToArray();
    }

    // ================= teste de force feedback (minijogo) =================

    readonly Stopwatch _roadClock = new();
    double _roadLast, _roadMinAngle, _roadMaxAngle;
    int _roadForce, _roadMaxForce;

    void StartFfbTest_Click(object sender, RoutedEventArgs e) => StartFfbTest(free: false);
    void StartFreeDrive_Click(object sender, RoutedEventArgs e) => StartFfbTest(free: true);

    void StartFfbTest(bool free)
    {
        if (!_dev.IsConnected) { ShowToast("FFB", "Warn", "G27 não conectado", "Ligue o volante para testar o force feedback."); return; }
        if (FfbShared.Read() is { AgeMs: < 1000 }) { ShowToast("FFB", "Warn", "Tem um jogo usando o force feedback", "Feche o jogo antes de rodar o teste."); return; }
        _dev.SetAutocenter(0); // a mola do volante brigaria com o teste
        Road.WheelLock = W.Range / 2.0;
        Road.Reset(_dev.State?.Angle ?? 0, free);
        _roadLast = 0;
        _roadForce = _roadMaxForce = 0;
        _roadMinAngle = _roadMaxAngle = _dev.State?.Angle ?? 0;
        _roadClock.Restart();
        FfbTestOverlay.Visibility = Visibility.Visible;
        Focus();
        AppLog.Info("FFB", free ? "Direção livre iniciada" : "Teste de force feedback iniciado");
    }

    void StepFfbTest()
    {
        if (!_dev.IsConnected) { StopFfbTest(); return; }
        var t = _roadClock.Elapsed.TotalSeconds;
        var dt = Math.Min(t - _roadLast, 0.05);
        _roadLast = t;
        var st = _dev.State;
        var angle = st?.Angle ?? 0;
        var raw = Road.Step(dt, angle, st?.Throttle ?? 0, st?.Brake ?? 0) * GainSlider.Value / 100;
        var min = MinForceSlider.Value;
        if (Math.Abs(raw) > 0.5 && min > 0) raw = Math.Sign(raw) * (min + Math.Abs(raw) * (100 - min) / 100); // mesma força mínima da DLL
        var pct = (int)Math.Round(Math.Clamp(raw, -100, 100));
        _roadMaxForce = Math.Max(_roadMaxForce, Math.Abs(pct));
        _roadMinAngle = Math.Min(_roadMinAngle, angle);
        _roadMaxAngle = Math.Max(_roadMaxAngle, angle);
        if (pct != _roadForce)
        {
            try { _dev.SetForce(pct); }
            catch (Exception ex) { AppLog.Erro("FFB", "Teste de force feedback parou", ex); StopFfbTest(); return; }
            _roadForce = pct;
        }
        RoadForce.Text = $"{pct:+0;-0;0}%";
        var barMax = Math.Max(((FrameworkElement)RoadForceBar.Parent).ActualWidth / 2, 1);
        RoadForceBar.Width = Math.Abs(pct) / 100.0 * barMax;
        RoadForceBar.Margin = new Thickness(pct > 0 ? barMax - RoadForceBar.Width : barMax, 0, 0, 0); // positivo = empurra para a esquerda
        RoadAngle.Text = $"{angle:+0;-0;0}°";
        if (Road.Finished) StopFfbTest();
    }

    void StopFfbTest_Click(object sender, RoutedEventArgs e) => StopFfbTest();

    void StopFfbTest()
    {
        if (FfbTestOverlay.Visibility != Visibility.Visible) return;
        FfbTestOverlay.Visibility = Visibility.Collapsed;
        try
        {
            _dev.SetForce(0);
            ApplyAutocenter();
        }
        catch (InvalidOperationException) { } // volante desconectou no meio
        AppLog.Ok("FFB", (Road.Free ? $"Direção livre encerrada após {Road.Distance / 1000:0.0} km" : $"Teste de force feedback {(Road.Finished ? "concluído" : "interrompido")}") +
                         $": força máxima {_roadMaxForce}%, o volante foi de {_roadMinAngle:+0;-0;0}° a {_roadMaxAngle:+0;-0;0}°");
    }

    string CurrentSection => Sections.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Visibility == Visibility.Visible)?.Tag as string ?? "";

    // ================= ajustes =================

    /// <summary>Mostra nas abas os ajustes do perfil ativo (os próprios ou os do Desktop).</summary>
    void LoadSettingsIntoUi()
    {
        var wasLoading = _loading;
        _loading = true;
        var w = W;
        RangeSlider.Value = w.Range;
        AutocenterSlider.Value = w.Autocenter;
        AutocenterCheck.IsChecked = w.AutocenterOn;
        FfbEnabled.IsChecked = FfbRegistration.IsRegistered();
        GainSlider.Value = w.MasterGain;
        MinForceSlider.Value = w.MinForce;
        AllowGameCheck.IsChecked = w.AllowGame;
        SpringSlider.Value = w.SpringGain;
        DamperSlider.Value = w.DamperGain;
        CenterCheck.IsChecked = w.CenterSpring > 0;
        CenterSlider.Value = w.CenterSpring > 0 ? w.CenterSpring : 20;
        InvertCheck.IsChecked = w.Invert;
        StartWithWindowsCheck.IsChecked = AutoStart.Enabled;
        CloseToTrayCheck.IsChecked = _config.CloseToTray;
        NotificationsCheck.IsChecked = _config.Notifications;
        UpdateArc(w.Range, 0);
        UpdateProfileHeader();
        RefreshAssignmentsUi();
        _loading = wasLoading;
    }

    void SettingChanged()
    {
        if (_loading) return;
        UpdateArc((int)RangeSlider.Value, _shownAngle);
        _applyTimer.Stop();
        _applyTimer.Start(); // espera parar de arrastar o slider
    }

    /// <summary>Guarda o que está nas abas no perfil em vigor e manda para o volante / DLL.</summary>
    void ApplySettings()
    {
        var w = W;
        w.Range = (int)Math.Round(RangeSlider.Value);
        w.Autocenter = (int)Math.Round(AutocenterSlider.Value);
        w.AutocenterOn = AutocenterCheck.IsChecked == true;
        w.MasterGain = (int)Math.Round(GainSlider.Value);
        w.MinForce = (int)Math.Round(MinForceSlider.Value);
        w.SpringGain = (int)Math.Round(SpringSlider.Value);
        w.DamperGain = (int)Math.Round(DamperSlider.Value);
        w.CenterSpring = CenterCheck.IsChecked == true ? Math.Max(1, (int)Math.Round(CenterSlider.Value)) : 0;
        w.Invert = InvertCheck.IsChecked == true;
        w.AllowGame = AllowGameCheck.IsChecked == true;
        _config.Save();
        ApplyWheel(force: false);
    }

    int _appliedRange = -1, _appliedAutocenter = -1;

    /// <summary>Manda os ajustes em vigor para o volante (só o que mudou, ou tudo com force) e para a DLL.</summary>
    void ApplyWheel(bool force)
    {
        var w = W;
        if (force || w.Range != _appliedRange)
        {
            _dev.SetRange(w.Range); // sem volante, só guarda para a próxima conexão
            _appliedRange = w.Range;
            UpdateDeviceStatus();
        }
        var ac = w.AutocenterOn ? w.Autocenter : 0;
        if (force || ac != _appliedAutocenter)
        {
            _appliedAutocenter = ac;
            ApplyAutocenter();
        }
        SetFfb("MasterGain", w.MasterGain, 100);
        SetFfb("SpringGain", w.SpringGain, 100);
        SetFfb("DamperGain", w.DamperGain, 100);
        SetFfb("CenterSpring", w.CenterSpring, 0);
        SetFfb("Invert", w.Invert ? 1 : 0, 0);
        SetFfb("MinForce", w.MinForce, 0);
        SetFfb("AllowGame", w.AllowGame ? 1 : 0, 1);
    }

    static void SetFfb(string name, int value, int def)
    {
        if (FfbRegistration.GetConfig(name, def) != value) FfbRegistration.SetConfig(name, value);
    }

    void ApplyAutocenter()
    {
        var w = W;
        SetFfb("ExitAutocenter", w.AutocenterOn ? w.Autocenter : 0, 40); // a DLL devolve esta mola quando o jogo fecha
        if (FfbShared.Read() is { AgeMs: < 1000 }) return; // jogo com FFB aberto: a mola do volante brigaria com ele
        if (_dev.IsConnected) _dev.SetAutocenter(w.AutocenterOn ? w.Autocenter : 0);
    }

    void ToggleFfb()
    {
        if (FfbEnabled.IsChecked == true)
        {
            if (!FfbRegistration.Register()) FfbEnabled.IsChecked = false;
        }
        else FfbRegistration.Unregister();
        UpdateDeviceStatus();
        ShowToast("FFB", "Accent", "Force feedback", "Feche e abra o jogo de novo para valer.");
    }

    void RestoreWheel_Click(object sender, RoutedEventArgs e)
    {
        RangeSlider.Value = 900;
        AutocenterSlider.Value = 20;
        AutocenterCheck.IsChecked = true;
        SettingChanged();
    }

    void RestoreFfb_Click(object sender, RoutedEventArgs e)
    {
        GainSlider.Value = SpringSlider.Value = DamperSlider.Value = 100;
        MinForceSlider.Value = 0;
        AllowGameCheck.IsChecked = true;
        CenterSlider.Value = 20;
        CenterCheck.IsChecked = InvertCheck.IsChecked = false;
        SettingChanged();
    }

    public void Reapply()
    {
        if (!_dev.IsConnected) { ShowToast("G27", "Warn", "G27 não conectado", "Ligue o volante na tomada e no USB."); return; }
        if (!_dev.IsNative) { AppLog.Info("MODO", "Reconectando para reenviar o modo nativo"); _dev.Reset(); return; }
        ApplyWheel(force: true);
    }

    void Reapply_Click(object sender, RoutedEventArgs e) => Reapply();

    async void Pulse_Click(object sender, RoutedEventArgs e) => await Pulse();

    /// <summary>Dois pulsos leves (±30%); devolve quanto o volante girou no primeiro.</summary>
    async Task<double?> Pulse()
    {
        if (!_dev.IsConnected) return null;
        var before = _dev.State?.Angle ?? 0;
        _dev.SetForce(30);
        await Task.Delay(250);
        _dev.SetForce(0);
        await Task.Delay(300);
        var moved = (_dev.State?.Angle ?? 0) - before;
        _dev.SetForce(-30);
        await Task.Delay(250);
        _dev.SetForce(0);
        ApplyAutocenter(); // a força direta desliga a mola em alguns casos
        AppLog.Info("FFB", $"Pulso de teste: o volante girou {moved:+0.0;-0.0}°");
        return moved;
    }

    async void TestLeds_Click(object sender, RoutedEventArgs e) => await TestLeds();

    async Task TestLeds()
    {
        if (!_dev.IsConnected) { ShowToast("LED", "Warn", "G27 não conectado", "Ligue o volante para testar os LEDs."); return; }
        foreach (var mask in new[] { 1, 2, 4, 8, 16, 31, 0 })
        {
            _dev.SetLeds(mask);
            SetLedVisual(mask);
            await Task.Delay(mask == 31 ? 900 : 450);
        }
    }

    void SetLedVisual(int mask)
    {
        LedWheel.SetLeds(mask);
        for (int i = 0; i < _ledBar.Count; i++)
        {
            var on = (mask >> i & 1) != 0;
            ((SolidColorBrush)_ledBar[i].Background).Opacity = on ? 1 : 0.13;
            _ledBar[i].Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { Color = ((SolidColorBrush)_ledBar[i].Background).Color, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 } : null;
        }
        LedText.Text = mask == 0 ? "sem telemetria" : "teste";
    }

    // ================= navegação e janela =================

    void ShowScreen(FrameworkElement screen)
    {
        foreach (var s in new FrameworkElement[] { HomeScreen, DevicesScreen, WheelScreen })
            s.Visibility = s == screen ? Visibility.Visible : Visibility.Collapsed;
        var wheel = screen == WheelScreen;
        BackButton.Visibility = HeaderTitle.Visibility = wheel ? Visibility.Visible : Visibility.Collapsed;
        MenuButton.Visibility = wheel ? Visibility.Collapsed : Visibility.Visible;
        if (wheel && NavVolante.IsChecked != true) NavVolante.IsChecked = true;
    }

    void ShowSection(string tag)
    {
        foreach (FrameworkElement s in Sections.Children)
            s.Visibility = (string)s.Tag == tag ? Visibility.Visible : Visibility.Collapsed;
        foreach (RadioButton r in Nav.Children)
            if ((string)r.Tag == tag) r.IsChecked = true;
    }

    void DeviceCard_Click(object sender, MouseButtonEventArgs e) => ShowScreen(WheelScreen);
    void Back_Click(object sender, RoutedEventArgs e) => ShowScreen(_dev.IsConnected ? DevicesScreen : HomeScreen);
    void Nav_Checked(object sender, RoutedEventArgs e) => ShowSection((string)((FrameworkElement)sender).Tag);
    void AppSettings_Click(object sender, RoutedEventArgs e) => AppSettingsPopup.IsOpen = true;
    void StartWithWindows_Click(object sender, RoutedEventArgs e) => AutoStart.Enabled = StartWithWindowsCheck.IsChecked == true;
    void CloseToTray_Click(object sender, RoutedEventArgs e) { _config.CloseToTray = CloseToTrayCheck.IsChecked == true; _config.Save(); }
    void Notifications_Click(object sender, RoutedEventArgs e) { _config.Notifications = NotificationsCheck.IsChecked == true; _config.Save(); }
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void Quit()
    {
        _quitting = true;
        Close();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StopFfbTest();
        if (!_quitting && _config.CloseToTray)
        {
            e.Cancel = true; // continua na bandeja mantendo o volante configurado
            Hide();
            return;
        }
        CompositionTarget.Rendering -= OnFrame;
        _dev.Dispose();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { ToggleConsole(animate: true); e.Handled = true; }
        else if (e.Key == Key.Escape && FfbTestOverlay.Visibility == Visibility.Visible) { StopFfbTest(); e.Handled = true; }
    }

    // ================= volante: arco de riscos =================

    void BuildArc()
    {
        const double cx = 350, cy = 400, r1 = 330, r2 = 316, r2Long = 304, span = 52;
        for (double t = -span; t <= span + 0.01; t += 2)
        {
            var a = t * Math.PI / 180;
            var inner = Math.Abs(t % 10) < 0.01 ? r2Long : r2;
            var line = new Line
            {
                X1 = cx + r1 * Math.Sin(a), Y1 = cy - r1 * Math.Cos(a),
                X2 = cx + inner * Math.Sin(a), Y2 = cy - inner * Math.Cos(a), StrokeThickness = 3,
            };
            ArcTicks.Children.Add(line);
            _arc.Add((t, line));
        }
    }

    /// <summary>Riscos azuis = limite de rotação; risco branco = posição atual do volante.</summary>
    void UpdateArc(int range, double angle)
    {
        const double span = 52;
        var active = span * range / 900;
        var current = Math.Clamp(angle / (range / 2.0), -1, 1) * active;
        var accent = (Brush)FindResource("Accent");
        var off = Draw.Hex("#1F2B35");
        foreach (var (t, line) in _arc)
            line.Stroke = Math.Abs(t - current) < 1.5 ? Brushes.White : Math.Abs(t) <= active + 0.01 ? accent : off;
        RangeLabel.Text = $"{range}° LIMITE OPERACIONAL";
    }

    // ================= pedais: medidores segmentados =================

    (Rectangle[] Segs, TextBlock Value)[] BuildMeters(Panel host, int segments, double segHeight)
    {
        var result = new List<(Rectangle[], TextBlock)>();
        foreach (var label in new[] { "EMBREAGEM", "FREIO", "ACELERADOR" })
        {
            var col = new StackPanel { Margin = new Thickness(26, 0, 26, 0) };
            var segs = new Rectangle[segments];
            for (int s = segments - 1; s >= 0; s--)
                col.Children.Add(segs[s] = new Rectangle { Width = 40, Height = segHeight, Margin = new Thickness(0, 0, 0, 3), Fill = Draw.Hex("#18232C") });
            var value = new TextBlock { Text = "0%", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2) };
            col.Children.Add(value);
            col.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("SettingLabel"), HorizontalAlignment = HorizontalAlignment.Center });
            host.Children.Add(col);
            result.Add((segs, value));
        }
        return result.ToArray();
    }

    void UpdateMeters((Rectangle[] Segs, TextBlock Value)[] meters, double[] values)
    {
        var accent = (Brush)FindResource("Accent");
        var off = Draw.Hex("#18232C");
        for (int m = 0; m < meters.Length; m++)
        {
            var lit = (int)Math.Round(values[m] * meters[m].Segs.Length);
            for (int s = 0; s < meters[m].Segs.Length; s++) meters[m].Segs[s].Fill = s < lit ? accent : off;
            meters[m].Value.Text = $"{values[m] * 100:0}%";
        }
    }

    // ================= LEDs =================

    void BuildLedBar()
    {
        foreach (var c in new[] { "#35E06B", "#35E06B", "#FFC531", "#FF8A1F", "#FF3B3B" })
        {
            var b = new Border { Width = 64, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(6, 0, 6, 0), Background = Draw.Hex(c) };
            _ledBar.Add(b);
            LedBar.Children.Add(b);
        }
        SetLedVisual(0);
    }

    // ================= força: gráfico ao vivo =================

    const double GraphW = 680, GraphH = 280, GraphMid = GraphH / 2, GraphFull = 110; // 100% = 110 px
    const int ForceSamples = 600; // 10 s a 60 fps

    void BuildForceGraph()
    {
        var grid = Draw.Hex("#1E1E1E");
        foreach (var y in new[] { GraphMid - GraphFull / 2, GraphMid, GraphMid + GraphFull / 2 })
            ForceGraph.Children.Add(new Line { X1 = 0, X2 = GraphW, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = y == GraphMid ? 1.5 : 1 });
        foreach (var y in new[] { GraphMid - GraphFull, GraphMid + GraphFull })
            ForceGraph.Children.Add(new Line { X1 = 0, X2 = GraphW, Y1 = y, Y2 = y, Stroke = (Brush)FindResource("Error"), StrokeThickness = 1, StrokeDashArray = [4, 4], Opacity = 0.7 });
        ForceGraph.At(new TextBlock { Text = "+100%", FontSize = 10, Foreground = (Brush)FindResource("TextFaint") }, 0, GraphMid - GraphFull - 16);
        ForceGraph.At(new TextBlock { Text = "-100%", FontSize = 10, Foreground = (Brush)FindResource("TextFaint") }, 0, GraphMid + GraphFull + 2);
        _forceLine.Stroke = (Brush)FindResource("Accent");
        ForceGraph.Children.Add(_forceLine);
    }

    void UpdateForce()
    {
        var f = FfbShared.Read();
        var active = f is { AgeMs: < 500 };
        _force.Enqueue(active ? f!.Force / 10000.0 : 0);
        while (_force.Count > ForceSamples) _force.Dequeue();

        var points = new PointCollection(_force.Count);
        int i = 0;
        foreach (var v in _force) points.Add(new Point(GraphW * i++ / ForceSamples, GraphMid - Math.Clamp(v, -1, 1) * GraphFull));
        _forceLine.Points = points;

        if (!active) { ForceNow.Text = "—"; ClipNow.Text = "—"; GameName.Text = "nenhum"; return; }
        ForceNow.Text = $"{f!.Force / 100.0:+0;-0;0}%";
        var clip = _force.Count(v => Math.Abs(v) >= 1) * 100.0 / _force.Count;
        ClipNow.Text = $"{clip:0.0}%";
        ClipNow.Foreground = clip > 5 ? (Brush)FindResource("Error") : clip > 0 ? (Brush)FindResource("Warn") : Brushes.White;
        ClipNow.ToolTip = clip > 5 ? "A força está batendo no máximo: baixe a intensidade no jogo ou a INTENSIDADE GLOBAL." : null;
        if (f.Pid != _forcePid)
        {
            _forcePid = f.Pid;
            try { _forceGame = Process.GetProcessById(f.Pid).ProcessName; } catch { _forceGame = $"pid {f.Pid}"; }
        }
        GameName.Text = _forceGame;
    }

    // ================= teste: botões =================

    void BuildButtonGrid()
    {
        for (int b = 1; b <= 23; b++)
        {
            var dot = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 8, 8) };
            var e = new Ellipse { Fill = Draw.Hex("#262626") };
            var t = new TextBlock { Text = b.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextDim"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dot.Children.Add(e);
            dot.Children.Add(t);
            ButtonGrid.Children.Add(dot);
            _buttonDots.Add((e, t));
        }
        _hatText = new TextBlock { Text = "Direcional: solto", Style = (Style)FindResource("Hint"), Width = 300, Margin = new Thickness(0, 6, 0, 0) };
        ButtonGrid.Children.Add(_hatText);
    }

    // ================= atribuições e perfis (Fase 5) =================

    // ================= console (seção 6) =================

    void OnLog(LogLine l)
    {
        if (l.Nivel == "ERRO" && ConsolePanel.Visibility != Visibility.Visible)
        {
            ConsoleBadge.Visibility = Visibility.Visible;
            if (DateTime.Now - _lastErrorToast > TimeSpan.FromSeconds(5))
            {
                _lastErrorToast = DateTime.Now;
                ShowToast("!", "Error", $"Erro · {l.Origem}", l.Mensagem, "VER DETALHES");
            }
        }
        if (_paused) { _pausedLines.Add(l); return; }
        AddLogLine(l);
    }

    void AddLogLine(LogLine l)
    {
        _log.Add(l);
        if (_log.Count > 5000) _log.RemoveAt(0);
        if (ConsolePanel.Visibility == Visibility.Visible) ConsoleList.ScrollIntoView(l);
    }

    bool ConsoleFilter(LogLine l)
    {
        var level = l.Nivel switch
        {
            "OK" => ChipOk.IsChecked,
            "AVISO" => ChipAviso.IsChecked,
            "ERRO" => ChipErro.IsChecked,
            _ => ChipInfo.IsChecked, // INFO e DEBUG
        };
        return level == true
            && (_origin == "" || l.Origem == _origin)
            && (ConsoleSearch.Text.Length == 0 || l.ToString().Contains(ConsoleSearch.Text, StringComparison.OrdinalIgnoreCase));
    }

    void ConsoleFilter_Changed(object sender, RoutedEventArgs e) => _logView?.Refresh();

    void Origin_Click(object sender, MouseButtonEventArgs e)
    {
        _origin = Origins[(Array.IndexOf(Origins, _origin) + 1) % Origins.Length];
        OriginText.Text = _origin == "" ? "Todas as origens" : _origin;
        _logView.Refresh();
    }

    void Verbose_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Verbose = VerboseCheck.IsChecked == true;
        AppLog.Info("APP", AppLog.Verbose ? "Modo detalhado ligado" : "Modo detalhado desligado");
    }

    void ConsolePause_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseIcon.Text = _paused ? "" : "";
        if (_paused) return;
        foreach (var l in _pausedLines) AddLogLine(l);
        _pausedLines.Clear();
    }

    void ConsoleClear_Click(object sender, RoutedEventArgs e) => _log.Clear();

    IEnumerable<string> VisibleLines => _logView.Cast<LogLine>().Select(l => l.ToString());

    void ConsoleCopy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, VisibleLines));
        ShowToast("LOG", "Ok", "Copiado", "As linhas do console estão na área de transferência.");
    }

    void ConsoleExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"g27hub-log-{DateTime.Now:yyyyMMdd-HHmm}.txt", Filter = "Texto (*.txt)|*.txt" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllLines(dlg.FileName, VisibleLines);
        AppLog.Ok("APP", $"Log exportado para {dlg.FileName}");
    }

    void ConsoleToggle_Click(object sender, RoutedEventArgs e) => ToggleConsole(animate: true);

    void ToggleConsole(bool animate)
    {
        var open = ConsolePanel.Visibility != Visibility.Visible;
        ConsoleBadge.Visibility = Visibility.Collapsed; // erro visto
        if (open)
        {
            ConsolePanel.Visibility = Visibility.Visible;
            if (_log.Count > 0) ConsoleList.ScrollIntoView(_log[^1]);
            if (!_testsRan && animate) QuickTests_Click(this, new RoutedEventArgs());
        }
        if (!animate)
        {
            ConsoleShift.BeginAnimation(TranslateTransform.YProperty, null);
            ConsoleShift.Y = 0;
            ConsolePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        var anim = new DoubleAnimation(open ? 300 : 0, open ? 0 : 300, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() };
        if (!open) anim.Completed += (_, _) => ConsolePanel.Visibility = Visibility.Collapsed;
        ConsoleShift.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    // ---- testes rápidos ----

    async void QuickTests_Click(object sender, RoutedEventArgs e)
    {
        _testsRan = true;
        var r = new List<(string, bool?, string)>();
        var conn = _dev.IsConnected;
        r.Add(("Volante conectado", conn, conn ? $"046D:{_dev.Device?.ProductID:X4}" : "não encontrado"));
        r.Add(("Modo nativo ativo", _dev.IsNative, _dev.IsNative ? "" : "use Reaplicar"));
        r.Add(("Rotação aplicada", conn, $"{_dev.Range}°"));
        var ffb = FfbRegistration.IsRegistered();
        r.Add(("DLL de FFB registrada", ffb, ffb ? "x64 + x86" : "aba Força → Ativar"));
        BuildQuickTests([.. r, ("Pulso de força de teste", null, "rodando…")]);

        var moved = conn ? await Pulse() : null;
        r.Add(("Pulso de força de teste", moved is { } m ? Math.Abs(m) > 2 : conn ? false : null, moved is { } mv ? $"girou {mv:+0;-0}°" : "sem volante"));
        if (conn) await TestLeds();
        r.Add(("Teste dos LEDs", null, conn ? "confira no volante" : "sem volante"));
        var hvci = Diagnostics.MemoryIntegrity();
        r.Add(("Integridade de Memória", null, hvci == true ? "ligada" : hvci == false ? "desligada" : "desconhecida"));
        var old = Diagnostics.OldLogitechDrivers();
        r.Add(("Drivers antigos da Logitech", old.Length == 0, old.Length == 0 ? "nenhum" : string.Join(", ", old)));
        var vjoy = Diagnostics.VJoyInstalled();
        var hidhide = Diagnostics.HidHideInstalled();
        r.Add(("vJoy / HidHide", null, !vjoy && !hidhide ? "não instalados" : $"{(vjoy ? "vJoy" : "")} {(hidhide ? "HidHide" : "")}".Trim() + " instalado"));
        BuildQuickTests(r);

        foreach (var (name, ok, detail) in r)
            AppLog.Write(ok == false ? "AVISO" : ok == true ? "OK" : "INFO", "APP", $"Teste rápido: {name} {(ok == false ? "✗" : ok == true ? "✓" : "·")} {detail}");
    }

    void BuildQuickTests(IEnumerable<(string Name, bool? Ok, string Detail)> tests)
    {
        QuickTests.Children.Clear();
        foreach (var (name, ok, detail) in tests)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.Children.Add(new TextBlock
            {
                Text = ok == true ? "" : ok == false ? "" : "", Style = (Style)FindResource("Icon"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = ok == true ? (Brush)FindResource("Ok") : ok == false ? (Brush)FindResource("Error") : (Brush)FindResource("TextFaint"),
            });
            g.Children.Add(new TextBlock { Text = name, FontSize = 12, Margin = new Thickness(20, 0, 0, 0) });
            g.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = (Brush)FindResource("TextDim"), HorizontalAlignment = HorizontalAlignment.Right });
            QuickTests.Children.Add(g);
        }
        if (!QuickTests.Children.OfType<Grid>().Any())
            QuickTests.Children.Add(new TextBlock { Text = "Clique em RODAR TODOS.", Style = (Style)FindResource("Hint") });
    }

    // ================= notificações (5.7) =================

    void ShowToast(string icon, string kind, string title, string body, params string[] actions)
    {
        if (!_config.Notifications && kind != "Error") return;
        ToastIcon.Text = icon;
        ToastMark.Text = kind switch { "Ok" => "", "Warn" => "", "Error" => "", _ => "" };
        ToastMark.Foreground = (Brush)FindResource(kind);
        ToastTitle.Text = title;
        ToastBody.Text = body;
        ToastActions.Children.Clear();
        foreach (var a in actions.Length == 0 ? ["OK"] : actions)
        {
            var b = new Button { Content = a, Style = (Style)FindResource("LinkButton") };
            b.Click += (_, _) =>
            {
                HideToast();
                if (a == "VER DETALHES" && ConsolePanel.Visibility != Visibility.Visible) ToggleConsole(true);
            };
            ToastActions.Children.Add(b);
        }

        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        ToastShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void HideToast()
    {
        _toastTimer.Stop();
        Toast.Visibility = Visibility.Collapsed;
    }

    void ToastClose_Click(object sender, RoutedEventArgs e) => HideToast();

    // ================= ícone (desenho do G27) =================

    static (BitmapSource Window, System.Drawing.Icon Tray) MakeIcons()
    {
        var wheel = new G27Wheel { Width = 64, Height = 64 };
        wheel.Measure(new Size(64, 64));
        wheel.Arrange(new Rect(0, 0, 64, 64));
        wheel.UpdateLayout();
        var bmp = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(wheel);
        bmp.Freeze();
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        png.Save(ms);
        ms.Position = 0;
        using var gdi = new System.Drawing.Bitmap(ms);
        return (bmp, System.Drawing.Icon.FromHandle(gdi.GetHicon()));
    }

    // ================= capturas para revisão (G27Hub.App --shots <pasta>) =================

    public void SaveShots(string dir, bool live)
    {
        Directory.CreateDirectory(dir);
        if (live && _dev.Connect()) OnConnection(true); // lê o volante de verdade, sem a thread de reconexão
        void Shot(string name)
        {
            Root.UpdateLayout();
            if (live) for (int i = 0; i < 30; i++) { OnFrame(null, EventArgs.Empty); Thread.Sleep(5); }
            Root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(Root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(System.IO.Path.Combine(dir, name + ".png"));
            png.Save(f);
        }

        ShowScreen(HomeScreen); Shot("01-inicio");
        ShowScreen(DevicesScreen); Shot("02-dispositivos");
        ShowScreen(WheelScreen);
        string[] sections = ["volante", "pedais", "forca", "cambio", "leds", "atribuicoes", "teste"];
        for (int i = 0; i < sections.Length; i++) { ShowSection(sections[i]); Shot($"{i + 3:00}-{sections[i]}"); }
        ShowSection("volante");
        ToggleConsole(animate: false); Shot("10-console"); ToggleConsole(animate: false);

        FfbTestOverlay.Visibility = Visibility.Visible;
        Road.Reset(0, free: false);
        foreach (var (t, name) in new[] { (6.5, "11-teste-ffb-zebra"), (11.2, "12-teste-ffb-lombada"), (16.8, "13-teste-ffb-grama"), (20.5, "14-teste-ffb-terra"), (25.6, "15-teste-ffb-batida") })
        {
            while (Road.Time < t) Road.Step(1 / 60.0, 0);
            Shot(name);
        }
        Road.Reset(0, free: true);
        while (Road.Time < 6) Road.Step(1 / 60.0, 20, throttle: 1);
        Shot("16-direcao-livre");
        FfbTestOverlay.Visibility = Visibility.Collapsed;
        _dev.Dispose();
    }
}
