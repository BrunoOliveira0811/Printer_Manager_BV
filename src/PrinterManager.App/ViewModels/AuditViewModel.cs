using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.ViewModels;

/// <summary>Consulta e verificação de integridade da trilha de auditoria (RNF10, RN14).</summary>
public sealed class AuditViewModel : PageViewModel
{
    private string searchText = "";
    private DateTime from = DateTime.Today.AddDays(-30);
    private DateTime to = DateTime.Today;
    private string integrity = "";

    public AuditViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        VerifyCommand = new AsyncRelayCommand(async () =>
        {
            Integrity = "Verificando…";
            var (ok, brokenId, count) = await Task.Run(AuditService.Verify);
            Integrity = ok
                ? $"Íntegra: {count:N0} registro(s) verificados, cadeia de hash consistente."
                : $"ATENÇÃO: cadeia de auditoria inconsistente a partir do registro #{brokenId}. O banco pode ter sido alterado fora do sistema.";
        });
    }

    public override string Title => "Auditoria";

    public ObservableCollection<AuditEntry> Items { get; } = new();
    public string SearchText { get => searchText; set => SetProperty(ref searchText, value); }
    public DateTime From { get => from; set => SetProperty(ref from, value); }
    public DateTime To { get => to; set => SetProperty(ref to, value); }
    public string Integrity { get => integrity; private set => SetProperty(ref integrity, value); }

    public ICommand RefreshCommand { get; }
    public ICommand VerifyCommand { get; }

    public override async Task LoadAsync()
    {
        Session.RequireAdmin();
        var text = SearchText.Trim().ToLower();
        var fromUtc = From.Date.ToUniversalTime();
        var toUtc = To.Date.AddDays(1).ToUniversalTime();

        var entries = await Task.Run(() =>
        {
            using var db = AppDbContext.Create();
            var q = db.AuditEntries.AsNoTracking().Where(a => a.Timestamp >= fromUtc && a.Timestamp < toUtc);
            if (text.Length > 0)
                q = q.Where(a => a.Action.ToLower().Contains(text) || a.Details.ToLower().Contains(text)
                                 || a.UserName.ToLower().Contains(text) || a.Entity.ToLower().Contains(text));
            return q.OrderByDescending(a => a.Id).Take(5000).ToList();
        });

        Items.Clear();
        foreach (var e in entries) Items.Add(e);
    }
}
