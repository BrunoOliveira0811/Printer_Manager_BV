using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

public sealed record SnmpCredentials(
    SnmpVersionOption Version,
    string Community,
    string? UserName = null,
    SnmpAuthProtocol AuthProtocol = SnmpAuthProtocol.None,
    string? AuthPassword = null,
    SnmpPrivProtocol PrivProtocol = SnmpPrivProtocol.None,
    string? PrivPassword = null,
    string? ContextName = null,
    int Port = 161)
{
    public static SnmpCredentials FromPrinter(Printer p) => new(
        p.SnmpVersion,
        p.Community,
        p.V3UserName,
        p.V3AuthProtocol,
        SecretProtector.Unprotect(p.V3AuthPasswordProtected),
        p.V3PrivProtocol,
        SecretProtector.Unprotect(p.V3PrivPasswordProtected),
        p.V3ContextName,
        p.SnmpPort);
}

public sealed class SnmpProtocolException(string message) : Exception(message);
public sealed class SnmpErrorStatusException() : Exception("O equipamento retornou erro na requisição SNMP.");

/// <summary>
/// Cliente SNMP síncrono com suporte a v1, v2c e v3 (authNoPriv/authPriv) — RNF01.
/// As chamadas bloqueiam até o timeout; use dentro de Task.Run.
/// </summary>
public sealed class SnmpClient
{
    private readonly IPEndPoint endpoint;
    private readonly SnmpCredentials credentials;
    private readonly int timeoutMs;
    private ReportMessage? v3Report;
    private IPrivacyProvider? v3Privacy;

    public SnmpClient(IPAddress address, SnmpCredentials credentials, int timeoutMs)
    {
        endpoint = new IPEndPoint(address, credentials.Port);
        this.credentials = credentials;
        this.timeoutMs = timeoutMs;
    }

    public IPEndPoint Endpoint => endpoint;

    /// <summary>
    /// GET de vários OIDs. Retorna nulo para OIDs inexistentes. Em SNMPv1 um OID inexistente
    /// invalida o pacote inteiro, então há fallback para consultas individuais.
    /// Timeouts são propagados (indicam falha de comunicação).
    /// </summary>
    public Dictionary<string, ISnmpData?> Get(IEnumerable<string> oids)
    {
        var list = oids.Distinct().ToList();
        var result = new Dictionary<string, ISnmpData?>();
        try
        {
            foreach (var v in GetRaw(list)) result[v.Id.ToString()] = Clean(v.Data);
        }
        catch (Exception ex) when (ex is ErrorException or SnmpErrorStatusException)
        {
            foreach (var oid in list)
            {
                try { result[oid] = Clean(GetRaw([oid]).FirstOrDefault()?.Data); }
                catch (Exception inner) when (inner is ErrorException or SnmpErrorStatusException) { result[oid] = null; }
            }
        }
        foreach (var oid in list) result.TryAdd(oid, null);
        return result;
    }

    public ISnmpData? GetOne(string oid) => Get([oid])[oid];

    /// <summary>Percorre uma subárvore (GETNEXT em v1/v2c, GETBULK em v3).</summary>
    public IList<Variable> Walk(string rootOid)
    {
        var list = new List<Variable>();
        var root = new ObjectIdentifier(rootOid);
        if (credentials.Version == SnmpVersionOption.V3)
        {
            EnsureV3();
            Messenger.BulkWalk(VersionCode.V3, endpoint, new OctetString(credentials.UserName ?? ""),
                new OctetString(credentials.ContextName ?? ""), root, list, timeoutMs, 10,
                WalkMode.WithinSubtree, v3Privacy!, v3Report!);
        }
        else
        {
            Messenger.Walk(ToVersionCode(credentials.Version), endpoint, new OctetString(credentials.Community),
                root, list, timeoutMs, WalkMode.WithinSubtree);
        }
        return list;
    }

    private IList<Variable> GetRaw(IList<string> oids)
    {
        var variables = oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();

        if (credentials.Version != SnmpVersionOption.V3)
            return Messenger.Get(ToVersionCode(credentials.Version), endpoint, new OctetString(credentials.Community), variables, timeoutMs);

        EnsureV3();
        var request = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
            new OctetString(credentials.UserName ?? ""), new OctetString(credentials.ContextName ?? ""),
            variables, v3Privacy!, Messenger.MaxMessageSize, v3Report!);
        var reply = request.GetResponse(timeoutMs, endpoint);
        var pdu = reply.Pdu();

        if (pdu.TypeCode == SnmpType.ReportPdu)
            throw new SnmpProtocolException(DescribeV3Report(pdu));
        if (pdu.ErrorStatus.ToInt32() != 0)
            throw new SnmpErrorStatusException();
        return pdu.Variables;
    }

    private void EnsureV3()
    {
        if (v3Report is not null) return;
        if (string.IsNullOrWhiteSpace(credentials.UserName))
            throw new SnmpProtocolException("SNMPv3 exige um nome de usuário.");

        v3Privacy = BuildPrivacy();
        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        v3Report = discovery.GetResponse(timeoutMs, endpoint);
    }

    private IPrivacyProvider BuildPrivacy()
    {
        if (credentials.AuthProtocol == SnmpAuthProtocol.None)
            return DefaultPrivacyProvider.DefaultPair;

        // MD5/SHA-1/DES/3DES são obsoletos, mas muitas impressoras em produção só oferecem esses.
#pragma warning disable CS0618
        var authPass = new OctetString(credentials.AuthPassword ?? "");
        IAuthenticationProvider auth = credentials.AuthProtocol switch
        {
            SnmpAuthProtocol.MD5    => new MD5AuthenticationProvider(authPass),
            SnmpAuthProtocol.SHA1   => new SHA1AuthenticationProvider(authPass),
            SnmpAuthProtocol.SHA256 => new SHA256AuthenticationProvider(authPass),
            SnmpAuthProtocol.SHA384 => new SHA384AuthenticationProvider(authPass),
            SnmpAuthProtocol.SHA512 => new SHA512AuthenticationProvider(authPass),
            _ => throw new SnmpProtocolException("Protocolo de autenticação inválido.")
        };

        var privPass = new OctetString(credentials.PrivPassword ?? "");
        return credentials.PrivProtocol switch
        {
            SnmpPrivProtocol.None      => new DefaultPrivacyProvider(auth),
            SnmpPrivProtocol.DES       => new DESPrivacyProvider(privPass, auth),
            SnmpPrivProtocol.TripleDES => new TripleDESPrivacyProvider(privPass, auth),
            SnmpPrivProtocol.AES128    => new AESPrivacyProvider(privPass, auth),
            SnmpPrivProtocol.AES192    => new AES192PrivacyProvider(privPass, auth),
            SnmpPrivProtocol.AES256    => new AES256PrivacyProvider(privPass, auth),
            _ => throw new SnmpProtocolException("Protocolo de privacidade inválido.")
        };
#pragma warning restore CS0618
    }

    private static string DescribeV3Report(ISnmpPdu pdu)
    {
        var oid = pdu.Variables.FirstOrDefault()?.Id.ToString() ?? "";
        return oid switch
        {
            "1.3.6.1.6.3.15.1.1.3.0" => "SNMPv3: usuário desconhecido.",
            "1.3.6.1.6.3.15.1.1.5.0" => "SNMPv3: senha de autenticação incorreta.",
            "1.3.6.1.6.3.15.1.1.6.0" => "SNMPv3: falha ao descriptografar (senha/protocolo de privacidade incorretos).",
            "1.3.6.1.6.3.15.1.1.1.0" => "SNMPv3: nível de segurança não suportado pelo equipamento.",
            "1.3.6.1.6.3.15.1.1.2.0" => "SNMPv3: fora da janela de tempo.",
            _ => $"SNMPv3: requisição rejeitada ({oid})."
        };
    }

    private static VersionCode ToVersionCode(SnmpVersionOption v) => v == SnmpVersionOption.V1 ? VersionCode.V1 : VersionCode.V2;

    private static ISnmpData? Clean(ISnmpData? data) =>
        data is null || data.TypeCode is SnmpType.NoSuchObject or SnmpType.NoSuchInstance or SnmpType.EndOfMibView or SnmpType.Null
            ? null
            : data;

    public static string? AsText(ISnmpData? data)
    {
        if (data is null) return null;
        var text = data is OctetString os ? os.ToString() : data.ToString();
        text = text?.Replace("\0", "").Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static long? AsLong(ISnmpData? data) => data switch
    {
        Integer32 i  => i.ToInt32(),
        Counter32 c  => (long)c.ToUInt32(),
        Gauge32 g    => (long)g.ToUInt32(),
        Counter64 c  => (long)c.ToUInt64(),
        TimeTicks t  => (long)t.ToUInt32(),
        null         => null,
        _            => long.TryParse(data.ToString(), out var v) ? v : null
    };
}
