using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Fase 9: assistente da primeira abertura. Confere drivers antigos da Logitech, volante, modo nativo, force feedback,
/// Steam Input e Integridade de Memória; testa força e LEDs. Reabre pelo menu ☰ → Assistente de configuração.
/// </summary>
public partial class MainWindow
{
    bool _wizardRunning, _wizardDone;

    void MaybeShowWizard()
    {
        if (!_config.FirstRunDone) ShowWizard();
    }

    void MenuWizard_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
        ShowWizard();
    }

    void ShowWizard()
    {
        _wizardDone = false;
        WizardList.Children.Clear();
        WizardIntro.Visibility = Visibility.Visible;
        WizardNext.Content = T("Começar");
        WizardNext.IsEnabled = WizardSkip.IsEnabled = true;
        WizardSkip.Visibility = Visibility.Visible;
        WizardOverlay.Visibility = Visibility.Visible;
        AppLog.Info("APP", "Assistente de configuração aberto");
    }

    void WizardSkip_Click(object sender, RoutedEventArgs e) => CloseWizard("pulado");

    async void WizardNext_Click(object sender, RoutedEventArgs e)
    {
        if (_wizardDone) { CloseWizard("concluído"); return; } // o log fica em português
        if (_wizardRunning) return;
        _wizardRunning = true;
        WizardNext.IsEnabled = WizardSkip.IsEnabled = false;
        WizardIntro.Visibility = Visibility.Collapsed;
        try { await RunWizard(); }
        catch (Exception ex) { AppLog.Erro("APP", "Assistente de configuração", ex); WizardRow(false, "Algo deu errado", ex.Message); }
        _wizardRunning = false;
        _wizardDone = true;
        WizardNext.Content = T("Pronto");
        WizardNext.IsEnabled = true;
        WizardSkip.Visibility = Visibility.Collapsed;
    }

    void CloseWizard(string how)
    {
        WizardOverlay.Visibility = Visibility.Collapsed;
        _config.FirstRunDone = true;
        _config.Save();
        AppLog.Info("APP", $"Assistente de configuração {how}");
    }

    /// <summary>Uma linha do assistente: ✓ (true), ✗ (false), · (null = informação), e o texto pode ser trocado depois.</summary>
    TextBlock[] WizardRow(bool? ok, string name, string detail)
    {
        var icon = new TextBlock { Style = (Style)FindResource("Icon"), FontSize = 12, Width = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var title = new TextBlock { Text = name, FontSize = 13 };
        var info = new TextBlock { Text = detail, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 2, 0, 0) };
        SetWizardIcon(icon, ok);
        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(info);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        WizardList.Children.Add(row);
        return [icon, info];
    }

    void SetWizardIcon(TextBlock icon, bool? ok)
    {
        icon.Text = ok == true ? "" : ok == false ? "" : "";
        icon.Foreground = ok == true ? (Brush)FindResource("Ok") : ok == false ? (Brush)FindResource("Error") : (Brush)FindResource("TextFaint");
    }

    async Task RunWizard()
    {
        // 1. drivers antigos da Logitech (LGS / G HUB) brigam com o Modo Padrão
        var old = Diagnostics.OldLogitechDrivers();
        WizardRow(old.Length == 0, T("Drivers antigos da Logitech"),
            old.Length == 0 ? T("Nenhum encontrado.") : T($"Encontrados: {string.Join(", ", old)}. Desinstale o Logitech Gaming Software / G HUB em Configurações → Aplicativos e reinicie o PC."));

        // 2. volante conectado (espera até 15 s)
        var conn = WizardRow(null, T("Volante G27"), T("Procurando..."));
        var sw = Stopwatch.StartNew();
        while (!_dev.IsConnected && sw.ElapsedMilliseconds < 15000)
        {
            conn[1].Text = T($"Ligue o G27 na tomada e no USB... ({15 - sw.ElapsedMilliseconds / 1000} s)");
            await Task.Delay(500);
        }
        SetWizardIcon(conn[0], _dev.IsConnected);
        conn[1].Text = _dev.IsConnected ? T("Conectado.") : T("Não encontrado. Ligue o volante e rode o assistente de novo (menu ☰).");
        if (!_dev.IsConnected) return;

        // 3. modo nativo
        if (!_dev.IsNative) { _dev.Reset(); await Task.Delay(4000); }
        WizardRow(_dev.IsNative, T("Modo nativo"), _dev.IsNative
            ? T($"{_dev.Range}° de rotação, pedais separados e embreagem. O app reaplica sozinho a cada vez que o volante é ligado.")
            : T("O volante não entrou em modo nativo. Desligue e ligue o G27 e rode o assistente de novo."));

        // 3b. pedais no lugar esperado (G27 de colega do John veio com outro mapa, 23/10/2026)
        if (_dev.IsNative && _dev.State is { } st)
        {
            var why = MapSuspicion(st);
            WizardRow(why == null, T("Pedais e marchas"), why == null
                ? T("Pedais no lugar esperado. Se algo aparecer trocado no app, use CONFIGURAR VOLANTE na tela inicial.")
                : why + T(" Depois do assistente, clique CONFIGURAR VOLANTE na tela inicial."));
        }

        // 4. force feedback para os jogos (HKCU, sem admin, reversível na aba Força)
        bool ffb = FfbRegistration.IsRegistered() || FfbRegistration.Register();
        if (ffb) LoadSettingsIntoUi();
        WizardRow(ffb, T("Force feedback para os jogos"), ffb
            ? T("Ativado (32 e 64 bits). Desliga na aba Força, se precisar.")
            : T("Não consegui ativar. Veja o console (Ctrl+Shift+L)."));

        // 5. informações: Steam Input e Integridade de Memória
        WizardRow(null, T("Steam Input"), T("Se um jogo da Steam não enxergar o volante: Steam → propriedades do jogo → Controle → desative a Entrada Steam."));
        var hvci = Diagnostics.MemoryIntegrity();
        WizardRow(null, T("Integridade de Memória"), (hvci == true ? T("Ligada") : hvci == false ? T("Desligada") : T("Não deu para ler")) +
            T(". O Modo Padrão funciona com ela ligada ou desligada."));

        // 6. teste rápido de força e LEDs
        var force = WizardRow(null, T("Teste de força"), T("O volante vai girar um pouco para um lado e para o outro..."));
        var moved = await Pulse();
        SetWizardIcon(force[0], moved is { } m && Math.Abs(m) > 2);
        force[1].Text = moved is { } mv && Math.Abs(mv) > 2 ? T($"Funcionou: o volante girou {Math.Abs(mv):0}°.") : T("O volante não girou. Confira a fonte de energia do G27 (o motor precisa dela).");
        var leds = WizardRow(null, T("Teste dos LEDs"), T("Olhe os LEDs em cima do volante..."));
        await TestLeds();
        leds[1].Text = T("Os LEDs devem ter enchido de verde a vermelho. Na aba LEDs eles passam a acompanhar a rotação dos jogos.");
        AppLog.Ok("APP", "Assistente de configuração: verificações concluídas");
    }
}
