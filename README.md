# Ferramentas de Impressoras

Windows 10/11 · .NET 8 · WPF · MVVM

Dois aplicativos desktop para cuidar de impressoras em ambientes corporativos: um para **encontrar, diagnosticar e configurar** impressoras, e outro para **monitorar o parque continuamente** (suprimentos, peças, contadores e alertas).

| | Utilitário de Impressoras | Gestão de Impressoras |
|---|---|---|
| **Versão** | 3.0 | 1.0 |
| **Para que serve** | Ações pontuais: descobrir impressoras, alterar IP, filas, spooler, drivers, porta USB | Acompanhamento diário: níveis de toner, desgaste de peças, contadores para faturamento, alertas e manutenções |
| **Uso típico** | Técnico em atendimento, instalação ou suporte | Gestor ou equipe técnica que cuida do parque |
| **Dados** | Arquivos JSON por usuário (`%AppData%`) | Banco SQLite compartilhado na máquina (`%ProgramData%`) |
| **Usuários e senhas** | Não | Sim (Administrador, Técnico, Visualizador) |
| **Projeto** | [`src/PrinterScanner.App`](src/PrinterScanner.App) | [`src/PrinterManager.App`](src/PrinterManager.App) |
| **Distribuição** | Portátil (exe único) ou instalador | Instalador |

Os dois são independentes e podem ser instalados na mesma máquina.

---

## Sumário

- [Compilar e publicar](#compilar-e-publicar)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Utilitário de Impressoras](#utilitário-de-impressoras)
  - [Funcionalidades](#funcionalidades)
  - [Alterar IP — protocolos suportados](#alterar-ip--protocolos-suportados)
  - [Identificação via SNMP](#identificação-via-snmp)
  - [Como usar](#como-usar)
  - [Configurações e colunas](#configurações-e-colunas)
- [Gestão de Impressoras](#gestão-de-impressoras)
  - [Funcionalidades](#funcionalidades-1)
  - [Instalação e primeiro acesso](#instalação-e-primeiro-acesso)
  - [Perfis de acesso](#perfis-de-acesso)
  - [Regras de alerta e monitoramento](#regras-de-alerta-e-monitoramento)
  - [Dados, backup e auditoria](#dados-backup-e-auditoria)
- [Dependências](#dependências)
- [Privacidade](#privacidade)

---

## Compilar e publicar

**Pré-requisitos:** Windows 10/11 64-bit e o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Para rodar os apps sem instalador é preciso o .NET 8 Desktop Runtime; os instaladores baixam o runtime quando ele falta.

### Compilar

```powershell
dotnet build src\PrinterScanner.App\PrinterScanner.App.csproj -c Release   # Utilitário
dotnet build src\PrinterManager.App\PrinterManager.App.csproj -c Release   # Gestão
```

### Publicar

```powershell
# Utilitário de Impressoras
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
powershell -ExecutionPolicy Bypass -File scripts\publish-installer.ps1

# Gestão de Impressoras
powershell -ExecutionPolicy Bypass -File scripts\publish-gestao-installer.ps1
```

Resultados:

```
publish\win-x64-portable\
└── PrinterScanner.exe            ← Utilitário portátil, sem dependências (~11 MB)

publish\win-x64-installer\
└── PrinterScanner-Setup.exe      ← Utilitário: instalador com runtime e utilitários (~72 MB)

publish\gestao-win-x64-installer\
└── GestaoImpressoras-Setup.exe   ← Gestão: instalador (~45 MB)
```

> Depois de alterar o código do Utilitário, execute os dois scripts dele para manter as versões portátil e instalável iguais.

---

## Estrutura do repositório

```
├── README.md
├── docs/
│   ├── arquitetura.md                   # Arquitetura do Utilitário
│   └── gestao-impressoras.md            # Gestão: requisitos mapeados para o código
├── scripts/
│   ├── publish-portable.ps1             # Utilitário portátil
│   ├── publish-installer.ps1            # Utilitário instalador
│   ├── publish-gestao-installer.ps1     # Gestão instalador
│   └── clean-cache.ps1                  # Limpa caches de build
└── src/
    ├── PrinterScanner.App/              # Utilitário de Impressoras (WPF)
    │   ├── Infrastructure/              # ObservableObject, RelayCommand, BindingProxy
    │   ├── Models/                      # PrinterDevice, ScanRequest, AppSettings…
    │   ├── Resources/Themes/            # Tema claro e escuro
    │   ├── Services/                    # Varredura, SNMP, alteração de IP, fila/spooler, drivers
    │   ├── ViewModels/
    │   └── Views/                       # Ações, alterar IP, porta USB, utilitários
    ├── PrinterScanner.Launcher/         # Executável portátil (extrai e roda o app embutido)
    ├── PrinterScanner.Installer/        # Instalador do Utilitário
    ├── PrinterManager.App/              # Gestão de Impressoras (WPF + SQLite)
    │   ├── Data/                        # DbContext EF Core e inicialização do banco
    │   ├── Infrastructure/              # MVVM, comandos assíncronos, conversores
    │   ├── Models/                      # Impressora, suprimento, peça, alerta, auditoria…
    │   ├── Resources/                   # Tema, estilos e ícone
    │   ├── Services/                    # SNMP, agendador, alertas, notificações, relatórios,
    │   │                                # backup, auditoria e usuários
    │   ├── ViewModels/
    │   └── Views/                       # Painel, impressoras, descoberta, alertas, relatórios…
    └── PrinterManager.Installer/        # Instalador do Gestão
```

---

## Utilitário de Impressoras

Ao abrir, já mostra as impressoras USB e de rede instaladas no Windows. Ao varrer, encontra novas impressoras na rede e permite alterar IP, gerenciar filas, instalar drivers e mais.

### Funcionalidades

#### Descoberta

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

#### Status, fila e spooler

| Funcionalidade | Descrição |
|---|---|
| **Modo offline** | Detecta "Usar impressora offline"; duplo clique na coluna desativa |
| **Fila pausada** | Detecta fila pausada; duplo clique na coluna retoma |
| **Colunas contextuais** | "Offline" e "Fila" só aparecem quando há problema |
| **Limpar fila** | Cancela os trabalhos da impressora selecionada ou de todas |
| **Pausar / retomar fila** | Via WMI (`Win32_Printer.Pause/Resume`) |
| **Reiniciar Spooler** | Para o serviço, apaga `%SystemRoot%\System32\spool\PRINTERS\*.*` e reinicia (UAC) |

#### Rede, impressão e driver

| Funcionalidade | Descrição |
|---|---|
| **Alterar IP** | Reconfigura IP, máscara e gateway, com protocolo detectado pelo modelo; confirma por ping e porta 9100 no novo IP |
| **Impressão de teste** | Página ESC/POS direta pela porta TCP 9100, sem driver; alternativa via Windows |
| **Instalar driver** | Executa o instalador embutido com elevação de administrador |
| **Compartilhar na rede** | Ativa ou remove o compartilhamento; avisa quando falta driver |
| **Alterar porta USB** | Move a impressora para outra porta (USB001, USB002…) sem reinstalar o driver |

#### Personalização

| Funcionalidade | Descrição |
|---|---|
| **Nome e descrição editáveis** | Duplo clique para editar; salvos por MAC e restaurados mesmo se o IP mudar |
| **Renomear no Windows** | Ao salvar um nome, a impressora instalada na porta correspondente é renomeada |
| **Relatório .txt** | Exporta o inventário para `C:\impressoras_AAAA-MM-DD_HH-mm.txt` |
| **Tema claro / escuro** | Alternância instantânea, salva entre sessões |
| **Utilitários** | Acesso a ferramentas externas de diagnóstico e configuração |

### Alterar IP — protocolos suportados

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

### Identificação via SNMP

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

### Como usar

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

### Configurações e colunas

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

## Gestão de Impressoras

Monitora o parque continuamente via SNMP e avisa a equipe antes que falte toner ou que uma peça quebre. Roda localmente na máquina, sem servidor web.

Documentação completa, com cada requisito mapeado para o código: [docs/gestao-impressoras.md](docs/gestao-impressoras.md).

### Funcionalidades

| Tela | O que faz |
|---|---|
| **Painel** | Saúde de todas as impressoras: status, níveis CMYK, peça mais desgastada, contador e alertas; indicadores de online, offline e licenças em uso; filtro por setor |
| **Impressoras** | Lista com busca por nome, IP, série ou local e filtros por status, situação, setor e modelo; cadastro, edição, exclusão e atualização sob demanda |
| **Detalhes da impressora** | Suprimentos, peças de desgaste (com troca/reset), erros do firmware, histórico de contadores, manutenções e alertas |
| **Descoberta na rede** | Varredura por faixa de IP ou CIDR via SNMP (v1, v2c ou v3) e cadastro em lote das encontradas |
| **Alertas** | Alertas ativos e resolvidos, com reconhecimento por técnico |
| **Manutenções** | Histórico de serviços e trocas de peças por impressora e período |
| **Relatórios** | Consumo de suprimentos, contadores de páginas, franquia mensal e manutenções, exportados em **PDF** e **CSV** |
| **Auditoria** | Registro de acessos e alterações, com verificação de integridade |
| **Usuários** | Cadastro, perfis, redefinição de senha e desbloqueio |
| **Configurações** | Limiares de alerta, agendamento, SNMP padrão, e-mail, webhooks e backup |

Outros pontos:

- **SNMP v1, v2c e v3**, com autenticação MD5/SHA-1/SHA-2 e criptografia DES/3DES/AES-128/192/256.
- **Teste de conexão** (ping + SNMP) ao cadastrar ou editar, preenchendo modelo, série e hostname automaticamente.
- **Erros do firmware** lidos da Printer-MIB e Host-Resources-MIB (porta aberta, atolamento, sem papel, serviço solicitado…).
- **Notificações** por e-mail (SMTP com SSL/TLS) e webhooks HTTPS compatíveis com Teams, Slack e Google Chat.

### Instalação e primeiro acesso

1. Gere o instalador com `scripts\publish-gestao-installer.ps1` (ver [Compilar e publicar](#compilar-e-publicar)).
2. Execute `GestaoImpressoras-Setup.exe` como administrador. Ele instala o .NET 8 Desktop Runtime se necessário, instala em `C:\Program Files\Gestao de Impressoras` e cria atalhos na Área de Trabalho e no Menu Iniciar.
3. No primeiro acesso, o app pede a criação do usuário **Administrador**.
4. Cadastre as impressoras em **Impressoras → Nova impressora** ou pela **Descoberta na rede**.

Para atualizar, basta executar um instalador mais novo: ele fecha o app aberto e preserva os dados. A desinstalação também mantém os dados.

### Perfis de acesso

| Ação | Administrador | Técnico | Visualizador |
|---|:-:|:-:|:-:|
| Ver painel, impressoras, alertas e relatórios | ✔ | ✔ | ✔ |
| Cadastrar e editar impressoras, registrar manutenções, reconhecer alertas | ✔ | ✔ | — |
| Resetar contador de peça após troca física | ✔ | ✔ | — |
| Excluir impressora | com senha de Administrador | com senha de Administrador | — |
| Usuários, configurações globais, auditoria e backup | ✔ | — | — |

Senhas armazenadas com bcrypt. Após 5 tentativas incorretas seguidas o usuário fica bloqueado por 15 minutos (o Administrador pode desbloquear).

### Regras de alerta e monitoramento

| Regra | Comportamento |
|---|---|
| **Suprimento crítico** | Toner ou tinta com 5% ou menos gera alerta crítico (limiar configurável) |
| **Desgaste de peça** | Peça com 90% ou mais da vida útil gera alerta preventivo de troca |
| **Cálculo do desgaste** | Páginas impressas desde a última troca ÷ vida útil nominal do fabricante |
| **Peças padrão** | Unidade de fusão (100.000), rolo de transferência (120.000) e rolos de alimentação (50.000), ajustáveis por impressora |
| **Varredura automática** | Intervalo global ou por impressora, nunca menor que 15 minutos |
| **Offline** | 3 tentativas consecutivas sem resposta SNMP marcam a impressora como Offline |
| **Notificações** | Disparadas logo após a varredura que confirmou o alerta; falhas são reenviadas a cada minuto |
| **Impressoras inativas ou descartadas** | Não são consultadas, não geram alertas e não contam como licença |
| **Exclusão** | Exige senha de Administrador e é lógica: o histórico continua nos relatórios |
| **Franquia mensal** | No primeiro dia útil do mês o período anterior é fechado por impressora e o acumulado recomeça |
| **Contadores mono/cor** | O total vem da Printer-MIB; mono/cor usam o perfil HP embutido ou OIDs informados no cadastro (*Opções avançadas*) |

### Dados, backup e auditoria

| Item | Local |
|---|---|
| Banco de dados | `%ProgramData%\GestaoImpressoras\gestao.db` |
| Backups diários | `%ProgramData%\GestaoImpressoras\Backups` (pasta configurável; recomenda-se uma pasta de rede) |
| Logs técnicos | `%ProgramData%\GestaoImpressoras\Logs` |

- **Banco compartilhado:** todos os usuários do Windows da máquina usam o mesmo banco; o grupo *Usuários* recebe permissão de gravação na pasta.
- **Uma instância por máquina:** impede duas varreduras simultâneas sobre o mesmo banco.
- **Backup diário automático** com retenção configurável e **restauração** em *Configurações* (uma cópia do banco atual é feita antes).
- **Auditoria imutável:** alterações e exclusões na tabela de auditoria são bloqueadas no banco e cada registro é encadeado por hash SHA-256. Mudanças nos limiares de alerta são registradas com data e ID do usuário. Não há expurgo, assim como no histórico de contadores.
- **Segredos** (senhas SNMPv3 e SMTP) criptografados com DPAPI da máquina.

O monitoramento roda enquanto o app está aberto; fechar a janela interrompe as varreduras.

---

## Dependências

| Pacote | Versão | Usado por | Uso |
|---|---|---|---|
| `Lextm.SharpSnmpLib` | 12.5.7 | Ambos | SNMP (v1/v2c no Utilitário; v1/v2c/v3 no Gestão) |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.11 | Gestão | Banco de dados local |
| `BCrypt.Net-Next` | 4.0.3 | Gestão | Hash de senhas |
| `QuestPDF` | 2024.10.2 | Gestão | Relatórios em PDF |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | Gestão | Criptografia de segredos (DPAPI) |

> O QuestPDF usa a licença *Community*, gratuita para empresas com receita anual abaixo de US$ 1 milhão. Acima disso é necessária licença comercial.

---

## Privacidade

- **Utilitário:** nomes, descrições e preferências ficam em `%AppData%\PrinterScannerApp`. Não há comunicação com servidores externos; a varredura ocorre só na rede definida pelo usuário.
- **Gestão:** os dados ficam no banco local em `%ProgramData%\GestaoImpressoras`. A única comunicação externa são as notificações por e-mail e webhook, quando o Administrador as configura.
- Os logs de ambos são gravados apenas localmente.
