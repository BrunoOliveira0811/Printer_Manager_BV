using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

/// <summary>
/// Auditoria de acesso e alterações (RNF10, RN14). Cada registro guarda o hash do anterior,
/// de modo que qualquer adulteração direta no arquivo do banco é detectada por <see cref="Verify"/>.
/// Nunca há expurgo: a retenção é ilimitada (mínimo exigido: 12 meses).
/// </summary>
public static class AuditService
{
    private static readonly object Sync = new();

    /// <summary>Registra uma ação do usuário informado (padrão: usuário da sessão).</summary>
    public static void Write(string action, string entity, string? entityId, string details, User? actor = null) =>
        WriteCore(action, entity, entityId, details, actor ?? Session.CurrentUser);

    /// <summary>Registra uma rotina automática (agendador, backup, fechamento mensal) como "Sistema".</summary>
    public static void WriteSystem(string action, string entity, string? entityId, string details) =>
        WriteCore(action, entity, entityId, details, null);

    private static void WriteCore(string action, string entity, string? entityId, string details, User? actor)
    {
        try
        {
            lock (Sync)
            {
                using var db = AppDbContext.Create();
                var previousHash = db.AuditEntries.OrderByDescending(a => a.Id).Select(a => a.Hash).FirstOrDefault() ?? "";

                var now = DateTime.UtcNow;
                var entry = new AuditEntry
                {
                    // Truncado em milissegundos para que o hash seja reproduzível após a leitura do banco.
                    Timestamp = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
                    UserId = actor?.Id,
                    UserName = actor?.UserName ?? "Sistema",
                    Action = action,
                    Entity = entity,
                    EntityId = entityId,
                    Details = details,
                    PreviousHash = previousHash
                };
                entry.Hash = ComputeHash(entry);

                db.AuditEntries.Add(entry);
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            FileLog.Error($"Falha ao gravar auditoria: {action} {entity} {entityId}", ex);
            throw;
        }
    }

    /// <summary>Recalcula toda a cadeia. Retorna o Id do primeiro registro inconsistente, ou nulo.</summary>
    public static (bool Ok, long? BrokenId, int Count) Verify()
    {
        using var db = AppDbContext.Create();
        var previous = "";
        var count = 0;
        foreach (var entry in db.AuditEntries.AsNoTracking().OrderBy(a => a.Id))
        {
            count++;
            if (entry.PreviousHash != previous || entry.Hash != ComputeHash(entry))
                return (false, entry.Id, count);
            previous = entry.Hash;
        }
        return (true, null, count);
    }

    private static string ComputeHash(AuditEntry e)
    {
        var payload = string.Join('|',
            e.PreviousHash,
            e.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
            e.UserId?.ToString() ?? "",
            e.UserName,
            e.Action,
            e.Entity,
            e.EntityId ?? "",
            e.Details);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
