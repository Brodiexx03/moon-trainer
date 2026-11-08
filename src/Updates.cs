using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoonTrainer
{
    /// <summary>
    /// Checks GitHub for a newer release at startup. It only ever tells the player; installing is always their choice.
    /// </summary>
    public static class Updates
    {
        public const string VersionUrl = "https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/version.json";
        public const string InstallCommand = "irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1 | iex";

        /// <summary>Newer version number, once found (read on the main thread).</summary>
        public static volatile string Available;
        public static volatile string Notes;
        public static volatile bool Announced;

        public static void CheckInBackground()
        {
            if (!Settings.CheckForUpdates.Value) return;
            Task.Run(async () =>
            {
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("MoonTrainer/" + Plugin.Version);
                        var json = await http.GetStringAsync(VersionUrl + "?t=" + DateTime.UtcNow.Ticks);
                        var v = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                        var n = Regex.Match(json, "\"notes\"\\s*:\\s*\"([^\"]*)\"");
                        if (!v.Success) return;
                        if (IsNewer(v.Groups[1].Value, Plugin.Version))
                        {
                            Notes = n.Success ? n.Groups[1].Value : "";
                            Available = v.Groups[1].Value;
                            var log = Plugin.Logger;
                            log.LogWarning("==============================================================");
                            log.LogWarning("  Moon Trainer update available: " + Plugin.Version + " -> " + Available);
                            if (!string.IsNullOrEmpty(Notes)) log.LogWarning("  " + Notes);
                            log.LogWarning("  To update: close the game, open PowerShell and run:");
                            log.LogWarning("  " + InstallCommand);
                            log.LogWarning("==============================================================");
                        }
                        else Plugin.Logger.LogInfo("Moon Trainer is up to date (" + Plugin.Version + ").");
                    }
                }
                catch (Exception e) { Plugin.Logger.LogInfo("Update check skipped: " + e.Message); }
            });
        }

        public static bool IsNewer(string remote, string local)
        {
            Version a, b;
            if (!System.Version.TryParse(remote, out a) || !System.Version.TryParse(local, out b)) return false;
            return a > b;
        }
    }
}
