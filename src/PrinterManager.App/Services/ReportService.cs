using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PrinterManager.App.Services;

public enum ReportKind
{
    [System.ComponentModel.Description("Consumo de suprimentos")] SupplyConsumption,
    [System.ComponentModel.Description("Contadores de páginas")]  PageCounters,
    [System.ComponentModel.Description("Franquia mensal (MPS)")]  MonthlyFranchise,
    [System.ComponentModel.Description("Histórico de manutenções")] Maintenance
}

/// <summary>Relatórios exportáveis em CSV e PDF (RF09). O histórico de impressoras excluídas é mantido (RN09).</summary>
public static class ReportService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DataTable Build(ReportKind kind, DateTime fromLocal, DateTime toLocal)
    {
        var fromUtc = fromLocal.Date.ToUniversalTime();
        var toUtc = toLocal.Date.AddDays(1).ToUniversalTime();
        var table = kind switch
        {
            ReportKind.SupplyConsumption => SupplyConsumption(fromUtc, toUtc),
            ReportKind.PageCounters      => PageCounters(fromUtc, toUtc),
            ReportKind.MonthlyFranchise  => Franchise(fromLocal, toLocal),
            ReportKind.Maintenance       => Maintenance(fromUtc, toUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        table.TableName = $"{kind.GetDescription()} — {fromLocal:dd/MM/yyyy} a {toLocal:dd/MM/yyyy}";
        return table;
    }

    private static DataTable SupplyConsumption(DateTime fromUtc, DateTime toUtc)
    {
        var table = NewTable("Impressora", "IP", "Setor", "Suprimento", "Cor", "Nível inicial (%)", "Nível final (%)", "Consumo (p.p.)", "Trocas detectadas");
        using var db = AppDbContext.Create();
        var printers = db.Printers.AsNoTracking().ToDictionary(p => p.Id);

        var readings = db.SupplyReadings.AsNoTracking()
            .Where(r => r.Timestamp >= fromUtc && r.Timestamp < toUtc && (r.Kind == SupplyKind.Toner || r.Kind == SupplyKind.Tinta))
            .OrderBy(r => r.Timestamp)
            .ToList();

        foreach (var group in readings.GroupBy(r => (r.PrinterId, r.Description)))
        {
            // Nível imediatamente anterior ao período, para não perder o consumo da primeira leitura.
            var before = db.SupplyReadings.AsNoTracking()
                .Where(r => r.PrinterId == group.Key.PrinterId && r.Description == group.Key.Description && r.Timestamp < fromUtc)
                .OrderByDescending(r => r.Timestamp).Select(r => (double?)r.Percent).FirstOrDefault();

            var values = (before is null ? [] : new List<double> { before.Value }).Concat(group.Select(r => r.Percent)).ToList();
            double consumption = 0;
            var replacements = 0;
            for (var i = 1; i < values.Count; i++)
            {
                var delta = values[i] - values[i - 1];
                if (delta < 0) consumption -= delta;
                else if (delta >= 30) replacements++;
            }

            var printer = printers.GetValueOrDefault(group.Key.PrinterId);
            table.Rows.Add(printer?.DisplayName, printer?.IpAddress, printer?.Sector, group.Key.Description,
                group.First().Color.GetDescription(), Fmt(values.First()), Fmt(values.Last()), Fmt(consumption), replacements);
        }
        return Sort(table, "Impressora");
    }

    private static DataTable PageCounters(DateTime fromUtc, DateTime toUtc)
    {
        var table = NewTable("Impressora", "IP", "Setor", "Modelo", "Contador inicial", "Contador final", "Páginas (total)", "Páginas mono", "Páginas cor");
        using var db = AppDbContext.Create();

        foreach (var printer in db.Printers.AsNoTracking().OrderBy(p => p.Name).ToList())
        {
            var start = db.CounterReadings.AsNoTracking()
                            .Where(c => c.PrinterId == printer.Id && c.Timestamp < fromUtc)
                            .OrderByDescending(c => c.Timestamp).FirstOrDefault()
                        ?? db.CounterReadings.AsNoTracking()
                            .Where(c => c.PrinterId == printer.Id && c.Timestamp >= fromUtc && c.Timestamp < toUtc)
                            .OrderBy(c => c.Timestamp).FirstOrDefault();
            var end = db.CounterReadings.AsNoTracking()
                .Where(c => c.PrinterId == printer.Id && c.Timestamp < toUtc)
                .OrderByDescending(c => c.Timestamp).FirstOrDefault();
            if (start is null || end is null) continue;

            table.Rows.Add(printer.DisplayName, printer.IpAddress, printer.Sector, printer.Model,
                start.Total, end.Total, end.Total - start.Total,
                Diff(start.Mono, end.Mono), Diff(start.Color, end.Color));
        }
        return table;
    }

    private static DataTable Franchise(DateTime fromLocal, DateTime toLocal)
    {
        var table = NewTable("Impressora", "IP", "Setor", "Período", "Situação", "Páginas (total)", "Páginas mono", "Páginas cor", "Franquia", "Excedente");
        using var db = AppDbContext.Create();
        var printers = db.Printers.AsNoTracking().ToDictionary(p => p.Id);
        int fromKey = fromLocal.Year * 100 + fromLocal.Month, toKey = toLocal.Year * 100 + toLocal.Month;

        var closed = db.MonthlyFranchises.AsNoTracking()
            .Where(f => f.Year * 100 + f.Month >= fromKey && f.Year * 100 + f.Month <= toKey)
            .ToList();

        foreach (var f in closed.OrderBy(f => f.Year).ThenBy(f => f.Month))
        {
            var p = printers.GetValueOrDefault(f.PrinterId);
            var pages = f.EndTotal - f.StartTotal;
            table.Rows.Add(p?.DisplayName, p?.IpAddress, p?.Sector, $"{f.Month:00}/{f.Year}", "Fechado",
                pages, Diff(f.StartMono, f.EndMono), Diff(f.StartColor, f.EndColor),
                f.FranchisePages, Excess(pages, f.FranchisePages));
        }

        // Mês corrente (aberto) com o acumulado até agora.
        var (year, month) = FranchiseService.CurrentOpenPeriod();
        if (year * 100 + month >= fromKey && year * 100 + month <= toKey)
        {
            foreach (var p in printers.Values.Where(p => !p.IsDeleted && p.TotalPages is not null))
            {
                if (closed.Any(f => f.PrinterId == p.Id && f.Year == year && f.Month == month)) continue;
                if (FranchiseService.GetPeriodStart(db, p.Id, year, month) is not { } start) continue;
                var pages = p.TotalPages!.Value - start.Total;
                table.Rows.Add(p.DisplayName, p.IpAddress, p.Sector, $"{month:00}/{year}", "Em aberto",
                    pages, Diff(start.Mono, p.MonoPages), Diff(start.Color, p.ColorPages),
                    p.MonthlyFranchisePages, Excess(pages, p.MonthlyFranchisePages));
            }
        }
        return table;
    }

    private static DataTable Maintenance(DateTime fromUtc, DateTime toUtc)
    {
        var table = NewTable("Data", "Impressora", "IP", "Tipo", "Peça", "Descrição", "Contador", "Responsável");
        foreach (var m in PrinterService.GetMaintenance(null, fromUtc, toUtc))
        {
            table.Rows.Add(m.PerformedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), m.Printer?.DisplayName, m.Printer?.IpAddress,
                m.Type.GetDescription(), m.PartName, m.Description, m.CounterAtService, m.UserName);
        }
        return table;
    }

    // ---------- Exportação ----------

    public static void ExportCsv(DataTable table, string path)
    {
        // Separador ";" e BOM UTF-8: abre corretamente no Excel em pt-BR.
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', table.Columns.Cast<DataColumn>().Select(c => Escape(c.ColumnName))));
        foreach (DataRow row in table.Rows)
            sb.AppendLine(string.Join(';', row.ItemArray.Select(v => Escape(Convert.ToString(v, PtBr) ?? ""))));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        AuditService.Write("Relatorio exportado", "Relatorio", "CSV", $"{table.TableName} -> {path}");
    }

    public static void ExportPdf(DataTable table, string path)
    {
        var columns = table.Columns.Cast<DataColumn>().ToList();
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(8));

                page.Header().Column(col =>
                {
                    col.Item().Text("Gestão de Impressoras").FontSize(14).SemiBold();
                    col.Item().Text(table.TableName).FontSize(10);
                    col.Item().Text($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm} por {Session.UserLabel}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingBottom(6);
                });

                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(c => { foreach (var _ in columns) c.RelativeColumn(); });
                    t.Header(h =>
                    {
                        foreach (var column in columns)
                            h.Cell().Background(Colors.Grey.Lighten3).BorderBottom(1).Padding(3).Text(column.ColumnName).SemiBold();
                    });
                    foreach (DataRow row in table.Rows)
                        foreach (var value in row.ItemArray)
                            t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(Convert.ToString(value, PtBr) ?? "");
                });

                page.Footer().AlignRight().Text(x =>
                {
                    x.Span("Página ");
                    x.CurrentPageNumber();
                    x.Span(" de ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(path);
        AuditService.Write("Relatorio exportado", "Relatorio", "PDF", $"{table.TableName} -> {path}");
    }

    private static DataTable NewTable(params string[] columns)
    {
        var table = new DataTable();
        foreach (var c in columns) table.Columns.Add(c);
        return table;
    }

    private static DataTable Sort(DataTable table, string column)
    {
        table.DefaultView.Sort = column;
        var sorted = table.DefaultView.ToTable();
        return sorted;
    }

    private static string Fmt(double value) => value.ToString("0.0", PtBr);
    private static long? Diff(long? start, long? end) => start is null || end is null ? null : end - start;
    private static long? Excess(long pages, int? franchise) => franchise is null ? null : Math.Max(0, pages - franchise.Value);

    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
