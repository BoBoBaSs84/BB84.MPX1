<#
.SYNOPSIS
    Publishes BB84.MPX1 with Native AOT for one runtime identifier and packages the release artifact.

.DESCRIPTION
    Publishes the app with the given version, copies the executable, its native libraries, LICENSE, README and
    CHANGELOG into a staging folder, refuses to package anything from legacy/ or other Lexicon/WazWare material
    (spec COM-001), and writes the release artifact plus a SHA-256 file and the release notes taken from the
    CHANGELOG section of that version.

    win-x64 produces BB84.MPX1-<version>-win-x64.zip. linux-x64 produces BB84.MPX1-<version>-x86_64.AppImage and
    BB84.MPX1-<version>-x86_64.flatpak from one AppDir with the desktop files and the Flatpak manifest in
    packaging/linux. It must run on Linux (Native AOT does not cross-compile), needs flatpak and flatpak-builder with
    the flathub remote added, and downloads pinned, checksum-verified copies of appimagetool and the AppImage runtime.

.PARAMETER Version
    The release version, for example 1.0.0 (no leading "v").

.PARAMETER Runtime
    The runtime identifier to publish for: win-x64 (default) or linux-x64.

.PARAMETER Output
    Output folder. Defaults to artifacts/release.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('win-x64', 'linux-x64')][string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts/release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "'$Version' is not a semantic version such as 1.0.0."
}

if ($Runtime -like 'linux-*' -and -not $IsLinux) { throw "The $Runtime package must be built on Linux." }
if ($Runtime -like 'linux-*' -and -not (Get-Command flatpak-builder -ErrorAction SilentlyContinue)) {
    throw "flatpak-builder is missing. Install flatpak and flatpak-builder, then add the flathub remote."
}

$appId = 'io.github.BoBoBaSs84.BB84.MPX1'
# Pinned AppImage tooling; change a URL and its SHA-256 together.
$appImageTool = @{ Url = 'https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage'; Sha256 = 'ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0' }
$appImageRuntime = @{ Url = 'https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64'; Sha256 = '2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d' }

# COM-001: Lexicon documents and the legacy editor must never ship.
function Assert-NoLegacyMaterial([string]$Path) {
    $forbidden = Get-ChildItem -Recurse $Path | Where-Object {
        $_.Extension -in '.pdf', '.hlp', '.lzh' -or $_.Name -match 'MPX1EDIT|MPX1HLP|MPX1_V1|_original' -or $_.FullName -match '[\\/]legacy[\\/]'
    }
    if ($forbidden) { throw "Forbidden files in the release: $($forbidden.Name -join ', ')" }
}

function Get-VerifiedFile([hashtable]$Tool, [string]$Folder) {
    $file = Join-Path $Folder (Split-Path -Leaf $Tool.Url)
    Invoke-WebRequest -Uri $Tool.Url -OutFile $file
    $actual = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    if ($actual -ne $Tool.Sha256) { throw "$(Split-Path -Leaf $file) has SHA-256 $actual, expected $($Tool.Sha256)." }
    return $file
}

function Write-Checksum([string]$File) {
    $hash = (Get-FileHash -Algorithm SHA256 $File).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $File)" | Set-Content -Encoding ascii "$File.sha256"
    Write-Host "Packaged $File"
    Write-Host "SHA-256 $hash"
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
    $staging = Join-Path $Output "BB84.MPX1-$Version-$Runtime"
    $executable = if ($Runtime -like 'win-*') { 'BB84.MPX1.exe' } else { 'BB84.MPX1' }
    Remove-Item -Recurse -Force $Output -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $staging | Out-Null

    dotnet publish src/BB84.MPX1.App -c Release -r $Runtime -o $publish `
        "-p:Version=$Version" -p:DebugType=none -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    if (-not (Test-Path (Join-Path $publish $executable))) { throw "$executable is missing from the publish output." }
    Copy-Item (Join-Path $publish $executable) $staging
    # Avalonia's native rendering libraries ship next to the executable.
    Get-ChildItem $publish -File | Where-Object Extension -in '.dll', '.so' | Copy-Item -Destination $staging
    Copy-Item LICENSE, README.md, CHANGELOG.md $staging

    Assert-NoLegacyMaterial $staging

    if ($Runtime -like 'win-*') {
        $zip = Join-Path $Output "BB84.MPX1-$Version-$Runtime.zip"
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
        Write-Checksum $zip
    }
    else {
        # A zip would lose the executable bit, so Linux gets an AppImage built from an AppDir.
        $linux = 'packaging/linux'
        $appDir = Join-Path $Output 'BB84.MPX1.AppDir'
        $bin = Join-Path $appDir 'usr/bin'
        $applications = Join-Path $appDir 'usr/share/applications'
        $metainfo = Join-Path $appDir 'usr/share/metainfo'
        $icons = Join-Path $appDir 'usr/share/icons/hicolor/scalable/apps'
        New-Item -ItemType Directory -Force $bin, $applications, $metainfo, $icons | Out-Null
        Copy-Item (Join-Path $staging '*') $bin
        Copy-Item "$linux/$appId.desktop" $applications
        Copy-Item "$linux/$appId.metainfo.xml" $metainfo
        Copy-Item "$linux/$appId.svg" $icons
        Copy-Item "$linux/$appId.desktop", "$linux/$appId.svg" $appDir
        $appRun = Join-Path $appDir 'AppRun'
        "#!/bin/sh`nexec `"`$(dirname `"`$(readlink -f `"`$0`")`")/usr/bin/$executable`" `"`$@`"`n" |
            Set-Content -Encoding ascii -NoNewline $appRun
        chmod +x $appRun (Join-Path $bin $executable)
        if ($LASTEXITCODE -ne 0) { throw "chmod failed with exit code $LASTEXITCODE." }
        Assert-NoLegacyMaterial $appDir

        $tools = Join-Path $Output 'tools'
        New-Item -ItemType Directory -Force $tools | Out-Null
        $tool = Get-VerifiedFile $appImageTool $tools
        $runtimeFile = Get-VerifiedFile $appImageRuntime $tools
        chmod +x $tool

        $appImage = Join-Path $Output "BB84.MPX1-$Version-x86_64.AppImage"
        $env:ARCH = 'x86_64'
        # CI runners have no FUSE, so appimagetool runs extracted.
        & $tool --appimage-extract-and-run --runtime-file $runtimeFile $appDir $appImage
        if ($LASTEXITCODE -ne 0) { throw "appimagetool failed with exit code $LASTEXITCODE." }
        Write-Checksum $appImage

        # The Flatpak bundle wraps the same AppDir; the manifest's dir source points at it.
        $manifest = Join-Path $Output "$appId.yml"
        $repo = Join-Path $Output 'flatpak-repo'
        Copy-Item "$linux/$appId.yml" $manifest
        flatpak-builder --user --install-deps-from=flathub --force-clean "--repo=$repo" "--state-dir=$(Join-Path $Output 'flatpak-state')"(Join-Path $Output 'flatpak-build') $manifest
        if ($LASTEXITCODE -ne 0) { throw "flatpak-builder failed with exit code $LASTEXITCODE." }
        $bundle = Join-Path $Output "BB84.MPX1-$Version-x86_64.flatpak"
        # With the runtime repo, flatpak install fetches the freedesktop runtime from Flathub.
        flatpak build-bundle --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo $repo $bundle $appId
        if ($LASTEXITCODE -ne 0) { throw "flatpak build-bundle failed with exit code $LASTEXITCODE." }
        Write-Checksum $bundle
    }
    ($notes -join "`n").Trim() | Set-Content -Encoding utf8 (Join-Path $Output 'release-notes.md')

    Get-ChildItem $staging | ForEach-Object { Write-Host "  $($_.Name)" }
}
finally {
    Pop-Location
}
