# build.ps1 — Compila MutagenManager v3, bundlea el CLI de mutagen y (opcional) el instalador.
# Requiere .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
# Primera vez: dotnet nuget add source https://api.nuget.org/v3/index.json --name nuget.org
# Para el instalador: Inno Setup 6 instalado (ISCC.exe en PATH o ruta estándar).

param(
    [switch]$Run,                  # Ejecuta el .exe tras compilar
    [switch]$Installer,            # Compila el instalador Inno Setup (installer.iss)
    [switch]$SkipMutagen,          # No re-descargar mutagen.exe si ya está en dist\
    [string]$MutagenVersion = ""   # Tag concreto (ej. v0.18.1). Vacío = última release.
)

$ErrorActionPreference = "Stop"
$projectDir  = $PSScriptRoot
$projectFile = Join-Path $projectDir "MutagenManager.csproj"
$outputDir   = Join-Path $projectDir "dist"
$mutagenExe  = Join-Path $outputDir "mutagen.exe"

Write-Host ""
Write-Host "== Compilando MutagenManager v3 ==" -ForegroundColor Cyan

dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --output $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: la compilacion fallo." -ForegroundColor Red
    exit 1
}

# ── Bundle del CLI de mutagen ───────────────────────────────────────────────
if (-not ($SkipMutagen -and (Test-Path $mutagenExe))) {
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
    & $iscc (Join-Path $projectDir "installer.iss")
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: compilacion del instalador fallo." -ForegroundColor Red; exit 1 }
    Write-Host "  Instalador generado en dist\MutagenManager-Setup-*.exe" -ForegroundColor Green
}

if ($Run) {
    Write-Host ""
    Write-Host "Iniciando MutagenManager..." -ForegroundColor Cyan
    Start-Process $exePath -WorkingDirectory $outputDir
}
