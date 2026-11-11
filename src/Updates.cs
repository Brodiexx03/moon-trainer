using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoonTrainer
{
    /// <summary>
    /// Checks GitHub for a newer release at startup, and installs it when the player asks for it.
    /// It never installs anything by itself: the check only tells the player, and the install only runs from a button.
    /// </summary>
    public static class Updates
    {
        const string Repo = "Brodiexx03/moon-trainer";
        public const string InstallCommand = "irm https://raw.githubusercontent.com/Brodiexx03/moon-trainer/main/install.ps1 | iex";

        /// <summary>Newer version number, once found (read on the main thread).</summary>
        public static volatile string Available;
        /// <summary>Newest version on GitHub (even if it's ours), once the check has finished.</summary>
        public static volatile string Latest;
        public static volatile string Notes;
        public static volatile bool Announced;

        // ---- one-click install (runs on a background thread; the menu and the message box read these) ----
        public const int Idle = 0, Working = 1, Done = 2, Failed = 3;
        public static volatile int State;
        /// <summary>What's happening, or how it ended, in words a player can read.</summary>
        public static volatile string Status = "";
        /// <summary>The version that was just installed and is waiting for a restart.</summary>
        public static volatile string Installed;

        sealed class Problem : Exception { public Problem(string message) : base(message) { } }

        static string ModDir { get { return Path.Combine(BepInEx.Paths.GameRootPath, "Mods", "MoonTrainer"); } }

        static string _branch;

        /// <summary>
        /// Where updates come from: "main", unless Mods\MoonTrainer\update-branch.txt names another branch of the same
        /// repository (used to try a release on one PC before everyone gets it).
        /// </summary>
        public static string Branch
        {
            get
            {
                if (_branch != null) return _branch;
                string b = "main";
                try
                {
                    var file = Path.Combine(ModDir, "update-branch.txt");
                    if (File.Exists(file))
                    {
                        var text = File.ReadAllText(file).Trim();
                        if (Regex.IsMatch(text, "^[A-Za-z0-9._-]{1,60}$")) b = text;
                    }
                }
                catch (Exception) { }
                _branch = b;
                return b;
            }
        }

        static string RawBase { get { return "https://raw.githubusercontent.com/" + Repo + "/" + Branch; } }

        public static void CheckInBackground()
        {
            if (!Settings.CheckForUpdates.Value) return;
            if (Branch != "main") Plugin.Logger.LogWarning("Updates are coming from the '" + Branch + "' test channel (Mods\\MoonTrainer\\update-branch.txt).");
            Task.Run(async () =>
            {
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("MoonTrainer/" + Plugin.Version);
                        var json = await http.GetStringAsync(RawBase + "/version.json?t=" + DateTime.UtcNow.Ticks);
                        var v = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                        var n = Regex.Match(json, "\"notes\"\\s*:\\s*\"([^\"]*)\"");
                        if (!v.Success) return;
                        Latest = v.Groups[1].Value;
                        if (IsNewer(v.Groups[1].Value, Plugin.Version))
                        {
                            Notes = n.Success ? n.Groups[1].Value : "";
                            Available = v.Groups[1].Value;
                            var log = Plugin.Logger;
                            log.LogWarning("==============================================================");
                            log.LogWarning("  Moon Trainer update available: " + Plugin.Version + " -> " + Available);
                            if (!string.IsNullOrEmpty(Notes)) log.LogWarning("  " + Notes);
                            log.LogWarning("  To update: choose UPDATE NOW in the box at the main menu, or open the");
                            log.LogWarning("  trainer menu in a game and click Update now. Or, with the game closed,");
                            log.LogWarning("  run this in PowerShell:");
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

        /// <summary>Puts a failed install back to "not started" so it can be tried again.</summary>
        public static void Reset()
        {
            if (State == Failed) { State = Idle; Status = ""; }
        }

        /// <summary>
        /// Downloads the new version and puts it in Mods\MoonTrainer. Only ever called from a button the player pressed.
        /// The running game keeps using the copy it loaded; the new one takes over at the next start.
        /// </summary>
        public static void InstallInBackground()
        {
            if (State == Working || State == Done) return;
            State = Working;
            Status = "Starting...";
            Task.Run(async () =>
            {
                try { await Install(); }
                catch (Problem p) { Fail(p.Message); }
                catch (UnauthorizedAccessException) { Fail("Windows wouldn't let the trainer write to the game folder."); }
                catch (HttpRequestException e) { Fail("Couldn't reach GitHub (" + e.Message + ")."); }
                catch (TaskCanceledException) { Fail("The download took too long."); }
                catch (Exception e) { Fail("Something went wrong (" + e.Message + ")."); }
            });
        }

        static void Fail(string why)
        {
            Status = why;
            State = Failed;
            Plugin.Logger.LogWarning("Update not installed: " + why + " Nothing was changed. You can still update with PowerShell: " + InstallCommand);
        }

        static async Task Install()
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MoonTrainer/" + Plugin.Version);

                // Everything comes from one exact commit, so the DLL, its description and its checksum belong together
                // (GitHub caches branch files for a few minutes and can otherwise hand out a mix of old and new).
                Status = "Finding the latest version...";
                string from = null, bust = "";
                try
                {
                    var commit = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/commits/" + Branch);
                    var sha = Regex.Match(commit, "\"sha\"\\s*:\\s*\"([0-9a-f]{40})\"");
                    if (sha.Success) from = "https://raw.githubusercontent.com/" + Repo + "/" + sha.Groups[1].Value;
                }
                catch (Exception) { }
                if (from == null) { from = RawBase; bust = "?t=" + DateTime.UtcNow.Ticks; }

                var json = await http.GetStringAsync(from + "/version.json" + bust);
                var v = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                var want = Regex.Match(json, "\"sha256\"\\s*:\\s*\"([0-9A-Fa-f]{64})\"");
                if (!v.Success) throw new Problem("The release information on GitHub couldn't be read.");
                string version = v.Groups[1].Value;
                if (!IsNewer(version, Plugin.Version)) throw new Problem("You already have the newest version (" + Plugin.Version + ").");
                if (!want.Success) throw new Problem("This release has no checksum, so it can't be installed from inside the game.");

                Status = "Downloading version " + version + "...";
                byte[] dll = await http.GetByteArrayAsync(from + "/Mods/MoonTrainer/MoonTrainer.dll" + bust);
                byte[] mod = await http.GetByteArrayAsync(from + "/Mods/MoonTrainer/mod.json" + bust);

                // Check it before touching the copy that's installed.
                Status = "Checking the download...";
                if (dll.Length < 10000 || dll.Length > 50 * 1024 * 1024) throw new Problem("The download was the wrong size.");
                if (!string.Equals(Hash(dll), want.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    throw new Problem("The download didn't match its checksum. Try again in a few minutes.");
                var mv = Regex.Match(Encoding.UTF8.GetString(mod), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (!mv.Success || mv.Groups[1].Value != version)
                    throw new Problem("The download was a mix of two versions. Try again in a few minutes.");

                Status = "Installing version " + version + "...";
                string dir = ModDir;
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "MoonTrainer.dll"), temp = target + ".download", previous = target + ".previous";
                try
                {
                    File.WriteAllBytes(temp, dll);
                    // Read it back: an antivirus or a full disk can leave a cut-off file behind.
                    if (!string.Equals(Hash(File.ReadAllBytes(temp)), want.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                        throw new Problem("The file didn't save correctly (an antivirus may have blocked it).");
                    if (File.Exists(target)) File.Copy(target, previous, true);       // one step back, in case it's ever needed
                    File.Copy(temp, target, true);
                    File.SetLastWriteTimeUtc(target, DateTime.UtcNow);                // so the mod loader sees it as changed
                    File.WriteAllBytes(Path.Combine(dir, "mod.json"), mod);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                }

                Installed = version;
                Status = "Moon Trainer " + version + " is installed. Restart the game to start using it.";
                State = Done;
                Plugin.Logger.LogWarning("Moon Trainer " + version + " installed (you're running " + Plugin.Version + " until the game restarts).");
            }
        }

        static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
