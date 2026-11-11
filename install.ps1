# Moon Trainer quick installer for Moon Sweepers.
#   irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1 | iex
# Installs the Moon Sweepers Mod Loader (MSML) if it isn't there yet, then drops Moon Trainer into Mods\ and enables it.
# Run it again any time to update. Nothing is changed unless the download checks out.

& {
    # Everything runs inside this block so none of its settings are left behind in your PowerShell window.
    $ErrorActionPreference = "Stop"
    $ProgressPreference = "SilentlyContinue"            # the progress bar makes downloads many times slower on Windows PowerShell 5
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

    $Repo = "Brodiexx03/moon-trainer"
    $Branch = if ($env:MOONTRAINER_BRANCH) { $env:MOONTRAINER_BRANCH } else { "main" }     # for trying a test build
    $MsmlGet = "https://github.com/Wehrum/MSML/releases/latest/download/get.ps1"
    $AppId = 5335510
    $Agent = @{ "User-Agent" = "MoonTrainer-Installer" }

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

    # raw.githubusercontent.com serves JSON as plain text, so Windows PowerShell hands back a string; newer ones an object.
    function Read-Json($value) { if ($value -is [string]) { return ($value | ConvertFrom-Json) } else { return $value } }

    $tmpDll = $null; $tmpJson = $null
    try {
        Say ""
        Say "Moon Trainer installer" "Cyan"
        Say "======================" "Cyan"
        if ($Branch -ne "main") { Say "Using the '$Branch' test build." "Yellow" }

        if (Get-Process -Name MoonSweepers -ErrorAction SilentlyContinue) {
            Say "Moon Sweepers is running - close it first, then run this again." "Yellow"
            return
        }

        $game = Find-GameDir
        if (-not $game) {
            $game = Read-Host "Couldn't find Moon Sweepers automatically. Paste the game folder path (the one with MoonSweepers.exe)"
            $game = "$game".Trim().Trim('"')
            if (-not $game -or -not (Test-Path (Join-Path $game "MoonSweepers.exe"))) { Say "That folder doesn't contain MoonSweepers.exe." "Red"; return }
        }
        Say "Game folder: $game"

        # ---- 1. Mod loader -------------------------------------------------------------------------
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

        # ---- 2. Moon Trainer -----------------------------------------------------------------------
        # Everything is fetched from one exact commit, so the DLL, its description and its checksum always belong
        # together (GitHub caches branch files for about 5 minutes, which once handed out a stale build).
        $modDir = Join-Path $game "Mods\MoonTrainer"
        New-Item -ItemType Directory -Force -Path $modDir | Out-Null
        $base = $null
        try {
            $sha = (Invoke-RestMethod "https://api.github.com/repos/$Repo/commits/$Branch" -UseBasicParsing -Headers $Agent).sha
            if ($sha) { $base = "https://raw.githubusercontent.com/$Repo/$sha" }
        } catch {}
        $bust = ""
        if (-not $base) {                                  # GitHub's API is unavailable or rate-limited: use the branch itself
            $base = "https://raw.githubusercontent.com/$Repo/$Branch"
            $bust = "?t=" + [DateTime]::UtcNow.Ticks
        }

        $dll = Join-Path $modDir "MoonTrainer.dll"
        $json = Join-Path $modDir "mod.json"
        $oldVersion = $null
        if (Test-Path $json) { try { $oldVersion = (Get-Content $json -Raw | ConvertFrom-Json).version } catch {} }

        Say "Downloading Moon Trainer..."
        $tmpDll = "$dll.download"; $tmpJson = "$json.download"
        Invoke-WebRequest "$base/Mods/MoonTrainer/MoonTrainer.dll$bust" -OutFile $tmpDll -UseBasicParsing -Headers $Agent
        Invoke-WebRequest "$base/Mods/MoonTrainer/mod.json$bust" -OutFile $tmpJson -UseBasicParsing -Headers $Agent
        $release = Read-Json (Invoke-RestMethod "$base/version.json$bust" -UseBasicParsing -Headers $Agent)

        # Check the download before touching the copy that's already installed.
        $newVersion = $null
        try { $newVersion = (Get-Content $tmpJson -Raw | ConvertFrom-Json).version } catch {}
        $problem = $null
        if ((Get-Item $tmpDll).Length -lt 10000) { $problem = "the file is far too small" }
        elseif (-not $newVersion) { $problem = "its description (mod.json) can't be read" }
        elseif ($release.sha256) {
            $actual = (Get-FileHash $tmpDll -Algorithm SHA256).Hash
            if ($actual -ne "$($release.sha256)".Trim()) { $problem = "its checksum doesn't match the published one" }
        }
        if ($problem) {
            Say "The download doesn't look right ($problem)." "Red"
            if ($oldVersion) { Say "Nothing was changed - you still have Moon Trainer $oldVersion. Try again in a few minutes." "Yellow" }
            else { Say "Nothing was installed. Try again in a few minutes." "Yellow" }
            return
        }

        Move-Item $tmpDll $dll -Force
        Move-Item $tmpJson $json -Force
        (Get-Item $dll).LastWriteTimeUtc = [DateTime]::UtcNow   # make sure the loader sees it as changed

        if ($oldVersion -and $oldVersion -ne $newVersion) { Say "Updated Moon Trainer $oldVersion -> $newVersion" "Green" }
        elseif ($oldVersion) { Say "Moon Trainer $newVersion reinstalled (you already had the latest)." "Green" }
        else { Say "Installed Moon Trainer $newVersion" "Green" }

        # ---- 3. Enable it --------------------------------------------------------------------------
        $state = Join-Path $game "BepInEx\config\MoonSweepMods.ModLoader.mods.json"
        $enabled = @()
        if (Test-Path $state) { try { $enabled = @((Get-Content $state -Raw | ConvertFrom-Json).Enabled | Where-Object { $_ }) } catch {} }
        if ($enabled -notcontains "MoonTrainer") { $enabled += "MoonTrainer" }
        New-Item -ItemType Directory -Force -Path (Split-Path $state) | Out-Null
        @{ Enabled = @($enabled) } | ConvertTo-Json | Set-Content $state -Encoding UTF8

        Say ""
        Say "Done! Launch Moon Sweepers from Steam." "Green"
        Say "  The console should say: Moon Trainer $newVersion loaded"
        Say "  Insert  opens the trainer menu      F  toggles fly"
        Say "  Chat:   !money 5000  !jetpack  !fly  !help"
        Say ""
    }
    catch {
        Say ""
        Say "The installer stopped: $($_.Exception.Message)" "Red"
        Say "Nothing half-finished was left in place. Things to try:" "Yellow"
        Say "  - check your internet connection and run the command again"
        Say "  - if your antivirus blocked it, allow PowerShell for this one command"
        Say "  - if it says access is denied, right-click PowerShell > Run as administrator"
    }
    finally {
        foreach ($f in $tmpDll, $tmpJson) { if ($f -and (Test-Path $f)) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
    }
}
