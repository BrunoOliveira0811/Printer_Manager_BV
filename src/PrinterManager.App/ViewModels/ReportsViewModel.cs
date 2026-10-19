using System.Data;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Services;

namespace PrinterManager.App.ViewModels;

/// <summary>Relatórios de consumo e contadores com exportação PDF/CSV (RF09).</summary>
public sealed class ReportsViewModel : PageViewModel
{
    private ReportKind kind = ReportKind.PageCounters;
    private DateTime from = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime to = DateTime.Today;
    private DataTable? table;

    public ReportsViewModel()
    {
        GenerateCommand = new AsyncRelayCommand(GenerateAsync);
        ExportCsvCommand = new AsyncRelayCommand(() => ExportAsync("csv"), () => Table is not null);
        ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync("pdf"), () => Table is not null);
    }

    public override string Title => "Relatórios";
    public override Task LoadAsync() => Table is null ? GenerateAsync() : Task.CompletedTask;

    public IReadOnlyList<EnumOption<ReportKind>> Kinds => EnumOption<ReportKind>.All;
    public ReportKind Kind { get => kind; set { if (SetProperty(ref kind, value)) _ = GenerateAsync(); } }
    public DateTime From { get => from; set => SetProperty(ref from, value); }
    public DateTime To { get => to; set => SetProperty(ref to, value); }

    public DataTable? Table { get => table; private set { SetProperty(ref table, value); RaisePropertyChanged(nameof(RowCountText)); } }
    public string RowCountText => Table is null ? "" : $"{Table.Rows.Count} linha(s) · {Table.TableName}";

    public ICommand GenerateCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportPdfCommand { get; }

    private async Task GenerateAsync()
    {
        if (To < From) throw new InvalidOperationException("A data final deve ser maior ou igual à inicial.");
        var (k, f, t) = (Kind, From, To);
        await RunBusyAsync(async () => Table = await Task.Run(() => ReportService.Build(k, f, t)));
    }

    private async Task ExportAsync(string format)
    {
        var current = Table!;
        var dialog = new SaveFileDialog
        {
            FileName = $"{Kind.GetDescription()} {From:yyyy-MM-dd} a {To:yyyy-MM-dd}.{format}".Replace("/", "-"),
            Filter = format == "pdf" ? "PDF (*.pdf)|*.pdf" : "CSV (*.csv)|*.csv"
        };
        if (dialog.ShowDialog() != true) return;

        var path = dialog.FileName;
        await RunBusyAsync(() => Task.Run(() =>
        {
            if (format == "pdf") ReportService.ExportPdf(current, path);
            else ReportService.ExportCsv(current, path);
        }));

        if (Dialogs.Confirm($"Relatório salvo em:\n{path}\n\nDeseja abrir o arquivo agora?"))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
