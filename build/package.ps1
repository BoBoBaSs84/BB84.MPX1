<#
.SYNOPSIS
    Publishes BB84.MPX1 with Native AOT for win-x64 and packages the release zip.

.DESCRIPTION
    Publishes the app with the given version, copies the executable, its native libraries, LICENSE, README and
    CHANGELOG into a staging folder, refuses to package anything from legacy/ or other Lexicon/WazWare material
    (spec COM-001), and writes BB84.MPX1-<version>-win-x64.zip plus a SHA-256 file and the release notes taken
    from the CHANGELOG section of that version.

.PARAMETER Version
    The release version, for example 1.0.0 (no leading "v").

.PARAMETER Output
    Output folder. Defaults to artifacts/release.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Output = 'artifacts/release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "'$Version' is not a semantic version such as 1.0.0."
}

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Release notes come from the CHANGELOG; a release without notes is a mistake.
    $changelog = Get-Content CHANGELOG.md
    $start = ($changelog | Select-String -Pattern "^## $([regex]::Escape($Version))\b" | Select-Object -First 1)
    if (-not $start) { throw "CHANGELOG.md has no '## $Version' section." }
    $notes = New-Object System.Collections.Generic.List[string]
    foreach ($line in $changelog[$start.LineNumber..($changelog.Count - 1)]) {
        if ($line -match '^## ') { break }
        $notes.Add($line)
    }

    $publish = Join-Path $Output 'publish'
    $staging = Join-Path $Output "BB84.MPX1-$Version-win-x64"
    Remove-Item -Recurse -Force $Output -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $staging | Out-Null

    dotnet publish src/BB84.MPX1.App -c Release -r win-x64 -o $publish `
        "-p:Version=$Version" -p:DebugType=none -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Copy-Item (Join-Path $publish '*.exe'), (Join-Path $publish '*.dll') $staging
    Copy-Item LICENSE, README.md, CHANGELOG.md $staging

    # COM-001: Lexicon documents and the legacy editor must never ship.
    $forbidden = Get-ChildItem -Recurse $staging | Where-Object {
        $_.Extension -in '.pdf', '.hlp', '.lzh' -or $_.Name -match 'MPX1EDIT|MPX1HLP|MPX1_V1|_original' -or $_.FullName -match '[\\/]legacy[\\/]'
    }
    if ($forbidden) { throw "Forbidden files in the release: $($forbidden.Name -join ', ')" }
    if (-not (Test-Path (Join-Path $staging 'BB84.MPX1.exe'))) { throw 'BB84.MPX1.exe is missing from the publish output.' }

    $zip = Join-Path $Output "BB84.MPX1-$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $zip)" | Set-Content -Encoding ascii "$zip.sha256"
    ($notes -join "`n").Trim() | Set-Content -Encoding utf8 (Join-Path $Output 'release-notes.md')

    Write-Host "Packaged $zip"
    Write-Host "SHA-256 $hash"
    Get-ChildItem $staging | ForEach-Object { Write-Host "  $($_.Name)" }
}
finally {
    Pop-Location
}
