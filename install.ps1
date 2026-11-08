# Moon Trainer quick installer for Moon Sweepers.
#   irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1 | iex
# Installs the Moon Sweepers Mod Loader (MSML) if it isn't there yet, then drops Moon Trainer into Mods\ and enables it.

$ErrorActionPreference = "Stop"
$Repo = "Brodiexx03/moon-trainer"
$Raw = "https://raw.githubusercontent.com/$Repo/main"
$MsmlGet = "https://github.com/Wehrum/MSML/releases/latest/download/get.ps1"
$AppId = 5335510

function Say([string]$m, [string]$c = "Gray") { Write-Host $m -ForegroundColor $c }

function Get-SteamLibraries {
    $roots = @()
    foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
        try { $p = Get-ItemProperty $key -ErrorAction Stop; if ($p.SteamPath) { $roots += $p.SteamPath }; if ($p.InstallPath) { $roots += $p.InstallPath } } catch {}
    }
    $roots += "C:\Program Files (x86)\Steam"
    $libs = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        if (-not (Test-Path $root)) { continue }
        $libs += $root
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += $m.Groups[1].Value.Replace('\\', '\') }
        }
    }
    return $libs | Select-Object -Unique
}

function Find-GameDir {
    foreach ($lib in Get-SteamLibraries) {
        $manifest = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
        $installDir = "Moon Sweepers"
        if (Test-Path $manifest) {
            $m = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"')
            if ($m.Success) { $installDir = $m.Groups[1].Value }
        }
        $dir = Join-Path $lib "steamapps\common\$installDir"
        if (Test-Path (Join-Path $dir "MoonSweepers.exe")) { return $dir }
    }
    return $null
}

Say ""
Say "Moon Trainer installer" "Cyan"
Say "======================" "Cyan"

if (Get-Process -Name MoonSweepers -ErrorAction SilentlyContinue) {
    Say "Moon Sweepers is running - close it first, then run this again." "Yellow"
    return
}

$game = Find-GameDir
if (-not $game) {
    $game = Read-Host "Couldn't find Moon Sweepers automatically. Paste the game folder path (the one with MoonSweepers.exe)"
    if (-not (Test-Path (Join-Path $game "MoonSweepers.exe"))) { Say "That folder doesn't contain MoonSweepers.exe." "Red"; return }
}
Say "Game folder: $game"

# ---- 1. Mod loader -----------------------------------------------------------------------------
$loader = Join-Path $game "BepInEx\plugins\MoonSweepMods.ModLoader\MoonSweepMods.ModLoader.dll"
$interop = Join-Path $game "BepInEx\interop\Assembly-CSharp.dll"
if ((Test-Path $loader) -and (Test-Path $interop)) {
    Say "Mod loader (MSML) already installed." "Green"
} else {
    Say "Installing the Moon Sweepers Mod Loader (MSML) - this takes about a minute..." "Yellow"
    $script = Invoke-RestMethod $MsmlGet -UseBasicParsing
    & ([scriptblock]::Create($script)) -Action Install -GameDir $game
    if (-not (Test-Path $interop)) { Say "MSML did not finish installing. Run this script again after it completes." "Red"; return }
}

# ---- 2. Moon Trainer ---------------------------------------------------------------------------
# Download from the exact latest commit, so GitHub's 5-minute file cache can never hand out an old build.
$modDir = Join-Path $game "Mods\MoonTrainer"
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
$base = $null
try {
    $sha = (Invoke-RestMethod "https://api.github.com/repos/$Repo/commits/main" -UseBasicParsing -Headers @{ "User-Agent" = "MoonTrainer-Installer" }).sha
    if ($sha) { $base = "https://raw.githubusercontent.com/$Repo/$sha" }
} catch {}
if (-not $base) { $base = $Raw }                       # API unavailable: fall back, with a cache-busting query below
$bust = "?t=" + [DateTime]::UtcNow.Ticks

$dll = Join-Path $modDir "MoonTrainer.dll"
$json = Join-Path $modDir "mod.json"
$oldVersion = $null
if (Test-Path $json) { try { $oldVersion = (Get-Content $json -Raw | ConvertFrom-Json).version } catch {} }

Say "Downloading Moon Trainer..."
$tmp = "$dll.download"
Invoke-WebRequest "$base/Mods/MoonTrainer/MoonTrainer.dll$bust" -OutFile $tmp -UseBasicParsing
Invoke-WebRequest "$base/Mods/MoonTrainer/mod.json$bust" -OutFile $json -UseBasicParsing
if ((Get-Item $tmp).Length -lt 10000) { Remove-Item $tmp -Force; Say "The download looks broken - try again in a minute." "Red"; return }
Move-Item $tmp $dll -Force
(Get-Item $dll).LastWriteTimeUtc = [DateTime]::UtcNow   # make sure the loader sees it as changed

$newVersion = "?"
try { $newVersion = (Get-Content $json -Raw | ConvertFrom-Json).version } catch {}
if ($oldVersion -and $oldVersion -ne $newVersion) { Say "Updated Moon Trainer $oldVersion -> $newVersion" "Green" }
elseif ($oldVersion) { Say "Moon Trainer $newVersion reinstalled (you already had the latest)." "Green" }
else { Say "Installed Moon Trainer $newVersion" "Green" }

# ---- 3. Enable it ------------------------------------------------------------------------------
$state = Join-Path $game "BepInEx\config\MoonSweepMods.ModLoader.mods.json"
$enabled = @()
if (Test-Path $state) { try { $enabled = @((Get-Content $state -Raw | ConvertFrom-Json).Enabled) } catch {} }
if ($enabled -notcontains "MoonTrainer") { $enabled += "MoonTrainer" }
New-Item -ItemType Directory -Force -Path (Split-Path $state) | Out-Null
@{ Enabled = $enabled } | ConvertTo-Json | Set-Content $state -Encoding UTF8

Say ""
Say "Done! Launch Moon Sweepers from Steam." "Green"
Say "  The console should say: Moon Trainer $newVersion loaded"
Say "  Insert  opens the trainer menu      F  toggles fly"
Say "  Chat:   !money 5000  !jetpack  !fly  !help"
Say ""
