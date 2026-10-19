using System.Security.Cryptography;
using System.Text;

namespace PrinterManager.App.Services;

/// <summary>
/// Criptografa segredos (senhas SNMPv3, SMTP) com DPAPI no escopo da máquina,
/// para que qualquer usuário do Windows nesta estação consiga ler o mesmo banco.
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GestaoImpressoras.v1");

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(bytes);
    }

    public static string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            FileLog.Warn("Falha ao descriptografar segredo (banco copiado de outra maquina?).", ex);
            return null;
        }
    }
}
