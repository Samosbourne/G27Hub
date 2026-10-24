using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using G27Hub.Logging;

namespace G27Hub.Core;

/// <summary>
/// Painel no celular (Fase 10, desligado de fábrica): página web na rede de casa com RPM, marcha, velocidade e LEDs,
/// e controles de perfil, rotação e força. Servidor HTTP mínimo em TcpListener (o HttpListener precisaria de admin
/// para ouvir fora do localhost). Toda requisição precisa do código (?k=), que vai no QR code do app.
/// O Windows pergunta uma vez se libera o app no firewall.
/// </summary>
public sealed class PhonePanel : IDisposable
{
    public const int Port = 8027;

    readonly Func<string> _state;               // JSON do estado atual
    readonly Action<string, string> _command;   // (do, valor) → roda na thread da interface
    readonly byte[] _token;
    readonly CancellationTokenSource _cts = new();
    TcpListener? _listener;

    public PhonePanel(string token, Func<string> state, Action<string, string> command)
    {
        _token = Encoding.ASCII.GetBytes(token);
        _state = state;
        _command = command;
    }

    /// <summary>Código novo de 8 letras/números (sem 0/O/1/I para não confundir).</summary>
    public static string NewToken() => RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 8);

    /// <summary>Endereço IPv4 do PC na rede local (a placa com gateway), ou null sem rede.</summary>
    public static string? LanAddress() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();

    public static string Url(string token) => $"http://{LanAddress() ?? "localhost"}:{Port}/?k={token}";

    /// <summary>Começa a ouvir. Falha (porta ocupada) vira log + false.</summary>
    public bool Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            AppLog.Erro("APP", $"Painel no celular: porta {Port} ocupada ({ex.SocketErrorCode})");
            return false;
        }
        AppLog.Ok("APP", $"Painel no celular ligado em http://{LanAddress() ?? "?"}:{Port}");
        _ = AcceptLoop();
        return true;
    }

    async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception) when (_cts.IsCancellationRequested) { return; }
            catch (SocketException) { continue; }
            _ = Handle(client);
        }
    }

    async Task Handle(TcpClient client)
    {
        using var _ = client;
        var remote = client.Client.RemoteEndPoint;
        try
        {
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            var (method, path, query) = await ReadRequest(stream);
            if (method == null) return;

            if (!query.TryGetValue("k", out var k) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(k), _token))
            {
                AppLog.Aviso("APP", $"Painel no celular: acesso sem o código certo de {remote}");
                await Send(stream, "403 Forbidden", "text/plain; charset=utf-8", "Código errado. Leia o QR code de novo no G27 Hub.");
                return;
            }

            switch (method, path)
            {
                case ("GET", "/"):
                    await Send(stream, "200 OK", "text/html; charset=utf-8", Page);
                    break;
                case ("GET", "/events"):
                    await Stream(stream);
                    break;
                case ("POST", "/cmd") when query.TryGetValue("do", out var what):
                    query.TryGetValue("v", out var value);
                    AppLog.Info("APP", $"Painel no celular ({remote}): {what} {value}");
                    _command(what, value ?? "");
                    await Send(stream, "200 OK", "text/plain", "ok");
                    break;
                default:
                    await Send(stream, "404 Not Found", "text/plain", "");
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { } // celular saiu
        catch (Exception ex) { AppLog.Erro("APP", "Painel no celular: erro numa requisição", ex); }
    }

    /// <summary>Lê só a linha de pedido e os cabeçalhos (os comandos vão na URL, sem corpo).</summary>
    static async Task<(string? Method, string Path, Dictionary<string, string> Query)> ReadRequest(NetworkStream s)
    {
        var buf = new byte[4096];
        int len = 0;
        while (len < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(len));
            if (n == 0) return (null, "", []);
            len += n;
            if (buf.AsSpan(0, len).IndexOf("\r\n\r\n"u8) >= 0) break;
        }
        var first = Encoding.ASCII.GetString(buf, 0, len).Split("\r\n")[0].Split(' ');
        if (first.Length < 2) return (null, "", []);
        var target = first[1];
        var q = target.IndexOf('?');
        var query = new Dictionary<string, string>();
        if (q >= 0)
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) query[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
        return (first[0], q >= 0 ? target[..q] : target, query);
    }

    static async Task Send(NetworkStream s, string status, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head));
        await s.WriteAsync(bytes);
    }

    /// <summary>Server-Sent Events: o estado 15x por segundo até o celular fechar a página.</summary>
    async Task Stream(NetworkStream s)
    {
        await s.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-store\r\nConnection: keep-alive\r\n\r\n"u8.ToArray());
        string last = "";
        long lastSent = 0;
        while (!_cts.IsCancellationRequested)
        {
            var json = _state();
            // sem mudança, manda de novo só a cada 2 s (mantém a conexão viva)
            if (json != last || Environment.TickCount64 - lastSent > 2000)
            {
                await s.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), _cts.Token);
                last = json;
                lastSent = Environment.TickCount64;
            }
            await Task.Delay(66, _cts.Token);
        }
    }

    static readonly string Page = LoadPage();

    static string LoadPage()
    {
        using var s = typeof(PhonePanel).Assembly.GetManifestResourceStream("phone.html")!;
        return new StreamReader(s).ReadToEnd();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        AppLog.Info("APP", "Painel no celular desligado");
    }
}
