param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('major', 'minor', 'patch')]
    [string]$BumpType = 'patch'
)

$ErrorActionPreference = "Stop"

function Ensure-ReleaseNotesFile {
    $releaseNotesPath = "RELEASE_NOTES.md"

    if (-not (Test-Path $releaseNotesPath)) {
        Write-Host "RELEASE_NOTES.md not found. Creating it now..." -ForegroundColor Yellow
        Set-Content -Path $releaseNotesPath -Value "# Release Notes`r`n`r`n" -NoNewline
    }

    return $releaseNotesPath
}

function Test-ReleaseNotesSectionExists {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [string]$Version
    )

    if (-not (Test-Path $Path)) {
        return $false
    }

    $content = Get-Content $Path -Raw
    $versionEscaped = [regex]::Escape($Version)
    $pattern = "(?im)^\s{0,3}#{1,6}\s*v?$versionEscaped(\s|$)"
    return [regex]::IsMatch($content, $pattern)
}

function Add-ReleaseNotesSectionIfMissing {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [Parameter(Mandatory=$true)]
        [string]$Version
    )

    if (Test-ReleaseNotesSectionExists -Path $Path -Version $Version) {
        return
    }

    $existingContent = ""
    if (Test-Path $Path) {
        $existingContent = Get-Content $Path -Raw
    }

    $today = Get-Date -Format "yyyy-MM-dd"
    $newSection = "## v$Version - $today`r`n`r`n- TODO: summarize changes`r`n`r`n"
    $updatedContent = $newSection + $existingContent.TrimStart()

    Set-Content -Path $Path -Value $updatedContent -NoNewline
    Write-Host "Added new section for v$Version at top of $Path" -ForegroundColor Green
}

function Show-ReleaseNotesPreview {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        [int]$TailLineCount = 60
    )

    Write-Host "`n---- Release Notes Preview ($Path, last $TailLineCount lines) ----" -ForegroundColor Cyan
    Get-Content $Path -Tail $TailLineCount | ForEach-Object { Write-Host $_ }
    Write-Host "---- End Preview ----`n" -ForegroundColor Cyan
}

function Confirm-ReleaseNotes {
    param(
        [string]$Path,
        [string]$Version
    )

    if (-not (Test-Path $Path)) {
        Write-Host "Error: $Path was not found." -ForegroundColor Red
        return $false
    }

    Write-Host "Release notes file found: $Path" -ForegroundColor Green

    Add-ReleaseNotesSectionIfMissing -Path $Path -Version $Version

    while ($true) {
        if (Test-ReleaseNotesSectionExists -Path $Path -Version $Version) {
            Write-Host "Release notes include a section for v$Version." -ForegroundColor Green
        }
        else {
            Write-Host "Release notes still do not include a section header for v$Version." -ForegroundColor Yellow
        }

        $previewNow = Read-Host "Preview release notes now? (y/n)"
        if ($previewNow -eq 'y' -or $previewNow -eq 'Y') {
            Show-ReleaseNotesPreview -Path $Path
        }

        $editNow = Read-Host "Open release notes in Notepad for edits? (y/n)"
        if ($editNow -eq 'y' -or $editNow -eq 'Y') {
            Start-Process notepad.exe $Path
            Read-Host "Press Enter after saving and closing Notepad"
        }

        if (-not (Test-ReleaseNotesSectionExists -Path $Path -Version $Version)) {
            Write-Host "Cannot continue: please add a section heading for v$Version in $Path." -ForegroundColor Red
            continue
        }

        $notesConfirmed = Read-Host "Are the release notes for v$Version ready to include in this release commit? (y/n)"
        if ($notesConfirmed -eq 'y' -or $notesConfirmed -eq 'Y') {
            return $true
        }

        $retry = Read-Host "Do you want to keep editing release notes? (y/n)"
        if ($retry -ne 'y' -and $retry -ne 'Y') {
            return $false
        }
    }
}

$csprojPath = "ApexAstro.Wpf\ApexAstro.Wpf.csproj"
$csprojContent = Get-Content $csprojPath -Raw

if ($csprojContent -match '<Version>(\d+\.\d+\.\d+)</Version>') {
    $currentVersion = $Matches[1]
} else {
    Write-Host "Error: Could not find version" -ForegroundColor Red
    exit 1
}

Write-Host "Current version: $currentVersion" -ForegroundColor Cyan

$versionParts = $currentVersion.Split('.')
$major = [int]$versionParts[0]
$minor = [int]$versionParts[1]
$patch = [int]$versionParts[2]

switch ($BumpType) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}

$newVersion = "$major.$minor.$patch"
$newVersionFull = "$major.$minor.$patch.0"
$tagName = "v$newVersion"
$releaseNotesPath = Ensure-ReleaseNotesFile

Write-Host "New version: $newVersion" -ForegroundColor Green
Write-Host "Tag: $tagName" -ForegroundColor Green
Write-Host "Release notes: $releaseNotesPath" -ForegroundColor Green

if (-not (Confirm-ReleaseNotes -Path $releaseNotesPath -Version $newVersion)) {
    Write-Host "Cancelled (release notes not confirmed)" -ForegroundColor Yellow
    exit 0
}

$confirm = Read-Host "`nContinue? (y/n)"
if ($confirm -ne 'y' -and $confirm -ne 'Y') {
    Write-Host "Cancelled" -ForegroundColor Yellow
    exit 0
}

Write-Host "`nUpdating $csprojPath..." -ForegroundColor Cyan
$csprojContent = Get-Content $csprojPath -Raw
$csprojContent = $csprojContent -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$newVersion</Version>"
$csprojContent = $csprojContent -replace '<AssemblyVersion>\d+\.\d+\.\d+\.\d+</AssemblyVersion>', "<AssemblyVersion>$newVersionFull</AssemblyVersion>"
$csprojContent = $csprojContent -replace '<FileVersion>\d+\.\d+\.\d+\.\d+</FileVersion>', "<FileVersion>$newVersionFull</FileVersion>"
Set-Content -Path $csprojPath -Value $csprojContent -NoNewline
Write-Host "Version updated" -ForegroundColor Green

Write-Host "`nGit operations..." -ForegroundColor Cyan
git add $csprojPath
git add $releaseNotesPath
git commit -m "Bump version to $newVersion"
git tag -a $tagName -m "Release $newVersion"
git push origin main
git push origin $tagName

Write-Host "`nRelease $newVersion complete!" -ForegroundColor Green
Write-Host "https://github.com/kfaubel/ApexAstro/actions" -ForegroundColor Cyan
