using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace G27Hub.Core;

/// <summary>
/// Idioma da interface (Fase 9, pedido do John): o texto em português é a chave; data/lang-en.json traz o inglês.
/// T("texto") e T($"texto {x}") (o $ vira o modelo "texto {0}"). Sem tradução, mostra o português e anota em Missing.
/// O log técnico (AppLog) continua em português.
/// </summary>
public static class Lang
{
    public static readonly string[] Supported = ["pt", "en"];
    public static string Current { get; private set; } = "pt";
    public static bool English => Current == "en";

    static Dictionary<string, string> _en = [];
    /// <summary>Textos pedidos em inglês sem tradução (para o teste de cobertura).</summary>
    public static readonly HashSet<string> Missing = [];

    public static CultureInfo Culture => English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("pt-BR");

    public static void Set(string lang)
    {
        Current = lang == "en" ? "en" : "pt";
        if (English && _en.Count == 0) _en = LoadEmbedded();
    }

    public static Dictionary<string, string> LoadEmbedded()
    {
        using var s = typeof(Lang).Assembly.GetManifestResourceStream("lang-en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
    }

    public static string T(string pt)
    {
        if (!English || string.IsNullOrEmpty(pt) || !pt.Any(char.IsLetter)) return pt; // "{0:0}%" etc.: nada a traduzir
        if (_en.TryGetValue(pt, out var en)) return en;
        if (pt.Length > 3) lock (Missing) Missing.Add(pt); // "Q", "F5": nomes de tecla iguais nos dois idiomas
        return pt;
    }

    public static string T([InterpolatedStringHandlerArgument] ref Template t) =>
        string.Format(Culture, T(t.Format), t.Args);

    /// <summary>Junta T($"…{x}…") como "…{0}…" + argumentos, para traduzir a frase inteira.</summary>
    [InterpolatedStringHandler]
    public ref struct Template
    {
        readonly StringBuilder _sb;
        readonly List<object?> _args;

        public Template(int literalLength, int formattedCount)
        {
            _sb = new StringBuilder(literalLength + formattedCount * 4);
            _args = new List<object?>(formattedCount);
        }

        public readonly string Format => _sb.ToString();
        public readonly object?[] Args => [.. _args];

        public readonly void AppendLiteral(string s) => _sb.Append(s.Replace("{", "{{").Replace("}", "}}"));
        public readonly void AppendFormatted<TValue>(TValue v) => Hole(v, null);
        public readonly void AppendFormatted<TValue>(TValue v, string? format) => Hole(v, format);

        readonly void Hole(object? v, string? format)
        {
            _sb.Append('{').Append(_args.Count);
            if (format != null) _sb.Append(':').Append(format);
            _sb.Append('}');
            _args.Add(v);
        }
    }
}
