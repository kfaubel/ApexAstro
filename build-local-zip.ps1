param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$Runtime = "win-x64",

    [Parameter(Mandatory = $false)]
    [string]$PublishDir = "publish",

    [Parameter(Mandatory = $false)]
    [string]$ZipFile = "ApexAstro-win-x64-local.zip",

    [Parameter(Mandatory = $false)]
    [switch]$NoClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

$publishPath = Join-Path $repoRoot $PublishDir
$zipPath = Join-Path $repoRoot $ZipFile

$wpfProject = Join-Path $repoRoot "ApexAstro.Wpf\ApexAstro.Wpf.csproj"
$cliProject = Join-Path $repoRoot "compute-snr\compute-snr.csproj"
$snrDoc = Join-Path $repoRoot "SNR.md"

Write-Host "Building local release package..." -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration"
Write-Host "  Runtime:       $Runtime"
Write-Host "  Publish path:  $publishPath"
Write-Host "  Zip path:      $zipPath"

if (-not $NoClean) {
    if (Test-Path $publishPath) {
        Write-Host "Cleaning existing publish folder..." -ForegroundColor Yellow
        Remove-Item -Path (Join-Path $publishPath "*") -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        New-Item -ItemType Directory -Path $publishPath | Out-Null
    }

    if (Test-Path $zipPath) {
        Write-Host "Removing existing zip..." -ForegroundColor Yellow
        Remove-Item -Path $zipPath -Force
    }
} else {
    if (-not (Test-Path $publishPath)) {
        New-Item -ItemType Directory -Path $publishPath | Out-Null
    }
}

Write-Host "Publishing ApexAstro UI..." -ForegroundColor Cyan
dotnet publish $wpfProject -c $Configuration -r $Runtime --no-self-contained --output $publishPath

Write-Host "Publishing compute-snr CLI..." -ForegroundColor Cyan
dotnet publish $cliProject -c $Configuration -r $Runtime --no-self-contained --output $publishPath

if (Test-Path $snrDoc) {
    Write-Host "Copying SNR.md..." -ForegroundColor Cyan
    Copy-Item -Path $snrDoc -Destination (Join-Path $publishPath "SNR.md") -Force
}

$uiExe = Join-Path $publishPath "ApexAstro.exe"
$cliExe = Join-Path $publishPath "compute-snr.exe"

if (-not (Test-Path $uiExe)) {
    throw "Missing expected UI executable: $uiExe"
}

if (-not (Test-Path $cliExe)) {
    throw "Missing expected CLI executable: $cliExe"
}

Write-Host "Creating zip package..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $zipPath -Force

Write-Host "Done." -ForegroundColor Green
Write-Host "Created: $zipPath" -ForegroundColor Green
Write-Host "Included executables:" -ForegroundColor Green
Write-Host "  - $uiExe"
Write-Host "  - $cliExe"
