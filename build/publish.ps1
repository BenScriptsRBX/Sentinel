<#
.SYNOPSIS
    Builds the Sentinel release packages into ./artifacts (used locally and by the release workflow).

.DESCRIPTION
    Produces two zips plus checksums and release notes:
      Sentinel-<ver>-win-x64.zip                       self-contained single exe, no .NET install needed
      Sentinel-<ver>-win-x64-framework-dependent.zip   small single exe, needs the .NET 10 Desktop Runtime
      SHA256SUMS.txt                                   checksums of both zips
      RELEASE_NOTES.md                                 this version's section of CHANGELOG.md
#>
param(
    [string]$Version = "1.0.0",
    [string]$Output = "artifacts"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root $Output
$project = Join-Path $root "src/Sentinel/Sentinel.csproj"
$docs = "LICENSE", "README.md", "TERMS.md", "PRIVACY.md", "THIRD-PARTY-NOTICES.md", "CHANGELOG.md"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

function Publish-Flavor([string]$name, [string[]]$extra) {
    $dir = Join-Path $out $name
    $common = @("publish", $project, "-c", "Release", "-r", "win-x64", "-p:PublishSingleFile=true",
                "-p:DebugType=None", "-p:DebugSymbols=false", "-p:Version=$Version", "-o", $dir)
    # Out-Host keeps the build log visible without it becoming part of this function's return value.
    & dotnet @common @extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($name) failed with exit code $LASTEXITCODE" }
    foreach ($doc in $docs) { Copy-Item (Join-Path $root $doc) $dir }
    return $dir
}

$sc = Publish-Flavor "self-contained" @("--self-contained", "true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true")
$fd = Publish-Flavor "framework-dependent" @("--self-contained", "false")

$zips = @(
    @{ Dir = $sc; Zip = Join-Path $out "Sentinel-$Version-win-x64.zip" },
    @{ Dir = $fd; Zip = Join-Path $out "Sentinel-$Version-win-x64-framework-dependent.zip" }
)
foreach ($z in $zips) { Compress-Archive -Path (Join-Path $z.Dir "*") -DestinationPath $z.Zip -CompressionLevel Optimal }

$sums = foreach ($z in $zips) {
    $hash = (Get-FileHash $z.Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $z.Zip -Leaf)"
}
# LF line endings so `sha256sum -c SHA256SUMS.txt` works as well as PowerShell's Get-FileHash.
[System.IO.File]::WriteAllText((Join-Path $out "SHA256SUMS.txt"), (($sums -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))

# Release notes: the matching CHANGELOG section, plus download guidance and checksums.
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Raw
$pattern = "(?ms)^## \[" + [regex]::Escape($Version) + "\][^\r\n]*\r?\n(.*?)(?=^## \[|^\[[^\]]+\]:|\z)"
$section = [regex]::Match($changelog, $pattern).Groups[1].Value.Trim()
if (-not $section) { $section = "See CHANGELOG.md." }
$notes = @"
$section

### Download

| File | Use it if |
| --- | --- |
| ``Sentinel-$Version-win-x64.zip`` | You just want it to work. Self-contained, no .NET install needed. |
| ``Sentinel-$Version-win-x64-framework-dependent.zip`` | You already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Much smaller download. |

Unzip anywhere and run ``Sentinel.exe``. Windows 10 or 11, 64-bit. The exe isn't code-signed yet, so SmartScreen may show "Windows protected your PC": choose **More info**, then **Run anyway**.

### SHA-256

``````
$($sums -join "`n")
``````
"@
# Explicit UTF-8 without BOM: Windows PowerShell's "-Encoding utf8" adds one, which GitHub shows as a stray character.
[System.IO.File]::WriteAllText((Join-Path $out "RELEASE_NOTES.md"), $notes, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Packages written to $out"
Get-ChildItem $out -File | ForEach-Object { "{0,-55} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
