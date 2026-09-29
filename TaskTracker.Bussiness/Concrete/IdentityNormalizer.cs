using TaskTracker.Bussiness.Abstract;

namespace TaskTracker.Bussiness.Concrete;

public sealed class IdentityNormalizer : IIdentityNormalizer
{
    public string NormalizeEmail(string value) =>
        value?.Trim().ToLowerInvariant() ?? throw new ArgumentNullException(nameof(value));

    public string NormalizeUserName(string value) =>
        TrimUserName(value).ToLowerInvariant();

    public string TrimUserName(string value) =>
        value?.Trim() ?? throw new ArgumentNullException(nameof(value));
}
