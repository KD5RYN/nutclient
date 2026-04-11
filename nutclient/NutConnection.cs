using System.Net.Sockets;
using System.Text;

namespace NutClient;

/// <summary>
/// Classifies NUT connection/protocol errors so the monitor can decide
/// whether to retry, back off, or stop entirely.
/// </summary>
public enum NutErrorKind
{
    Transient,   // Network timeout, connection refused, DNS failure — retry with backoff
    AccessDenied, // Bad credentials — don't retry until config changes
    Protocol,    // Unexpected response from server — retry, may be transient
}

public class NutException : Exception
{
    public NutErrorKind Kind { get; }

    public NutException(string message, NutErrorKind kind, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

/// <summary>
/// Handles TCP communication with a NUT (Network UPS Tools) server.
/// Protocol: simple line-based text over TCP on port 3493.
/// </summary>
public class NutConnection : IDisposable
{
    // SECURITY (F3): cap NUT response lines to prevent a malicious or MITM'd
    // server from streaming gigabytes without a newline (memory DoS / poll stall).
    // Real NUT lines are well under 100 bytes; 8 KB is generous headroom.
    private const int MaxLineBytes = 8192;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamWriter? _writer;

    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly int _connectTimeoutMs;

    public bool IsConnected => _client?.Connected ?? false;

    public NutConnection(string host, int port, string username, string password, int connectTimeoutMs = 5000)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _connectTimeoutMs = connectTimeoutMs;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Disconnect();

        try
        {
            _client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_connectTimeoutMs);

            await _client.ConnectAsync(_host, _port, cts.Token);

            _stream = _client.GetStream();
            _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Disconnect();
            throw new NutException(
                $"Connection to {_host}:{_port} timed out after {_connectTimeoutMs}ms",
                NutErrorKind.Transient);
        }
        catch (SocketException ex)
        {
            Disconnect();
            throw new NutException(
                $"Connection to {_host}:{_port} failed: {ex.Message}",
                NutErrorKind.Transient, ex);
        }

        // Authenticate
        await SendCommandAsync($"USERNAME {_username}");
        var resp = await ReadResponseAsync(ct);
        if (!resp.StartsWith("OK"))
            throw new NutException($"USERNAME rejected: {resp}", NutErrorKind.AccessDenied);

        await SendCommandAsync($"PASSWORD {_password}");
        resp = await ReadResponseAsync(ct);
        if (!resp.StartsWith("OK"))
            throw new NutException($"PASSWORD rejected: {resp}", NutErrorKind.AccessDenied);
    }

    /// <summary>
    /// Register this client as an active monitor of the named UPS by sending
    /// the NUT LOGIN command. This makes the client visible in `LIST CLIENT`
    /// and `NUMLOGINS` queries on the server, and is the standard NUT mechanism
    /// for tracking who's monitoring what. Call this once after ConnectAsync
    /// for the persistent-connection lifecycle.
    /// </summary>
    public async Task LoginAsync(string upsName, CancellationToken ct = default)
    {
        await SendCommandAsync($"LOGIN {upsName}");
        var resp = await ReadResponseAsync(ct);

        if (resp.StartsWith("OK"))
            return;

        if (resp.StartsWith("ERR"))
        {
            var errCode = resp.Length > 4 ? resp.Substring(4).Trim() : resp;

            // Classify the LOGIN response. ACCESS-DENIED is fatal (terminal),
            // DRIVER-NOT-CONNECTED / DATA-STALE / UNKNOWN-UPS are transient
            // (the server may not have the UPS ready yet but might soon).
            var kind = errCode switch
            {
                "ACCESS-DENIED" => NutErrorKind.AccessDenied,
                "UNKNOWN-UPS" => NutErrorKind.Transient,
                "DRIVER-NOT-CONNECTED" => NutErrorKind.Transient,
                "DATA-STALE" => NutErrorKind.Transient,
                _ => NutErrorKind.Protocol,
            };

            throw new NutException($"LOGIN rejected: {errCode}", kind);
        }

        throw new NutException(
            $"Unexpected response to LOGIN: {resp}",
            NutErrorKind.Protocol);
    }

    public async Task<string> GetVariableAsync(string upsName, string variable, CancellationToken ct = default)
    {
        await SendCommandAsync($"GET VAR {upsName} {variable}");
        var resp = await ReadResponseAsync(ct);

        // Response format: VAR <ups> <variable> "<value>"
        if (resp.StartsWith("VAR"))
        {
            var firstQuote = resp.IndexOf('"');
            var lastQuote = resp.LastIndexOf('"');
            if (firstQuote >= 0 && lastQuote > firstQuote)
                return resp.Substring(firstQuote + 1, lastQuote - firstQuote - 1);

            throw new NutException(
                $"Malformed VAR response (no quoted value): {resp}",
                NutErrorKind.Protocol);
        }

        if (resp.StartsWith("ERR"))
        {
            var errCode = resp.Length > 4 ? resp.Substring(4).Trim() : resp;

            // Classify known NUT error codes
            var kind = errCode switch
            {
                "ACCESS-DENIED" => NutErrorKind.AccessDenied,
                "UNKNOWN-UPS" => NutErrorKind.Protocol,
                "VAR-NOT-SUPPORTED" => NutErrorKind.Protocol,
                "DRIVER-NOT-CONNECTED" => NutErrorKind.Transient,
                "DATA-STALE" => NutErrorKind.Transient,
                _ => NutErrorKind.Protocol,
            };

            throw new NutException($"NUT error: {errCode}", kind);
        }

        throw new NutException(
            $"Unexpected response: {resp}",
            NutErrorKind.Protocol);
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (_writer != null)
                await SendCommandAsync("LOGOUT");
        }
        catch { }
    }

    public void Disconnect()
    {
        _writer?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _writer = null;
        _stream = null;
        _client = null;
    }

    public void Dispose() => Disconnect();

    private async Task SendCommandAsync(string command)
    {
        if (_writer == null) throw new InvalidOperationException("Not connected");
        try
        {
            await _writer.WriteLineAsync(command);
        }
        catch (IOException ex)
        {
            throw new NutException(
                $"Send failed: {ex.Message}", NutErrorKind.Transient, ex);
        }
    }

    /// <summary>
    /// Reads a single line from the NUT server with a hard cap on length.
    /// SECURITY: enforces MaxLineBytes (8 KB) to prevent a malicious or MITM'd
    /// server from streaming gigabytes without a newline. Real NUT lines are
    /// well under 100 bytes.
    /// </summary>
    private async Task<string> ReadResponseAsync(CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);

            var buffer = new byte[MaxLineBytes];
            int pos = 0;
            var oneByte = new byte[1];

            while (pos < MaxLineBytes)
            {
                int read = await _stream.ReadAsync(oneByte.AsMemory(0, 1), cts.Token);
                if (read == 0)
                    throw new NutException("Connection closed by server", NutErrorKind.Transient);

                if (oneByte[0] == (byte)'\n')
                {
                    // Trim trailing \r if present (handle both LF and CRLF)
                    var len = (pos > 0 && buffer[pos - 1] == (byte)'\r') ? pos - 1 : pos;
                    return Encoding.ASCII.GetString(buffer, 0, len);
                }

                buffer[pos++] = oneByte[0];
            }

            throw new NutException(
                $"Response line exceeded {MaxLineBytes} bytes — possible DoS attempt",
                NutErrorKind.Transient);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new NutException(
                "Read timed out (5s)", NutErrorKind.Transient);
        }
        catch (IOException ex)
        {
            throw new NutException(
                $"Read failed: {ex.Message}", NutErrorKind.Transient, ex);
        }
    }
}
