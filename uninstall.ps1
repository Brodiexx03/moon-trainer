# Moon Trainer uninstaller for Moon Sweepers.
#   irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/uninstall.ps1 | iex
# Removes Moon Trainer and switches it off in the mod loader. The mod loader itself (MSML), any other mods and your
# saves are left alone, and your trainer settings are kept unless you say otherwise.

& {
    # Everything runs inside this block so none of its settings are left behind in your PowerShell window.
    $ErrorActionPreference = "Stop"
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

    $done = @()         # what has been removed so far (said in the catch below if it stops half-way)
    try {
        Say ""
        Say "Moon Trainer uninstaller" "Cyan"
        Say "========================" "Cyan"

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

        # Only these two folders are ever deleted: the mod itself, and the copy the mod loader makes of it at each start.
        $modDir = Join-Path $game "Mods\MoonTrainer"
        $active = Join-Path $game "BepInEx\plugins\MoonSweepMods.ModLoader.Active\MoonTrainer"
        $settings = Join-Path $game "BepInEx\config\moontrainer.cfg"
        $state = Join-Path $game "BepInEx\config\MoonSweepMods.ModLoader.mods.json"

        # ---- 1. Switch it off in the mod loader ------------------------------------------------------
        # Only "Enabled" is touched: anything else the loader keeps in this file is written back as it was.
        $listed = $false
        $text = if (Test-Path -LiteralPath $state) { Get-Content -LiteralPath $state -Raw -Encoding UTF8 } else { $null }
        if ("$text".Trim()) {
            $config = $null
            try { $config = $text | ConvertFrom-Json } catch {}
            if ($config -is [System.Management.Automation.PSCustomObject] -and (@($config.Enabled) -contains "MoonTrainer")) {
                $listed = $true
                $enabled = @($config.Enabled | Where-Object { $_ -and $_ -ne "MoonTrainer" })
                $config | Add-Member -NotePropertyName Enabled -NotePropertyValue @($enabled) -Force
                # -Depth: the default of 2 would turn anything nested deeper into a useless string.
                $config | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $state -Encoding UTF8
                $done += "switched off in the mod loader"
                Say "Switched off in the mod loader."
            }
        }

        if (-not $listed -and -not (Test-Path -LiteralPath $modDir) -and -not (Test-Path -LiteralPath $active)) {
            Say "Moon Trainer isn't installed in this game folder. Nothing was changed." "Yellow"
            return
        }

        # ---- 2. Remove the mod -----------------------------------------------------------------------
        foreach ($dir in $modDir, $active) {
            if (Test-Path -LiteralPath $dir) {
                Remove-Item -LiteralPath $dir -Recurse -Force
                $done += "removed $dir"
                Say "Removed $dir"
            }
        }

        # ---- 3. Settings -----------------------------------------------------------------------------
        if (Test-Path -LiteralPath $settings) {
            $answer = Read-Host "Also delete your trainer settings, saved locations and presets? [y/N]"
            if ("$answer".Trim() -match '^(y|yes)$') {
                Remove-Item -LiteralPath $settings -Force
                Say "Settings deleted."
            } else {
                Say "Settings kept, in case you install it again: $settings"
            }
        }

        Say ""
        Say "Moon Trainer is uninstalled." "Green"
        Say "  The mod loader (MSML) is still there. To remove that too, run its own command and choose Uninstall:"
        Say "    irm https://github.com/Wehrum/MSML/releases/latest/download/get.ps1 | iex"
        Say "  To get Moon Trainer back, run its install command again."
        Say ""
    }
    catch {
        Say ""
        Say "The uninstaller stopped: $($_.Exception.Message)" "Red"
        if ($done.Count -gt 0) { Say "Done before it stopped: $($done -join '; ')." "Yellow" }
        else { Say "Nothing was changed." "Yellow" }
        Say "  - make sure the game is closed, then run the command again"
        Say "  - if it says access is denied, right-click PowerShell > Run as administrator"
        Say "  - or delete the folder Mods\MoonTrainer in the game folder by hand"
    }
}
