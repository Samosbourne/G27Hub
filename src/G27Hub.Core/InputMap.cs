using System.Text.Json.Serialization;

namespace G27Hub.Core;

/// <summary>
/// Qual eixo HID e qual número de botão é cada controle do G27. O padrão é o G27 nativo do John (confirmado no volante
/// em 21/10/2026); o volante de um colega do John mostrou a embreagem e as marchas em outros lugares (23/10/2026),
/// então o assistente "Configurar volante" aprende o mapa de cada volante (<see cref="InputMapLearner"/>).
/// Gravado em AppConfig.Map.
/// </summary>
public sealed class InputMap
{
    public string Steer { get; set; } = "X";
    public string Throttle { get; set; } = "Z";
    public string Brake { get; set; } = "Rz";
    public string Clutch { get; set; } = "Y";
    public int[] Gears { get; set; } = [13, 14, 15, 16, 17, 18];
    public int Reverse { get; set; } = 23;
    public int PaddleLeft { get; set; } = 6;
    public int PaddleRight { get; set; } = 5;
    public int[] WheelLeft { get; set; } = [8, 21, 22];  // lateral esquerda, de cima para baixo
    public int[] WheelRight { get; set; } = [7, 19, 20]; // lateral direita, de cima para baixo
    public int[] ShifterRed { get; set; } = [12, 9, 10, 11]; // da esquerda para a direita
    public int[] ShifterBlack { get; set; } = [1, 2, 3, 4];  // losango: baixo, esquerda, direita, cima
    /// <summary>
    /// Pedais cujo valor SOBE ao pisar. No G27 do John todos descem (255 solto → 0 no fundo); no do colega a embreagem
    /// (Slider) fica em 0 solta (23/10/2026).
    /// </summary>
    public List<string> Rising { get; set; } = [];

    public bool RisesWhenPressed(string axis) => Rising.Contains(axis);

    [JsonIgnore] public bool IsDefault => Describe() == new InputMap().Describe();

    public InputMap Clone() => new()
    {
        Steer = Steer, Throttle = Throttle, Brake = Brake, Clutch = Clutch, Gears = [.. Gears], Reverse = Reverse,
        PaddleLeft = PaddleLeft, PaddleRight = PaddleRight, WheelLeft = [.. WheelLeft], WheelRight = [.. WheelRight],
        ShifterRed = [.. ShifterRed], ShifterBlack = [.. ShifterBlack], Rising = [.. Rising],
    };

    /// <summary>Uma linha para o log.</summary>
    public string Describe() =>
        $"volante {Steer}, acelerador {Throttle}, freio {Brake}, embreagem {Clutch}; marchas {string.Join(",", Gears)}, ré {Reverse}; " +
        $"borboletas E{PaddleLeft} D{PaddleRight}; volante E {string.Join(",", WheelLeft)} D {string.Join(",", WheelRight)}; " +
        $"câmbio vermelhos {string.Join(",", ShifterRed)} pretos {string.Join(",", ShifterBlack)}" +
        (Rising.Count > 0 ? $"; sobem ao pisar: {string.Join(",", Rising)}" : "");
}

/// <summary>
/// Passo a passo do "Configurar volante": em cada passo o usuário mexe um controle e o primeiro eixo/botão que muda
/// (e ainda não foi usado) vira aquele controle. Sem WPF: testável.
/// </summary>
public sealed class InputMapLearner
{
    public enum Kind { Axis, Button }

    /// <summary>Passos na ordem: id interno, texto (em português; a tela traduz), eixo ou botão.</summary>
    public static readonly (string Id, string Text, Kind Kind)[] Steps =
    [
        ("steerL", "Gire o volante TODO para a ESQUERDA, até travar", Kind.Axis),
        ("steerR", "Agora gire TODO para a DIREITA, até travar", Kind.Axis),
        ("throttle", "Pise no ACELERADOR até o fundo e solte", Kind.Axis),
        ("brake", "Pise no FREIO até o fundo e solte", Kind.Axis),
        ("clutch", "Pise na EMBREAGEM até o fundo e solte", Kind.Axis),
        ("gear1", "Engate a 1ª marcha", Kind.Button), ("gear2", "Engate a 2ª marcha", Kind.Button),
        ("gear3", "Engate a 3ª marcha", Kind.Button), ("gear4", "Engate a 4ª marcha", Kind.Button),
        ("gear5", "Engate a 5ª marcha", Kind.Button), ("gear6", "Engate a 6ª marcha", Kind.Button),
        ("reverse", "Engate a ré (empurre a alavanca para baixo)", Kind.Button),
        ("paddleL", "Puxe a borboleta ESQUERDA (atrás do volante)", Kind.Button),
        ("paddleR", "Puxe a borboleta DIREITA", Kind.Button),
        ("wheelL0", "Aperte o botão vermelho da ESQUERDA do volante, o de CIMA", Kind.Button),
        ("wheelL1", "Aperte o botão vermelho da ESQUERDA do volante, o do MEIO", Kind.Button),
        ("wheelL2", "Aperte o botão vermelho da ESQUERDA do volante, o de BAIXO", Kind.Button),
        ("wheelR0", "Aperte o botão vermelho da DIREITA do volante, o de CIMA", Kind.Button),
        ("wheelR1", "Aperte o botão vermelho da DIREITA do volante, o do MEIO", Kind.Button),
        ("wheelR2", "Aperte o botão vermelho da DIREITA do volante, o de BAIXO", Kind.Button),
        ("red0", "Na base do câmbio: aperte o 1º botão vermelho (da esquerda)", Kind.Button),
        ("red1", "Na base do câmbio: aperte o 2º botão vermelho", Kind.Button),
        ("red2", "Na base do câmbio: aperte o 3º botão vermelho", Kind.Button),
        ("red3", "Na base do câmbio: aperte o 4º botão vermelho (da direita)", Kind.Button),
        ("black0", "Na base do câmbio: aperte o botão preto de BAIXO (do losango)", Kind.Button),
        ("black1", "Na base do câmbio: aperte o botão preto da ESQUERDA", Kind.Button),
        ("black2", "Na base do câmbio: aperte o botão preto da DIREITA", Kind.Button),
        ("black3", "Na base do câmbio: aperte o botão preto de CIMA", Kind.Button),
    ];

    const double AxisMoved = 0.5;   // fração da faixa do eixo que conta como "pisou/girou"
    const double SteerEnd = 0.05;   // volante: chegou a 5% da ponta = travou (o G27 manda a faixa toda até o limite de rotação)
    const double BackToRest = 0.15; // pedal voltou para perto de onde estava

    public InputMap Map { get; }
    public int Step { get; private set; }
    public bool Done => Step >= Steps.Length;

    readonly HashSet<string> _axesUsed = [];
    readonly HashSet<int> _buttonsUsed = [];
    Dictionary<string, AxisValue> _baseAxes = [];
    HashSet<int> _baseButtons = [];
    readonly Dictionary<string, (int Min, int Max)> _seen = [];
    string? _candidate;
    bool _steerLow; // em que ponta o volante travou no passo "steerL"

    public InputMapLearner(InputMap start) => Map = start.Clone();

    /// <summary>Estado de partida do passo atual (chamar ao começar e depois de cada passo).</summary>
    public void Begin(G27State s)
    {
        _baseAxes = new(s.Axes);
        _baseButtons = [.. s.Buttons];
        _seen.Clear();
        _candidate = null;
    }

    /// <summary>Novo estado do volante. True quando o passo atual terminou (aí <see cref="Step"/> já avançou).</summary>
    public bool Feed(G27State s)
    {
        if (Done) return false;
        var (id, _, kind) = Steps[Step];
        if (kind == Kind.Button)
        {
            var fresh = s.Buttons.FirstOrDefault(b => !_baseButtons.Contains(b) && !_buttonsUsed.Contains(b));
            if (fresh == 0) { _baseButtons.IntersectWith(s.Buttons); return false; } // soltou algo: pode apertar de novo
            Assign(id, fresh);
            _buttonsUsed.Add(fresh);
            return Next(s);
        }

        foreach (var (name, a) in s.Axes)
        {
            if (_axesUsed.Contains(name) || a.Max <= a.Min) continue;
            var (min, max) = _seen.TryGetValue(name, out var m) ? m : (a.Value, a.Value);
            _seen[name] = (Math.Min(min, a.Value), Math.Max(max, a.Value));
        }
        if (id == "steerL")
        {
            // o primeiro eixo livre que vai de onde estava até uma das pontas
            var hit = s.Axes.FirstOrDefault(p => !_axesUsed.Contains(p.Key) && p.Value.Max > p.Value.Min && AtEnd(p.Value) != null
                && _baseAxes.TryGetValue(p.Key, out var b) && Math.Abs(p.Value.Normalized - b.Normalized) >= 0.3);
            if (hit.Key == null) return false;
            _steerLow = AtEnd(hit.Value) == true;
            Assign(id, hit.Key);
            return Next(s);
        }
        if (id == "steerR")
        {
            // o mesmo eixo na ponta oposta (pulou o anterior: aceita qualquer ponta do eixo do mapa)
            if (!s.Axes.TryGetValue(Map.Steer, out var v) || AtEnd(v) is not { } low || (_steerKnown && low == _steerLow)) return false;
            _axesUsed.Add(Map.Steer);
            return Next(s);
        }

        _candidate ??= _seen.Where(p => Span(p.Key, p.Value) >= AxisMoved).OrderByDescending(p => Span(p.Key, p.Value)).Select(p => p.Key).FirstOrDefault();
        if (_candidate == null) return false;
        // pedal: espera soltar, para o próximo passo não começar com ele pisado
        if (_baseAxes.TryGetValue(_candidate, out var rest) && s.Axes.TryGetValue(_candidate, out var now)
            && Math.Abs(now.Normalized - rest.Normalized) > BackToRest) return false;
        Assign(id, _candidate);
        _axesUsed.Add(_candidate);
        Map.Rising.Remove(_candidate);
        if (rest is { Normalized: < 0.5 }) Map.Rising.Add(_candidate); // solto embaixo: o valor sobe ao pisar
        return Next(s);
    }

    bool _steerKnown; // o passo "steerL" achou a ponta (não foi pulado)

    static bool? AtEnd(AxisValue a) => a.Normalized <= SteerEnd ? true : a.Normalized >= 1 - SteerEnd ? false : null;

    /// <summary>Pula o passo atual: mantém o que o mapa já tinha.</summary>
    public void Skip(G27State s)
    {
        var (id, _, kind) = Steps[Step];
        if (id == "steerL") { Next(s); return; } // o eixo só fica "usado" depois do passo da direita
        if (kind == Kind.Axis) _axesUsed.Add(Current(id) as string ?? "");
        else if (Current(id) is int b) _buttonsUsed.Add(b);
        Next(s);
    }

    /// <summary>Movimento do passo atual até agora, 0..1 (para a barra da tela). Volante: quanto falta para travar.</summary>
    public double Progress(G27State s)
    {
        if (Done) return 0;
        var id = Steps[Step].Id;
        if (id is "steerL" or "steerR")
            return s.Axes.TryGetValue(Map.Steer, out var a)
                ? Math.Clamp(Math.Abs(a.Normalized - 0.5) / (0.5 - SteerEnd), 0, 1) : 0;
        return _seen.Count == 0 ? 0 : _seen.Max(p => Span(p.Key, p.Value));
    }

    double Span(string axis, (int Min, int Max) v) =>
        _baseAxes.TryGetValue(axis, out var a) && a.Max > a.Min ? (double)(v.Max - v.Min) / (a.Max - a.Min) : 0;

    bool Next(G27State s)
    {
        Step++;
        Begin(s);
        return true;
    }

    object? Current(string id) => id switch
    {
        "steerL" or "steerR" => Map.Steer, "throttle" => Map.Throttle, "brake" => Map.Brake, "clutch" => Map.Clutch,
        "reverse" => Map.Reverse, "paddleL" => Map.PaddleLeft, "paddleR" => Map.PaddleRight,
        _ when id.StartsWith("gear") => Map.Gears[id[4] - '1'],
        _ when id.StartsWith("wheelL") => Map.WheelLeft[id[6] - '0'],
        _ when id.StartsWith("wheelR") => Map.WheelRight[id[6] - '0'],
        _ when id.StartsWith("red") => Map.ShifterRed[id[3] - '0'],
        _ when id.StartsWith("black") => Map.ShifterBlack[id[5] - '0'],
        _ => null,
    };

    void Assign(string id, object v)
    {
        switch (id)
        {
            case "steerL": Map.Steer = (string)v; _steerKnown = true; break;
            case "throttle": Map.Throttle = (string)v; break;
            case "brake": Map.Brake = (string)v; break;
            case "clutch": Map.Clutch = (string)v; break;
            case "reverse": Map.Reverse = (int)v; break;
            case "paddleL": Map.PaddleLeft = (int)v; break;
            case "paddleR": Map.PaddleRight = (int)v; break;
            case var g when g.StartsWith("gear"): Map.Gears[g[4] - '1'] = (int)v; break;
            case var w when w.StartsWith("wheelL"): Map.WheelLeft[w[6] - '0'] = (int)v; break;
            case var w when w.StartsWith("wheelR"): Map.WheelRight[w[6] - '0'] = (int)v; break;
            case var r when r.StartsWith("red"): Map.ShifterRed[r[3] - '0'] = (int)v; break;
            case var b when b.StartsWith("black"): Map.ShifterBlack[b[5] - '0'] = (int)v; break;
        }
    }
}
