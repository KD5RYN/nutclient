using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NutClient.Tests;

/// <summary>
/// A minimal TCP server that speaks the NUT text protocol.
/// Tests can configure responses before connecting a NutConnection.
/// </summary>
public class MockNutServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public string UpsName { get; set; } = "ups1";
    public Dictionary<string, string> Variables { get; } = new()
    {
        ["ups.status"] = "OL",
        ["battery.charge"] = "100",
        ["battery.runtime"] = "3600",
        ["input.voltage"] = "120.0",
        ["ups.load"] = "35",
    };

    /// <summary>Set to return this string instead of OK on USERNAME/PASSWORD.</summary>
    public string? AuthErrorResponse { get; set; }

    /// <summary>Set to return this string instead of a VAR response on GET VAR.</summary>
    public string? GetVarErrorResponse { get; set; }

    /// <summary>If true, close connection immediately after auth succeeds.</summary>
    public bool DisconnectAfterAuth { get; set; }

    /// <summary>
    /// If set, the server uses the normal auth handshake but then, on the
    /// FIRST GET VAR request, sends these raw bytes instead of a normal response.
    /// Used by tests that need to simulate oversized lines, missing newlines,
    /// or specific byte sequences.
    /// </summary>
    public byte[]? RawGetVarResponse { get; set; }

    /// <summary>If set, returns this string instead of OK on LOGIN.</summary>
    public string? LoginErrorResponse { get; set; }

    /// <summary>Tracks IPs that have sent LOGIN. Cleared on dispose.</summary>
    public List<string> RegisteredClients { get; } = new();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string Host => "127.0.0.1";

    public MockNutServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (line.StartsWith("USERNAME "))
                {
                    await writer.WriteLineAsync(AuthErrorResponse ?? "OK");
                    if (AuthErrorResponse != null) break;
                }
                else if (line.StartsWith("PASSWORD "))
                {
                    await writer.WriteLineAsync(AuthErrorResponse ?? "OK");
                    if (AuthErrorResponse != null) break;
                    if (DisconnectAfterAuth)
                    {
                        client.Close();
                        return;
                    }
                }
                else if (line.StartsWith("LOGIN "))
                {
                    if (LoginErrorResponse != null)
                    {
                        await writer.WriteLineAsync(LoginErrorResponse);
                    }
                    else
                    {
                        var clientEp = (System.Net.IPEndPoint?)client.Client.RemoteEndPoint;
                        if (clientEp != null)
                        {
                            lock (RegisteredClients)
                                RegisteredClients.Add(clientEp.Address.ToString());
                        }
                        await writer.WriteLineAsync("OK");
                    }
                }
                else if (line.StartsWith("GET VAR "))
                {
                    if (RawGetVarResponse != null)
                    {
                        // Bypass the StreamWriter and send raw bytes directly so
                        // tests can craft oversized lines, missing newlines, etc.
                        await writer.FlushAsync();
                        await stream.WriteAsync(RawGetVarResponse);
                        await stream.FlushAsync();
                    }
                    else if (GetVarErrorResponse != null)
                    {
                        await writer.WriteLineAsync(GetVarErrorResponse);
                    }
                    else
                    {
                        // Parse: GET VAR <ups> <variable>
                        var parts = line.Split(' ', 4);
                        if (parts.Length == 4)
                        {
                            var varName = parts[3];
                            if (Variables.TryGetValue(varName, out var value))
                                await writer.WriteLineAsync($"VAR {parts[2]} {varName} \"{value}\"");
                            else
                                await writer.WriteLineAsync($"ERR VAR-NOT-SUPPORTED");
                        }
                        else
                        {
                            await writer.WriteLineAsync("ERR INVALID-ARGUMENT");
                        }
                    }
                }
                else if (line == "LOGOUT")
                {
                    await writer.WriteLineAsync("OK Goodbye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("ERR UNKNOWN-COMMAND");
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _listener.Stop();
        try { await _acceptLoop; } catch { }
        _cts.Dispose();
    }
}
