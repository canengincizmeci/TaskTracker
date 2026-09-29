using TaskTracker.Bussiness.Concrete;

namespace TaskTracker.Tests;

public class IdentityNormalizerTests
{
    private readonly IdentityNormalizer _normalizer = new();

    [Fact]
    public void EmailIsTrimmedAndInvariantLowercased()
    {
        Assert.Equal("identity@example.test",
            _normalizer.NormalizeEmail("  Identity@EXAMPLE.TEST  "));
    }

    [Fact]
    public void UserNameKeepsDisplayCasingAndGetsASeparateLookupKey()
    {
        Assert.Equal("İpek", _normalizer.TrimUserName("  İpek  "));
        Assert.Equal("İpek".ToLowerInvariant(), _normalizer.NormalizeUserName("  İpek  "));
    }

    [Fact]
    public void NonAsciiInputIsSupported()
    {
        var normalized = _normalizer.NormalizeUserName("  Ångström用户  ");

        Assert.Equal("Ångström用户".ToLowerInvariant(), normalized);
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _normalizer.NormalizeEmail(null!));
        Assert.Throws<ArgumentNullException>(() => _normalizer.NormalizeUserName(null!));
        Assert.Throws<ArgumentNullException>(() => _normalizer.TrimUserName(null!));
    }
}
