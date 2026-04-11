namespace NutClient.Tests;

/// <summary>
/// Tests for the NUT status sanitization that prevents argument injection
/// from a malicious or MITM'd NUT server (security finding F1).
/// </summary>
public class SanitizeUpsStatusTests
{
    [Theory]
    [InlineData("OL", "OL")]
    [InlineData("OB", "OB")]
    [InlineData("OL CHRG", "OL CHRG")]
    [InlineData("OB DISCHRG", "OB DISCHRG")]
    [InlineData("OB LB", "OB LB")]
    [InlineData("OB LB DISCHRG", "OB LB DISCHRG")]
    [InlineData("FSD", "FSD")]
    [InlineData("OL CHRG ALARM", "OL CHRG ALARM")]
    public void NormalStatuses_PassThroughUnchanged(string input, string expected)
    {
        Assert.Equal(expected, NutMonitorService.SanitizeUpsStatus(input));
    }

    [Fact]
    public void EmptyString_ReturnsUnknown()
    {
        Assert.Equal("UNKNOWN", NutMonitorService.SanitizeUpsStatus(""));
    }

    [Fact]
    public void Whitespace_ReturnsUnknown()
    {
        Assert.Equal("UNKNOWN", NutMonitorService.SanitizeUpsStatus("   "));
    }

    [Fact]
    public void Null_ReturnsUnknown()
    {
        Assert.Equal("UNKNOWN", NutMonitorService.SanitizeUpsStatus(null));
    }

    [Fact]
    public void UnknownTokens_AreDropped()
    {
        // bogus and malicious are not in the whitelist; OL and OB survive
        Assert.Equal("OL OB", NutMonitorService.SanitizeUpsStatus("OL bogus OB malicious"));
    }

    [Fact]
    public void OnlyUnknownTokens_ReturnsUnknown()
    {
        Assert.Equal("UNKNOWN", NutMonitorService.SanitizeUpsStatus("foo bar baz"));
    }

    // --- Security tests: argument injection attempts ---

    [Fact]
    public void QuoteBreakoutAttempt_IsNeutralized()
    {
        // Classic attempt to escape the surrounding quotes and inject extra args
        var malicious = "OB LB\" extra-arg \"";
        var result = NutMonitorService.SanitizeUpsStatus(malicious);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("extra-arg", result);
    }

    [Fact]
    public void ShellMetacharacters_AreDropped()
    {
        // "OB;" is not a valid flag (the semicolon makes it not match "OB").
        // None of the other tokens are valid either, so the whole thing is dropped.
        // This is the safer behavior — strict whitelist, no attempt to be clever
        // about extracting "OB" from "OB;".
        var malicious = "OB; rm -rf /";
        var result = NutMonitorService.SanitizeUpsStatus(malicious);
        Assert.Equal("UNKNOWN", result);
        Assert.DoesNotContain(";", result);
        Assert.DoesNotContain("rm", result);
    }

    [Fact]
    public void BackticksAndDollarSigns_AreDropped()
    {
        var malicious = "OL `whoami` $(id)";
        var result = NutMonitorService.SanitizeUpsStatus(malicious);
        Assert.Equal("OL", result);
        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("$", result);
    }

    [Fact]
    public void NewlinesAndCRLF_AreNotPreserved()
    {
        var malicious = "OB\nINJECT\r\n";
        var result = NutMonitorService.SanitizeUpsStatus(malicious);
        // Newlines aren't whitespace splitters in our split, so the whole token
        // becomes "OB\nINJECT\r\n" which isn't a known flag → dropped → UNKNOWN.
        // Verify no newline survives.
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("\r", result);
    }

    [Fact]
    public void TabSeparated_StillSplits()
    {
        var input = "OL\tCHRG";
        Assert.Equal("OL CHRG", NutMonitorService.SanitizeUpsStatus(input));
    }

    [Fact]
    public void MixedValidAndInjection_KeepsOnlyValid()
    {
        var malicious = "OB LB && curl evil.example.com";
        var result = NutMonitorService.SanitizeUpsStatus(malicious);
        Assert.Equal("OB LB", result);
    }

    [Fact]
    public void CaseSensitive_LowercaseIsDropped()
    {
        // NUT flags are uppercase. Lowercase should not be accepted as a flag.
        Assert.Equal("UNKNOWN", NutMonitorService.SanitizeUpsStatus("ol ob lb"));
    }
}
