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
        /// <summary>
        /// Set when an install stopped half-way and couldn't be undone: this version's DLL is in place but its mod.json
        /// isn't. Null otherwise: a failed install has then left the installed files exactly as they were.
        /// </summary>
        public static volatile string HalfInstalled;

        /// <summary>
        /// What a failed install left behind, in one sentence. Every place that reports a failure adds this instead of
        /// wording of its own, so "nothing was changed" is only ever said when it's true.
        /// </summary>
        public static string Aftermath
        {
            get
            {
                string half = HalfInstalled;
                return half == null ? "Nothing was changed." : "Version " + half + "'s DLL is in place, but not its mod.json.";
            }
        }

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
            if (HalfInstalled == null)
                Plugin.Logger.LogWarning("Update not installed: " + why + " " + Aftermath + " You can still update with PowerShell: " + InstallCommand);
            else
                Plugin.Logger.LogWarning("Update only partly installed: " + why + " " + Aftermath
                    + " The new version will still load at the next start. To finish the job, try again, or close the game and use PowerShell: " + InstallCommand);
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
                var want = Regex.Match(json, "\"sha256\"\\s*:\\s*\"\\s*([0-9A-Fa-f]{64})\\s*\"");    // (spaces allowed, as install.ps1 and the release check allow them)
                if (!v.Success) throw new Problem("The release information on GitHub couldn't be read.");
                string version = v.Groups[1].Value;
                if (!IsNewer(version, Plugin.Version)) throw new Problem("You already have the newest version (" + Plugin.Version + ").");
                if (!want.Success) throw new Problem("This release has no checksum yet, so it can't be installed. Try again later.");

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
                PutInPlace(ModDir, dll, mod, want.Groups[1].Value, version);

                Installed = version;
                Status = "Moon Trainer " + version + " is installed. Restart the game to start using it.";
                State = Done;
                Plugin.Logger.LogWarning("Moon Trainer " + version + " installed (you're running " + Plugin.Version + " until the game restarts).");
            }
        }

        /// <summary>
        /// Swaps a checked download in for the installed DLL and mod.json, as close to all-or-nothing as plain files allow.
        /// Everything is made ready next to the installed files first. The swap itself is then two renames, and if the
        /// second can't be done the first is undone, so the DLL and its description always belong to the same version.
        /// </summary>
        static void PutInPlace(string dir, byte[] dll, byte[] mod, string hash, string version)
        {
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "MoonTrainer.dll"), temp = target + ".download", previous = target + ".previous", old = target + ".old";
            string json = Path.Combine(dir, "mod.json"), tempJson = json + ".download";
            bool spare = false, stays = false, keepOld = false;
            try
            {
                File.WriteAllBytes(temp, dll);
                File.WriteAllBytes(tempJson, mod);
                // Read them back: an antivirus or a full disk can leave a cut-off file behind.
                if (!string.Equals(Hash(File.ReadAllBytes(temp)), hash, StringComparison.OrdinalIgnoreCase)
                    || Hash(File.ReadAllBytes(tempJson)) != Hash(mod))
                    throw new Problem("The file didn't save correctly (an antivirus may have blocked it).");
                File.SetLastWriteTimeUtc(temp, DateTime.UtcNow);                      // so the mod loader sees it as changed

                // If an earlier try stopped half-way, this very DLL is already in place and only mod.json is still to
                // come: leave it, and leave the ".previous" from that try alone too.
                bool had = File.Exists(target);
                bool swap = !had || !string.Equals(Hash(File.ReadAllBytes(target)), hash, StringComparison.OrdinalIgnoreCase);
                // A spare copy of the DLL that's installed now. It goes back if the swap can't be finished, and becomes
                // ".previous" once it has; until then the ".previous" that's there stays as it is.
                spare = swap && had;
                if (spare) File.Copy(target, old, true);

                // Nothing that's installed has been touched up to here. A rename either happens or it doesn't, and can't
                // run out of disk half-way, so from here on MoonTrainer.dll is always a whole file: the old one or the new.
                if (swap) Move(temp, target);
                stays = true;
                try { Move(tempJson, json); }
                catch (Exception)
                {
                    // The new DLL is in but its description isn't. Put the old DLL back so the two still belong together.
                    if (swap)
                    {
                        try
                        {
                            if (had) Move(old, target); else File.Delete(target);
                            stays = false;
                        }
                        catch (Exception) { }
                    }
                    if (stays) HalfInstalled = version;
                    throw;
                }
                HalfInstalled = null;
            }
            finally
            {
                if (spare && stays)
                {
                    // One step back, in case it's ever needed: the DLL the new one replaced.
                    try { Move(old, previous); }
                    catch (Exception)
                    {
                        keepOld = true;     // better left next to it under this name than thrown away
                        Plugin.Logger.LogInfo("The version you had couldn't be kept as MoonTrainer.dll.previous, so it's MoonTrainer.dll.old instead.");
                    }
                }
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch (Exception) { }
                try { if (!keepOld && File.Exists(old)) File.Delete(old); } catch (Exception) { }
            }
        }

        /// <summary>Renames a file over another. An antivirus can hold a file for a moment, so it tries a few times.</summary>
        static void Move(string from, string to)
        {
            for (int tries = 1; ; tries++)
            {
                try { File.Move(from, to, true); return; }
                catch (IOException) { if (tries >= 4) throw; }
                catch (UnauthorizedAccessException)
                {
                    // Windows says "access denied" for a file that's open somewhere else too, so don't blame the folder.
                    if (tries >= 4) throw new Problem("Windows wouldn't let the trainer replace " + Path.GetFileName(to) + " (another program may have it open).");
                }
                Task.Delay(250).Wait();
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
