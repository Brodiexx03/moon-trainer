using System;
using System.Collections.Generic;
using System.Globalization;
using MoonCleaners;

namespace MoonTrainer
{
    /// <summary>
    /// Chat commands, e.g. "!money 5000", "!jetpack", "!fly".
    /// Give-type commands run on the host for whoever typed them. Movement commands only ever affect the machine
    /// that typed them (and only if that machine has the mod), because each player's movement is their own.
    /// </summary>
    public static class Chat
    {
        private static readonly Queue<string> _replies = new Queue<string>();
        private static float _lastReplyAt;

        public const string InstallUrl = "https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1";

        public static readonly string Help =
            "trainer (install link), money <amt>, jetpack, boots, nightvision, speed, jump, width, payout, all, tp <marker#> | fly, noclip, flyspeed <n> (needs the mod)";

        public static void Handle(string senderName, string said, bool fromSelf)
        {
            if (!Settings.ChatCommands.Value && !fromSelf) return;
            if (string.IsNullOrEmpty(said)) return;
            var prefix = Settings.ChatPrefix.Value ?? "!";
            if (prefix.Length == 0 || !said.StartsWith(prefix, StringComparison.Ordinal)) return;

            var body = said.Substring(prefix.Length).Trim();
            if (body.Length == 0) return;
            var parts = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1] : null;

            string playerId;
            string who;
            if (fromSelf) { playerId = Game.PlayerId; who = "you"; }
            else
            {
                if (Game.IsCrewmate) return;             // only the host owns wallets and upgrades
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

                // ---- giveables (host applies them for anyone) ----
                case "money": case "cash": case "$":
                    if (!Allowed(fromSelf, true)) return;
                    {
                        double amount;
                        if (arg == null || !double.TryParse(arg.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out amount)) { result = "Usage: " + prefix + "money 5000"; break; }
                        double cap = Settings.ChatMoneyCap.Value;
                        if (!fromSelf && cap > 0 && Math.Abs(amount) > cap) amount = Math.Sign(amount) * cap;
                        var balance = Game.AddMoney(playerId, amount);
                        result = (amount >= 0 ? "+$" : "-$") + Math.Abs(amount).ToString("N0") + " for " + who + " (now $" + balance.ToString("N0") + ")";
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
                    if (!fromSelf) return;
                    result = Cheats.TeleportToWaypointByNumber(arg); break;
                default:
                    return;   // not a command we know; leave the message alone
            }

            if (result != null)
            {
                Plugin.Logger.LogInfo("Chat command from " + who + ": " + said + " -> " + result);
                if (fromSelf) Trainer.Notice(result); else Reply(result);
            }
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

        private static void Reply(string text)
        {
            Trainer.Notice(text);
            if (Settings.ChatReply.Value) _replies.Enqueue(text);
        }

        /// <summary>Called every frame; sends queued replies one at a time so the chat isn't flooded.</summary>
        public static void Tick()
        {
            if (_replies.Count == 0) return;
            if (UnityEngine.Time.unscaledTime - _lastReplyAt < 0.5f) return;
            _lastReplyAt = UnityEngine.Time.unscaledTime;
            var text = _replies.Dequeue();
            try { MoonTalk.Say("[trainer] " + text); }
            catch (Exception e) { Plugin.Logger.LogWarning("Could not reply in chat: " + e.Message); }
        }
    }
}
