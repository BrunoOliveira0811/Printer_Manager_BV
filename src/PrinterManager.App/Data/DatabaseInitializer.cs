using Microsoft.EntityFrameworkCore;

namespace PrinterManager.App.Data;

public static class DatabaseInitializer
{
    public static void Initialize()
    {
        using var db = AppDbContext.Create();
        db.Database.EnsureCreated();

        // WAL: leituras da interface não bloqueiam a gravação da varredura em segundo plano.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

        // RNF10: registros de auditoria imutáveis.
        db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER IF NOT EXISTS trg_audit_no_update
            BEFORE UPDATE ON AuditEntries
            BEGIN SELECT RAISE(ABORT, 'Registros de auditoria sao imutaveis'); END;
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER IF NOT EXISTS trg_audit_no_delete
            BEFORE DELETE ON AuditEntries
            BEGIN SELECT RAISE(ABORT, 'Registros de auditoria sao imutaveis'); END;
            """);
    }
}
