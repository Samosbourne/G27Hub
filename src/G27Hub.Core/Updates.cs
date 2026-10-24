using System.Text.Json;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>
/// Aviso de versão nova (Fase 10, desligado de fábrica): lê a última "Release" do GitHub. Só avisa e abre a página;
/// nunca baixa nem instala sozinho.
/// </summary>
public static class Updates
{
    public const string Repo = "Samosbourne/G27Hub";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";

    /// <summary>Versão publicada mais nova que <paramref name="current"/>, ou null (já atualizado, sem internet, sem release).</summary>
    public static async Task<(Version Version, string Url)?> CheckAsync(Version current)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("G27Hub"); // o GitHub recusa pedido sem User-Agent
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            var found = Parse(json, current);
            AppLog.Info("APP", found is { } f ? $"Versão nova disponível: {f.Version} (instalada {current.ToString(3)})"
                                              : $"Sem versão nova (instalada {current.ToString(3)})");
            return found;
        }
        catch (Exception ex)
        {
            AppLog.Aviso("APP", $"Não deu para procurar atualização: {ex.Message}");
            return null;
        }
    }

    /// <summary>Lê o JSON de releases/latest: "tag_name" v1.2.3 e "html_url".</summary>
    public static (Version Version, string Url)? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag) || !Version.TryParse(tag.GetString()?.TrimStart('v', 'V'), out var v)) return null;
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
        return Normal(v) > Normal(current) ? (Normal(v), url) : null;
    }

    // 1.0.0 do csproj vira 1.0.0.0 no assembly; compara sempre com 3 partes
    static Version Normal(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
