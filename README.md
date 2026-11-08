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

The menu has collapsible sections (click a header) and can be dragged by its title bar. Your toggles, multipliers, menu position and folded sections are remembered between launches.

- **Presets:** Exploring, Grinding, Building, All off.
- **Player:** fly, no clip, fly speed, move speed, jump height, unlimited jetpack, gravity boots, night vision, refill, max all upgrades.
- **Money:** add / set team wallet, earnings multiplier, free upgrades & tools, free station travel.
- **Dust & vehicle:** unlimited bag, auto-sell (experimental), fill / empty / sell bag, vehicle speed, brush width, unlimited hopper, fill / empty hopper.
- **World:** freeze time, time speed, shift time, machine speed, clean N rings around you, finish the Moon, reset all dust, save now.
- **Flags & sites:** show them on the map, browse unraised ones nearest-first with Go / Raise, raise all.
- **Teleport:** nearest flag or site, any map marker, any crewmate.
- **Build:** place a dust catcher or robot dock (Mk I, Mk II, Joonas) where you're looking.
- **Crew (host):** give money, upgrades, flags and sites to any crewmate.
- **Chat:** allowlist, per-player cooldown and money limit, welcome message for new joiners.
- **Settings:** remember toggles, active-cheats line, update check, unlock all Steam achievements.

## Updates

At startup the trainer checks this repo for a newer version. If there is one, the BepInEx console prints a notice, you hear a higher chime in game, and the menu shows an **Update available** box with a **Copy update command** button. Nothing installs by itself: close the game, paste the command into PowerShell, done.

## Chat commands

Prefix `!` (changeable). Crewmates can use these in your lobby when you're hosting and have chat commands enabled:

```
!trainer                 posts this install link
!money 5000              add money (team wallet)
!jetpack !boots !nightvision !belt !lamp !speed !jump !width !payout
!all                     max every upgrade
!upgrade gear.jetpack    a specific upgrade-tree node
!flag  !site             raise the nearest unraised flag / site to you
!players                 list who's in the lobby
```

These only work for players who also have the trainer installed (movement runs on their own PC):

```
!fly [on|off]   !noclip [on|off]   !flyspeed 40   !tp 2   !tpto <name>
```

## Notes

- Everyone in the lobby sees the *effects* (you flying, the wallet changing). Only you see the menu.
- The host owns wallets and upgrades, so give-commands only work when the player with the trainer is hosting.
- Source is in `src/`. Built with Roslyn against MSML's generated interop assemblies.
