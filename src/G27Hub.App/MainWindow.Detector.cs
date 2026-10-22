using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using G27Hub.App.Controls;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Fase 6: detector inteligente (opcional, desligado de fábrica) + base de compatibilidade.
/// Avisa ao abrir um jogo da base, pergunta 👍/👎 ao fechar, acha jogos instalados e preenche perfis pela base.
/// </summary>
public partial class MainWindow
{
    readonly CompatDb _db = CompatDb.LoadEmbedded();
    readonly UserCompat _userCompat = UserCompat.Load();
    readonly Dictionary<string, DateTime> _runningGames = []; // id da base → quando abriu

    static readonly TimeSpan MinSessionForQuestion = TimeSpan.FromMinutes(2);

    void DetectorCheck_Click(object sender, RoutedEventArgs e)
    {
        _config.DetectorOn = DetectorCheck.IsChecked == true;
        _config.Save();
        AppLog.Info("JOGO", _config.DetectorOn ? "Detector inteligente ligado" : "Detector inteligente desligado");
    }

    /// <summary>Chamado a cada varredura de processos (2 s), depois da troca de perfil.</summary>
    void DetectGames(string[] processNames)
    {
        var now = processNames.Select(_db.Find).OfType<CompatEntry>().DistinctBy(g => g.Id).ToDictionary(g => g.Id);

        foreach (var (id, started) in _runningGames.ToList())
        {
            if (now.ContainsKey(id)) continue;
            _runningGames.Remove(id);
            var game = _db.Games.First(g => g.Id == id);
            var played = DateTime.Now - started;
            AppLog.Info("JOGO", $"{game.Name} fechado após {played.TotalMinutes:0} min");
            if (_config.DetectorOn && played >= MinSessionForQuestion && !_userCompat.IsMuted(id)) AskHowItWent(game);
        }

        foreach (var (id, game) in now)
        {
            if (_runningGames.ContainsKey(id)) continue;
            _runningGames[id] = DateTime.Now;
            AppLog.Info("JOGO", $"{game.Name} iniciado ({game.Status}, {game.Confidence})");
            if (_config.DetectorOn && !_userCompat.IsMuted(id))
                RunPreflight(game, processNames.Any(n => n.Equals("steam", StringComparison.OrdinalIgnoreCase)));
        }

        // jogo que só existe nos perfis (fora da base): análise leve uma vez por sessão
        var profile = _config.MatchRunning(processNames);
        if (profile == null) _analyzed.Clear();
        else if (_config.DetectorOn && profile.Exes.All(x => _db.Find(x) == null) && _analyzed.Add(profile.Id))
            _ = AnalyzeUnknown(profile);
    }

    readonly HashSet<string> _analyzed = [];

    /// <summary>
    /// Jogo desconhecido: 10 s depois de abrir, se só carregou XInput (controle de Xbox) e nada de DirectInput,
    /// provavelmente não aceita volante. Só para jogos fora da base, que não têm marcação de anti-cheat.
    /// </summary>
    async Task AnalyzeUnknown(Profile p)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        var exe = CompatDb.WithoutExe(p.Exes.FirstOrDefault() ?? "");
        bool? xinputOnly = await Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetProcessesByName(exe).FirstOrDefault();
                return proc == null ? (bool?)null : Preflight.LooksXInputOnly(proc.Modules.Cast<ProcessModule>().Select(m => m.ModuleName));
            }
            catch (Exception ex)
            {
                AppLog.Info("JOGO", $"Não deu para analisar {p.Name} ({ex.Message})");
                return null;
            }
        });
        if (xinputOnly != true) { if (xinputOnly == false) AppLog.Info("JOGO", $"{p.Name}: carregou DirectInput/Windows.Gaming.Input, deve aceitar volante"); return; }
        AppLog.Aviso("JOGO", $"{p.Name} parece aceitar apenas controle de Xbox (só XInput carregado)");
        ShowToast(Initials(p.Name), "Warn", p.Name, T("Este jogo parece aceitar apenas controle de Xbox. O G27 pode não aparecer nos controles; o Modo Compatibilidade Total (Fase 8) é o caminho para esses jogos."));
    }

    void RunPreflight(CompatEntry game, bool steamRunning)
    {
        var ctx = new PreflightContext(_dev.IsConnected, _dev.IsNative, XboxControllers.Count(), steamRunning, W.Range,
            CompatModeOn: _vwheel != null, ControlsFixed: game.Checklist.Contains("corrigir-controles-beamng") && BeamNgFix.IsApplied());
        var advice = Preflight.Check(game, ctx, _userCompat.Games.GetValueOrDefault(game.Id));
        if (ctx.WheelConnected && !ctx.NativeMode) _dev.Reset(); // reaplica o modo nativo

        foreach (var a in advice)
            AppLog.Write(a.Kind switch { "Warn" => "AVISO", "Ok" => "OK", _ => "INFO" }, "JOGO", $"{game.Name}: {a.Body}");

        var kind = advice.Any(a => a.Kind == "Warn") ? "Warn" : advice.Any(a => a.Kind == "Accent" && a.Body.StartsWith("Este jogo")) ? "Accent" : "Ok";
        var body = string.Join("\n", advice.Select(a => "• " + a.Body));
        var actions = new List<(string, Action?)> { ("OK", null) };
        if (game.Checklist.Contains("corrigir-controles-beamng") && !BeamNgFix.IsApplied())
            actions.Insert(0, (T("Corrigir controles"), FixBeamNg));
        // só recomenda: trocar de modo é sempre um clique do usuário
        if (ctx.CompatModeOn && game.Status is "nativo" or "directinput")
            actions.Insert(0, (T("Voltar ao Modo Padrão"), () => _ = SetCompat(false)));
        else if (!ctx.CompatModeOn && game.Status == "precisa-compat-total")
            actions.Insert(0, (T("Ligar Compatibilidade Total"), () => _ = SetCompat(true)));
        actions.Add((T("Não avisar para este jogo"), () => Mute(game)));
        ShowToast(Initials(game.Name), kind, game.Name, body, TimeSpan.FromSeconds(20), actions.ToArray());
        if (!IsVisible || WindowState == WindowState.Minimized)
            (Application.Current as App)?.Balloon(game.Name, string.Join("\n", advice.Take(3).Select(a => a.Body)));
    }

    void FixBeamNg()
    {
        try
        {
            BeamNgFix.Apply();
            ShowToast("BNG", "Ok", T("BeamNG: controles corrigidos"), T("Acelerador, embreagem e marchas no layout do G27 sem LGS; o seu ajuste de force feedback foi mantido. Reinicie o BeamNG."));
        }
        catch (Exception ex) { AppLog.Erro("JOGO", "Não consegui corrigir os controles do BeamNG", ex); }
    }

    void Mute(CompatEntry game)
    {
        _userCompat.For(game.Id).Muted = true;
        _userCompat.Save();
        AppLog.Info("JOGO", $"Não avisar mais para {game.Name} (reverta apagando o jogo em compat-user.json)");
    }

    void AskHowItWent(CompatEntry game)
    {
        void Vote(bool good)
        {
            _userCompat.Vote(game.Id, good);
            _userCompat.Save();
            AppLog.Ok("JOGO", $"{game.Name}: {(good ? "👍 funcionou bem" : "👎 teve problema")} (guardado em compat-user.json)");
        }
        ShowToast(Initials(game.Name), "Accent", game.Name, T("O volante funcionou bem neste jogo?"), TimeSpan.FromSeconds(30),
            (T("👍 Funcionou"), () => Vote(true)),
            (T("👎 Teve problema"), () => Vote(false)),
            (T("Não perguntar"), () => Mute(game)));
    }

    // ================= perfil pré-preenchido pela base =================

    /// <summary>Cria o perfil de um jogo; se ele está na base, já vem com o nome, todos os .exe e a rotação sugerida.</summary>
    Profile AddGameProfile(string exePath, string fallbackName)
    {
        var entry = _db.Find(exePath);
        var p = _config.AddGame(exePath, entry?.Name ?? fallbackName);
        if (entry != null)
        {
            p.Exes = entry.Exes.ToList();
            p.UseOwnSettings = true;
            p.Wheel.Range = entry.SuggestedRange;
        }
        _config.Save();
        AppLog.Ok("PERFIL", $"Jogo adicionado: {p.Name} ({string.Join(", ", p.Exes)}){(entry != null ? $" — preenchido pela base: {entry.SuggestedRange}°" : "")}");
        ShowToast(Initials(p.Name), "Ok", T($"Perfil criado: {p.Name}"),
            entry != null
                ? T($"{StatusText(entry.Status)} · rotação sugerida {entry.SuggestedRange}° (já aplicada no perfil).\n{T(entry.Notes)}")
                : T("Ele entra sozinho quando o jogo abrir. Para ajustes próprios, marque \"Usar configurações especiais\" na aba Volante."));
        _detectedId = "?"; // se o jogo já estiver aberto, troca na próxima varredura
        return p;
    }

    static string StatusText(string status) => status switch
    {
        "nativo" => T("Compatível com o G27 (preset oficial)"),
        "directinput" => T("Aceita o G27 como volante (configure uma vez no jogo)"),
        "precisa-compat-total" => T("Precisa do Modo Compatibilidade Total"),
        "limitado" => T("Suporte a volante limitado"),
        _ => T("Compatibilidade desconhecida"),
    };

    // ================= jogos instalados =================

    async void FindInstalled_Click(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = false;
        AppLog.Info("JOGO", "Procurando jogos instalados (Steam, Epic, Xbox)…");
        var found = await Task.Run(() => GameScanner.Scan(_db));
        AppLog.Ok("JOGO", $"{found.Count} jogo(s) da base encontrados: {string.Join(", ", found.Select(f => f.Game.Name))}");
        ShowInstalledGames(found);
    }

    void ShowInstalledGames(List<InstalledGame> found)
    {
        var list = new StackPanel();
        var win = new Window
        {
            Title = T("Jogos encontrados"), Owner = this, Width = 560, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Draw.Hex("#141923"), Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"),
            Content = new DockPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = found.Count == 0 ? T("Nenhum jogo da base foi encontrado na Steam, Epic ou Xbox. Use \"+ Adicionar jogo\" para escolher o .exe.")
                            : T("Jogos da base de compatibilidade instalados neste PC. Criar o perfil já aplica a rotação sugerida."),
                        Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 0, 0, 14),
                    },
                    new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list },
                },
            },
        };
        DockPanel.SetDock(((DockPanel)win.Content).Children[0], Dock.Top);

        foreach (var g in found)
        {
            var exists = _config.Profiles.Any(p => p.Exes.Any(x => g.Game.Exes.Contains(x, StringComparer.OrdinalIgnoreCase)));
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = g.Game.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = T($"{g.Store} · {StatusText(g.Game.Status)} · {g.Game.SuggestedRange}°"), Style = (Style)FindResource("Hint") });
            row.Children.Add(info);
            var create = new Button { Style = (Style)FindResource(exists ? "FlatButton" : "AccentButton"), Content = exists ? T("Já tem perfil") : T("Criar perfil"), IsEnabled = !exists, VerticalAlignment = VerticalAlignment.Center };
            create.Click += (_, _) =>
            {
                AddGameProfile(g.ExePath, g.Game.Name);
                create.Content = T("Criado");
                create.IsEnabled = false;
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (g.Game.Checklist.Contains("corrigir-controles-beamng"))
            {
                var fix = new Button { Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 8, 0), Content = BeamNgFix.IsApplied() ? T("Desfazer correção") : T("Corrigir controles"),
                    ToolTip = T("O mapa do G27 do BeamNG foi feito para o LGS. Corrige acelerador, embreagem e marchas (com backup).") };
                fix.Click += (_, _) =>
                {
                    if (BeamNgFix.IsApplied()) BeamNgFix.Undo(); else FixBeamNg();
                    fix.Content = BeamNgFix.IsApplied() ? T("Desfazer correção") : T("Corrigir controles");
                };
                buttons.Children.Add(fix);
            }
            buttons.Children.Add(create);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            list.Children.Add(new Border { Background = Draw.Hex("#242424"), CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = row });
        }
        win.ShowDialog();
    }
}
