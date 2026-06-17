using TimeTrack.Core.Security;

namespace TimeTrack.Core.Tests;

public class PasswordVerifierTests
{
    [Fact]
    public void Verify_AcceptsCorrectPassword()
    {
        var stored = PasswordVerifier.Hash("S3cret!pass");
        Assert.True(PasswordVerifier.Verify("S3cret!pass", stored));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var stored = PasswordVerifier.Hash("S3cret!pass");
        Assert.False(PasswordVerifier.Verify("s3cret!pass", stored)); // case-sensitive
        Assert.False(PasswordVerifier.Verify("wrong", stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-verifier")]
    [InlineData("100000.onlytwo")]
    [InlineData("abc.def.ghi")] // non-numeric iterations
    public void Verify_RejectsMissingOrMalformedVerifier(string? stored)
    {
        Assert.False(PasswordVerifier.Verify("anything", stored));
    }

    [Fact]
    public void Hash_UsesRandomSalt_SoSamePasswordProducesDifferentVerifiers()
    {
        var a = PasswordVerifier.Hash("same");
        var b = PasswordVerifier.Hash("same");
        Assert.NotEqual(a, b);
        Assert.True(PasswordVerifier.Verify("same", a));
        Assert.True(PasswordVerifier.Verify("same", b));
    }

    [Fact]
    public void Verify_RejectsTamperedHash()
    {
        var stored = PasswordVerifier.Hash("pw");
        var parts = stored.Split('.');
        // flip a character in the hash segment
        var tamperedHash = (parts[2][0] == 'A' ? 'B' : 'A') + parts[2].Substring(1);
        var tampered = $"{parts[0]}.{parts[1]}.{tamperedHash}";
        Assert.False(PasswordVerifier.Verify("pw", tampered));
    }
}
