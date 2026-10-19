using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Input;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.ViewModels;

public sealed class DiscoveredRowViewModel(DiscoveredPrinter d) : ObservableObject
{
    private bool isSelected = !d.AlreadyRegistered;
    public DiscoveredPrinter Data { get; } = d;
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
    public bool CanSelect => !Data.AlreadyRegistered;
    public string IpAddress => Data.IpAddress;
    public string? Name => Data.Name;
    public string? Model => Data.Model;
    public string? SerialNumber => Data.SerialNumber;
    public string? SysDescription => Data.SysDescription;
    public string Situation => Data.AlreadyRegistered ? "Já cadastrada" : "Nova";
}

/// <summary>Descoberta automática por faixa de IP / CIDR via SNMP (RF01).</summary>
public sealed class DiscoveryViewModel : PageViewModel
{
    private readonly IpRangeService ipRange = new();
    private CancellationTokenSource? cts;
    private string mode = "Faixa";
    private string startIp = "", endIp = "", cidr = "";
    private SnmpVersionOption version = SnmpVersionOption.V2c;
    private string community = "public";
    private string? v3User, v3Context;
    private SnmpAuthProtocol v3Auth = SnmpAuthProtocol.SHA1;
    private SnmpPrivProtocol v3Priv = SnmpPrivProtocol.AES128;
    private string defaultSector = "";
    private double progress;
    private string progressText = "";
    private bool isScanning;

    public DiscoveryViewModel()
    {
        try
        {
            var subnet = ipRange.GetCurrentSubnet();
            var prefix = subnet.SubnetMask.GetAddressBytes().Sum(b => Convert.ToString(b, 2).Count(c => c == '1'));
            var network = IpRangeService.ToUInt32(subnet.NetworkAddress);
            var broadcast = IpRangeService.ToUInt32(subnet.BroadcastAddress);
            // Redes maiores que /24 (ex.: /8, /16) gerariam milhões de endereços: sugere só a /24 da máquina.
            if (prefix < 24)
            {
                var localIp = IpRangeService.ToUInt32(IPAddress.Parse(LocalIpInSubnet(subnet.NetworkAddress, subnet.SubnetMask)));
                network = localIp & 0xFFFFFF00;
                broadcast = network | 0xFF;
                prefix = 24;
            }
            startIp = IpRangeService.ToIpAddress(network + 1).ToString();
            endIp = IpRangeService.ToIpAddress(broadcast - 1).ToString();
            cidr = $"{IpRangeService.ToIpAddress(network)}/{prefix}";
        }
        catch
        {
        }
        community = SettingsService.Load().DefaultCommunity;

        StartCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        CancelCommand = new UiCommand(() => cts?.Cancel(), () => IsScanning);
        AddSelectedCommand = new AsyncRelayCommand(AddSelectedAsync, () => !IsScanning && Session.CanEdit && Results.Any(r => r.IsSelected));
        SelectAllCommand = new UiCommand(() => { foreach (var r in Results.Where(r => r.CanSelect)) r.IsSelected = true; });
        SelectNoneCommand = new UiCommand(() => { foreach (var r in Results) r.IsSelected = false; });
    }

    private static string LocalIpInSubnet(IPAddress network, IPAddress mask)
    {
        var net = IpRangeService.ToUInt32(network);
        var m = IpRangeService.ToUInt32(mask);
        return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (IpRangeService.ToUInt32(a) & m) == net)
            .ToString();
    }

    public override string Title => "Descoberta na rede";
    public override Task LoadAsync() => Task.CompletedTask;

    public IReadOnlyList<string> Modes { get; } = ["Faixa", "CIDR"];
    public string Mode { get => mode; set { if (SetProperty(ref mode, value)) RaisePropertyChanged(nameof(IsRangeMode)); } }
    public bool IsRangeMode => Mode == "Faixa";
    public string StartIp { get => startIp; set => SetProperty(ref startIp, value); }
    public string EndIp { get => endIp; set => SetProperty(ref endIp, value); }
    public string Cidr { get => cidr; set => SetProperty(ref cidr, value); }

    public IReadOnlyList<EnumOption<SnmpVersionOption>> SnmpVersions => EnumOption<SnmpVersionOption>.All;
    public IReadOnlyList<EnumOption<SnmpAuthProtocol>> AuthProtocols => EnumOption<SnmpAuthProtocol>.All;
    public IReadOnlyList<EnumOption<SnmpPrivProtocol>> PrivProtocols => EnumOption<SnmpPrivProtocol>.All;
    public SnmpVersionOption Version { get => version; set { if (SetProperty(ref version, value)) RaisePropertyChanged(nameof(IsV3)); } }
    public bool IsV3 => Version == SnmpVersionOption.V3;
    public string Community { get => community; set => SetProperty(ref community, value); }
    public string? V3User { get => v3User; set => SetProperty(ref v3User, value); }
    public string? V3Context { get => v3Context; set => SetProperty(ref v3Context, value); }
    public SnmpAuthProtocol V3Auth { get => v3Auth; set => SetProperty(ref v3Auth, value); }
    public SnmpPrivProtocol V3Priv { get => v3Priv; set => SetProperty(ref v3Priv, value); }
    public string? V3AuthPassword { get; set; }
    public string? V3PrivPassword { get; set; }
    public string DefaultSector { get => defaultSector; set => SetProperty(ref defaultSector, value); }

    public ObservableCollection<DiscoveredRowViewModel> Results { get; } = new();
    public double Progress { get => progress; private set => SetProperty(ref progress, value); }
    public string ProgressText { get => progressText; private set => SetProperty(ref progressText, value); }
    public bool IsScanning { get => isScanning; private set => SetProperty(ref isScanning, value); }

    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    private SnmpCredentials Credentials() =>
        new(Version, Community, V3User, V3Auth, V3AuthPassword, V3Priv, V3PrivPassword, V3Context);

    private async Task ScanAsync()
    {
        var addresses = IsRangeMode ? ipRange.ExpandManualRange(StartIp.Trim(), EndIp.Trim()) : ipRange.ExpandCidr(Cidr.Trim());
        var credentials = Credentials();
        var timeout = Math.Min(SettingsService.Load().SnmpTimeoutMs, 2000);

        Results.Clear();
        IsScanning = true;
        Progress = 0;
        ProgressText = $"Varrendo {addresses.Count:N0} endereços…";
        cts = new CancellationTokenSource();
        var dispatcher = Application.Current.Dispatcher;

        var reporter = new Progress<DiscoveryProgress>(p =>
        {
            Progress = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
            ProgressText = $"{p.Done:N0} de {p.Total:N0} endereços · {p.Found} impressora(s) encontrada(s)";
        });

        try
        {
            await DiscoveryService.ScanAsync(addresses, credentials, timeout, reporter,
                found => dispatcher.BeginInvoke(() => Results.Add(new DiscoveredRowViewModel(found))), cts.Token);
            ProgressText = $"Concluído: {Results.Count} impressora(s) encontrada(s) em {addresses.Count:N0} endereços.";
        }
        catch (OperationCanceledException)
        {
            ProgressText = $"Varredura cancelada. {Results.Count} impressora(s) encontrada(s).";
        }
        finally
        {
            IsScanning = false;
            cts.Dispose();
            cts = null;
            CommandManager.InvalidateRequerySuggested();
        }

        var ordered = Results.OrderBy(r => IpRangeService.ToUInt32(IPAddress.Parse(r.IpAddress))).ToList();
        Results.Clear();
        foreach (var r in ordered) Results.Add(r);
    }

    private async Task AddSelectedAsync()
    {
        var selected = Results.Where(r => r.IsSelected && r.CanSelect).ToList();
        var errors = new List<string>();
        var added = 0;

        await Task.Run(() =>
        {
            foreach (var row in selected)
            {
                try
                {
                    PrinterService.Save(new Printer
                    {
                        Name = row.Name ?? row.IpAddress,
                        IpAddress = row.IpAddress,
                        Hostname = row.Name,
                        Model = row.Model,
                        SerialNumber = row.SerialNumber,
                        Sector = string.IsNullOrWhiteSpace(DefaultSector) ? null : DefaultSector,
                        SnmpVersion = Version,
                        Community = Community,
                        V3UserName = V3User,
                        V3AuthProtocol = V3Auth,
                        V3PrivProtocol = V3Priv,
                        V3ContextName = V3Context
                    }, V3AuthPassword, V3PrivPassword);
                    added++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.IpAddress}: {ex.Message}");
                }
            }
        });

        Dialogs.Info($"{added} impressora(s) cadastrada(s)." + (errors.Count > 0 ? "\n\nFalhas:\n" + string.Join("\n", errors) : "") +
                     "\n\nA primeira leitura completa acontecerá no próximo ciclo do agendador (até 1 minuto).");

        var registered = selected.Select(s => s.IpAddress).ToHashSet();
        var refreshed = Results.Select(r => registered.Contains(r.IpAddress)
            ? new DiscoveredRowViewModel(new DiscoveredPrinter
            {
                IpAddress = r.IpAddress, Name = r.Name, Model = r.Model, SerialNumber = r.SerialNumber,
                SysDescription = r.SysDescription, AlreadyRegistered = true
            })
            : r).ToList();
        Results.Clear();
        foreach (var r in refreshed) Results.Add(r);
    }
}
