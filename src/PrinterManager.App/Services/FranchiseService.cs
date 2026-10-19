using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

/// <summary>
/// RN15: no primeiro dia útil de cada mês o período anterior é fechado e a contagem
/// acumulada do novo mês recomeça do contador atual. Dias úteis = segunda a sexta
/// (feriados não são considerados).
/// </summary>
public static class FranchiseService
{
    public static DateTime FirstBusinessDay(int year, int month)
    {
        var day = new DateTime(year, month, 1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) day = day.AddDays(1);
        return day;
    }

    public static void CloseDueMonths()
    {
        var today = DateTime.Now.Date;
        if (today < FirstBusinessDay(today.Year, today.Month)) return;

        var period = today.AddMonths(-1);
        int year = period.Year, month = period.Month;

        using var db = AppDbContext.Create();
        var printers = db.Printers.AsNoTracking().Where(p => !p.IsDeleted && p.TotalPages != null).ToList();
        var closed = 0;

        foreach (var printer in printers)
        {
            if (db.MonthlyFranchises.Any(f => f.PrinterId == printer.Id && f.Year == year && f.Month == month)) continue;

            var start = GetPeriodStart(db, printer.Id, year, month);
            if (start is null) continue;

            db.MonthlyFranchises.Add(new MonthlyFranchise
            {
                PrinterId = printer.Id,
                Year = year,
                Month = month,
                StartTotal = start.Value.Total,
                StartMono = start.Value.Mono,
                StartColor = start.Value.Color,
                EndTotal = printer.TotalPages!.Value,
                EndMono = printer.MonoPages,
                EndColor = printer.ColorPages,
                FranchisePages = printer.MonthlyFranchisePages,
                ClosedAt = DateTime.UtcNow
            });
            closed++;
        }

        if (closed == 0) return;
        db.SaveChanges();
        AuditService.WriteSystem("Franquia mensal fechada", "Franquia", $"{year}-{month:00}",
            $"{closed} impressora(s) fechadas; contagem do novo mes reiniciada");
    }

    /// <summary>Início do período: fim do mês anterior fechado ou a leitura mais antiga disponível.</summary>
    public static (long Total, long? Mono, long? Color)? GetPeriodStart(AppDbContext db, int printerId, int year, int month)
    {
        var previous = db.MonthlyFranchises.AsNoTracking()
            .Where(f => f.PrinterId == printerId && (f.Year < year || (f.Year == year && f.Month < month)))
            .OrderByDescending(f => f.Year).ThenByDescending(f => f.Month)
            .FirstOrDefault();
        if (previous is not null) return (previous.EndTotal, previous.EndMono, previous.EndColor);

        var monthStartUtc = new DateTime(year, month, 1).ToUniversalTime();
        var reading = db.CounterReadings.AsNoTracking()
                          .Where(c => c.PrinterId == printerId && c.Timestamp <= monthStartUtc)
                          .OrderByDescending(c => c.Timestamp).FirstOrDefault()
                      ?? db.CounterReadings.AsNoTracking()
                          .Where(c => c.PrinterId == printerId)
                          .OrderBy(c => c.Timestamp).FirstOrDefault();
        return reading is null ? null : (reading.Total, reading.Mono, reading.Color);
    }

    /// <summary>Período ainda aberto (mês corrente) até o contador atual.</summary>
    public static (int Year, int Month) CurrentOpenPeriod()
    {
        var today = DateTime.Now.Date;
        var period = today < FirstBusinessDay(today.Year, today.Month) ? today.AddMonths(-1) : today;
        return (period.Year, period.Month);
    }
}
