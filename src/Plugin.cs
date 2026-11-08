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
        public const string Version = "1.0.0";

        public static ManualLogSource Logger;

        public override void Load()
        {
            Logger = Log;
            Settings.Bind(Config);
            try
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(Patches));
                harmony.PatchAll(typeof(ChatPatches));
                Log.LogInfo("Patches applied.");
            }
            catch (Exception e) { Log.LogError("Patching failed: " + e); }
            AddComponent<Trainer>();
            Log.LogInfo(Name + " " + Version + " loaded. Press " + Settings.MenuKey.Value + " in-game to open the menu.");
        }
    }

    /// <summary>All config entries (BepInEx\config\moontrainer.cfg, also editable in the MODS screen).</summary>
    public static class Settings
    {
        public static ConfigEntry<string> MenuKey;
        public static ConfigEntry<string> FlyKey;
        public static ConfigEntry<string> ChatPrefix;
        public static ConfigEntry<bool> ChatCommands;
        public static ConfigEntry<bool> ChatGiveables;
        public static ConfigEntry<bool> ChatMovement;
        public static ConfigEntry<bool> ChatReply;
        public static ConfigEntry<float> ChatMoneyCap;

        public static void Bind(ConfigFile c)
        {
            MenuKey = c.Bind("Keys", "MenuKey", "Insert", "Key that opens and closes the trainer menu (Unity Input System key name, e.g. Insert, F4, Home).");
            FlyKey = c.Bind("Keys", "FlyKey", "F", "Key that toggles fly mode without opening the menu. Set to None to disable.");
            ChatPrefix = c.Bind("Chat", "Prefix", "!", "Characters a chat message must start with to be treated as a command.");
            ChatCommands = c.Bind("Chat", "Enabled", true, "Let crewmates use chat commands at all.");
            ChatGiveables = c.Bind("Chat", "AllowGiveables", true, "Crewmates may give themselves money and upgrades (!money, !jetpack, !boots, !nightvision, !speed, !jump, !width, !payout, !all).");
            ChatMovement = c.Bind("Chat", "AllowMovement", true, "Your own game reacts to !fly, !noclip and !flyspeed typed by you. Crewmates need the mod installed for these to affect them.");
            ChatReply = c.Bind("Chat", "ReplyInChat", true, "Post a short confirmation in chat when a command is carried out.");
            ChatMoneyCap = c.Bind("Chat", "MoneyCap", 1000000f, "Most money a single !money command may give.");
        }
    }
}
