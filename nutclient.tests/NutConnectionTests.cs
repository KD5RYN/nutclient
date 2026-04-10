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
}
