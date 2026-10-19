using System.ComponentModel;

namespace PrinterManager.App.Models;

public enum UserRole
{
    [Description("Administrador")] Administrador = 0,
    [Description("Técnico")]       Tecnico       = 1,
    [Description("Visualizador")]  Visualizador  = 2
}

public enum SnmpVersionOption
{
    [Description("SNMP v1")]  V1  = 0,
    [Description("SNMP v2c")] V2c = 1,
    [Description("SNMP v3")]  V3  = 2
}

public enum SnmpAuthProtocol
{
    [Description("Sem autenticação")] None = 0,
    [Description("MD5")]    MD5    = 1,
    [Description("SHA-1")]  SHA1   = 2,
    [Description("SHA-256")] SHA256 = 3,
    [Description("SHA-384")] SHA384 = 4,
    [Description("SHA-512")] SHA512 = 5
}

public enum SnmpPrivProtocol
{
    [Description("Sem criptografia")] None = 0,
    [Description("DES")]     DES       = 1,
    [Description("3DES")]    TripleDES = 2,
    [Description("AES-128")] AES128    = 3,
    [Description("AES-192")] AES192    = 4,
    [Description("AES-256")] AES256    = 5
}

/// <summary>Ciclo de vida do equipamento (RN13).</summary>
public enum LifecycleStatus
{
    [Description("Ativa")]      Ativa      = 0,
    [Description("Inativa")]    Inativa    = 1,
    [Description("Descartada")] Descartada = 2
}

/// <summary>Estado operacional obtido pela varredura.</summary>
public enum ConnectionStatus
{
    [Description("Desconhecido")] Desconhecido = 0,
    [Description("Online")]       Online       = 1,
    [Description("Alerta")]       Alerta       = 2,
    [Description("Erro")]         Erro         = 3,
    [Description("Offline")]      Offline      = 4
}

public enum SupplyColor
{
    [Description("Preto")]   Preto   = 0,
    [Description("Ciano")]   Ciano   = 1,
    [Description("Magenta")] Magenta = 2,
    [Description("Amarelo")] Amarelo = 3,
    [Description("Outro")]   Outro   = 4
}

public enum SupplyKind
{
    [Description("Toner")]                Toner         = 0,
    [Description("Tinta")]                Tinta         = 1,
    [Description("Resíduo")]              Residuo       = 2,
    [Description("Cilindro / Revelador")] Cilindro      = 3,
    [Description("Fusor")]                Fusor         = 4,
    [Description("Transferência")]        Transferencia = 5,
    [Description("Outro")]                Outro         = 6
}

public enum AlertType
{
    [Description("Suprimento crítico")] SuprimentoCritico = 0,
    [Description("Desgaste de peça")]   DesgastePeca      = 1,
    [Description("Offline")]            Offline           = 2,
    [Description("Erro da impressora")] ErroImpressora    = 3
}

public enum AlertSeverity
{
    [Description("Informação")] Info    = 0,
    [Description("Aviso")]      Aviso   = 1,
    [Description("Crítico")]    Critico = 2
}

public enum MaintenanceType
{
    [Description("Substituição de peça")] Substituicao  = 0,
    [Description("Manutenção preventiva")] Preventiva   = 1,
    [Description("Manutenção corretiva")]  Corretiva    = 2,
    [Description("Reset de contador")]     ResetContador = 3,
    [Description("Troca de suprimento")]   TrocaSuprimento = 4
}
