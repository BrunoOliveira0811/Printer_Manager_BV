using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PrinterManager.App.Services;

/// <summary>Backup diário automático (RNF08) e restauração (RNF15).</summary>
public static class BackupService
{
    private const string LastBackupKey = "LastBackupDate";
    private static readonly object Sync = new();

    public static void RunIfDue(AppSettingsModel settings)
    {
        if (!settings.BackupEnabled) return;
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (SettingsService.GetInternal(LastBackupKey) == today) return;

        try
        {
            BackupNow(settings, automatic: true);
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha no backup automatico", ex);
        }
    }

    public static string BackupNow(AppSettingsModel settings, bool automatic = false)
    {
        lock (Sync)
        {
            var directory = settings.EffectiveBackupDirectory;
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, $"gestao-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            // API de backup online do SQLite: cópia consistente mesmo com a varredura gravando.
            using (var source = new SqliteConnection($"Data Source={AppPaths.DatabasePath}"))
            using (var destination = new SqliteConnection($"Data Source={target};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            SettingsService.SetInternal(LastBackupKey, DateTime.Now.ToString("yyyy-MM-dd"));
            Prune(directory, settings.BackupRetentionDays);
            if (automatic) AuditService.WriteSystem("Backup automatico", "Banco de dados", null, target);
            else AuditService.Write("Backup manual", "Banco de dados", null, target);
            FileLog.Info($"Backup gerado: {target}");
            return target;
        }
    }

    /// <summary>Substitui o banco atual por um backup. O aplicativo deve ser reiniciado em seguida.</summary>
    public static void Restore(string backupFile)
    {
        Session.RequireAdmin();
        if (!IsSqliteFile(backupFile)) throw new InvalidOperationException("O arquivo selecionado não é um banco SQLite válido.");

        lock (Sync)
        {
            AuditService.Write("Restauracao iniciada", "Banco de dados", null, backupFile);

            // Guarda o banco atual antes de sobrescrever.
            var safety = Path.Combine(AppPaths.DefaultBackupDirectory, $"pre-restauracao-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            using (var source = new SqliteConnection($"Data Source={AppPaths.DatabasePath}"))
            using (var destination = new SqliteConnection($"Data Source={safety};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            SqliteConnection.ClearAllPools();
            using (var source = new SqliteConnection($"Data Source={backupFile};Mode=ReadOnly;Pooling=False"))
            using (var destination = new SqliteConnection($"Data Source={AppPaths.DatabasePath};Pooling=False"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            SqliteConnection.ClearAllPools();
        }

        AuditService.Write("Banco restaurado", "Banco de dados", null, $"Restaurado a partir de {backupFile}");
    }

    private static void Prune(string directory, int retentionDays)
    {
        var limit = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in Directory.GetFiles(directory, "gestao-*.db"))
        {
            try
            {
                if (File.GetCreationTime(file) < limit) File.Delete(file);
            }
            catch (Exception ex)
            {
                FileLog.Warn($"Nao foi possivel remover backup antigo {file}", ex);
            }
        }
    }

    private static bool IsSqliteFile(string path)
    {
        if (!File.Exists(path)) return false;
        var header = new byte[16];
        using var fs = File.OpenRead(path);
        return fs.Read(header, 0, 16) == 16 && Encoding.ASCII.GetString(header, 0, 15) == "SQLite format 3";
    }
}
