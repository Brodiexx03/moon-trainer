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

Keys, chat prefix and permissions can be changed in the game's **MODS → Moon Trainer → Settings**.

## Features

**Player:** fly, no clip, fly speed, move speed, jump height, unlimited jetpack, gravity boots, night vision, refill, max all upgrades.
**Money:** add / set wallet, earnings multiplier, free upgrades & tools, free station travel.
**Vehicle:** speed, brush width.
**World:** freeze / shift time of day, machine speed, clean N rings around you, finish the Moon (optionally leaving one section + a marker), raise all flags / sites, save now.
**Teleport:** to any of your map markers.
**Crew (host only):** give money or max upgrades to any crewmate.

## Chat commands

Prefix `!` (changeable). Crewmates can use these in your lobby when you're hosting and have chat commands enabled:

```
!trainer                 posts this install link
!money 5000              add money (team wallet)
!jetpack !boots !nightvision !belt !lamp !speed !jump !width !payout
!all                     max every upgrade
!upgrade gear.jetpack    a specific upgrade-tree node
```

These only work for players who also have the trainer installed (movement runs on their own PC):

```
!fly [on|off]   !noclip [on|off]   !flyspeed 40   !tp 2
```

## Notes

- Everyone in the lobby sees the *effects* (you flying, the wallet changing). Only you see the menu.
- The host owns wallets and upgrades, so give-commands only work when the player with the trainer is hosting.
- Source is in `src/`. Built with Roslyn against MSML's generated interop assemblies.
