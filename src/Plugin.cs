using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MoonTrainer
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "moontrainer";
        public const string Name = "Moon Trainer";
        public const string Version = "1.2.1";

        public static ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;
            Settings.Bind(Config);
            Settings.LoadCheats();
            try
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(Patches));
                harmony.PatchAll(typeof(ChatPatches));
                Log.LogInfo("Patches applied.");
            }
            catch (Exception e) { Log.LogError("Patching failed: " + e); }
            AddComponent<Trainer>();
            Updates.CheckInBackground();
            Log.LogInfo(Name + " " + Version + " loaded. Press " + Settings.MenuKey.Value + " in-game to open the menu.");
        }
    }

    /// <summary>All config entries (BepInEx\config\moontrainer.cfg, also editable in the MODS screen).</summary>
    public static class Settings
    {
        // Keys
        public static ConfigEntry<string> MenuKey, FlyKey, NoclipKey, SpeedKey, JetpackKey, SaveKey, HudKey;
        // Chat
        public static ConfigEntry<string> ChatPrefix;
        public static ConfigEntry<bool> ChatCommands, ChatGiveables, ChatMovement, ChatReply, ChatWelcome, ChatAllowlistOnly;
        public static ConfigEntry<string> ChatAllowlist;
        public static ConfigEntry<float> ChatMoneyCap, ChatSessionMoneyCap, ChatCooldown;
        // Interface
        public static ConfigEntry<bool> ShowHud, CheckForUpdates;
        public static ConfigEntry<float> MenuX, MenuY;
        public static ConfigEntry<string> Collapsed;
        // Remembered cheats
        public static ConfigEntry<bool> RememberCheats;
        public static ConfigEntry<string> SavedCheats;

        static ConfigFile _cfg;
        static readonly System.Collections.Generic.Dictionary<string, ConfigEntry<string>> _locations = new System.Collections.Generic.Dictionary<string, ConfigEntry<string>>();

        /// <summary>Saved locations for one save slot (one config line per slot, under [Locations]).</summary>
        static ConfigEntry<string> LocationEntry(string slot)
        {
            var key = System.Text.RegularExpressions.Regex.Replace(slot ?? "default", @"[^A-Za-z0-9_.-]", "_");
            ConfigEntry<string> e;
            if (!_locations.TryGetValue(key, out e))
            {
                e = _cfg.Bind("Locations", key, "", "Saved locations for save slot " + key + ". Managed by the trainer.");
                _locations[key] = e;
            }
            return e;
        }

        public static string LocationsFor(string slot) { return LocationEntry(slot).Value; }
        public static void SetLocationsFor(string slot, string value) { LocationEntry(slot).Value = value; }

        public static void Bind(ConfigFile c)
        {
            _cfg = c;
            MenuKey    = c.Bind("Keys", "MenuKey", "Insert", "Opens and closes the trainer menu (Unity Input System key name, e.g. Insert, F4, Home).");
            FlyKey     = c.Bind("Keys", "FlyKey", "F", "Toggles fly. None to disable.");
            NoclipKey  = c.Bind("Keys", "NoclipKey", "Numpad1", "Toggles no clip (turns fly on too). None to disable.");
            SpeedKey   = c.Bind("Keys", "SpeedBoostKey", "Numpad2", "Toggles a 3x move-speed boost. None to disable.");
            JetpackKey = c.Bind("Keys", "JetpackKey", "Numpad3", "Toggles unlimited jetpack. None to disable.");
            SaveKey    = c.Bind("Keys", "SaveKey", "Numpad4", "Saves the game now. None to disable.");
            HudKey     = c.Bind("Keys", "HudKey", "Numpad0", "Shows / hides the active-cheats line. None to disable.");

            ChatPrefix        = c.Bind("Chat", "Prefix", "!", "Characters a chat message must start with to be treated as a command.");
            ChatCommands      = c.Bind("Chat", "Enabled", true, "Let crewmates use chat commands at all.");
            ChatGiveables     = c.Bind("Chat", "AllowGiveables", true, "Crewmates may give themselves money, upgrades, flags and sites.");
            ChatMovement      = c.Bind("Chat", "AllowMovement", true, "Your own game reacts to !fly, !noclip, !flyspeed, !tp, !tpto typed by you.");
            ChatReply         = c.Bind("Chat", "ReplyInChat", true, "Post a short confirmation in chat when a command is carried out.");
            ChatWelcome       = c.Bind("Chat", "WelcomeMessage", true, "When someone joins your lobby, post a one-line welcome with the command list.");
            ChatAllowlistOnly = c.Bind("Chat", "AllowlistOnly", false, "Only players on the allowlist may use chat commands.");
            ChatAllowlist     = c.Bind("Chat", "Allowlist", "", "Comma-separated player names allowed to use chat commands when AllowlistOnly is on.");
            ChatMoneyCap        = c.Bind("Chat", "MoneyCap", 1000000f, "Most money a single !money command may give.");
            ChatSessionMoneyCap = c.Bind("Chat", "SessionMoneyCap", 10000000f, "Most money one crewmate may request per game session (0 = no limit).");
            ChatCooldown        = c.Bind("Chat", "CooldownSeconds", 3f, "Seconds a crewmate must wait between commands.");

            ShowHud         = c.Bind("Interface", "ShowActiveCheats", true, "Show a small line listing active cheats while the menu is closed.");
            CheckForUpdates = c.Bind("Interface", "CheckForUpdates", true, "Check GitHub for a newer Moon Trainer at startup and tell you (never installs anything by itself).");
            MenuX     = c.Bind("Interface", "MenuX", 32f, "Menu position (drag the title bar to move it).");
            MenuY     = c.Bind("Interface", "MenuY", -40f, "Menu position (drag the title bar to move it).");
            Collapsed = c.Bind("Interface", "CollapsedSections", "", "Sections folded away in the menu.");

            RememberCheats = c.Bind("Cheats", "RememberBetweenLaunches", true, "Restore your toggles and multipliers next time the game starts.");
            SavedCheats    = c.Bind("Cheats", "Saved", "", "Managed by the trainer.");
        }

        public static bool IsCollapsed(string section) { return ("," + Collapsed.Value + ",").Contains("," + section + ","); }

        public static void SetCollapsed(string section, bool collapsed)
        {
            var list = new System.Collections.Generic.List<string>(Collapsed.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            list.Remove(section);
            if (collapsed) list.Add(section);
            Collapsed.Value = string.Join(",", list);
        }

        public static bool OnAllowlist(string name)
        {
            foreach (var n in ChatAllowlist.Value.Split(','))
                if (string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void SetAllowlisted(string name, bool on)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var n in ChatAllowlist.Value.Split(','))
                if (n.Trim().Length > 0 && !string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)) list.Add(n.Trim());
            if (on) list.Add(name);
            ChatAllowlist.Value = string.Join(",", list);
        }

        // ---- remembered cheats: one "key=value;" string ----

        static string _lastSaved;

        public static void LoadCheats()
        {
            if (!RememberCheats.Value || string.IsNullOrEmpty(SavedCheats.Value)) return;
            foreach (var pair in SavedCheats.Value.Split(';'))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                Cheats.SetByName(kv[0], kv[1]);
            }
            _lastSaved = SavedCheats.Value;
        }

        public static void SaveCheatsIfChanged()
        {
            if (!RememberCheats.Value) return;
            var now = Cheats.Serialize();
            if (now == _lastSaved) return;
            _lastSaved = now;
            SavedCheats.Value = now;
        }
    }
}
