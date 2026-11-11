# Moon Trainer

In-game trainer menu for **Moon Sweepers** (Steam). Runs on the [Moon Sweepers Mod Loader (MSML)](https://github.com/Wehrum/MSML).

## Install (one line)

Close the game, open **PowerShell**, paste:

```powershell
irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1 | iex
```

It installs MSML if you don't have it (about a minute), then adds Moon Trainer and enables it. Launch the game from Steam as normal — you'll hear a two-tone chime when the trainer attaches.

## Controls

| Key | |
|---|---|
| **Insert** | open / close the menu |
| **F** | toggle fly |
| Space / Ctrl | up / down while flying |
| **Numpad 1** | toggle no clip |
| **Numpad 2** | toggle 3x speed boost |
| **Numpad 3** | toggle unlimited jetpack |
| **Numpad 4** | save the game now |
| **Numpad 0** | show / hide the active-cheats line |

Keys, chat prefix and permissions can be changed in the game's **MODS → Moon Trainer → Settings**.

## Features

The menu has a tab for each area down the left side and can be dragged by its top bar. Close it with **Insert** or the **X**. Your toggles, multipliers, menu position and last tab are remembered between launches.

Hotkeys are ignored while you're typing in chat.

- **Presets:** Exploring, Grinding, Building, All off.
- **Player:** fly, no clip, fly speed, move speed, jump height, unlimited jetpack, gravity boots, night vision, refill, max all upgrades.
- **Money:** add / set team wallet, earnings multiplier, free upgrades & tools, free station travel.
- **Dust & vehicle:** unlimited bag, auto-sell (experimental), fill / empty / sell bag, vehicle speed, brush width, unlimited hopper, fill / empty hopper.
- **World:** freeze time, time speed, shift time, machine speed, clean N rings around you, finish the Moon, reset all dust, save now.
- **Flags & sites:** show them on the map, browse unraised ones nearest-first with Go / Raise, raise all.
- **Teleport:** nearest flag or site, any map marker, any crewmate.
- **Saved locations:** save the spot you're standing on (auto-named after the nearest landmark, or `!save base` in chat) and teleport back any time. Stored per save slot.
- **Backups:** make one of the game's full backups on demand, and restore any of the last 8 (your current state is backed up first, then the shift reloads).
- **Build:** place a dust catcher or robot dock (Mk I, Mk II, Joonas) where you're looking.
- **Crew (host):** give money, upgrades, flags and sites to any crewmate, and see who has the trainer and whether they're up to date, with a button to post them the install link.
- **Chat:** allowlist (tied to each player's Steam account, not just their name), per-player cooldown and money limit, welcome message for new joiners.
- **Settings:** remember toggles, active-cheats line, update check, unlock all Steam achievements.

## Updates

At startup the trainer checks this repo for a newer version. If there is one, the BepInEx console prints a notice, you hear a higher chime in game, and the menu shows an **Update available** box with a **Copy update command** button. Nothing installs by itself: close the game, paste the command into PowerShell, done.

## Chat commands

Prefix `!` (changeable). Crewmates can use these in your lobby when you're hosting and have chat commands enabled:

```
!trainer                 posts this install link
!money 5000              add money (team wallet; crewmates can only add, the host can also take away)
!jetpack !boots !nightvision !belt !lamp !speed !jump !width !payout
!all                     max every upgrade
!upgrade gear.jetpack    a specific upgrade-tree node
!flag  !site             raise the nearest unraised flag / site to you
!players                 list who's in the lobby
```

These only work for players who also have the trainer installed (movement runs on their own PC):

```
!fly [on|off]   !noclip [on|off]   !flyspeed 40   !tp 2   !tpto <name>
!save [name]    !go <name>        (saved locations)
```

## Notes

- Everyone in the lobby sees the *effects* (you flying, the wallet changing). Only you see the menu.
- The host owns wallets and upgrades, so give-commands only work when the player with the trainer is hosting. If you join someone else's lobby with the trainer, the money, world, build, crew and backup pages show a reminder of that.
- Each feature hooks into the game separately. If a game update breaks one, the console and the Home tab name the features that are off, and the rest keep working.
- If two people in the lobby have the same name, give-commands from that name are refused, because chat only tells the trainer the name.

## Building

Source is in `src/`. You need the game with MSML installed and started once (that creates `BepInEx\interop`), and the .NET SDK 6 or newer:

```powershell
dotnet build -c Release
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Moon Sweepers"   # if the game isn't in the default Steam folder
```

The DLL lands in `bin\`. Copy it and `Mods\MoonTrainer\mod.json` into `<game>\Mods\MoonTrainer\` to try it.

The published builds so far were compiled with the Roslyn compiler directly against the same references, so the first `dotnet build` of `MoonTrainer.csproj` is itself a test.

## Releasing

1. Set the new version in `src/Plugin.cs`, `Mods/MoonTrainer/mod.json` and `MoonTrainer.csproj`.
2. Build, and check the DLL really reports the new version (start the game once and read the console line).
3. Copy the DLL to `Mods/MoonTrainer/MoonTrainer.dll`.
4. Update `version.json`: the version, a one-line note, and the DLL's checksum from `(Get-FileHash Mods\MoonTrainer\MoonTrainer.dll).Hash`. The installer refuses a download whose checksum doesn't match, so a stale or cut-off file can't be installed.
5. Push to a test branch first and install from it on one PC:

   ```powershell
   $env:MOONTRAINER_BRANCH = "test-1.3"
   irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/test-1.3/install.ps1 | iex
   ```

6. Play with it. When it's good, merge the branch into `main`. Everyone on an older version gets the update notice next time they start the game; nothing installs until they run the command themselves.
