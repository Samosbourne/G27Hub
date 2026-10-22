using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using static G27Hub.Core.Lang;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>Um jogo da base de compatibilidade (data/compat-db.json, seção 8 do plano).</summary>
public sealed class CompatEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("nome")] public string Name { get; set; } = "";
    [JsonPropertyName("exes")] public List<string> Exes { get; set; } = [];
    /// <summary>nativo, directinput, precisa-compat-total, limitado, desconhecido.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "desconhecido";
    [JsonPropertyName("modoRecomendado")] public string RecommendedMode { get; set; } = "padrao";
    /// <summary>oficial, comunidade, testado, a-confirmar.</summary>
    [JsonPropertyName("confianca")] public string Confidence { get; set; } = "a-confirmar";
    [JsonPropertyName("fonte")] public string Source { get; set; } = "";
    [JsonPropertyName("rotacaoSugerida")] public int SuggestedRange { get; set; } = 900;
    [JsonPropertyName("telemetria")] public string Telemetry { get; set; } = "";
    [JsonPropertyName("antiCheat")] public bool AntiCheat { get; set; }
    [JsonPropertyName("checklist")] public List<string> Checklist { get; set; } = [];
    [JsonPropertyName("notas")] public string Notes { get; set; } = "";
}

/// <summary>Base embutida no Core (EmbeddedResource "compat-db.json").</summary>
public sealed class CompatDb
{
    [JsonPropertyName("jogos")] public List<CompatEntry> Games { get; set; } = [];

    public static CompatDb LoadEmbedded()
    {
        using var s = typeof(CompatDb).Assembly.GetManifestResourceStream("compat-db.json")
            ?? throw new InvalidOperationException("compat-db.json não está embutido no G27Hub.Core");
        return JsonSerializer.Deserialize<CompatDb>(s) ?? new();
    }

    /// <summary>Jogo pelo nome do processo ("ForzaHorizon5") ou do arquivo ("ForzaHorizon5.exe").</summary>
    public CompatEntry? Find(string exeOrProcess)
    {
        var name = WithoutExe(Path.GetFileName(exeOrProcess));
        return Games.FirstOrDefault(g => g.Exes.Any(e => string.Equals(WithoutExe(e), name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Tira só ".exe": "BeamNG.drive.x64" é o nome do processo, não "BeamNG.drive" + extensão ".x64".</summary>
    public static string WithoutExe(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}

/// <summary>O que o usuário respondeu por jogo (%APPDATA%\G27Hub\compat-user.json): 👍/👎 e "não avisar".</summary>
public sealed class UserCompat
{
    public sealed class Result
    {
        public int Good { get; set; }
        public int Bad { get; set; }
        public bool Muted { get; set; }
        public DateTime? Last { get; set; }
    }

    public Dictionary<string, Result> Games { get; set; } = [];

    [JsonIgnore] public string FilePath { get; private set; } = DefaultPath;
    public static readonly string DefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G27Hub", "compat-user.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static UserCompat Load(string? path = null)
    {
        path ??= DefaultPath;
        UserCompat u;
        try { u = File.Exists(path) ? JsonSerializer.Deserialize<UserCompat>(File.ReadAllText(path)) ?? new() : new(); }
        catch (Exception ex)
        {
            AppLog.Aviso("JOGO", $"compat-user.json inválido, começando do zero ({ex.Message})");
            u = new();
        }
        u.FilePath = path;
        return u;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public Result For(string gameId) => Games.TryGetValue(gameId, out var r) ? r : Games[gameId] = new();
    public bool IsMuted(string gameId) => Games.TryGetValue(gameId, out var r) && r.Muted;

    public void Vote(string gameId, bool good)
    {
        var r = For(gameId);
        if (good) r.Good++; else r.Bad++;
        r.Last = DateTime.Now;
    }
}

/// <summary>Um aviso do detector (vira notificação).</summary>
public sealed record Advice(string Kind, string Title, string Body)
{
    /// <summary>"Ok", "Warn", "Accent" (recomendação/dica), como as cores das notificações do app.</summary>
    public static Advice Ok(string t, string b) => new("Ok", t, b);
    public static Advice Warn(string t, string b) => new("Warn", t, b);
    public static Advice Tip(string t, string b) => new("Accent", t, b);
}

/// <summary>Situação no momento em que o jogo abriu (o que o detector olha no pré-voo).</summary>
public sealed record PreflightContext(bool WheelConnected, bool NativeMode, int XboxControllers, bool SteamRunning, int AppRange, bool CompatModeOn = false, bool ControlsFixed = false);

/// <summary>Verificações de pré-voo da seção 7: status, modo, modo nativo, outros controles, Steam Input, steer lock.</summary>
public static class Preflight
{
    public static List<Advice> Check(CompatEntry game, PreflightContext ctx, UserCompat.Result? user = null)
    {
        var list = new List<Advice>();
        var t = game.Name;

        // 1–2. status na base × modo atual
        switch (game.Status)
        {
            case "nativo" or "directinput" when ctx.CompatModeOn:
                list.Add(Advice.Tip(t, T("Este jogo funciona melhor no Modo Padrão: no Compatibilidade Total ele vê \"vJoy Device\", não o G27. Recomendado: desligar o Compatibilidade Total.")));
                break;
            case "nativo":
                list.Add(Advice.Ok(t, T($"Compatível com o G27 no Modo Padrão ({ConfidenceText(game.Confidence)}).")));
                // descoberto no BeamNG (21/10/2026): presets do G27 feitos com o LGS, que reorganiza pedais e botões
                if (!game.Checklist.Contains("corrigir-controles-beamng"))
                    list.Add(Advice.Tip(t, T("Se pedais ou marchas vierem trocados: o preset do G27 deste jogo foi feito com o LGS, que muda a ordem dos pedais e botões. Remapeie uma vez nos controles do jogo.")));
                break;
            case "directinput":
                list.Add(Advice.Ok(t, T($"Aceita o G27 como volante DirectInput: configure os controles uma vez dentro do jogo ({ConfidenceText(game.Confidence)}).")));
                break;
            case "precisa-compat-total" when !ctx.CompatModeOn:
                list.Add(Advice.Tip(t, T("Este jogo não tem suporte nativo ao G27. Recomendado: ligar o Modo Compatibilidade Total (na primeira vez instala os drivers e pede para reiniciar o PC).")));
                break;
            case "limitado":
                list.Add(Advice.Warn(t, T("Suporte a volante limitado neste jogo. O force feedback pode não funcionar em nenhum modo.")));
                break;
        }
        if (user is { Bad: > 0 } && user.Bad >= user.Good)
            list.Add(Advice.Warn(t, T($"Da última vez você marcou 👎 neste jogo ({user.Bad}×). Veja as notas: {T(game.Notes)}")));

        // 3. modo nativo
        if (ctx.WheelConnected && !ctx.NativeMode)
            list.Add(Advice.Warn(t, T("O G27 não estava em modo nativo: o app reaplicou. Reinicie o jogo para ele enxergar os 900°.")));
        if (!ctx.WheelConnected)
            list.Add(Advice.Warn(t, T("O G27 não está conectado. Ligue o volante e reinicie o jogo.")));

        // 4–5. outros controles e Steam Input
        if (ctx.XboxControllers > 0 && game.Checklist.Contains("desconectar-outros-controles"))
            list.Add(Advice.Warn(t, (ctx.XboxControllers == 1 ? T("Um controle de Xbox está conectado.") : T($"{ctx.XboxControllers} controles de Xbox estão conectados.")) + T(" Desconecte para o jogo usar o G27 como Device 1.")));
        if (ctx.SteamRunning && game.Checklist.Contains("desligar-steam-input"))
            list.Add(Advice.Tip(t, T("Confira se o Steam Input está desligado para este jogo (Propriedades → Controle), senão a Steam pode \"roubar\" o volante.")));

        // dicas da base
        if (game.Checklist.Contains("steer-lock-igual") || game.SuggestedRange != ctx.AppRange)
            list.Add(Advice.Tip(t, game.SuggestedRange != ctx.AppRange
                ? T($"Sugerido para este jogo: {game.SuggestedRange}° (o app está em {ctx.AppRange}°). Ajuste a rotação/steer lock do jogo para bater com o app.")
                : T($"Deixe a rotação/steer lock do jogo em {ctx.AppRange}°, igual ao app.")));
        if (game.Checklist.Contains("sem-restos-ghub"))
            list.Add(Advice.Tip(t, T("Este jogo conflita com restos do G HUB/SDK da Logitech. Se o volante não responder, rode os testes rápidos do console (drivers antigos).")));
        if (game.Checklist.Contains("corrigir-controles-beamng") && !ctx.ControlsFixed)
            list.Add(Advice.Warn(t, T("O mapa do G27 que vem no BeamNG foi feito para o LGS: sem ele o acelerador vira embreagem e as marchas ficam erradas. Use CORRIGIR CONTROLES (o app ajusta o arquivo do BeamNG, com backup).")));
        if (game.Checklist.Contains("mola-centralizadora"))
            list.Add(Advice.Tip(t, T("O jogo tem mola de centragem própria: deixe a CENTRALIZAÇÃO DA FORÇA DA MOLA do app baixa neste perfil.")));
        return list;
    }

    static string ConfidenceText(string c) => c switch
    {
        "oficial" => T("lista oficial do jogo"),
        "testado" => T("testado"),
        "comunidade" => T("relatos da comunidade"),
        _ => T("a confirmar"),
    };

    /// <summary>
    /// Jogo desconhecido: se carregou só bibliotecas de controle de Xbox (XInput) e nenhuma de DirectInput,
    /// provavelmente não aceita volante. Nunca usar em jogo com anti-cheat.
    /// </summary>
    public static bool LooksXInputOnly(IEnumerable<string> modules)
    {
        var m = modules.Select(x => Path.GetFileName(x).ToLowerInvariant()).ToHashSet();
        var xinput = m.Any(x => x.StartsWith("xinput"));
        var dinput = m.Contains("dinput8.dll") || m.Contains("dinput.dll") || m.Contains("windows.gaming.input.dll");
        return xinput && !dinput;
    }
}
