# Utilitário de Impressoras

**Versão 3.0** · Windows 10/11 · .NET 8 · WPF · MVVM

Ferramenta para descoberta, diagnóstico e configuração de impressoras (com foco em impressoras térmicas e de rede) em ambientes corporativos. Ao abrir, já mostra as impressoras instaladas no computador; ao varrer, encontra novas na rede e permite alterar IP, gerenciar filas, instalar drivers e mais.

> O monitoramento contínuo de suprimentos, peças e contadores (Gestão de Impressoras) agora é um projeto separado: **ImpGest**.

---

## Sumário

- [Funcionalidades](#funcionalidades)
- [Alterar IP — protocolos suportados](#alterar-ip--protocolos-suportados)
- [Identificação via SNMP](#identificação-via-snmp)
- [Compilar e publicar](#compilar-e-publicar)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Como usar](#como-usar)
- [Configurações e colunas](#configurações-e-colunas)
- [Dependências](#dependências)
- [Privacidade](#privacidade)

---

## Funcionalidades

### Descoberta

| Funcionalidade | Descrição |
|---|---|
| **Impressoras instaladas** | USB e rede já instaladas no Windows aparecem ao abrir, sem varredura |
| **Varredura por sub-rede, faixa ou CIDR** | Sub-rede da interface selecionada, intervalo manual (`192.168.1.10` → `192.168.1.50`) ou CIDR (`192.168.0.0/24`) |
| **Busca Ampliada** | ARP + broadcast UDP para achar impressoras em sub-redes vizinhas (ex.: PC em 192.168.100.x encontra impressora em 192.168.1.x) |
| **IPs temporários** | Quando a sub-rede vizinha é inacessível, adiciona IPs secundários na placa de rede (UAC) e os remove ao final |
| **Seleção de interface** | Escolha da placa de rede, útil em máquinas com VPN |
| **Identificação SNMP** | Modelo, fabricante, máscara, gateway e DHCP via SNMP v1/v2c |
| **Endereço MAC** | Lido da tabela ARP local |
| **Filtro em tempo real** | Busca por IP, nome, MAC ou descrição |

### Status, fila e spooler

| Funcionalidade | Descrição |
|---|---|
| **Modo offline** | Detecta "Usar impressora offline"; duplo clique na coluna desativa |
| **Fila pausada** | Detecta fila pausada; duplo clique na coluna retoma |
| **Colunas contextuais** | "Offline" e "Fila" só aparecem quando há problema |
| **Limpar fila** | Cancela os trabalhos da impressora selecionada ou de todas |
| **Pausar / retomar fila** | Via WMI (`Win32_Printer.Pause/Resume`) |
| **Reiniciar Spooler** | Para o serviço, apaga `%SystemRoot%\System32\spool\PRINTERS\*.*` e reinicia (UAC) |

### Rede, impressão e driver

| Funcionalidade | Descrição |
|---|---|
| **Alterar IP** | Reconfigura IP, máscara e gateway, com protocolo detectado pelo modelo; confirma por ping e porta 9100 no novo IP |
| **Impressão de teste** | Página ESC/POS direta pela porta TCP 9100, sem driver; alternativa via Windows |
| **Instalar driver** | Executa o instalador embutido com elevação de administrador |
| **Compartilhar na rede** | Ativa ou remove o compartilhamento; avisa quando falta driver |
| **Alterar porta USB** | Move a impressora para outra porta (USB001, USB002…) sem reinstalar o driver |

### Personalização

| Funcionalidade | Descrição |
|---|---|
| **Nome e descrição editáveis** | Duplo clique para editar; salvos por MAC e restaurados mesmo se o IP mudar |
| **Renomear no Windows** | Ao salvar um nome, a impressora instalada na porta correspondente é renomeada |
| **Relatório .txt** | Exporta o inventário para `C:\impressoras_AAAA-MM-DD_HH-mm.txt` |
| **Tema claro / escuro** | Alternância instantânea, salva entre sessões |
| **Utilitários** | Acesso a ferramentas externas de diagnóstico e configuração |

## Alterar IP — protocolos suportados

O protocolo é detectado pelo nome do dispositivo e pode ser escolhido manualmente.

| Protocolo | Modelos | Mecanismo |
|---|---|---|
| **Rongta / Jetway JP-800** | Rongta, Jetway JP-800 | TCP 9100 · `1F 69 [IP] 1F 25 00 [MASK] 1F 25 01 [GW]` (confirmado via Wireshark) |
| **Tectoy Q4 / i7** | Tectoy Q4, i7 | TCP 9100 · `1F 1B 1F 22 [IP 4B]` (confirmado via captura de pacotes) |
| **ESC/POS GS(N)** | Epson, Elgin e compatíveis | TCP 9100 · `GS ( N` (fn 00h IP, 01h máscara, 02h gateway, 10h aplicar) |
| **HTTP** | Modelos com interface web | GET em `/cgi-bin/netconfig.cgi` e `/config`, portas 80 e 8080 |
| **Automático** | Qualquer modelo | Testa todos em sequência até confirmar o novo IP |

| Nome / descrição contém | Protocolo escolhido |
|---|---|
| `tectoy`, `q4`, `i7` | Tectoy Q4 / i7 |
| `jetway`, `jp-800`, `rongta` | Rongta / Jetway JP-800 |
| `epson`, `elgin`, `tm-` | ESC/POS GS(N) |
| outros | Automático |

## Identificação via SNMP

**Modelo do dispositivo** (em ordem de prioridade):

```
1. SNMP    — hrDeviceDescr          (1.3.6.1.2.1.25.3.2.1.3.1)
2. SNMP    — prtGeneralPrinterName  (1.3.6.1.2.1.43.5.1.1.16.1)
3. HTTP    — título da página web de gerenciamento (porta 80)
4. ESC/POS — comandos GS I pela porta 9100 (térmicas)
5. SNMP    — sysDescr               (1.3.6.1.2.1.1.1.0, até 64 caracteres)
6. SNMP    — sysName                (1.3.6.1.2.1.1.5.0)
```

**Configuração de rede:**

| Campo | OID | Valores |
|---|---|---|
| Máscara | `1.3.6.1.2.1.4.20.1.3.{ip}` | `ipAdEntNetMask` (RFC 1213) |
| Gateway | `1.3.6.1.2.1.4.21.1.7.0.0.0.0` | `ipRouteNextHop` da rota 0.0.0.0 |
| DHCP (HP) | `1.3.6.1.4.1.11.2.4.3.5.13.0` | 1 = fixo, 3/4 = DHCP |
| DHCP (Epson) | `1.3.6.1.4.1.1248.1.2.2.1.1.3.1` | 0 = fixo, 1 = DHCP |
| DHCP (Brother) | `1.3.6.1.4.1.2435.2.3.9.4.2.1.4.3.1.0` | 0 = DHCP, 1 = fixo |
| DHCP (Lexmark) | `1.3.6.1.4.1.641.2.1.2.1.3.1` | 1 = fixo, 3 = DHCP |
| DHCP (Kyocera) | `1.3.6.1.4.1.1602.1.2.1.6.0` | 0 = fixo, 1 = DHCP |

A consulta tenta SNMPv2c com todos os OIDs de uma vez, depois SNMPv1 e, por último, um OID por vez para equipamentos que rejeitam GET com vários OIDs.

---

## Compilar e publicar

**Pré-requisitos:** Windows 10/11 64-bit e o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). O portátil não precisa de nada instalado; para rodar o app diretamente é preciso o [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Compilar
dotnet build src\PrinterScanner.App\PrinterScanner.App.csproj -c Release

# Publicar
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
powershell -ExecutionPolicy Bypass -File scripts\publish-installer.ps1
```

Resultados:

```
publish\win-x64-portable\
└── PrinterScanner.exe          ← executável portátil, sem dependências (~11 MB)

publish\win-x64-installer\
└── PrinterScanner-Setup.exe    ← instalador com runtime .NET 8 e utilitários (~72 MB)
```

> Depois de qualquer alteração de código, execute os dois scripts para manter as versões portátil e instalável iguais.

---

## Estrutura do repositório

```
├── README.md
├── docs/
│   └── arquitetura.md               # Arquitetura e mapeamento de requisitos
├── scripts/
│   ├── publish-portable.ps1         # Gera o executável portátil
│   ├── publish-installer.ps1        # Gera o instalador
│   └── clean-cache.ps1              # Limpa caches de build
└── src/
    ├── PrinterScanner.App/          # Aplicativo WPF
    │   ├── Infrastructure/          # ObservableObject, RelayCommand, BindingProxy
    │   ├── Models/                  # PrinterDevice, ScanRequest, AppSettings…
    │   ├── Resources/Themes/        # Tema claro e escuro
    │   ├── Services/                # Varredura, SNMP, alteração de IP, fila/spooler, drivers
    │   ├── ViewModels/
    │   └── Views/                   # Ações, alterar IP, porta USB, utilitários
    ├── PrinterScanner.Launcher/     # Executável portátil (extrai e roda o app embutido)
    └── PrinterScanner.Installer/    # Instalador
```

---

## Como usar

1. **Tela inicial:** as impressoras USB e de rede instaladas aparecem automaticamente.
2. **Interface de rede:** escolha a placa ligada à rede das impressoras. Use **Atualizar redes** depois de conectar ou desconectar uma VPN.
3. **Modo de varredura:** *Sub-rede atual*, *Faixa manual* (IP inicial e final) ou *CIDR*.
4. **Busca Ampliada:** em **Ações → Busca Ampliada**, para impressoras em outra sub-rede. Os IPs temporários são adicionados via UAC e removidos ao final.
5. **Nome e descrição:** duplo clique na coluna para editar.
6. **Alterar IP:** selecione a impressora e use **Ações → Alterar IP** (ou duplo clique na coluna IP). O app só confirma quando a impressora responde no novo IP.
7. **Problemas de fila:** duplo clique em **Offline** ou **Fila** quando aparecerem em vermelho; para trabalhos travados, **Ações → Limpar Fila** ou **Reiniciar Spooler**.
8. **Impressão de teste:** **Ações → Enviar Impressão de Teste** (ESC/POS pela porta 9100, sem driver).
9. **Compartilhar:** botão direito → **Compartilhar na Rede**.
10. **Relatório:** **Ações → Salvar Relatório** gera um `.txt` em `C:\`.

---

## Configurações e colunas

Dados em `%AppData%\PrinterScannerApp\`:

| Arquivo | Conteúdo |
|---|---|
| `settings.json` | Comunidades SNMP, timeout, concorrência, tema |
| `device-names.json` | MAC → nome personalizado |
| `device-descriptions.json` | MAC → descrição personalizada |
| `startup-error.log` | Erros de inicialização |

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `SnmpCommunities` | `public` | Comunidades SNMP tentadas em cada host |
| `TimeoutMilliseconds` | `1500` | Espera máxima por host (ms) |
| `MaxConcurrentScans` | `0` (auto) | Hosts em paralelo; `0` = `ProcessorCount × 8`, entre 8 e 64 |
| `DarkModeEnabled` | `false` | Tema escuro |

| Coluna | Fonte | Editável | Observação |
|---|---|---|---|
| **IP / Porta** | Varredura / Windows | Não | Duplo clique abre alteração de IP ou troca de porta USB |
| **Tipo** | Windows / varredura | Não | `USB` ou `Rede` |
| **Nome** | SNMP / ESC-POS / HTTP / manual | **Sim** | Salvo por MAC |
| **MAC** | Tabela ARP | Não | |
| **Máscara** | SNMP `ipAdEntNetMask` | Não | |
| **Gateway** | SNMP `ipRouteNextHop` | Não | |
| **DHCP** | SNMP do fabricante | Não | |
| **Offline** | `Win32_Printer.WorkOffline` | Duplo clique | Só aparece quando há problema |
| **Fila** | `Get-Printer.PrinterStatus` bit 1 | Duplo clique | Só aparece quando há problema |
| **Descrição** | SNMP `sysDescr` / manual | **Sim** | Salvo por MAC |

---

## Dependências

| Pacote | Versão | Uso |
|---|---|---|
| `Lextm.SharpSnmpLib` | 12.5.7 | Consultas SNMP v1/v2c |

---

## Privacidade

- Nomes, descrições e preferências ficam em `%AppData%\PrinterScannerApp`.
- O aplicativo **não se comunica com servidores externos**; a varredura ocorre só na rede definida pelo usuário.
- Logs de erro são gravados apenas localmente.
