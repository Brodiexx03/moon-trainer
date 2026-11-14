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
    $left = $null       # once anything installed has been touched: what stopping here would leave behind (said in the catch below)
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

        # Check the download before touching the copy that's already installed. These are the checks the in-game updater
        # makes too: the DLL has to match the published checksum (every release since 1.3 has one, so a release without
        # one is refused rather than installed unchecked), and the three files have to be the same version.
        $newVersion = $null
        try { $newVersion = (Get-Content $tmpJson -Raw | ConvertFrom-Json).version } catch {}
        $want = "$($release.sha256)".Trim()
        $problem = $null
        if ((Get-Item $tmpDll).Length -lt 10000) { $problem = "the file is far too small" }
        elseif (-not $newVersion) { $problem = "its description (mod.json) can't be read" }
        elseif (-not $release.version) { $problem = "the release information (version.json) can't be read" }
        elseif ($want -notmatch '^[0-9A-Fa-f]{64}$') { $problem = "the release has no checksum to check it against" }
        elseif ((Get-FileHash $tmpDll -Algorithm SHA256).Hash -ne $want) { $problem = "its checksum doesn't match the published one" }
        elseif ("$($release.version)" -cne "$newVersion") { $problem = "it's a mix of two versions, $($release.version) and $newVersion" }
        if ($problem) {
            Say "The download doesn't look right ($problem)." "Red"
            if ($oldVersion) { Say "Nothing was changed - you still have Moon Trainer $oldVersion. Try again in a few minutes." "Yellow" }
            else { Say "Nothing was installed. Try again in a few minutes." "Yellow" }
            return
        }

        Move-Item $tmpDll $dll -Force
        $left = "Moon Trainer's DLL was replaced but its description (mod.json) wasn't. Run the command again to finish."
        Move-Item $tmpJson $json -Force
        $left = "Moon Trainer $newVersion is in place, but it may not be switched on in the mod loader yet. Run the command again to finish."
        (Get-Item $dll).LastWriteTimeUtc = [DateTime]::UtcNow   # make sure the loader sees it as changed

        if ($oldVersion -and $oldVersion -ne $newVersion) { Say "Updated Moon Trainer $oldVersion -> $newVersion" "Green" }
        elseif ($oldVersion) { Say "Moon Trainer $newVersion reinstalled (you already had the latest)." "Green" }
        else { Say "Installed Moon Trainer $newVersion" "Green" }

        # ---- 3. Enable it --------------------------------------------------------------------------
        # The mod loader keeps its list of enabled mods in this file. Only "Enabled" is touched: anything else the
        # loader stores there, now or in a later version, is written back as it was.
        $state = Join-Path $game "BepInEx\config\MoonSweepMods.ModLoader.mods.json"
        $config = $null
        $text = if (Test-Path $state) { Get-Content $state -Raw -Encoding UTF8 } else { $null }
        if ("$text".Trim()) {
            try { $config = $text | ConvertFrom-Json } catch {}
            if ($config -isnot [System.Management.Automation.PSCustomObject]) {
                # Not something this script can read (the loader may still manage to): keep a copy rather than lose it.
                $config = $null
                Copy-Item $state "$state.bak" -Force
                Say "The mod loader's list of enabled mods couldn't be read, so a new one is being started with just Moon Trainer." "Yellow"
                Say "  The old one was kept as $state.bak" "Yellow"
            }
        }
        if (-not $config) { $config = New-Object PSObject }
        # Already switched on: leave the file exactly as the loader wrote it.
        if (-not ($config.Enabled -is [array] -and $config.Enabled -contains "MoonTrainer")) {
            $enabled = @($config.Enabled | Where-Object { $_ })
            if ($enabled -notcontains "MoonTrainer") { $enabled += "MoonTrainer" }
            $config | Add-Member -NotePropertyName Enabled -NotePropertyValue @($enabled) -Force
            New-Item -ItemType Directory -Force -Path (Split-Path $state) | Out-Null
            # -Depth: the default of 2 would turn anything nested deeper into a useless string.
            $config | ConvertTo-Json -Depth 100 | Set-Content $state -Encoding UTF8
        }

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
        if ($left) { Say "$left Things to try:" "Yellow" }
        else { Say "Nothing half-finished was left in place. Things to try:" "Yellow" }
        Say "  - check your internet connection and run the command again"
        Say "  - if your antivirus blocked it, allow PowerShell for this one command"
        Say "  - if it says access is denied, right-click PowerShell > Run as administrator"
    }
    finally {
        foreach ($f in $tmpDll, $tmpJson) { if ($f -and (Test-Path $f)) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }
    }
}
