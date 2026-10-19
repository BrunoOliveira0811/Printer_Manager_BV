using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.Data;

public sealed class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<SupplyReading> SupplyReadings => Set<SupplyReading>();
    public DbSet<WearPart> WearParts => Set<WearPart>();
    public DbSet<CounterReading> CounterReadings => Set<CounterReading>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<PrinterEvent> PrinterEvents => Set<PrinterEvent>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<MonthlyFranchise> MonthlyFranchises => Set<MonthlyFranchise>();

    public static AppDbContext Create() => new();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={AppPaths.DatabasePath};Default Timeout=30");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite não guarda o Kind; marca tudo como UTC na leitura.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.UserName).IsUnique();

        b.Entity<Printer>().HasIndex(p => p.IpAddress);
        b.Entity<Printer>().HasMany(p => p.Supplies).WithOne().HasForeignKey(s => s.PrinterId);
        b.Entity<Printer>().HasMany(p => p.WearParts).WithOne().HasForeignKey(w => w.PrinterId);
        b.Entity<Printer>().Ignore(p => p.DisplayName);

        b.Entity<SupplyReading>().HasIndex(r => new { r.PrinterId, r.Timestamp });
        b.Entity<CounterReading>().HasIndex(r => new { r.PrinterId, r.Timestamp });
        b.Entity<PrinterEvent>().HasIndex(e => new { e.PrinterId, e.IsActive });
        b.Entity<Alert>().HasIndex(a => new { a.PrinterId, a.ResolvedAt });
        b.Entity<MaintenanceRecord>().HasIndex(m => new { m.PrinterId, m.PerformedAt });
        b.Entity<MonthlyFranchise>().HasIndex(f => new { f.PrinterId, f.Year, f.Month }).IsUnique();
        b.Entity<AuditEntry>().HasIndex(a => a.Timestamp);
        b.Entity<AppSetting>().HasKey(s => s.Key);
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
