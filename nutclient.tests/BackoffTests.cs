namespace NutClient.Tests;

public class BackoffTests
{
    private readonly UpsStateMachine _sm;

    public BackoffTests()
    {
        _sm = new UpsStateMachine(new MonitoringConfig { PollIntervalSeconds = 5 });
    }

    [Fact]
    public void NoFailures_ReturnsPollInterval()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), _sm.GetPollDelay());
    }

    [Theory]
    [InlineData(1, 10)]   // 5 * 2^1 = 10
    [InlineData(2, 20)]   // 5 * 2^2 = 20
    [InlineData(3, 40)]   // 5 * 2^3 = 40
    [InlineData(4, 60)]   // 5 * 2^4 = 80, capped at 60
    [InlineData(5, 60)]   // capped
    [InlineData(6, 60)]   // capped
    [InlineData(10, 60)]  // capped
    public void Backoff_CorrectValues(int failures, int expectedSeconds)
    {
        for (int i = 0; i < failures; i++)
            _sm.OnPollFailure("error");

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), _sm.GetPollDelay());
    }

    [Fact]
    public void Backoff_ResetsAfterSuccess()
    {
        _sm.OnPollFailure("error");
        _sm.OnPollFailure("error");
        Assert.Equal(TimeSpan.FromSeconds(20), _sm.GetPollDelay());

        _sm.OnPollSuccess();
        Assert.Equal(TimeSpan.FromSeconds(5), _sm.GetPollDelay());
    }
}
