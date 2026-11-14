# Checks that a release hangs together before it is pushed. Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File tools\check-release.ps1
# It only reads files. It fails (exit code 1) if:
#   - the version in src\Plugin.cs, Mods\MoonTrainer\mod.json, MoonTrainer.csproj and version.json are not all the same
#   - version.json has no sha256, or it is not the checksum of Mods\MoonTrainer\MoonTrainer.dll
#   - the DLL does not contain that version number (it was built from older sources: this is what went wrong in 1.2.1)
param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = "Stop"
$problems = @()
function Read-Text([string]$relative) { return Get-Content -LiteralPath (Join-Path $Root $relative) -Raw }

$versions = [ordered]@{}
$versions["src\Plugin.cs"] = [regex]::Match((Read-Text "src\Plugin.cs"), 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"').Groups[1].Value
$versions["Mods\MoonTrainer\mod.json"] = "$((Read-Text "Mods\MoonTrainer\mod.json" | ConvertFrom-Json).version)"
$versions["MoonTrainer.csproj"] = [regex]::Match((Read-Text "MoonTrainer.csproj"), '<Version>\s*([^<\s]+)\s*</Version>').Groups[1].Value
$release = Read-Text "version.json" | ConvertFrom-Json
$versions["version.json"] = "$($release.version)"

Write-Host ""
Write-Host "Versions"
foreach ($k in $versions.Keys) {
    $shown = $versions[$k]; if (-not $shown) { $shown = "(not found)" }
    Write-Host ("  {0,-28} {1}" -f $k, $shown)
}
$distinct = @($versions.Values | Sort-Object -Unique)
if (@($versions.Values | Where-Object { -not $_ }).Count -gt 0) { $problems += "A version number could not be read from one of the files above." }
elseif ($distinct.Count -ne 1) { $problems += "The version numbers above are not all the same." }
$version = $versions["src\Plugin.cs"]

$dll = Join-Path $Root "Mods\MoonTrainer\MoonTrainer.dll"
Write-Host ""
Write-Host "DLL"
if (-not (Test-Path -LiteralPath $dll)) { $problems += "Mods\MoonTrainer\MoonTrainer.dll is missing." }
else {
    $actual = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLower()
    $published = "$($release.sha256)".Trim().ToLower()
    Write-Host ("  {0,-28} {1}" -f "checksum of the DLL", $actual)
    Write-Host ("  {0,-28} {1}" -f "sha256 in version.json", $(if ($published) { $published } else { "(none)" }))
    if (-not $published) { $problems += "version.json has no sha256. Both installers refuse a release without one." }
    elseif ($published -ne $actual) { $problems += "version.json's sha256 is not the checksum of the DLL. Update it with: (Get-FileHash Mods\MoonTrainer\MoonTrainer.dll).Hash" }

    if ($version) {
        # The version is compiled into the DLL as text: as UTF-8 in the plugin's attribute and as UTF-16 in its messages.
        $text = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($dll))
        $wide = -join ($version.ToCharArray() | ForEach-Object { "$_" + [char]0 })
        $inside = $text.Contains($version) -or $text.Contains($wide)
        Write-Host ("  {0,-28} {1}" -f "DLL reports $version", $(if ($inside) { "yes" } else { "NO" }))
        if (-not $inside) { $problems += "The DLL does not contain '$version'. It was probably built from older sources - rebuild and copy it again." }
    }
}

Write-Host ""
if ($problems.Count -eq 0) { Write-Host "Release check OK: $version" -ForegroundColor Green; exit 0 }
Write-Host "Release check FAILED:" -ForegroundColor Red
foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
exit 1
