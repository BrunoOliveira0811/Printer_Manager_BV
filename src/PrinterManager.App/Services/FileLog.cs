using System.IO;
using System.Text;

namespace PrinterManager.App.Services;

/// <summary>Log técnico em arquivo (um por dia). Não substitui a auditoria.</summary>
public static class FileLog
{
    private static readonly object Sync = new();

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERRO", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppPaths.LogDirectory, $"gestao-{DateTime.Now:yyyyMMdd}.log");
            var line = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .Append(level).Append(' ').AppendLine(message);
            if (ex is not null) line.AppendLine(ex.ToString());

            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
