using System.Text.Json;
using System.Text.Json.Serialization;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>Ajustes do volante e do force feedback de um perfil (abas Volante e Força).</summary>
public sealed class WheelSettings
{
    public int Range { get; set; } = 900;
    public int Autocenter { get; set; } = 20;
    public bool AutocenterOn { get; set; } = true;
    public int MasterGain { get; set; } = 100;
    public int MinForce { get; set; }
    /// <summary>Reforço das forças fracas (Fase 10): 0 = linear; sobe o começo da curva sem mudar os 100%.</summary>
    public int ForceCurve { get; set; }
    public int SpringGain { get; set; } = 100;
    public int DamperGain { get; set; } = 100;
    public int CenterSpring { get; set; } // 0 = força central desligada
    public bool Invert { get; set; }
    public bool AllowGame { get; set; } = true;

    // Compatibilidade Total (Fase 8b): curvas aplicadas no volante virtual; 50 = linear
    public int SteerSensitivity { get; set; } = 50;
    public int ThrottleSensitivity { get; set; } = 50;
    public int BrakeSensitivity { get; set; } = 50;
    public int ClutchSensitivity { get; set; } = 50;
    /// <summary>Vibração extra pela telemetria (Fase 10): desligada de fábrica; força de cada efeito em %.</summary>
    public bool VibOn { get; set; }
    public int VibEngine { get; set; } = 20;
    public int VibRoad { get; set; } = 50;
    public int VibShift { get; set; } = 40;
    public int VibAbs { get; set; } = 50;
    public int VibImpact { get; set; } = 60;

    public WheelSettings Clone() => (WheelSettings)MemberwiseClone();
}

public sealed class Profile
{
    public const string DesktopId = "desktop";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Nomes de executável que ativam o perfil (ex.: "ForzaHorizon5.exe").</summary>
    public List<string> Exes { get; set; } = [];
    /// <summary>Caminho do .exe escolhido, para o ícone.</summary>
    public string? ExePath { get; set; }
    /// <summary>"Usar configurações especiais do volante"; desligado = herda do Desktop.</summary>
    public bool UseOwnSettings { get; set; }
    public WheelSettings Wheel { get; set; } = new();

    // Fase 5b: atribuições (número do botão → ação; o direcional usa 101–104, ver ButtonEngine)
    public bool AssignmentsOn { get; set; } = true;
    public Dictionary<int, ButtonAction> Actions { get; set; } = [];
    /// <summary>Camada 2: valem enquanto o botão ShiftButton estiver apertado (0 = sem shift).</summary>
    public int ShiftButton { get; set; }
    public Dictionary<int, ButtonAction> ShiftActions { get; set; } = [];
    /// <summary>Embreagem, freio, acelerador.</summary>
    public List<PedalKey> Pedals { get; set; } = [new(), new(), new()];
    public SequentialShift Sequential { get; set; } = new();

    [JsonIgnore] public bool IsDesktop => Id == DesktopId;
}

/// <summary>
/// Configuração do app + perfis, em %APPDATA%\G27Hub\config.json.
/// O config.json da Fase 4 (Range/Autocenter soltos) vira o perfil Desktop na primeira leitura.
/// </summary>
public sealed class AppConfig
{
    public bool CloseToTray { get; set; } = true;
    public bool Notifications { get; set; } = true;
    /// <summary>Cadeado: trava o perfil ativo, sem troca automática.</summary>
    public bool Locked { get; set; }
    public string ActiveId { get; set; } = Profile.DesktopId;
    /// <summary>Segurar os dois vermelhos de cima do volante alterna a rotação entre RotationSteps.</summary>
    public bool RotationShortcut { get; set; } = true;
    /// <summary>Detector inteligente (Fase 6): avisos de compatibilidade ao abrir um jogo. Desligado de fábrica.</summary>
    public bool DetectorOn { get; set; }
    public int[] RotationSteps { get; set; } = [180, 270, 540, 900];
    public LedSettings Leds { get; set; } = new();
    /// <summary>Compatibilidade Total ligada (G27 escondido, jogos veem o vJoy). Desligada de fábrica; só o usuário liga.</summary>
    public bool CompatOn { get; set; }
    /// <summary>Assistente da primeira abertura já foi concluído ou pulado (Fase 9).</summary>
    public bool FirstRunDone { get; set; }
    /// <summary>Eixos e botões deste volante (assistente "Configurar volante"). Padrão = G27 do John.</summary>
    public InputMap Map { get; set; } = new();
    /// <summary>Idioma da interface: "pt" (padrão) ou "en".</summary>
    public string Language { get; set; } = "pt";
    /// <summary>Avisar quando houver versão nova no GitHub (Fase 10). Desligado de fábrica; procura no máximo 1x por dia.</summary>
    public bool UpdateCheckOn { get; set; }
    public DateTime LastUpdateCheck { get; set; }
    /// <summary>Painel no celular pela rede de casa (Fase 10). Desligado de fábrica; PhoneToken vai no QR code.</summary>
    public bool PhoneOn { get; set; }
    public string? PhoneToken { get; set; }
    public List<Profile> Profiles { get; set; } = [];

    // legado da Fase 4, só lido para migrar
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Range { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Autocenter { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? AutocenterOn { get; set; }

    [JsonIgnore] public string FilePath { get; private set; } = DefaultPath;

    public static readonly string DefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G27Hub", "config.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }; // acentos legíveis no .json

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        AppConfig cfg;
        try
        {
            cfg = File.Exists(path) ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex)
        {
            AppLog.Aviso("PERFIL", $"config.json inválido, começando do zero ({ex.Message})");
            cfg = new();
        }
        cfg.FilePath = path;

        if (!cfg.Profiles.Any(p => p.IsDesktop))
        {
            // Primeira vez com perfis: o que estava solto (Fase 4) e no registro da DLL vira o Desktop.
            var w = new WheelSettings
            {
                Range = cfg.Range ?? 900,
                Autocenter = cfg.Autocenter ?? 20,
                AutocenterOn = cfg.AutocenterOn ?? true,
                MasterGain = FfbRegistration.GetConfig("MasterGain", 100),
                MinForce = FfbRegistration.GetConfig("MinForce", 0),
                SpringGain = FfbRegistration.GetConfig("SpringGain", 100),
                DamperGain = FfbRegistration.GetConfig("DamperGain", 100),
                CenterSpring = FfbRegistration.GetConfig("CenterSpring", 0),
                Invert = FfbRegistration.GetConfig("Invert", 0) == 1,
                AllowGame = FfbRegistration.GetConfig("AllowGame", 1) == 1,
            };
            cfg.Profiles.Insert(0, new Profile { Id = Profile.DesktopId, Name = "Desktop: Padrão", UseOwnSettings = true, Wheel = w });
            cfg.Range = cfg.Autocenter = null;
            cfg.AutocenterOn = null;
            AppLog.Info("PERFIL", "Perfil \"Desktop: Padrão\" criado a partir das configurações atuais");
        }
        return cfg;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    [JsonIgnore] public Profile Desktop => Profiles.First(p => p.IsDesktop);
    [JsonIgnore] public Profile Active => Profiles.FirstOrDefault(p => p.Id == ActiveId) ?? Desktop;

    /// <summary>Ajustes que valem para o perfil (os próprios ou os do Desktop).</summary>
    public WheelSettings Effective(Profile p) => p.IsDesktop || p.UseOwnSettings ? p.Wheel : Desktop.Wheel;

    /// <summary>Primeiro perfil de jogo cujo .exe está rodando (nomes de processo sem ".exe", como o Windows dá).</summary>
    public Profile? MatchRunning(IEnumerable<string> processNames)
    {
        var running = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        return Profiles.FirstOrDefault(p => !p.IsDesktop && p.Exes.Any(e => running.Contains(CompatDb.WithoutExe(Path.GetFileName(e)))));
    }

    public Profile AddGame(string exePath, string name)
    {
        var p = new Profile { Name = name, ExePath = exePath, Exes = [Path.GetFileName(exePath)], Wheel = Desktop.Wheel.Clone() };
        Profiles.Add(p);
        return p;
    }

    public Profile Duplicate(Profile source)
    {
        var copy = FromJson(ToJson(source));
        copy.Name = source.Name + " (cópia)";
        copy.Exes = []; // dois perfis com o mesmo .exe brigariam na troca automática
        Profiles.Add(copy);
        return copy;
    }

    public bool Remove(Profile p)
    {
        if (p.IsDesktop) return false;
        Profiles.Remove(p);
        if (ActiveId == p.Id) ActiveId = Profile.DesktopId;
        return true;
    }

    /// <summary>Arquivo .g27profile: o perfil inteiro em JSON.</summary>
    public static string ToJson(Profile p) => JsonSerializer.Serialize(p, Json);

    /// <summary>Lê um .g27profile como perfil novo (id novo; nunca substitui o Desktop).</summary>
    public static Profile FromJson(string json)
    {
        var p = JsonSerializer.Deserialize<Profile>(json) ?? throw new InvalidDataException("arquivo de perfil vazio");
        p.Id = Guid.NewGuid().ToString("N");
        return p;
    }

    public Profile Import(string json)
    {
        var p = FromJson(json);
        if (p.Name.Length == 0) p.Name = "Perfil importado";
        Profiles.Add(p);
        return p;
    }
}
