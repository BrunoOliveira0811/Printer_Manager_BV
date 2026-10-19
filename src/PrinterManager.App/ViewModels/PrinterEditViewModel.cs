using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.ViewModels;

/// <summary>Cadastro manual e edição (RF02) com teste de conexão sob demanda (RF13).</summary>
public sealed class PrinterEditViewModel : ObservableObject
{
    private readonly Printer model;
    private string testResult = "";

    public PrinterEditViewModel(Printer? existing, string defaultCommunity)
    {
        model = existing ?? new Printer { Community = defaultCommunity };
        HasStoredAuthPassword = !string.IsNullOrEmpty(model.V3AuthPasswordProtected);
        HasStoredPrivPassword = !string.IsNullOrEmpty(model.V3PrivPasswordProtected);
    }

    public bool IsNew => model.Id == 0;
    public string WindowTitle => IsNew ? "Nova impressora" : $"Editar impressora — {model.Name}";

    public string Name { get => model.Name; set { model.Name = value; RaisePropertyChanged(); } }
    public string IpAddress { get => model.IpAddress; set { model.IpAddress = value; RaisePropertyChanged(); } }
    public string? Hostname { get => model.Hostname; set { model.Hostname = value; RaisePropertyChanged(); } }
    public string? Sector { get => model.Sector; set { model.Sector = value; RaisePropertyChanged(); } }
    public string? Location { get => model.Location; set { model.Location = value; RaisePropertyChanged(); } }
    public string? Manufacturer { get => model.Manufacturer; set { model.Manufacturer = value; RaisePropertyChanged(); } }
    public string? Model { get => model.Model; set { model.Model = value; RaisePropertyChanged(); } }
    public string? SerialNumber { get => model.SerialNumber; set { model.SerialNumber = value; RaisePropertyChanged(); } }

    public IReadOnlyList<EnumOption<SnmpVersionOption>> SnmpVersions => EnumOption<SnmpVersionOption>.All;
    public IReadOnlyList<EnumOption<SnmpAuthProtocol>> AuthProtocols => EnumOption<SnmpAuthProtocol>.All;
    public IReadOnlyList<EnumOption<SnmpPrivProtocol>> PrivProtocols => EnumOption<SnmpPrivProtocol>.All;
    public IReadOnlyList<EnumOption<LifecycleStatus>> Lifecycles => EnumOption<LifecycleStatus>.All;

    public SnmpVersionOption SnmpVersion
    {
        get => model.SnmpVersion;
        set { model.SnmpVersion = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(IsV3)); }
    }

    public bool IsV3 => SnmpVersion == SnmpVersionOption.V3;
    public string SnmpPort { get => model.SnmpPort.ToString(); set { if (int.TryParse(value, out var p)) model.SnmpPort = p; RaisePropertyChanged(); } }
    public string Community { get => model.Community; set { model.Community = value; RaisePropertyChanged(); } }
    public string? V3UserName { get => model.V3UserName; set { model.V3UserName = value; RaisePropertyChanged(); } }
    public SnmpAuthProtocol V3AuthProtocol { get => model.V3AuthProtocol; set { model.V3AuthProtocol = value; RaisePropertyChanged(); } }
    public SnmpPrivProtocol V3PrivProtocol { get => model.V3PrivProtocol; set { model.V3PrivProtocol = value; RaisePropertyChanged(); } }
    public string? V3ContextName { get => model.V3ContextName; set { model.V3ContextName = value; RaisePropertyChanged(); } }
    public bool HasStoredAuthPassword { get; }
    public bool HasStoredPrivPassword { get; }

    /// <summary>Preenchidos pela janela a partir dos PasswordBox; nulo = manter a senha armazenada.</summary>
    public string? V3AuthPassword { get; set; }
    public string? V3PrivPassword { get; set; }

    public string? MonoCounterOid { get => model.MonoCounterOid; set { model.MonoCounterOid = value; RaisePropertyChanged(); } }
    public string? ColorCounterOid { get => model.ColorCounterOid; set { model.ColorCounterOid = value; RaisePropertyChanged(); } }

    public LifecycleStatus Lifecycle { get => model.Lifecycle; set { model.Lifecycle = value; RaisePropertyChanged(); } }

    public string PollIntervalMinutes
    {
        get => model.PollIntervalMinutes?.ToString() ?? "";
        set { model.PollIntervalMinutes = int.TryParse(value, out var v) ? v : null; RaisePropertyChanged(); }
    }

    public string MonthlyFranchisePages
    {
        get => model.MonthlyFranchisePages?.ToString() ?? "";
        set { model.MonthlyFranchisePages = int.TryParse(value, out var v) && v > 0 ? v : null; RaisePropertyChanged(); }
    }

    public string TestResult { get => testResult; private set => SetProperty(ref testResult, value); }

    private SnmpCredentials BuildCredentials() => new(
        SnmpVersion,
        Community,
        V3UserName,
        V3AuthProtocol,
        V3AuthPassword ?? SecretProtector.Unprotect(model.V3AuthPasswordProtected),
        V3PrivProtocol,
        V3PrivPassword ?? SecretProtector.Unprotect(model.V3PrivPasswordProtected),
        V3ContextName,
        model.SnmpPort);

    /// <summary>Executado pela janela depois de copiar as senhas dos PasswordBox.</summary>
    public async Task TestAsync()
    {
        if (string.IsNullOrWhiteSpace(IpAddress))
        {
            TestResult = "Informe o endereço IP.";
            return;
        }

        TestResult = "Testando ping e SNMP…";
        try
        {
            var timeout = SettingsService.Load().SnmpTimeoutMs;
            var result = await DiscoveryService.TestConnectionAsync(IpAddress, BuildCredentials(), timeout);
            TestResult = result.Summary;

            // Preenche campos vazios com o que o equipamento informou.
            if (result.SnmpOk)
            {
                if (string.IsNullOrWhiteSpace(Model)) Model = result.Model;
                if (string.IsNullOrWhiteSpace(SerialNumber)) SerialNumber = result.SerialNumber;
                if (string.IsNullOrWhiteSpace(Hostname)) Hostname = result.SysName;
                if (string.IsNullOrWhiteSpace(Name)) Name = result.SysName ?? result.Model ?? IpAddress;
            }
        }
        catch (Exception ex)
        {
            TestResult = $"Falha no teste: {ex.Message}";
        }
    }

    public Printer Save() => PrinterService.Save(model, V3AuthPassword, V3PrivPassword);
}
