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
        private static string _ownText;
        private static float _ownAt = -100f;

        public static readonly string Help =
            "trainer, money <amt>, jetpack, boots, nv, belt, lamp, speed, jump, width, payout, all, flag, site, players | fly, noclip, flyspeed <n>, tp <marker#>, tpto <name>, save [name], go <name> (need the mod)";

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

            bool giveable = IsKnown(cmd);
            string playerId;
            string who;
            if (fromSelf)
            {
                playerId = Game.PlayerId; who = "you";
                _ownText = said; _ownAt = Time.unscaledTime;
                if (giveable && Game.IsCrewmate && Game.HostHasTrainer())
                {
                    // The host owns the wallet and everyone's upgrades, and their trainer reads this same chat line.
                    // Doing it here as well would apply it twice, or only on this PC.
                    Trainer.Notice("Sent. The host's trainer handles " + prefix + cmd + ".");
                    return;
                }
            }
            else
            {
                if (Game.IsCrewmate) return;             // only the host owns wallets and upgrades
                if (string.IsNullOrEmpty(senderName)) return;
                // Our own lines can come back through "heard" too. The hook on sending has already dealt with them.
                bool sayHooked = !Plugin.FailedHooks.Contains("ChatPatches.Say");
                if (sayHooked && said == _ownText && Time.unscaledTime - _ownAt < 3f && Game.IsLocalName(senderName)) return;

                float wait = Settings.ChatCooldown.Value;
                float last;
                if (wait > 0 && _lastCommandAt.TryGetValue(senderName, out last) && Time.unscaledTime - last < wait)
                    return;                              // quietly ignore spam

                // Chat only gives us a display name. Tie it to exactly one person in the lobby before giving anything.
                Game.Member sender;
                int matches = Game.FindMember(senderName, out sender);
                // Playing solo there's no lobby list to match against, and anything we hear is our own line.
                if ((matches == 1 && sender.Local) || (matches == 0 && !MoonSteamLobby.InLobby))
                {
                    if (sayHooked) return;               // us again
                    fromSelf = true;                     // the sending hook is missing on this game version: handle it here
                }
                if (!fromSelf)
                {
                    if (giveable && matches != 1)
                    {
                        _lastCommandAt[senderName] = Time.unscaledTime;
                        Reply(matches == 0
                            ? "Can't find a player called " + senderName + "."
                            : "Two players here are called " + senderName + ", so I can't tell who asked. Change one name and try again.");
                        return;
                    }
                    if (Settings.ChatAllowlistOnly.Value && (matches != 1 || !Settings.OnAllowlist(sender.Name, sender.SteamId)))
                    {
                        if (giveable) { _lastCommandAt[senderName] = Time.unscaledTime; Reply(senderName + ": chat commands are allowlist-only in this lobby."); }
                        return;
                    }
                }
                playerId = fromSelf ? Game.PlayerId : (matches == 1 ? sender.Id : null);
                who = fromSelf ? "you" : senderName;
            }

            string result = null;
            switch (cmd)
            {
                case "help": case "commands":
                    result = "Commands: " + Help; break;
                case "trainer": case "mod": case "install":
                    SayInstall(fromSelf ? null : senderName);
                    if (!fromSelf) _lastCommandAt[senderName] = Time.unscaledTime;
                    return;
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
                        if (!TryAmount(arg, out amount)) { result = "Usage: " + prefix + "money 5000"; break; }
                        if (!fromSelf)
                        {
                            // Crewmates can only add. (Taking money away is for the host: the wallet is shared.)
                            if (amount <= 0) { result = who + ": the amount has to be more than 0."; break; }
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
                    { float v; if (arg != null && float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v)) { Cheats.FlySpeed = Mathf.Clamp(v, 1f, 500f); result = "Fly speed " + Cheats.FlySpeed; } else result = "Usage: " + prefix + "flyspeed 40"; }
                    break;
                case "tp":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    result = Cheats.TeleportToWaypointByNumber(arg); break;
                case "save": case "mark":
                    if (!fromSelf) return;
                    result = Locations.SaveHere(arg); break;
                case "go":
                    if (!fromSelf || !Settings.ChatMovement.Value) return;
                    result = arg == null ? "Usage: " + prefix + "go <saved location>" : Locations.Go(arg); break;
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

        /// <summary>Reads "5000", "5,000", "$5000" or "2.5e3". Refuses anything that isn't a real number; caps the size.</summary>
        static bool TryAmount(string arg, out double amount)
        {
            amount = 0;
            if (string.IsNullOrEmpty(arg)) return false;
            arg = arg.Replace(",", "").Replace("$", "").Replace(" ", "");
            if (!double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) return false;
            if (double.IsNaN(amount) || double.IsInfinity(amount)) return false;
            amount = Math.Max(-1e12, Math.Min(1e12, amount));
            return true;
        }

        /// <summary>Commands that give something (the host carries these out for whoever typed them).</summary>
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

        /// <summary>The game cuts chat messages at 120 characters (MoonTalk.MaxLength), prefix included.</summary>
        const int ChatLimit = 120;
        const string Tag = "[trainer] ";

        public static string InstallLine { get { return "irm " + InstallUrl + " | iex"; } }

        public static void Reply(string text)
        {
            Trainer.Notice(text);
            if (Settings.ChatReply.Value) Say(text);
        }

        /// <summary>Queues a chat message, split at word boundaries so nothing is cut off.</summary>
        public static void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int room = ChatLimit - Tag.Length;
            text = text.Trim();
            while (text.Length > room)
            {
                int cut = text.LastIndexOf(' ', room);
                if (cut < room / 2) cut = room;            // one very long word: hard cut
                _replies.Enqueue(text.Substring(0, cut).TrimEnd());
                text = text.Substring(cut).TrimStart();
            }
            if (text.Length > 0) _replies.Enqueue(text);
        }

        /// <summary>Posts how to install / update, with the command on a line of its own so it can be copied whole.</summary>
        public static void SayInstall(string forName)
        {
            Say((forName != null ? forName + ": to " : "To ") + "get or update Moon Trainer, close the game, open PowerShell and run this:");
            Say(InstallLine);
        }

        /// <summary>Called every frame; sends queued replies one at a time and watches for crewmates joining.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime >= _nextMemberPoll)
            {
                _nextMemberPoll = Time.unscaledTime + 3f;
                PollMembers();
            }

            if (_replies.Count == 0) return;

            // The game drops a message sent too soon after the last one (MoonTalk.Gap), silently.
            // Wait out the gap, then check our line landed in the chat history before moving on.
            float spacing = Mathf.Max(MoonTalk.Gap + 0.35f, 1.0f);
            if (Time.unscaledTime - _lastReplyAt < spacing) return;

            var text = _replies.Peek();
            string line = Tag + text;
            if (_sentPending != null)
            {
                if (InHistory(_sentPending) || _attempts >= 3)
                {
                    if (_attempts >= 3 && !InHistory(_sentPending)) Plugin.Logger.LogWarning("Chat line never showed up after 3 tries: " + _sentPending);
                    _replies.Dequeue();
                    _sentPending = null; _attempts = 0;
                    return;
                }
            }
            try
            {
                MoonTalk.Say(line);
                _sentPending = line; _attempts++;
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Could not reply in chat: " + e.Message); _replies.Dequeue(); _sentPending = null; _attempts = 0; }
            _lastReplyAt = Time.unscaledTime;
        }

        static string _sentPending;
        static int _attempts;

        /// <summary>True if one of the last few chat rows is this line (the game may trim or escape it, so compare loosely).</summary>
        static bool InHistory(string line)
        {
            try
            {
                var h = MoonTalk.history;
                if (h == null) return true;                     // can't tell: assume it went
                int n = h.Count;
                string want = Norm(line);
                for (int i = n - 1; i >= 0 && i >= n - 6; i--)
                {
                    var said = h[i];
                    if (said == null || said.text == null) continue;
                    var got = Norm(said.text);
                    if (got.Length > 0 && (got == want || want.StartsWith(got) || got.StartsWith(want))) return true;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        static string Norm(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
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
