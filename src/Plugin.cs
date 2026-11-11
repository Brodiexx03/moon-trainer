using System;
using System.Collections.Generic;
using System.Reflection;
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
        public const string Version = "1.4.1";

        public static ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;
            Settings.Bind(Config);
            Settings.LoadCheats();
            try
            {
                Log.LogInfo("Game version " + UnityEngine.Application.version + ", Unity " + UnityEngine.Application.unityVersion + ".");
            }
            catch (Exception) { }
            try
            {
                var harmony = new Harmony(Guid);
                ApplyHooks(harmony, typeof(Patches));
                ApplyHooks(harmony, typeof(ChatPatches));
                if (FailedHooks.Count == 0) Log.LogInfo("All " + HooksApplied + " hooks applied.");
                else Log.LogWarning(HooksApplied + " hooks applied, " + FailedHooks.Count + " failed (" + string.Join(", ", FailedHooks)
                    + "). The game has probably been updated; those features are off until the trainer is updated: " + FailedFeatures() + ".");
            }
            catch (Exception e) { Log.LogError("Patching failed: " + e); }
            AddComponent<Trainer>();
            Updates.CheckInBackground();
            Log.LogInfo(Name + " " + Version + " loaded. Press " + Settings.MenuKey.Value + " in-game to open the menu.");
        }

        /// <summary>Hooks that couldn't be applied ("Patches.Jump"). Empty on a game version the trainer was built for.</summary>
        public static readonly List<string> FailedHooks = new List<string>();
        public static int HooksApplied;

        /// <summary>
        /// Applies each hook on its own. Harmony's PatchAll stops at the first one that fails, so after a game update
        /// that renamed one function it would silently take every later feature (and chat commands) down with it.
        /// </summary>
        static void ApplyHooks(Harmony harmony, Type holder)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var m in holder.GetMethods(flags))
            {
                List<HarmonyMethod> attrs;
                try { attrs = HarmonyMethodExtensions.GetFromMethod(m); } catch (Exception) { continue; }
                if (attrs == null || attrs.Count == 0) continue;
                string label = holder.Name + "." + m.Name;
                try
                {
                    var info = HarmonyMethod.Merge(attrs);
                    if (info.declaringType == null || info.methodName == null) continue;     // not a hook
                    MethodBase original;
                    if (info.methodType == MethodType.Getter)
                    {
                        var prop = AccessTools.DeclaredProperty(info.declaringType, info.methodName);
                        original = prop == null ? null : prop.GetGetMethod(true);
                    }
                    else original = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
                    if (original == null) throw new MissingMethodException(info.declaringType.Name + "." + info.methodName + " isn't in this game version");
                    var patch = new HarmonyMethod(m);
                    bool prefix = m.GetCustomAttributes(typeof(HarmonyPrefix), false).Length > 0;
                    harmony.Patch(original, prefix ? patch : null, prefix ? null : patch);
                    HooksApplied++;
                }
                catch (Exception e)
                {
                    FailedHooks.Add(label);
                    Logger.LogWarning("Hook " + label + " could not be applied: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        /// <summary>The failed hooks as the features a player would recognise ("jump height, free upgrades").</summary>
        public static string FailedFeatures()
        {
            var names = new List<string>();
            foreach (var hook in FailedHooks)
            {
                string f;
                switch (hook.Substring(hook.IndexOf('.') + 1))
                {
                    case "DevTools": f = "fly and the one-click world tools"; break;
                    case "MoveSpeed": f = "move speed"; break;
                    case "Jump": f = "jump height"; break;
                    case "Jetpack": f = "unlimited jetpack"; break;
                    case "Boots": f = "gravity boots"; break;
                    case "NightVision": f = "night vision"; break;
                    case "Width": f = "brush width"; break;
                    case "BagCapacity": case "BagFull": case "WorldBagFull": f = "unlimited bag"; break;
                    case "HopperCapacity": case "HopperFull": f = "unlimited hopper"; break;
                    case "MapFlags": f = "flags on the map"; break;
                    case "Payout": f = "earnings multiplier"; break;
                    case "Discounted": case "PriceMult": f = "free upgrades"; break;
                    case "FreeTravel": f = "free travel"; break;
                    case "VehicleSpeed": f = "vehicle speed"; break;
                    case "VehicleWidth": f = "vehicle brush width"; break;
                    case "Production": f = "machine speed"; break;
                    case "Heard": f = "crewmates' chat commands"; break;
                    case "Say": f = "your own chat commands"; break;
                    default: f = hook; break;
                }
                if (!names.Contains(f)) names.Add(f);
            }
            return string.Join(", ", names);
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
        public static ConfigEntry<string> ChatAllowlist, ChatAllowlistIds;
        public static ConfigEntry<float> ChatMoneyCap, ChatSessionMoneyCap, ChatCooldown;
        // Interface
        public static ConfigEntry<bool> ShowHud, CheckForUpdates, MenuEffects, MenuSounds;
        public static ConfigEntry<float> MenuX, MenuY;
        public static ConfigEntry<string> LastTab;
        // Remembered cheats
        public static ConfigEntry<bool> RememberCheats;
        public static ConfigEntry<string> SavedCheats;

        static ConfigFile _cfg;
        static readonly Dictionary<string, ConfigEntry<string>> _locations = new Dictionary<string, ConfigEntry<string>>();

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
            ChatAllowlistIds  = c.Bind("Chat", "AllowlistIds", "", "Steam accounts behind the allowlisted names (id=name;id=name), so a copied name isn't enough. Managed by the trainer.");
            ChatMoneyCap        = c.Bind("Chat", "MoneyCap", 1000000f, "Most money a single !money command may give.");
            ChatSessionMoneyCap = c.Bind("Chat", "SessionMoneyCap", 10000000f, "Most money one crewmate may request per game session (0 = no limit).");
            ChatCooldown        = c.Bind("Chat", "CooldownSeconds", 3f, "Seconds a crewmate must wait between commands.");

            ShowHud         = c.Bind("Interface", "ShowActiveCheats", true, "Show a small line listing active cheats while the menu is closed.");
            CheckForUpdates = c.Bind("Interface", "CheckForUpdates", true, "Check GitHub for a newer Moon Trainer at startup and tell you. It only installs if you press Update now.");
            MenuX     = c.Bind("Interface", "MenuX2", 60f, "Menu position (drag the title bar to move it).");
            MenuY     = c.Bind("Interface", "MenuY2", -60f, "Menu position (drag the title bar to move it).");
            LastTab   = c.Bind("Interface", "LastTab", "Home", "The menu tab that was open last.");
            MenuEffects = c.Bind("Interface", "MenuEffects", true, "Animations, glow and background dimming in the menu. Turn off if they're distracting or the game is struggling.");
            MenuSounds  = c.Bind("Interface", "MenuSounds", true, "A soft click when you press something in the menu.");

            RememberCheats = c.Bind("Cheats", "RememberBetweenLaunches", true, "Restore your toggles and multipliers next time the game starts.");
            SavedCheats    = c.Bind("Cheats", "Saved", "", "Managed by the trainer.");
        }

        // ---- allowlist: a display name plus the Steam account that was using it ----

        static bool NameListed(string name)
        {
            foreach (var n in ChatAllowlist.Value.Split(','))
                if (string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static List<KeyValuePair<ulong, string>> AllowedAccounts()
        {
            var list = new List<KeyValuePair<ulong, string>>();
            foreach (var pair in ChatAllowlistIds.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                ulong id;
                if (eq > 0 && ulong.TryParse(pair.Substring(0, eq), out id)) list.Add(new KeyValuePair<ulong, string>(id, pair.Substring(eq + 1)));
            }
            return list;
        }

        static void SaveAccounts(List<KeyValuePair<ulong, string>> list)
        {
            var parts = new List<string>();
            foreach (var kv in list) parts.Add(kv.Key + "=" + (kv.Value ?? "").Replace(";", " "));
            ChatAllowlistIds.Value = string.Join(";", parts);
        }

        /// <summary>
        /// True if this player may use chat commands in allowlist-only mode. The Steam account decides, not the name:
        /// a name written into the list by hand (or by 1.2) is tied to the first account seen using it.
        /// With bind=false nothing is changed (the menu uses that to draw its switches).
        /// </summary>
        public static bool OnAllowlist(string name, ulong steamId, bool bind = true)
        {
            bool listed = NameListed(name);
            if (steamId == 0) return listed;                 // no Steam id to go on
            var accounts = AllowedAccounts();
            bool nameTaken = false;
            foreach (var kv in accounts)
            {
                if (kv.Key == steamId) return true;
                if (string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase)) nameTaken = true;
            }
            if (!listed || nameTaken) return false;
            if (bind)
            {
                accounts.Add(new KeyValuePair<ulong, string>(steamId, name));
                SaveAccounts(accounts);
                Plugin.Logger.LogInfo("Allowlist: '" + name + "' is now tied to the Steam account using that name in this lobby.");
            }
            return true;
        }

        public static void SetAllowlisted(string name, ulong steamId, bool on)
        {
            var names = new List<string>();
            foreach (var n in ChatAllowlist.Value.Split(','))
                if (n.Trim().Length > 0 && !string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase)) names.Add(n.Trim());
            if (on) names.Add(name.Replace(",", " "));
            ChatAllowlist.Value = string.Join(",", names);

            var accounts = AllowedAccounts();
            accounts.RemoveAll(kv => (steamId != 0 && kv.Key == steamId) || string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase));
            if (on && steamId != 0) accounts.Add(new KeyValuePair<ulong, string>(steamId, name));
            SaveAccounts(accounts);
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
