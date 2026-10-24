using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Fase 5a: perfis por jogo, troca automática, cadeado, menu ☰ e configurações do G27.</summary>
public partial class MainWindow
{
    readonly AppConfig _config = AppConfig.Load();
    Timer? _processTimer;
    string? _detectedId;
    readonly Dictionary<string, ImageSource?> _icons = [];

    /// <summary>Ajustes em vigor: os do perfil ativo ou, se ele não tem "configurações especiais", os do Desktop.</summary>
    WheelSettings W => _config.Effective(_config.Active);

    // ================= troca automática =================

    void StartProfileWatcher()
    {
        // ponytail: varre os processos a cada 2 s; a Fase 6 troca por eventos de criação de processo se precisar
        _processTimer = new Timer(_ =>
        {
            string[] names;
            try { names = Process.GetProcesses().Select(p => { using (p) return p.ProcessName; }).ToArray(); }
            catch { return; }
            Dispatcher.BeginInvoke(() => OnProcesses(names));
        }, null, 1000, 2000);
    }

    void OnProcesses(string[] names)
    {
        SwitchProfileFor(names);
        DetectGames(names);
        UpdateGameBadge(names);
    }

    /// <summary>Indicador discreto do jogo reconhecido: tela inicial (canto) e topo da página do volante.</summary>
    void UpdateGameBadge(string[] names)
    {
        var game = names.Select(_db.Find).FirstOrDefault(g => g != null)?.Name ?? _config.MatchRunning(names)?.Name;
        if (game == null)
        {
            GameBadge.Visibility = HeaderGame.Visibility = Visibility.Collapsed;
            return;
        }
        var text = T($"{game} aberto · perfil {(_config.Active.IsDesktop ? "Desktop" : _config.Active.Name)}");
        GameBadgeText.Text = text;
        HeaderGameText.Text = text;
        GameBadge.Visibility = Visibility.Visible;
        HeaderGame.Visibility = HeaderTitle.Visibility; // só junto do título "G27" (página do volante)
        GameBadge.ToolTip = HeaderGame.ToolTip = T("Jogo reconhecido pelo G27 Hub") + (_config.DetectorOn ? "" : T(" (avisos do detector desligados)"));
    }

    void SwitchProfileFor(string[] names)
    {
        var match = _config.MatchRunning(names);
        if (match?.Id == _detectedId) return; // só reage quando o jogo abre ou fecha
        var hadGame = _detectedId != null;
        _detectedId = match?.Id;
        if (match != null) AppLog.Info("JOGO", $"{match.Exes.FirstOrDefault()} aberto → perfil \"{match.Name}\"");
        else if (hadGame) AppLog.Info("JOGO", "Jogo fechado → perfil \"Desktop: Padrão\"");

        if (_config.Locked)
        {
            AppLog.Info("PERFIL", $"Perfil travado em \"{_config.Active.Name}\"; nenhuma troca automática");
            return;
        }
        ActivateProfile(match ?? _config.Desktop, auto: true);
    }

    void ActivateProfile(Profile p, bool auto)
    {
        if (_config.ActiveId == p.Id && auto) return;
        _engine?.ReleaseAll(); // não deixar tecla presa do perfil anterior
        _config.ActiveId = p.Id;
        _config.Save();
        LoadSettingsIntoUi();
        ApplyWheel(force: true);
        AppLog.Ok("PERFIL", $"Perfil ativo: {p.Name}{(auto ? " (automático)" : "")}");
        if (auto)
            ShowToast(p.IsDesktop ? "PC" : Initials(p.Name), "Accent", T($"Perfil: {p.Name}"),
                T($"{W.Range}° · ") + (p.IsDesktop || p.UseOwnSettings ? T("configurações deste perfil") : T("configurações do Desktop: Padrão")));
    }

    // ================= cabeçalho, cadeado e "configurações especiais" =================

    void UpdateProfileHeader()
    {
        RefreshPhoneProfiles();
        var p = _config.Active;
        ProfileName.Text = p.IsDesktop ? T("DESKTOP: Padrão") : p.Name;
        ProfileCaption.Text = _config.Locked ? T("PERFIL TRAVADO") : T("PERFIL ATIVO");
        var lockGlyph = _config.Locked ? "" : "";
        ProfileLockIcon.Text = lockGlyph;
        ProfileLockIcon.Foreground = (Brush)FindResource(_config.Locked ? "Accent" : "TextDim");
        foreach (var l in new[] { LockVolante, LockPedais, LockForca })
        {
            l.Text = lockGlyph;
            l.Foreground = (Brush)FindResource(_config.Locked ? "Accent" : "TextFaint");
        }
        LockCheckPopup.IsChecked = LockCheckSettings.IsChecked = _config.Locked;

        var game = !p.IsDesktop;
        foreach (var c in new[] { UseOwnVolante, UseOwnForca })
        {
            c.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
            c.IsChecked = p.UseOwnSettings;
        }
        foreach (var h in new[] { InheritHintVolante, InheritHintForca })
            h.Visibility = game && !p.UseOwnSettings ? Visibility.Visible : Visibility.Collapsed;
    }

    void LockToggle_Click(object sender, RoutedEventArgs e) => SetLocked(!_config.Locked);
    void LockToggle_Click(object sender, MouseButtonEventArgs e) => SetLocked(!_config.Locked);

    void SetLocked(bool locked)
    {
        _config.Locked = locked;
        _config.Save();
        UpdateProfileHeader();
        AppLog.Info("PERFIL", locked ? $"Perfil travado em \"{_config.Active.Name}\"" : "Perfil destravado: troca automática ligada");
        if (!locked) _detectedId = "?"; // força reavaliar o jogo aberto na próxima varredura
    }

    void UseOwn_Click(object sender, RoutedEventArgs e)
    {
        var p = _config.Active;
        if (p.IsDesktop) return;
        p.UseOwnSettings = ((CheckBox)sender).IsChecked == true;
        if (p.UseOwnSettings) p.Wheel = _config.Desktop.Wheel.Clone(); // começa do que estava valendo
        _config.Save();
        LoadSettingsIntoUi();
        ApplyWheel(force: true);
        AppLog.Ok("PERFIL", $"\"{p.Name}\": {(p.UseOwnSettings ? "configurações especiais do volante" : "usa as configurações do Desktop")}");
    }

    // ================= seletor de perfis =================

    void Profile_Click(object sender, RoutedEventArgs e)
    {
        RebuildProfileList();
        ProfilePopup.IsOpen = true;
    }

    void RebuildProfileList()
    {
        ProfileList.Children.Clear();
        foreach (var p in _config.Profiles)
        {
            var active = p.Id == _config.ActiveId;
            var row = new Grid { Background = active ? Draw.Hex("#232B3A") : Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Background = (Brush)FindResource("AccentDim"), Margin = new Thickness(8, 6, 10, 6) };
            icon.Child = ProfileIcon(p) is { } img
                ? new Image { Source = img, Width = 22, Height = 22 }
                : new TextBlock { Text = p.IsDesktop ? "" : Initials(p.Name), FontFamily = p.IsDesktop ? (FontFamily)FindResource("Icons") : new FontFamily("Segoe UI"), FontSize = p.IsDesktop ? 13 : 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = p.Name, FontSize = 12.5, Foreground = active ? (Brush)FindResource("Accent") : Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!p.IsDesktop)
                text.Children.Add(new TextBlock { Text = (p.Exes.Count > 0 ? string.Join(", ", p.Exes) : T("sem .exe (não troca sozinho)")) + (p.Exes.Select(_db.Find).FirstOrDefault(x => x != null) is { } entry ? $" · {T(entry.Status)}" : ""), FontSize = 10.5, Foreground = (Brush)FindResource("TextFaint"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var more = new Button { Style = (Style)FindResource("IconButton"), Width = 30, Height = 30, ToolTip = T("Opções do perfil"), Content = new TextBlock { Text = "", Style = (Style)FindResource("Icon"), FontSize = 12, Foreground = (Brush)FindResource("TextDim") } };
            more.Click += (_, ev) => { ev.Handled = true; ProfileMenu(p, more); };
            Grid.SetColumn(more, 2);
            row.Children.Add(more);

            row.MouseLeftButtonUp += (_, _) =>
            {
                ProfilePopup.IsOpen = false;
                ActivateProfile(p, auto: false);
                if (!_config.Locked && _detectedId != null && _detectedId != p.Id)
                    ShowToast(T("PERFIL"), "Accent", T($"Perfil: {p.Name}"), T("Escolhido à mão. Vale até um jogo abrir ou fechar (ou trave com o cadeado)."));
            };
            ProfileList.Children.Add(row);
        }
    }

    void ProfileMenu(Profile p, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor };
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => { ProfilePopup.IsOpen = false; action(); };
            menu.Items.Add(mi);
            return mi;
        }
        Item("Duplicar", () => { var c = _config.Duplicate(p); _config.Save(); AppLog.Ok("PERFIL", $"Perfil duplicado: {c.Name}"); });
        Item(T("Renomear…"), () => Rename(p), !p.IsDesktop);
        Item(T("Exportar…"), () => Export(p));
        Item(T("Excluir"), () => Delete(p), !p.IsDesktop);
        menu.IsOpen = true;
    }

    void Rename(Profile p)
    {
        var name = InputDialog.Ask(this, T("Renomear perfil"), p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        AppLog.Ok("PERFIL", $"Perfil \"{p.Name}\" renomeado para \"{name.Trim()}\"");
        p.Name = name.Trim();
        _config.Save();
        UpdateProfileHeader();
    }

    void Delete(Profile p)
    {
        if (MessageBox.Show(this, T($"Excluir o perfil \"{p.Name}\"?"), T("G27 Hub"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var wasActive = p.Id == _config.ActiveId;
        _config.Remove(p);
        _config.Save();
        AppLog.Ok("PERFIL", $"Perfil excluído: {p.Name}");
        if (wasActive) ActivateProfile(_config.Desktop, auto: false);
    }

    void Export(Profile p)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"{string.Concat(p.Name.Split(Path.GetInvalidFileNameChars()))}.g27profile", Filter = T("Perfil do G27 Hub (*.g27profile)|*.g27profile") };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, AppConfig.ToJson(p));
        AppLog.Ok("PERFIL", $"Perfil \"{p.Name}\" exportado para {dlg.FileName}");
    }

    void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = T("Perfil do G27 Hub (*.g27profile)|*.g27profile") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var p = _config.Import(File.ReadAllText(dlg.FileName));
            _config.Save();
            AppLog.Ok("PERFIL", $"Perfil importado: {p.Name}");
            ShowToast(T("PERFIL"), "Ok", T("Perfil importado"), p.Name);
        }
        catch (Exception ex)
        {
            AppLog.Erro("PERFIL", $"Não consegui importar {Path.GetFileName(dlg.FileName)}", ex);
        }
    }

    void AddGame_Click(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = T("Jogo (*.exe)|*.exe"), Title = T("Escolha o .exe do jogo") };
        if (dlg.ShowDialog(this) != true) return;
        var info = FileVersionInfo.GetVersionInfo(dlg.FileName);
        var name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim() : Path.GetFileNameWithoutExtension(dlg.FileName);
        AddGameProfile(dlg.FileName, name);
    }

    ImageSource? ProfileIcon(Profile p)
    {
        if (p.ExePath is not { } path) return null;
        if (_icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    img = src;
                }
            }
        }
        catch { } // sem ícone: usa as iniciais
        return _icons[path] = img;
    }

    static string Initials(string name) =>
        new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(w => char.ToUpperInvariant(w[0])).ToArray()) is { Length: > 0 } s ? s : "?";

    // ================= menu ☰, configurações do G27, sobre =================

    void Menu_Click(object sender, RoutedEventArgs e) => MenuPopup.IsOpen = true;
    void MenuProfiles_Click(object sender, RoutedEventArgs e) { MenuPopup.IsOpen = false; Profile_Click(sender, e); }
    void MenuSettings_Click(object sender, RoutedEventArgs e) { MenuPopup.IsOpen = false; UpdateCompatUi(); AppSettingsPopup.IsOpen = true; }
    void MenuConsole_Click(object sender, RoutedEventArgs e) { MenuPopup.IsOpen = false; if (ConsolePanel.Visibility != Visibility.Visible) ToggleConsole(animate: true); }

    void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        MessageBox.Show(this,
            T($"G27 Hub {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\n\n") +
            T("Faz pelo Logitech G27 o que o LGS fazia, sem driver de kernel.\n\n") +
            T("Idealizado e desenvolvido por John Salim / Samosbourne, junto com IA (Claude, da Anthropic): o código foi escrito com inteligência artificial.\n\n") +
            T("Créditos:\n") +
            T("• gcampa/g27-ffb-driverless (MIT) — base da DLL de force feedback\n") +
            T("• SCS Software, SCS SDK (MIT) — plugin de telemetria do ETS2/ATS\n") +
            T("• vJoy (Shaul Eizikovich, njz3, BrunnerInnovation) e HidHide (Nefarius) — Modo Compatibilidade Total\n") +
            T("• HidSharp, Serilog\n") +
            T("• Protocolo do G27: fatos do hid-lg4ff (Linux)\n\n") +
            T("Logitech, G27 e G HUB são marcas da Logitech; este app não é da Logitech."),
            T("Sobre o G27 Hub"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void DeviceSettings_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // não abrir o volante ao clicar na engrenagem do card
        var d = _dev.Device;
        DeviceInfo.Text = d == null
            ? T("G27 não conectado.")
            : T($"{d.GetFriendlyName()}\nID 046D:{d.ProductID:X4} · ") + (_dev.IsNative ? T("modo nativo") : T("modo compatível")) + "\n" +
              T($"Versão do dispositivo: {d.ReleaseNumberBcd >> 8:X}.{d.ReleaseNumberBcd & 0xFF:X2}\nRotação: {_dev.Range}° · Perfil: {_config.Active.Name}");
        DevicePopup.IsOpen = true;
    }
}

/// <summary>Caixinha de texto escura (renomear perfil).</summary>
static class InputDialog
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var box = new TextBox { Text = initial, FontSize = 13, Padding = new Thickness(6), Background = Draw.Hex("#232B3A"), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderBrush = Draw.Hex("#00D2FF") };
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)owner.FindResource("AccentButton"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
        var cancel = new Button { Content = T("Cancelar"), IsCancel = true, Style = (Style)owner.FindResource("FlatButton"), MinWidth = 80 };
        var win = new Window
        {
            Title = title, Owner = owner, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Draw.Hex("#141923"), Foreground = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Children =
                {
                    new TextBlock { Text = title.ToUpperInvariant(), Style = (Style)owner.FindResource("SettingLabel"), Margin = new Thickness(0, 0, 0, 8) },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? box.Text : null;
    }
}
