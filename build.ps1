# build.ps1 — Compila MutagenManager v3, bundlea el CLI de mutagen y (opcional) el instalador.
# Requiere .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
# Primera vez: dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
# Para el instalador: Inno Setup 6 instalado (ISCC.exe en PATH o ruta estándar).

param(
    [switch]$Run,                  # Ejecuta el .exe tras compilar
    [switch]$Installer,            # Compila el instalador Inno Setup (installer.iss)
    [switch]$SkipMutagen,          # No re-descargar mutagen.exe si ya está en dist\
    [string]$MutagenVersion = "",  # Tag concreto (ej. v0.18.1). Vacío = última release.
    [string]$Version = ""          # Versión de la app (ej. 3.1.3). Vacío = la de los ficheros. CI la pasa desde el git tag.
)

$ErrorActionPreference = "Stop"
$projectDir  = $PSScriptRoot
$projectFile = Join-Path $projectDir "MutagenManager.csproj"
$outputDir   = Join-Path $projectDir "dist"
$mutagenExe  = Join-Path $outputDir "mutagen.exe"
$mutagenAgents = Join-Path $outputDir "mutagen-agents.tar.gz"

Write-Host ""
Write-Host "== Compilando MutagenManager v3 ==" -ForegroundColor Cyan

# Versión: la pasada por -Version (CI desde el tag) manda; si no, la de los ficheros.
$versionArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $v = $Version.TrimStart('v')                     # acepta "v3.1.3" o "3.1.3"
    Write-Host "  Versión (override): $v" -ForegroundColor White
    $versionArgs = @("-p:Version=$v", "-p:AssemblyVersion=$v.0", "-p:FileVersion=$v.0")
}

dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    @versionArgs `
    --output $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: la compilacion fallo." -ForegroundColor Red
    exit 1
}

# ── Bundle del CLI de mutagen ───────────────────────────────────────────────
if (-not ($SkipMutagen -and (Test-Path $mutagenExe) -and (Test-Path $mutagenAgents))) {
    Write-Host ""
    Write-Host "== Descargando mutagen CLI ==" -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ "User-Agent" = "MutagenManager-build" }

    if ([string]::IsNullOrWhiteSpace($MutagenVersion)) {
        $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/mutagen-io/mutagen/releases/latest" -Headers $headers
    } else {
        $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/mutagen-io/mutagen/releases/tags/$MutagenVersion" -Headers $headers
    }

    $asset = $rel.assets | Where-Object {
        $_.name -match "windows" -and $_.name -match "amd64" -and $_.name -match "\.zip$"
    } | Select-Object -First 1

    if (-not $asset) { Write-Host "ERROR: no se encontro binario windows/amd64 en la release." -ForegroundColor Red; exit 1 }

    Write-Host "  Versión: $($rel.tag_name)  ($($asset.name))" -ForegroundColor White
    $tmp = Join-Path $env:TEMP "mutagen-build"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp $asset.name
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers $headers

    $extract = Join-Path $tmp "extract"
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $found = Get-ChildItem -Path $extract -Filter "mutagen.exe" -Recurse | Select-Object -First 1
    if (-not $found) { Write-Host "ERROR: el zip no contenia mutagen.exe." -ForegroundColor Red; exit 1 }
    Copy-Item $found.FullName $mutagenExe -Force
    Write-Host "  mutagen.exe bundleado en dist\ (pinned: $($rel.tag_name))" -ForegroundColor Green

    # Agent bundle: lo necesita mutagen para instalar el agente POSIX en el server.
    # Sin el (search paths junto a mutagen.exe) -> "unable to locate agent bundle" al crear sync.
    $agents = Get-ChildItem -Path $extract -Filter "mutagen-agents.tar.gz" -Recurse | Select-Object -First 1
    if (-not $agents) { Write-Host "ERROR: el zip no contenia mutagen-agents.tar.gz." -ForegroundColor Red; exit 1 }
    Copy-Item $agents.FullName $mutagenAgents -Force
    Write-Host "  mutagen-agents.tar.gz bundleado en dist\" -ForegroundColor Green
}

$exePath = Join-Path $outputDir "MutagenManager.exe"
Write-Host ""
Write-Host "OK - dist\ listo:" -ForegroundColor Green
Write-Host "  $exePath" -ForegroundColor White
Write-Host "  $mutagenExe" -ForegroundColor White

# ── Instalador Inno Setup ───────────────────────────────────────────────────
if ($Installer) {
    Write-Host ""
    Write-Host "== Compilando instalador (Inno Setup) ==" -ForegroundColor Cyan
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source
    if (-not $iscc) {
        foreach ($p in @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
            if (Test-Path $p) { $iscc = $p; break }
        }
    }
    if (-not $iscc) {
        Write-Host "ERROR: ISCC.exe no encontrado. Instala Inno Setup 6: https://jrsoftware.org/isdl.php" -ForegroundColor Red
        exit 1
    }
    $isccArgs = @()
    if (-not [string]::IsNullOrWhiteSpace($Version)) { $isccArgs += "/DAppVersion=$($Version.TrimStart('v'))" }
    & $iscc @isccArgs (Join-Path $projectDir "installer.iss")
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: compilacion del instalador fallo." -ForegroundColor Red; exit 1 }
    Write-Host "  Instalador generado en dist\MutagenManager-Setup-*.exe" -ForegroundColor Green
}

if ($Run) {
    Write-Host ""
    Write-Host "Iniciando MutagenManager..." -ForegroundColor Cyan
    Start-Process $exePath -WorkingDirectory $outputDir
}
