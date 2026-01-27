using System.Text;

namespace CrudApp.Settings;

public class ApiSettings
{
    public const string SectionName = "ApiSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string PasswordKey { get; set; } = string.Empty;

    public byte[] GetSecretBytes()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException("ApiSettings:SecretKey is missing.");
        }

        return Encoding.UTF8.GetBytes(SecretKey);
    }

    public byte[] GetPasswordBytes()
    {
        if (string.IsNullOrWhiteSpace(PasswordKey))
        {
            throw new InvalidOperationException("ApiSettings:PasswordKey is missing.");
        }

        return Encoding.UTF8.GetBytes(PasswordKey);
    }
}