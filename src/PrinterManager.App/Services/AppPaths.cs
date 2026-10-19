using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PrinterManager.App.Services;

public static class AppPaths
{
    /// <summary>
    /// Dados compartilhados por todos os usuários do Windows nesta máquina.
    /// A variável GESTAO_IMPRESSORAS_DATA permite apontar para outra pasta (testes, servidor de arquivos).
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("GESTAO_IMPRESSORAS_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GestaoImpressoras");

    public static string DatabasePath => Path.Combine(DataDirectory, "gestao.db");
    public static string DefaultBackupDirectory => Path.Combine(DataDirectory, "Backups");
    public static string LogDirectory => Path.Combine(DataDirectory, "Logs");

    public static void EnsureCreated()
    {
        var isNew = !Directory.Exists(DataDirectory);
        Directory.CreateDirectory(DataDirectory);
        if (isNew) GrantUsersModify(DataDirectory);
        Directory.CreateDirectory(DefaultBackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>
    /// Em %ProgramData% os arquivos criados por um usuário ficam somente leitura para os demais;
    /// concede "Modificar" ao grupo Usuários para que todos os perfis do Windows usem o mesmo banco.
    /// O instalador (elevado) faz o mesmo; aqui cobre a execução sem instalador.
    /// </summary>
    public static void GrantUsersModify(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            var acl = info.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        catch (Exception ex)
        {
            FileLog.Warn($"Nao foi possivel ajustar permissoes de {directory}", ex);
        }
    }
}
