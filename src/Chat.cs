using System;
using System.Collections.Generic;
using System.Globalization;
using MoonCleaners;
using UnityEngine;

namespace MoonTrainer
{
    /// <summary>
    /// Chat commands, e.g. "!money 5000", "!jetpack", "!fly".
    /// Give-type commands run on the host for whoever typed them. Movement commands only ever affect the machine
    /// that typed them (and only if that machine has the mod), because each player's movement is their own.
    /// </summary>
    public static class Chat
    {
        public const string InstallUrl = "https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1";

        private static readonly Queue<string> _replies = new Queue<string>();
        private static float _lastReplyAt;
        private static readonly Dictionary<string, float> _lastCommandAt = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, double> _moneyGiven = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _seenMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static float _nextMemberPoll;
        private static bool _membersPrimed;

        public static readonly string Help =
            "trainer, money <amt>, jetpack, boots, nv, belt, lamp, speed, jump, width, payout, all, flag, site, players | fly, noclip, flyspeed <n>, tp <marker#>, tpto <name> (need the mod)";

        public static void Handle(string senderName, string said, bool fromSelf)
        {
            if (!Settings.ChatCommands.Value && !fromSelf) return;
            if (string.IsNullOrEmpty(said)) return;
            var prefix = Settings.ChatPrefix.Value ?? "!";
            if (prefix.Length == 0 || !said.StartsWith(prefix, StringComparison.Ordinal)) return;

            var body = said.Substring(prefix.Length).Trim();
            if (body.Length == 0) return;
            var parts = body.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1].Trim() : null;

            string playerId;
            string who;
            if (fromSelf) { playerId = Game.PlayerId; who = "you"; }
            else
            {
                if (Game.IsCrewmate) return;             // only the host owns wallets and upgrades
                if (Settings.ChatAllowlistOnly.Value && !Settings.OnAllowlist(senderName))
                {
                    if (IsKnown(cmd)) Reply(senderName + ": chat commands are allowlist-only in this lobby.");
                    return;
                }
                float wait = Settings.ChatCooldown.Value;
                float last;
                if (wait > 0 && _lastCommandAt.TryGetValue(senderName, out last) && Time.unscaledTime - last < wait)
                    return;                              // quietly ignore spam
                playerId = Game.PlayerIdByName(senderName);
                who = senderName;
                if (playerId == null) { Reply("Can't find a player called " + senderName + "."); return; }
            }

            string result = null;
            switch (cmd)
            {
                case "help": case "commands":
                    result = "Commands: " + Help; break;
                case "trainer": case "mod": case "install":
                    result = "Get Moon Trainer: open PowerShell and run   irm " + InstallUrl + " | iex"; break;
                case "players": case "who":
                    {
                        var names = new List<string>();
                        foreach (var m in Game.Members()) names.Add(m.Name);
                        result = "In the lobby: " + string.Join(", ", names);
                    }
                    break;

                // ---- giveables (host applies them for anyone) ----
                case "money": case "cash": case "$":
                    if (!Allowed(fromSelf, true)) return;
                    {
                        double amount;
                        if (arg == null || !double.TryParse(arg.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) { result = "Usage: " + prefix + "money 5000"; break; }
                        if (!fromSelf)
                        {
                            double cap = Settings.ChatMoneyCap.Value;
                            if (cap > 0 && Math.Abs(amount) > cap) amount = Math.Sign(amount) * cap;
                            double sessionCap = Settings.ChatSessionMoneyCap.Value;
                            double given;
                            _moneyGiven.TryGetValue(senderName, out given);
                            if (sessionCap > 0 && amount > 0 && given + amount > sessionCap)
                            {
                                amount = Math.Max(0, sessionCap - given);
                                if (amount <= 0) { result = who + " has hit this session's money limit."; break; }
                            }
                            if (amount > 0) _moneyGiven[senderName] = given + amount;
                        }
                        var balance = Game.AddMoney(playerId, amount);
                        result = (amount >= 0 ? "+$" : "-$") + Math.Abs(amount).ToString("N0") + " for " + who + " (team now $" + balance.ToString("N0") + ")";
                    }
                    break;
                case "jetpack":     if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "gear.jetpack"); break;
                case "boots":       if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "gear.boots"); break;
                case "nightvision": case "nv": if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "gear.nightvision"); break;
                case "belt":        if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "gear.belt"); break;
                case "lamp":        if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "suit.lamp"); break;
                case "speed": case "stride": if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "suit.stride"); break;
                case "jump":        if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "suit.jump"); break;
                case "width":       if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "clean.width"); break;
                case "payout":      if (Allowed(fromSelf, true)) result = Game.GiveUpgrade(playerId, "eco.payout"); break;
                case "all": case "max": if (Allowed(fromSelf, true)) result = Game.GiveAllUpgrades(playerId); break;
                case "upgrade":
                    if (!Allowed(fromSelf, true)) return;
                    result = arg == null ? "Usage: " + prefix + "upgrade gear.jetpack" : Game.GiveUpgrade(playerId, arg.ToLowerInvariant());
                    break;
                case "flag": case "site":
                    if (!Allowed(fromSelf, true)) return;
                    {
                        Vector3 pos;
                        if (!Game.PositionOf(senderName, fromSelf, out pos)) { result = "Can't see where " + who + " is."; break; }
                        result = Cheats.RaiseNearest(pos, cmd == "flag" ? MoonLandmarkKind.Flag : MoonLandmarkKind.Site, playerId);
                        if (!fromSelf) result = who + ": " + result;
                    }
                    break;

                // ---- movement (only ever the typing machine) ----
                case "fly":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    Cheats.SetFly(ParseToggle(arg, !Cheats.Fly)); result = "Fly " + OnOff(Cheats.Fly); break;
                case "noclip":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    Cheats.Noclip = ParseToggle(arg, !Cheats.Noclip); if (Cheats.Noclip) Cheats.Fly = true;
                    result = "Noclip " + OnOff(Cheats.Noclip); break;
                case "flyspeed":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    { float v; if (arg != null && float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { Cheats.FlySpeed = Math.Max(1f, v); result = "Fly speed " + Cheats.FlySpeed; } else result = "Usage: " + prefix + "flyspeed 40"; }
                    break;
                case "tp":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    result = Cheats.TeleportToWaypointByNumber(arg); break;
                case "tpto": case "goto":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    result = arg == null ? "Usage: " + prefix + "tpto <player name>" : Cheats.TeleportToPlayer(arg); break;
                default:
                    return;   // not a command we know; leave the message alone
            }

            if (result != null)
            {
                if (!fromSelf) _lastCommandAt[senderName] = Time.unscaledTime;
                Plugin.Logger.LogInfo("Chat command from " + who + ": " + said + " -> " + result);
                if (fromSelf) Trainer.Notice(result); else Reply(result);
            }
        }

        static bool IsKnown(string cmd)
        {
            switch (cmd)
            {
                case "money": case "cash": case "$": case "jetpack": case "boots": case "nightvision": case "nv": case "belt": case "lamp":
                case "speed": case "stride": case "jump": case "width": case "payout": case "all": case "max": case "upgrade": case "flag": case "site":
                    return true;
            }
            return false;
        }

        private static bool Allowed(bool fromSelf, bool giveable)
        {
            if (fromSelf) return true;
            return !giveable || Settings.ChatGiveables.Value;
        }

        private static bool ParseToggle(string arg, bool fallback)
        {
            if (arg == null) return fallback;
            arg = arg.ToLowerInvariant();
            if (arg == "on" || arg == "1" || arg == "true") return true;
            if (arg == "off" || arg == "0" || arg == "false") return false;
            return fallback;
        }

        private static string OnOff(bool on) { return on ? "on" : "off"; }

        public static void Reply(string text)
        {
            Trainer.Notice(text);
            if (Settings.ChatReply.Value) _replies.Enqueue(text);
        }

        public static void Say(string text) { _replies.Enqueue(text); }

        /// <summary>Called every frame; sends queued replies one at a time and watches for crewmates joining.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime >= _nextMemberPoll)
            {
                _nextMemberPoll = Time.unscaledTime + 3f;
                PollMembers();
            }

            if (_replies.Count == 0) return;
            if (Time.unscaledTime - _lastReplyAt < 0.5f) return;
            _lastReplyAt = Time.unscaledTime;
            var text = _replies.Dequeue();
            try { MoonTalk.Say("[trainer] " + text); }
            catch (Exception e) { Plugin.Logger.LogWarning("Could not reply in chat: " + e.Message); }
        }

        static void PollMembers()
        {
            if (!Game.InGame || !MoonSteamLobby.InLobby) { _seenMembers.Clear(); _membersPrimed = false; return; }
            var now = Game.Members();
            var joined = new List<string>();
            foreach (var m in now)
            {
                if (m.Local || string.IsNullOrEmpty(m.Name)) continue;
                if (_seenMembers.Add(m.Name) && _membersPrimed) joined.Add(m.Name);
            }
            _membersPrimed = true;
            if (joined.Count == 0) return;
            Trainer.MembersChanged();
            if (!Settings.ChatWelcome.Value || Game.IsCrewmate || !Settings.ChatCommands.Value) return;
            var prefix = Settings.ChatPrefix.Value;
            Say("Welcome " + string.Join(", ", joined) + "! This lobby runs Moon Trainer - type " + prefix + "help for commands, " + prefix + "trainer to get it.");
        }
    }
}
