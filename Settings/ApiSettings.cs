using System.Text;

namespace CrudApp.Settings;

public class ApiSettings
{
    private static readonly string SecretKey = "6ceccd7405ef4b00b2630009be568cfa";
    private static readonly string PasswordKey = "6ceccd7405ef4b00b2630009be568cfa";
    internal static byte[] GenerateSecretByte() => Encoding.ASCII.GetBytes(SecretKey);
    internal static byte[] GeneratePasswordByte() =>  Encoding.ASCII.GetBytes(PasswordKey);
}