# Gestão de Impressoras

**Versão 1.0** · Windows 10/11 · .NET 8 WPF · MVVM · SQLite

Aplicativo desktop instalável para monitoramento contínuo de um parque de impressoras via SNMP: suprimentos, desgaste de peças, contadores para faturamento MPS, alertas e histórico de manutenções. Roda localmente na máquina, sem servidor web. Os dados ficam num banco SQLite compartilhado por todos os usuários do Windows daquela estação.

Projeto: [`src/PrinterManager.App`](../src/PrinterManager.App) · Instalador: [`src/PrinterManager.Installer`](../src/PrinterManager.Installer)

---

## Instalação

1. Gere o instalador: `.\scripts\publish-gestao-installer.ps1`
2. Execute `publish\gestao-win-x64-installer\GestaoImpressoras-Setup.exe` como administrador.
   - Se faltar, ele instala o .NET 8 Desktop Runtime.
   - Instala em `C:\Program Files\Gestao de Impressoras` e cria atalhos.
   - Cria `C:\ProgramData\GestaoImpressoras` e dá permissão de gravação ao grupo *Usuários*.
3. No primeiro acesso, o app pede a criação do usuário **Administrador**.

A desinstalação (Painel de Controle ou `desinstalar.bat`) **mantém** os dados em `%ProgramData%`, para preservar o histórico de contadores e a auditoria.

## Dados

| Item | Local |
|---|---|
| Banco de dados | `%ProgramData%\GestaoImpressoras\gestao.db` |
| Backups diários | `...\GestaoImpressoras\Backups` (a pasta pode ser alterada em Configurações) |
| Logs técnicos | `...\GestaoImpressoras\Logs` |

Para testes, a variável de ambiente `GESTAO_IMPRESSORAS_DATA` aponta o app para outra pasta.

## Perfis de acesso

| Ação | Administrador | Técnico | Visualizador |
|---|:-:|:-:|:-:|
| Ver painel, impressoras, alertas e relatórios | ✔ | ✔ | ✔ |
| Cadastrar/editar impressoras e registrar manutenções | ✔ | ✔ | — |
| Resetar contador de peça (troca física) | ✔ | ✔ | — |
| Excluir impressora | precisa da senha de um Administrador | precisa da senha de um Administrador | — |
| Usuários, configurações globais, auditoria, backup | ✔ | — | — |

---

## Mapeamento dos requisitos

### Requisitos funcionais

| Req. | Implementação |
|---|---|
| RF01 | `DiscoveryService.ScanAsync` — varre faixa de IPs ou rede CIDR com 64 consultas SNMP em paralelo e reconhece impressoras pela Printer-MIB. Tela *Descoberta na rede*. |
| RF02 | `PrinterEditWindow` + `PrinterService.Save`: IP, hostname, setor, local, modelo, série e credenciais SNMP v1/v2c/v3. |
| RF03 | `PrinterSnmpReader.ReadSupplies` lê `prtMarkerSuppliesTable`; a cor vem de `prtMarkerColorantValue` ou da descrição. |
| RF04 | Duas fontes: peças controladas pelo sistema (fusor, transferência, pickup — RN08) e itens que o próprio equipamento informa (fusor, cilindro, transferência). |
| RF05 | `prtMarkerLifeCount` (total). Mono/cor vêm do perfil HP ou de OIDs configuráveis por impressora; em impressora monocromática, mono = total. |
| RF06 | `DashboardView`: indicadores, cartões com barras CMYK, status, peça mais desgastada e alertas, com filtro por setor. |
| RF07 | Faixa vermelha fixa no topo, piscar da faixa e da barra de tarefas, e som do Windows quando surge um alerta crítico novo. |
| RF08 | `PollingService`: verifica a cada minuto quais impressoras estão vencidas pelo intervalo global ou próprio. Há também o botão "Atualizar agora". |
| RF09 | `ReportService`: consumo de suprimentos, contadores de páginas, franquia mensal e manutenções, exportados em **PDF** (QuestPDF) e **CSV** (`;` + UTF-8 BOM, para Excel pt-BR). |
| RF10 | `MaintenanceRecord` + telas *Manutenções* e aba da impressora. O reset de peça gera o registro automaticamente. |
| RF11 | `AuthService` + `Session`: perfis Administrador, Técnico e Visualizador, aplicados nos serviços e não só na interface. |
| RF12 | `NotificationService` com canais `INotificationChannel`: e-mail (SMTP/TLS) e webhooks HTTPS. O payload traz `text`, aceito por Teams/Slack/Google Chat, e o JSON estruturado. |
| RF13 | Botão *Testar conexão* no cadastro: ping + GET SNMP de sysDescr, modelo, série e contador. Preenche os campos vazios. |
| RF14 | `hrPrinterDetectedErrorState` (bits decodificados), `prtAlertTable` (códigos RFC 3805 traduzidos) e mensagem do painel. Histórico na aba *Erros e eventos do firmware*. |
| RF15 | Busca por nome/IP/série/local e filtros por status, situação, setor e modelo. |

### Regras de negócio

| Regra | Implementação |
|---|---|
| RN01 | Toner/tinta com nível ≤ 5% gera alerta **Crítico**. O limiar é configurável. |
| RN02 | Peça com desgaste ≥ 90% gera alerta preventivo (*Aviso*). |
| RN03 | `Session.RequireAdmin()` em configurações, usuários, auditoria e backup. O menu também oculta essas telas. |
| RN04 | `AppSettingsModel.MinPollIntervalMinutes = 15`, validado tanto no intervalo global quanto no de cada impressora. |
| RN05 | Até 3 tentativas consecutivas por varredura. Falhando todas, o status vira **Offline** e abre alerta crítico. |
| RN06 | Leituras de contador gravadas sempre que mudam (e no mínimo 1×/dia), **sem expurgo**. |
| RN07 | Visualizador é bloqueado nos serviços (`RequireEdit`), não apenas nos botões. |
| RN08 | `WearPart.WearPercent` = (contador atual − contador na última troca) ÷ vida nominal. |
| RN09 | A exclusão pede login e senha de um Administrador e faz *soft delete*. Os relatórios mantêm o histórico ("(excluída)"). |
| RN10 | O disparo acontece logo ao fim da varredura que criou o alerta, com reenvio a cada minuto em caso de falha. |
| RN11 | 5 senhas erradas seguidas bloqueiam o usuário por 15 minutos. O administrador pode desbloquear. |
| RN12 | *Trocar / resetar* só para Técnico/Administrador; registra a manutenção e encerra o alerta da peça. |
| RN13 | Impressoras inativas ou descartadas não geram alertas, não são consultadas e não contam como licença. |
| RN14 | Cada parâmetro alterado gera um registro de auditoria com data, ID do usuário e valores antes/depois. |
| RN15 | `FranchiseService.CloseDueMonths`: no 1º dia útil (seg–sex) fecha o mês anterior por impressora e reinicia o acumulado. |

### Requisitos não funcionais — interpretação para app desktop

| Req. | Situação |
|---|---|
| RNF01 | ✔ v1, v2c e v3. Autenticação MD5/SHA-1/SHA-2 e criptografia DES/3DES/AES-128/192/256. |
| RNF02 | ✔ O painel lê só o banco local. 500 impressoras são carregadas em ~15 ms (medido). |
| RNF03 | ✔ Consultas paralelas com limite configurável (padrão 16). |
| RNF04 | ⚠ Não se aplica a desktop. O monitoramento roda enquanto o app está aberto; fechar a janela encerra as varreduras. |
| RNF05 | ✔ bcrypt (fator 12). |
| RNF06 | ⚠ Não há agentes remotos. Webhooks só por HTTPS (TLS 1.2/1.3), SMTP com SSL/TLS e segredos criptografados com DPAPI. TLS 1.3 depende do Windows (Win 11 / Server 2022). |
| RNF07 | ⚠ A janela é redimensionável e os painéis se reorganizam (WrapPanel). Não roda em celular, por ser desktop. |
| RNF08 | ✔ Backup diário automático pela API online do SQLite, com retenção configurável. |
| RNF09 | ⚠ Windows 10/11 e Windows Server (WPF). Linux não é suportado. |
| RNF10 | ✔ Triggers bloqueiam UPDATE/DELETE na auditoria e os registros são encadeados por SHA-256, com verificação na tela *Auditoria*. Não há expurgo. |
| RNF11 | ✔ I/O assíncrono com paralelismo limitado. |
| RNF12 | ✔ Estado sempre em texto além da cor, nomes de acessibilidade em todos os campos, foco visível e navegação por teclado. |
| RNF13 | ✔ Serviços isolados e `INotificationChannel` para novos canais (Service Desk/ERP). |
| RNF14 | ⚠ Não há API REST, porque o app não é web. Uma API local pode ser acrescentada no futuro sobre os mesmos serviços. |
| RNF15 | ✔ *Configurações → Restaurar backup* (guarda uma cópia do banco atual antes) e reinicia o app. |

---

## Observações técnicas

- **Contadores mono/cor**: a Printer-MIB padrão só traz o total. Há um perfil HP embutido. Para outros fabricantes, informe os OIDs em *Editar → Opções avançadas*.
- **Peças padrão**: toda impressora nova recebe Unidade de fusão (100.000), Rolo de transferência (120.000) e Rolos de alimentação (50.000). Ajuste a vida nominal conforme o manual do fabricante. O contador de partida é a primeira leitura feita.
- **Segurança local**: qualquer usuário do Windows com acesso à pasta de dados pode alterar o arquivo do banco diretamente. As permissões do app valem dentro dele, e a cadeia de hash da auditoria acusa adulteração externa.
- **Uma instância por máquina**: um mutex global impede duas varreduras concorrentes no mesmo banco.
- **QuestPDF** usa a licença *Community*, gratuita para empresas com receita anual abaixo de US$ 1 milhão. Acima disso, é preciso licença comercial.
- **Banco**: o esquema é criado por `EnsureCreated`. Mudanças futuras de esquema vão exigir migrações (EF Core Migrations).

## Dependências

| Pacote | Uso |
|---|---|
| Lextm.SharpSnmpLib 12.5.7 | SNMP v1/v2c/v3 |
| Microsoft.EntityFrameworkCore.Sqlite 8.0.11 | Banco local |
| BCrypt.Net-Next 4.0.3 | Hash de senhas |
| QuestPDF 2024.10.2 | Relatórios PDF |
| System.Security.Cryptography.ProtectedData 8.0.0 | DPAPI para segredos |
