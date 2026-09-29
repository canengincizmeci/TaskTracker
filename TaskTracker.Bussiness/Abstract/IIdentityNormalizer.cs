namespace TaskTracker.Bussiness.Abstract;

public interface IIdentityNormalizer
{
    string NormalizeEmail(string value);
    string NormalizeUserName(string value);
    string TrimUserName(string value);
}
