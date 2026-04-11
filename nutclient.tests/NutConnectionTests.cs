namespace NutClient.Tests;

public class NutConnectionTests : IAsyncLifetime
{
    private MockNutServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = new MockNutServer();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    private NutConnection CreateConnection() =>
        new(_server.Host, _server.Port, "testuser", "testpass");

    [Fact]
    public async Task ConnectAndAuthenticate_Success()
    {
        using var conn = CreateConnection();
        await conn.ConnectAsync();
        Assert.True(conn.IsConnected);
    }

    [Fact]
    public async Task GetVariable_ReturnsCorrectValue()
    {
        _server.Variables["ups.status"] = "OB LB";
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var status = await conn.GetVariableAsync("ups1", "ups.status");
        Assert.Equal("OB LB", status);
    }

    [Fact]
    public async Task GetVariable_UnknownVar_ThrowsProtocol()
    {
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(
            () => conn.GetVariableAsync("ups1", "nonexistent.var"));
        Assert.Equal(NutErrorKind.Protocol, ex.Kind);
    }

    [Fact]
    public async Task Authentication_Rejected_ThrowsAccessDenied()
    {
        _server.AuthErrorResponse = "ERR ACCESS-DENIED";
        using var conn = CreateConnection();

        var ex = await Assert.ThrowsAsync<NutException>(() => conn.ConnectAsync());
        Assert.Equal(NutErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public async Task Connection_Refused_ThrowsTransient()
    {
        // Use a separate server that we stop before connecting
        await using var deadServer = new MockNutServer();
        var port = deadServer.Port;
        await deadServer.DisposeAsync(); // stop it

        using var conn = new NutConnection("127.0.0.1", port, "test", "test");
        var ex = await Assert.ThrowsAsync<NutException>(() => conn.ConnectAsync());
        Assert.Equal(NutErrorKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task GetVariable_ErrDataStale_ThrowsTransient()
    {
        _server.GetVarErrorResponse = "ERR DATA-STALE";
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(
            () => conn.GetVariableAsync("ups1", "ups.status"));
        Assert.Equal(NutErrorKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task GetVariable_ErrAccessDenied_ThrowsAccessDenied()
    {
        _server.GetVarErrorResponse = "ERR ACCESS-DENIED";
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(
            () => conn.GetVariableAsync("ups1", "ups.status"));
        Assert.Equal(NutErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public async Task GetVariable_ServerClosesConnection_ThrowsTransient()
    {
        _server.DisconnectAfterAuth = true;
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(
            () => conn.GetVariableAsync("ups1", "ups.status"));
        Assert.Equal(NutErrorKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task Logout_Succeeds()
    {
        using var conn = CreateConnection();
        await conn.ConnectAsync();
        await conn.LogoutAsync(); // should not throw
    }

    // --- LoginAsync (persistent connection registration) ---

    [Fact]
    public async Task LoginAsync_Success_RegistersClient()
    {
        using var conn = CreateConnection();
        await conn.ConnectAsync();
        await conn.LoginAsync("ups1");

        // The mock server tracks IPs that successfully sent LOGIN
        Assert.Single(_server.RegisteredClients);
        Assert.Contains("127.0.0.1", _server.RegisteredClients);
    }

    [Fact]
    public async Task LoginAsync_DriverNotConnected_ThrowsTransient()
    {
        _server.LoginErrorResponse = "ERR DRIVER-NOT-CONNECTED";
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(() => conn.LoginAsync("ups1"));
        Assert.Equal(NutErrorKind.Transient, ex.Kind);
        Assert.Empty(_server.RegisteredClients);
    }

    [Fact]
    public async Task LoginAsync_AccessDenied_ThrowsAccessDenied()
    {
        _server.LoginErrorResponse = "ERR ACCESS-DENIED";
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(() => conn.LoginAsync("ups1"));
        Assert.Equal(NutErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public async Task LoginAsync_UnknownUps_ThrowsTransient()
    {
        // UNKNOWN-UPS during LOGIN is transient because the server may add the
        // UPS later (e.g., driver hasn't started yet).
        _server.LoginErrorResponse = "ERR UNKNOWN-UPS";
        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(() => conn.LoginAsync("ups1"));
        Assert.Equal(NutErrorKind.Transient, ex.Kind);
    }

    [Fact]
    public async Task PersistentConnection_LoginThenMultipleQueries_Works()
    {
        // Validates the v1.5.0 persistent-connection model: login once,
        // then run many GET VAR queries on the same connection.
        _server.Variables["ups.status"] = "OL";
        _server.Variables["battery.charge"] = "75";

        using var conn = CreateConnection();
        await conn.ConnectAsync();
        await conn.LoginAsync("ups1");

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("OL", await conn.GetVariableAsync("ups1", "ups.status"));
            Assert.Equal("75", await conn.GetVariableAsync("ups1", "battery.charge"));
        }

        // The login should still be tracked
        Assert.Single(_server.RegisteredClients);
    }

    [Fact]
    public async Task GetVariable_MultipleVars_WorkSequentially()
    {
        _server.Variables["ups.status"] = "OL";
        _server.Variables["battery.charge"] = "85";
        _server.Variables["battery.runtime"] = "1200";

        using var conn = CreateConnection();
        await conn.ConnectAsync();

        Assert.Equal("OL", await conn.GetVariableAsync("ups1", "ups.status"));
        Assert.Equal("85", await conn.GetVariableAsync("ups1", "battery.charge"));
        Assert.Equal("1200", await conn.GetVariableAsync("ups1", "battery.runtime"));
    }

    // --- Security tests: bounded line reader (F3) ---

    [Fact]
    public async Task OversizedLine_ThrowsTransient()
    {
        // Send 16 KB of 'A' bytes (no newline) on the GET VAR response.
        // The bounded reader caps at 8 KB so this must throw before OOM.
        _server.RawGetVarResponse = System.Text.Encoding.ASCII.GetBytes(new string('A', 16384));

        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var ex = await Assert.ThrowsAsync<NutException>(
            () => conn.GetVariableAsync("ups1", "ups.status"));
        Assert.Equal(NutErrorKind.Transient, ex.Kind);
        Assert.Contains("exceeded", ex.Message);
    }

    [Fact]
    public async Task LineExactlyAtLimit_Succeeds()
    {
        // Build a VAR response that totals 8192 bytes including the \n.
        // The data portion (8191 bytes) plus '\n' = 8192. The bounded reader's
        // buffer is 8192 bytes, so this is the max successful case.
        var prefix = "VAR ups1 ups.status \"";
        var suffix = "\"";
        var paddingLen = 8191 - prefix.Length - suffix.Length;
        var line = prefix + new string('X', paddingLen) + suffix + "\n";
        _server.RawGetVarResponse = System.Text.Encoding.ASCII.GetBytes(line);

        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var result = await conn.GetVariableAsync("ups1", "ups.status");
        Assert.Equal(paddingLen, result.Length);
    }

    [Fact]
    public async Task LineWithCRLF_HandlesCorrectly()
    {
        _server.RawGetVarResponse = System.Text.Encoding.ASCII.GetBytes("VAR ups1 ups.status \"OL\"\r\n");

        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var result = await conn.GetVariableAsync("ups1", "ups.status");
        Assert.Equal("OL", result);
    }

    [Fact]
    public async Task LineWithLFOnly_HandlesCorrectly()
    {
        _server.RawGetVarResponse = System.Text.Encoding.ASCII.GetBytes("VAR ups1 ups.status \"OL\"\n");

        using var conn = CreateConnection();
        await conn.ConnectAsync();

        var result = await conn.GetVariableAsync("ups1", "ups.status");
        Assert.Equal("OL", result);
    }
}
