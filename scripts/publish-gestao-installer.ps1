param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectRoot      = Split-Path -Parent $PSScriptRoot
$AppProject       = Join-Path $ProjectRoot "src\PrinterManager.App\PrinterManager.App.csproj"
$InstallerProject = Join-Path $ProjectRoot "src\PrinterManager.Installer\PrinterManager.Installer.csproj"
$EmbeddedDir      = Join-Path $ProjectRoot "src\PrinterManager.Installer\embedded-app"
$PublishDir       = Join-Path $ProjectRoot "publish\gestao-$Runtime-installer"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "O SDK do .NET 8 nao foi encontrado no PATH. Instale o SDK e execute este script novamente."
}

if (Test-Path $PublishDir)  { Remove-Item $PublishDir -Recurse -Force }
if (Test-Path $EmbeddedDir) { Remove-Item $EmbeddedDir -Recurse -Force }
New-Item -ItemType Directory -Force $EmbeddedDir | Out-Null
New-Item -ItemType Directory -Force $PublishDir  | Out-Null

# ------------------------------------------------------------------
# Etapa 1: Gestao de Impressoras como framework-dependent (arquivo unico)
# ------------------------------------------------------------------
Write-Host "Etapa 1/2: publicando Gestao de Impressoras (framework-dependent)..."

& $dotnet.Source publish $AppProject `
    -c Release -r $Runtime --self-contained false `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None /p:DebugSymbols=false /p:PublishReadyToRun=false `
    ("/p:PublishDir=" + $EmbeddedDir)

if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar PrinterManager.App (codigo $LASTEXITCODE)" }

$publishedApp = Join-Path $EmbeddedDir "GestaoImpressoras.exe"
$target       = Join-Path $EmbeddedDir "GestaoImpressoras.App.exe"
if (Test-Path $target) { Remove-Item $target -Force }
Rename-Item $publishedApp "GestaoImpressoras.App.exe" -Force

$appSize = [Math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host "  App embutido: $appSize MB"
Write-Host ""

# ------------------------------------------------------------------
# Etapa 2: Instalador self-contained (embute o app)
# ------------------------------------------------------------------
Write-Host "Etapa 2/2: publicando o instalador (self-contained)..."

& $dotnet.Source publish $InstallerProject `
    -c Release -r $Runtime --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None /p:DebugSymbols=false /p:PublishReadyToRun=false `
    ("/p:PublishDir=" + $PublishDir)

if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o instalador (codigo $LASTEXITCODE)" }

Write-Host ""
Write-Host "==========================================="
$setupExe = Get-ChildItem $PublishDir -Filter "*.exe" | Select-Object -First 1
if ($setupExe) {
    $setupSize = [Math]::Round($setupExe.Length / 1MB, 1)
    Write-Host "Instalador gerado : $($setupExe.FullName)"
    Write-Host "Tamanho           : $setupSize MB"
} else {
    Write-Host "Publicacao concluida em: $PublishDir"
}
Write-Host "==========================================="
