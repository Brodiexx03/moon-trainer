using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MoonTrainer
{
    /// <summary>Finds the running game's objects and wraps the awkward bits of talking to IL2CPP.</summary>
    public static class Game
    {
        private static MoonPrototypeEquipment _equipment;
        private static long _nextEquipmentSearch;

        static bool Alive(UnityEngine.Object o) { return o != null && !o.WasCollected && o; }

        public static MoonPrototypeEquipment Equipment
        {
            get
            {
                if (Alive(_equipment)) return _equipment;
                _equipment = null;
                // Not in a game (menus, loading): look again at most twice a second instead of on every access.
                long now = Environment.TickCount64;
                if (now < _nextEquipmentSearch) return null;
                _nextEquipmentSearch = now + 500;
                var found = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<MoonPrototypeEquipment>(), FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < found.Length; i++)
                {
                    var eq = found[i].TryCast<MoonPrototypeEquipment>();
                    if (eq != null && eq.Player != null) { _equipment = eq; break; }
                }
                return _equipment;
            }
        }

        public static MoonWalker Walker { get { var e = Equipment; return e == null ? null : e.Player; } }
        public static MoonWorld World { get { var e = Equipment; return e == null ? null : e.World; } }
        public static MoonPrototypeVehicle Vehicle { get { var e = Equipment; return e == null ? null : e.Vehicle; } }
        public static bool InGame { get { return Equipment != null && Walker != null && World != null; } }

        public static string PlayerId
        {
            get
            {
                var e = Equipment;
                if (e != null && e.Inventory != null && !string.IsNullOrEmpty(e.Inventory.playerId)) return e.Inventory.playerId;
                return MoonWorld.LocalPlayer ?? "local";
            }
        }

        public static bool IsCrewmate { get { return MoonSteamLobby.InLobby && !MoonSteamLobby.IsHost; } }

        static string _idA, _idB; static long _idsAt = -100000;

        /// <summary>True if a player id from the game is ours. Called from hooks on hot paths, so the ids are cached for a second.</summary>
        public static bool IsLocalId(string playerId)
        {
            if (playerId == null) return true;
            long now = Environment.TickCount64;
            if (_idA == null || now - _idsAt > 1000)
            {
                _idsAt = now;
                _idA = PlayerId;
                try { _idB = MoonWorld.LocalPlayer; } catch (Exception) { _idB = null; }
            }
            return playerId == _idA || playerId == _idB;
        }

        static string[] _crewIds; static long _crewIdsAt = -100000;

        /// <summary>
        /// True if a player id from the game is the key of somebody else in the lobby (never when playing alone).
        /// For hooks that should skip crewmates without having to recognise every form our own id can take.
        /// Hot path like IsLocalId: the crew's keys are re-read every three seconds at most.
        /// </summary>
        public static bool IsCrewmateId(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            long now = Environment.TickCount64;
            if (_crewIds == null || now - _crewIdsAt > 3000)
            {
                _crewIdsAt = now;
                var ids = new List<string>();
                try
                {
                    if (MoonSteamLobby.InLobby)
                        foreach (var m in Members())
                            if (!m.Local && !string.IsNullOrEmpty(m.Id)) ids.Add(m.Id);
                }
                catch (Exception) { }
                _crewIds = ids.ToArray();
            }
            for (int i = 0; i < _crewIds.Length; i++)
                if (_crewIds[i] == playerId) return true;
            return false;
        }

        static bool _bagOwnerBroken;

        /// <summary>
        /// Whose bag this is. Null - which the hooks count as ours - if this game version no longer has the field.
        /// </summary>
        public static string BagOwner(MoonDustInventory bag)
        {
            if (_bagOwnerBroken) return null;
            try { return ReadBagOwner(bag); }
            catch (Exception e) { _bagOwnerBroken = true; Plugin.Logger.LogWarning("Can't tell whose bag is whose on this game version: " + e.Message); return null; }
        }

        // On its own and never inlined: if a game update drops the field, that surfaces when THIS method is first
        // compiled, inside the try above. Named directly in a hook it would fail the whole hook on every call,
        // cheat on or off.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string ReadBagOwner(MoonDustInventory bag) { return bag.playerId; }

        /// <summary>True while the player is typing in the game's chat box (hotkeys must not fire then).</summary>
        public static bool Typing
        {
            get
            {
                if (_typingBroken) return false;
                try { return MoonInput.Typing; }
                catch (Exception e) { _typingBroken = true; Plugin.Logger.LogWarning("Can't tell when chat is open on this game version: " + e.Message); return false; }
            }
        }
        static bool _typingBroken;

        /// <summary>The game's own key for a crewmate (what wallets, upgrades and landmarks are filed under).</summary>
        public static string PlayerKey(ulong steamId)
        {
            try { var k = MoonCrew.PlayerId(steamId); if (!string.IsNullOrEmpty(k)) return k; }
            catch (Exception e) { Plugin.Logger.LogWarning("MoonCrew.PlayerId failed, using the raw Steam id: " + e.Message); }
            return steamId.ToString();
        }

        private static MoonSolarSystem _sky;
        public static MoonSolarSystem Sky
        {
            get
            {
                if (Alive(_sky)) return _sky;
                _sky = Find<MoonSolarSystem>();
                return _sky;
            }
        }

        public static T Find<T>() where T : UnityEngine.Object
        {
            var found = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<T>(), FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return found.Length > 0 ? found[0].TryCast<T>() : null;
        }

        // ---- saving -----------------------------------------------------------------------------

        /// <summary>
        /// The game only writes a save when its own code has flagged a change. Trainer changes bypass those paths,
        /// so flag it ourselves and ask for a save; the game's normal save (debounced, with backups) does the rest.
        /// </summary>
        public static string MarkDirty()
        {
            var w = World; if (w == null) return "Not in a game.";
            try
            {
                w.progressDirty = true;
                w.RequestSave();
                return "Save requested";
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Could not request a save: " + e.Message); return "Save request failed: " + e.Message; }
        }

        static long _nextQuietSave;

        /// <summary>For things that repeat (auto-sell): flags the change but asks for a save at most every 30 seconds.</summary>
        public static void MarkDirtyQuiet()
        {
            var w = World; if (w == null) return;
            try
            {
                w.progressDirty = true;
                long now = Environment.TickCount64;
                if (now >= _nextQuietSave) { _nextQuietSave = now + 30000; w.RequestSave(); }
            }
            catch (Exception) { }
        }

        public static string SaveNow()
        {
            var w = World; if (w == null) return "Not in a game.";
            _saveWatch = StartSave(w);          // TickBackups says so if this save turns out to have failed
            return "Saving... " + (MoonWorld.SaveStatus ?? "");
        }

        static Il2CppSystem.Threading.Tasks.Task _saveWatch;

        /// <summary>
        /// Starts a full save. The game writes it off the main thread: the task it hands back completes once the files
        /// are written and is faulted if that failed. Null when the game wouldn't start one and only took a request
        /// (which it gets round to within a minute).
        /// </summary>
        static Il2CppSystem.Threading.Tasks.Task StartSave(MoonWorld w)
        {
            w.progressDirty = true;
            try { return w.SaveProgress(true); }
            catch (Exception e) { Plugin.Logger.LogWarning("SaveProgress: " + e.Message); w.RequestSave(); return null; }
        }

        /// <summary>Why a finished game task failed, or null if it ran to its end.</summary>
        static string Failure(Il2CppSystem.Threading.Tasks.Task task)
        {
            if (task.IsCanceled) return "it was cancelled";
            if (!task.IsFaulted) return null;
            try
            {
                var error = task.Exception;
                if (error != null) return error.GetBaseException().Message;
            }
            catch (Exception) { }
            return "no reason given";
        }

        static void WatchSave()
        {
            if (!_saveWatch.IsCompleted) return;
            string why = Failure(_saveWatch);
            _saveWatch = null;
            if (why != null) Trainer.Notice("Save failed: " + why);
        }

        // ---- money ---------------------------------------------------------------------------

        static bool _decimalDirect = true;

        public static double ToDouble(Il2CppSystem.Decimal d)
        {
            // Direct conversion. The old route (number -> text -> number) misread balances on PCs whose Windows
            // region writes decimals with a comma.
            if (_decimalDirect)
            {
                try { return Il2CppSystem.Decimal.ToDouble(d); }
                catch (Exception e) { _decimalDirect = false; Plugin.Logger.LogWarning("Decimal.ToDouble unavailable, falling back to text: " + e.Message); }
            }
            return ParseNumber(d.ToString());
        }

        /// <summary>Parses "1234567.89", "1234567,89", "1,234,567.89" or "1.234.567,89".</summary>
        public static double ParseNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            text = text.Replace(" ", "").Replace("\u00A0", "").Replace("'", "");
            int dot = text.LastIndexOf('.'), comma = text.LastIndexOf(',');
            if (dot >= 0 && comma >= 0)
            {
                // Both present: whichever comes last is the decimal point.
                if (comma > dot) text = text.Replace(".", "").Replace(',', '.');
                else text = text.Replace(",", "");
            }
            else if (comma >= 0)
            {
                // Only commas: one comma followed by 1-2 digits (or more than 3) is a decimal comma; otherwise thousands.
                int after = text.Length - comma - 1;
                bool single = text.IndexOf(',') == comma;
                text = single && after != 3 ? text.Replace(',', '.') : text.Replace(",", "");
            }
            double v;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public static Il2CppSystem.Decimal Dec(double d) { return new Il2CppSystem.Decimal(d); }

        public const double MaxMoney = 1e15;

        public static double Money(string playerId)
        {
            var w = World; if (w == null) return 0;
            return ToDouble(w.WalletDollars(playerId));
        }

        /// <summary>
        /// Adds (or removes, when negative) dollars. The game keeps one wallet per team, so this reaches everyone in the
        /// crew; playerId only matters for the game's own dev path, which is tried if the team setter does nothing.
        /// </summary>
        public static double AddMoney(string playerId, double dollars)
        {
            return SetMoney(playerId, Money(playerId) + dollars);
        }

        public static double SetMoney(string playerId, double dollars)
        {
            var w = World; if (w == null) throw new InvalidOperationException("Not in a game.");
            if (double.IsNaN(dollars) || double.IsInfinity(dollars)) throw new ArgumentException("That isn't an amount.");
            dollars = Math.Max(0, Math.Min(MaxMoney, dollars));     // never below zero, never past what the game can store
            double before = Money(playerId);
            w.SetTeamDollars(Dec(dollars));
            double after = Money(playerId);
            if (Math.Abs(after - dollars) > 1 && Math.Abs(after - before) < 1)
            {
                // Team setter had no effect; use the developer path with the dev switch forced on.
                Cheats.DevScope = true;
                try { w.DevAddMoney(playerId, Dec(dollars - before)); }
                finally { Cheats.DevScope = Cheats.KeepDev; }
                after = Money(playerId);
            }
            MarkDirty();
            return after;
        }

        // ---- crew --------------------------------------------------------------------------------

        public struct Member { public string Id; public string Name; public bool Local; public ulong SteamId; }

        /// <summary>Everyone in the lobby (just you when playing solo).</summary>
        public static List<Member> Members()
        {
            var list = new List<Member>();
            try
            {
                var members = MoonSteamLobby.Members;
                if (members != null)
                {
                    // Members is an IReadOnlyList; the interop layer doesn't surface Count on it, so enumerate instead.
                    var asList = members.TryCast<Il2CppSystem.Collections.Generic.List<MoonSteamLobby.Member>>();
                    if (asList != null)
                    {
                        for (int i = 0; i < asList.Count; i++)
                        {
                            var m = asList[i];
                            if (m == null) continue;
                            list.Add(new Member { Id = m.local ? PlayerId : PlayerKey(m.id), Name = m.name, Local = m.local, SteamId = m.id });
                        }
                    }
                    else
                    {
                        var e = members.Cast<Il2CppSystem.Collections.Generic.IEnumerable<MoonSteamLobby.Member>>().GetEnumerator();
                        var plain = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                        while (plain.MoveNext())
                        {
                            var m = e.Current;
                            if (m == null) continue;
                            list.Add(new Member { Id = m.local ? PlayerId : PlayerKey(m.id), Name = m.name, Local = m.local, SteamId = m.id });
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Could not read lobby members: " + e.Message); }
            if (list.Count == 0) list.Add(new Member { Id = PlayerId, Name = "You", Local = true });
            return list;
        }

        /// <summary>
        /// Finds the lobby member using a display name and returns how many members use it. Chat only tells us the
        /// name, so anything other than exactly one match can't be trusted to be a particular person.
        /// </summary>
        public static int FindMember(string name, out Member member)
        {
            member = default(Member);
            int n = 0;
            foreach (var m in Members())
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) { if (n == 0) member = m; n++; }
            return n;
        }

        /// <summary>True if this display name is ours (or we're alone, so it can only be us).</summary>
        public static bool IsLocalName(string name)
        {
            var members = Members();
            if (members.Count <= 1) return true;
            foreach (var m in members)
                if (m.Local && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>True if the lobby's host is running the trainer (1.1 or newer), so it will carry out give-type chat commands.</summary>
        public static bool HostHasTrainer()
        {
            try
            {
                if (!MoonSteamLobby.InLobby) return false;
                ulong host = MoonSteamLobby.HostId;
                if (host == 0) return false;
                var v = Steamworks.SteamMatchmaking.GetLobbyMemberData(new Steamworks.CSteamID(MoonSteamLobby.LobbyId), new Steamworks.CSteamID(host), VersionKey);
                return !string.IsNullOrEmpty(v);
            }
            catch (Exception) { return false; }
        }

        // ---- upgrades ------------------------------------------------------------------------------

        public static readonly string[] AllUpgrades =
        {
            "gear.jetpack", "gear.boots", "gear.nightvision", "gear.belt", "suit.jump", "suit.stride", "suit.lamp", "suit.exo",
            "clean.width", "clean.depth", "clean.reach", "clean.surge", "clean.autopack", "clean.compactor",
            "eco.bounty", "eco.discount", "eco.exchange", "eco.payout",
            "logi.express", "logi.tether", "logi.throw", "logi.transfer",
        };

        /// <summary>Sets an upgrade-tree node to its highest level for a player. Returns what happened.</summary>
        public static string GiveUpgrade(string playerId, string nodeId)
        {
            var w = World; if (w == null) return "Not in a game.";
            var node = MoonUpgradeTree.Find(nodeId);
            if (node == null) return "Unknown upgrade '" + nodeId + "'.";
            int max = Math.Max(1, node.maxLevel);
            var state = w.PlayerUpgradeState(playerId);
            if (state == null) return "No upgrade record for that player.";
            var tree = state.tree;
            if (tree == null) return "Player has no upgrade tree.";
            int current = tree.ContainsKey(nodeId) ? tree[nodeId] : 0;
            if (current >= max) return node.title + " already maxed.";
            tree[nodeId] = max;
            w.SetPlayerUpgradeState(playerId, state);
            try { w.CommitUpgrade(playerId); } catch (Exception e) { Plugin.Logger.LogWarning("CommitUpgrade: " + e.Message); }
            MarkDirty();
            return node.title + " -> level " + max;
        }

        public static string GiveAllUpgrades(string playerId)
        {
            int n = 0;
            foreach (var id in AllUpgrades)
            {
                var r = GiveUpgrade(playerId, id);
                if (r.Contains("->")) n++;
            }
            return n + " upgrades maxed.";
        }

        // ---- who has the trainer (Steam lobby member data, invisible to players) ----------------------

        const string VersionKey = "moontrainer";
        static ulong _announcedLobby;

        /// <summary>Publishes our trainer version on the lobby so the host's crew list can show it. Cheap; call often.</summary>
        public static void AnnounceVersion()
        {
            try
            {
                if (!MoonSteamLobby.InLobby) { _announcedLobby = 0; return; }
                ulong lobby = MoonSteamLobby.LobbyId;
                if (lobby == 0 || lobby == _announcedLobby) return;
                Steamworks.SteamMatchmaking.SetLobbyMemberData(new Steamworks.CSteamID(lobby), VersionKey, Plugin.Version);
                _announcedLobby = lobby;
                try
                {
                    var sb = new System.Text.StringBuilder("Lobby joined as " + (MoonSteamLobby.IsHost ? "host" : "crewmate") + ". Player keys: local='" + Mask(PlayerId) + "'");
                    foreach (var m in Members()) sb.Append(", " + m.Name + (m.Local ? " (you)" : "") + "='" + Mask(m.Id) + "'");
                    Plugin.Logger.LogInfo(sb.ToString());
                }
                catch (Exception) { }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Could not publish trainer version: " + e.Message); _announcedLobby = MoonSteamLobby.LobbyId; }
        }

        /// <summary>Hides the middle of a long id in the log ("7656...4821").</summary>
        static string Mask(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 10) return id;
            return id.Substring(0, 4) + "..." + id.Substring(id.Length - 4) + " (" + id.Length + " chars)";
        }

        /// <summary>The trainer version a lobby member is running, or null if they don't have it (or have 1.0, which didn't publish it).</summary>
        public static string TrainerVersionOf(Member m)
        {
            if (m.Local) return Plugin.Version;
            try
            {
                if (!MoonSteamLobby.InLobby || m.SteamId == 0) return null;
                var v = Steamworks.SteamMatchmaking.GetLobbyMemberData(new Steamworks.CSteamID(MoonSteamLobby.LobbyId), new Steamworks.CSteamID(m.SteamId), VersionKey);
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch (Exception) { return null; }
        }

        // ---- backups ------------------------------------------------------------------------------

        // How the game behaves here (read out of the game itself, because guessing got it wrong):
        //  - A full backup copies the save's FILES, so it is only as fresh as the last save that finished writing.
        //  - Saving runs off the main thread. The task SaveProgress hands back completes when the files are written and
        //    is faulted if that failed; the game's own "save, then reload" waits on that same task.
        //  - A world that is being unloaded saves itself one last time. Straight after a backup has been put back, that
        //    last save would write the current game over it again. The game's own reload-from-disk (its dust reset)
        //    switches saving off first, with MoonWorld.dustReset, so restoring has to do the same.
        // Both jobs therefore run as steps from TickBackups - one look per frame, always on the main thread:
        //    save -> wait for it -> full backup -> wait for it -> (restore only) saving off -> put back -> reload -> gone.

        const int OpNone = 0, OpBackup = 1, OpRestore = 2;
        const int StepSave = 1, StepBackup = 2, StepPutBack = 3, StepReload = 4;
        const float SaveTimeout = 30f, BackupTimeout = 60f, PutBackTimeout = 10f, ReloadGrace = 3f;

        static int _op, _step;
        static MoonWorld _opWorld;                              // the world the job belongs to; if it goes, so does the job
        static MoonProgressStore _opStore;
        static Il2CppSystem.Threading.Tasks.Task _opTask;       // what the current step is waiting for
        static float _opSince;
        static string _opManifest, _opNewest;
        static bool _opUnsaved, _opAnyway, _opSavesOff, _opNoBackup;
        static string _anywayManifest; static float _anywayUntil;

        /// <summary>Goes up whenever the list of backups may have changed, so the Backups page knows to rebuild.</summary>
        public static int BackupRevision;

        /// <summary>What a backup or restore in progress is doing right now, or null when there isn't one.</summary>
        public static string BackupBusy
        {
            get
            {
                if (_op == OpNone) return null;
                return _step == StepSave ? "Saving the game..." : _step == StepBackup ? "Backing up..." : "Restoring...";
            }
        }

        /// <summary>Saves, and once that save is on disk has the game take one of its own full backups. How it went arrives as a notice.</summary>
        public static string BackupNow()
        {
            var w = World; if (w == null) return "Not in a game.";
            if (w.progressStore == null) return "This save has no backup store.";
            if (_op != OpNone) return "Still busy. " + BackupBusy;
            Begin(OpBackup, w, null, true);
            return "Saving, then backing up...";
        }

        static void Begin(int op, MoonWorld w, string manifest, bool save, bool noBackup = false)
        {
            _opNoBackup = noBackup;
            var task = save ? StartSave(w) : null;      // if this throws, no job has been set up yet
            _op = op; _step = StepSave; _opSince = Time.unscaledTime;
            _opWorld = w; _opStore = w.progressStore; _opManifest = manifest; _opTask = task;
            _opUnsaved = task == null; _opAnyway = !save; _opSavesOff = false; _opNewest = null;
        }

        /// <summary>Ends the job in progress, says how it went, and switches saving back on if the job had switched it off.</summary>
        static void Finish(string notice)
        {
            if (_opSavesOff)
            {
                _opSavesOff = false;
                try { if (Alive(_opWorld)) HoldSaves(_opWorld, false); }
                catch (Exception e) { Plugin.Logger.LogWarning("Could not switch saving back on: " + e.Message); }
            }
            _op = OpNone; _step = 0;
            _opWorld = null; _opStore = null; _opTask = null; _opManifest = null; _opNewest = null;
            BackupRevision++;
            if (notice != null) Trainer.Notice(notice);
        }

        /// <summary>Ends the job because a step didn't work, before anything on disk was replaced.</summary>
        static void GiveUp(string why, bool canGoAhead = true)
        {
            if (_op != OpRestore) { Finish("Backup not taken: " + why + "."); return; }
            // After a timeout the game is still busy with the same thing, so trying again at once could not go ahead either.
            if (!canGoAhead) { Finish("Not restored: " + why + ". Try again in a moment."); return; }
            // Asking for the same restore again soon means "go ahead without that step".
            _anywayManifest = _opManifest; _anywayUntil = Time.unscaledTime + 60f;
            Finish("Not restored - click Restore again to go ahead unsaved: " + why + ".");
        }

        public static void TickBackups()
        {
            // Everything that talks to the game is in the methods called from here, inside try/catch: if a game update
            // takes one of those calls away, the job ends with a message instead of failing every frame.
            if (_saveWatch != null)
            {
                try { WatchSave(); } catch (Exception) { _saveWatch = null; }
            }
            if (_op == OpNone) return;
            try { StepJob(); }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning(e);
                Finish((_op == OpRestore ? "Restore" : "Backup") + " stopped: " + e.Message);
            }
        }

        static void StepJob()
        {
            float now = Time.unscaledTime;
            if (!Alive(_opWorld) || !Same(_opWorld.progressStore, _opStore))
            {
                // The world the job belonged to is gone. After a restore's reload that is the job done; any earlier it
                // means the player went back to the main menu.
                if (_step == StepReload) { _opSavesOff = false; Finish(null); }
                else Finish((_op == OpRestore ? "Restore" : "Backup") + " cancelled: you left the game.");
                return;
            }
            if (_step == StepReload)
            {
                // Still in the old world. If the game has not been loading for a few seconds in a row it gave up on the
                // reload, and left like this the world would never save again. One frame's reading is not enough to
                // go on: saving the old game now, in the middle of a real reload, would undo the restore.
                if (Loading()) _opSince = now;
                else if (now - _opSince > ReloadGrace) FailRestore("the game gave up reloading");
                return;
            }
            if (_op == OpRestore && IsCrewmate) { Finish("Restore cancelled: you're no longer the host."); return; }

            if (_step == StepSave) { WaitForSave(now); return; }
            if (_step == StepBackup && !WaitForBackup(now)) return;

            // Putting back. Wait out anything the game is still writing first; saving is only switched off in the very
            // frame the backup goes in (PutBackAndReload), so leaving the game while we wait still saves as usual.
            if (StillWriting(_opStore))
            {
                if (now - _opSince > PutBackTimeout) Finish("Not restored: the game is still writing its save. Try again in a moment.");
                return;
            }
            PutBackAndReload();
        }

        /// <summary>Waits for the save to be on disk, then starts the full backup.</summary>
        static void WaitForSave(float now)
        {
            if (_opTask == null)
            {
                // The game wouldn't start a save for us. A backup of its last autosave is still worth having. Putting an
                // older save back over a game that hasn't been saved is not, unless the player asked for exactly that.
                if (_op == OpRestore && !_opAnyway) { GiveUp("the game wouldn't save first"); return; }
            }
            else
            {
                if (!_opTask.IsCompleted)
                {
                    if (now - _opSince > SaveTimeout) GiveUp("the game is still saving after " + (int)SaveTimeout + " seconds", false);
                    return;
                }
                string failed = Failure(_opTask);
                if (failed != null) { GiveUp("the game couldn't save first (" + failed + ")"); return; }
            }

            // Restoring the oldest backup when the list is full: a new backup would push that very one out (see RestoreBackup).
            if (_op == OpRestore && _opNoBackup) { _step = StepPutBack; _opSince = now; _opTask = null; return; }

            _step = StepBackup; _opSince = now;
            _opNewest = Newest(_opStore);       // to tell afterwards whether a new backup really appeared
            _opTask = null;
            // The game's own way in: queued behind any save still being written, and copied off the main thread.
            try { _opTask = QueueBackup(_opStore); }
            catch (Exception e)
            {
                // A game version without that call: copy the files right here, the way 1.5.0 did.
                Plugin.Logger.LogWarning("FullBackup: " + e.Message);
                _opStore.TakeFullBackup();
            }
        }

        /// <summary>Waits for the full backup and reports it. True when a restore may go on to put its backup back.</summary>
        static bool WaitForBackup(float now)
        {
            if (_opTask != null && !_opTask.IsCompleted)
            {
                if (now - _opSince <= BackupTimeout) return false;
                if (_op == OpBackup) Finish("The backup is taking more than a minute. Look at the list again in a while.");
                else GiveUp("backing up your current game is taking more than a minute", false);
                return false;
            }

            string failed = _opTask == null ? null : Failure(_opTask);
            string newest = Newest(_opStore);
            bool added = newest != null && newest != _opNewest;
            if (failed == null && !added)
            {
                // Nothing new is fine when the save hasn't changed since the last backup: the game skips those. It isn't
                // when the game's note on its last full backup says it failed (the same test the game's own log uses).
                string note = null;
                try { note = BackupNote(_opStore); } catch (Exception) { }
                if (note != null && note.StartsWith("full backup failed", StringComparison.Ordinal))
                {
                    int colon = note.IndexOf(':');
                    failed = colon >= 0 && colon + 1 < note.Length ? note.Substring(colon + 1).Trim() : note;
                }
                else if (newest == null) failed = "this save doesn't keep backups";
            }

            // A backup can also appear with files missing; the game says so in the same note.
            bool partial = false;
            if (failed == null && added)
            {
                try
                {
                    string said = BackupNote(_opStore);
                    if (said != null && said.Contains("left out")) { partial = true; Plugin.Logger.LogWarning("Full backup: " + said); }
                }
                catch (Exception) { }
            }

            if (_op == OpBackup)
            {
                if (failed != null) Finish("Backup failed: " + failed);
                else if (!added) Finish("No new backup needed - nothing has changed since the last one.");
                else if (partial) Finish("Backup taken, but some files were left out - details are in the console.");
                else Finish(_opUnsaved ? "Backup taken, but of the last autosave - the game wouldn't save on request." : "Backup taken.");
                return false;
            }
            if (failed == null && partial) failed = "some files were left out";
            if (failed != null)
            {
                if (!_opAnyway) { GiveUp("backing up your current game failed (" + failed + ")"); return false; }
                Plugin.Logger.LogWarning("Restoring without a new backup: " + failed);
            }
            // Taking a backup tidies old ones away. Make sure the one being restored is still there.
            if (!System.IO.File.Exists(_opManifest)) { Finish("Not restored: that backup no longer exists."); return false; }

            _step = StepPutBack; _opSince = now;
            return true;
        }

        // The calls into the game that 1.5.0 didn't already make, each in a method of its own: a call the game no longer
        // has stops the method around it from loading at all, which a try/catch inside that same method never sees.
        // Kept apart like this, only the step that needs the call fails, and its caller's catch deals with it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Il2CppSystem.Threading.Tasks.Task QueueBackup(MoonProgressStore store) { return store.FullBackup(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string BackupNote(MoonProgressStore store) { return store.FullBackupNote; }

        /// <summary>True while the save's files are being written or read (the game does those one after another).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool StillWriting(MoonProgressStore store) { var queued = store.Pending; return queued != null && !queued.IsCompleted; }

        /// <summary>The game's switch for "the save on disk is about to be loaded again, don't write over it".</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void HoldSaves(MoonWorld w, bool hold) { w.dustReset = hold; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool Loading() { return MoonSession.Loading; }

        /// <summary>How many full backups the game keeps before it drops the oldest.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int BackupsKept() { return MoonProgressStore.FullBackupsKept; }

        static void PutBackAndReload()
        {
            string label = BackupLabel(_opManifest);
            try
            {
                // ReloadShift does nothing while another load is under way: find that out before touching any file.
                if (Loading()) { Finish("Not restored: the game is already loading."); return; }
                if (!System.IO.File.Exists(_opManifest)) { Finish("Not restored: that backup no longer exists."); return; }
                // From here on this world must not write its save again (see the notes at the top of this section).
                HoldSaves(_opWorld, true);
                _opSavesOff = true;
                _opStore.PutBack(_opManifest);
                Plugin.Logger.LogInfo("Restored backup " + _opManifest + "; reloading shift.");
                MoonSession.ReloadShift();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e);
                FailRestore(e.Message);
                return;
            }
            // Saving stays off on purpose: this world is on its way out and must not write over the backup. The job
            // stays until the world has gone, in case the game gives up on the reload (see StepJob).
            _step = StepReload; _opSince = Time.unscaledTime;
            Trainer.Notice("Restoring " + label + "...");
        }

        /// <summary>
        /// A restore went wrong when some of the backup's files may already be in place. Saving comes back on (Finish
        /// does that) and the game as it is being played is saved over them, so the save on disk is whole again.
        /// </summary>
        static void FailRestore(string why)
        {
            var w = _opWorld;
            Finish("Restore failed: " + (why ?? "").TrimEnd('.', ' ') + ". Your game carries on.");
            try { StartSave(w); } catch (Exception) { }
        }

        static bool Same(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase a, Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b)
        {
            return a != null && b != null && !a.WasCollected && !b.WasCollected && a.Pointer == b.Pointer;
        }

        static string Newest(MoonProgressStore store)
        {
            var list = Backups(store);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>Full-backup manifest paths for this save, newest first.</summary>
        public static List<string> Backups()
        {
            var w = World;
            return Backups(w == null ? null : w.progressStore);
        }

        static List<string> Backups(MoonProgressStore store)
        {
            var list = new List<string>();
            if (store == null) return list;
            try
            {
                var raw = store.FullBackupManifests();
                if (raw == null) return list;
                var asList = raw.TryCast<Il2CppSystem.Collections.Generic.List<string>>();
                if (asList != null) { for (int i = 0; i < asList.Count; i++) list.Add(asList[i]); }
                else
                {
                    var e = raw.Cast<Il2CppSystem.Collections.Generic.IEnumerable<string>>().GetEnumerator();
                    var plain = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (plain.MoveNext()) list.Add(e.Current);
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Listing backups: " + e.Message); }
            // Each backup is a folder named after the time it was taken, with a file of the same name ("session.moon") in
            // every one - so the order is in the whole path, not in the file name.
            list.Sort((a, b) => string.CompareOrdinal(b, a));
            return list;
        }

        /// <summary>"Sat 7 Nov  4:17 PM" from a backup's folder name, as in "...\20261107-161729-187\session.moon".</summary>
        public static string BackupLabel(string manifest)
        {
            var name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(manifest) ?? "");
            if (string.IsNullOrEmpty(name)) name = System.IO.Path.GetFileNameWithoutExtension(manifest) ?? manifest;
            var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})");
            if (!m.Success) return name;
            try
            {
                var utc = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                                       int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), DateTimeKind.Utc);
                return utc.ToLocalTime().ToString("ddd d MMM  h:mm tt");
            }
            catch (Exception) { return name; }
        }

        /// <summary>
        /// Puts a backup back in place of the current save and reloads the shift - after the game as it is now has been
        /// saved and backed up. If that can't be done nothing is restored, and asking for the same backup again within a
        /// minute goes ahead without the save. How it went arrives as a notice.
        /// </summary>
        public static string RestoreBackup(string manifest)
        {
            var w = World; if (w == null || w.progressStore == null) return "Not in a game.";
            if (IsCrewmate) return "Only the host can restore the save.";
            if (_op != OpNone) return "Still busy. " + BackupBusy;
            bool anyway = manifest == _anywayManifest && Time.unscaledTime < _anywayUntil;
            _anywayManifest = null;
            // The game only keeps so many full backups and drops the oldest when a new one is taken. Backing up first
            // would therefore delete the oldest one just as it is being restored.
            bool oldest = false;
            try
            {
                var all = Backups(w.progressStore);
                oldest = all.Count > 0 && all[all.Count - 1] == manifest && all.Count >= BackupsKept();
            }
            catch (Exception e) { Plugin.Logger.LogWarning("Can't tell how many backups the game keeps: " + e.Message); }
            if (oldest && !anyway)
            {
                _anywayManifest = manifest; _anywayUntil = Time.unscaledTime + 60f;
                return "Oldest backup: click Restore again to restore it without backing up first.";
            }
            Begin(OpRestore, w, manifest, !anyway, oldest);
            if (anyway && oldest) return "Restoring " + BackupLabel(manifest) + " without a backup of your current game...";
            if (anyway) return "Restoring " + BackupLabel(manifest) + " without saving first...";
            return "Saving and backing up first, then restoring " + BackupLabel(manifest) + "...";
        }

        // ---- crewmates in the world -------------------------------------------------------------------

        public static List<MoonRemotePlayer> RemotePlayers()
        {
            var list = new List<MoonRemotePlayer>();
            var found = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<MoonRemotePlayer>(), FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                var rp = found[i].TryCast<MoonRemotePlayer>();
                if (rp != null && !string.IsNullOrEmpty(rp.DisplayName)) list.Add(rp);
            }
            return list;
        }

        public static MoonRemotePlayer RemotePlayerByName(string name)
        {
            foreach (var rp in RemotePlayers())
                if (string.Equals(rp.DisplayName, name, StringComparison.OrdinalIgnoreCase)) return rp;
            return null;
        }

        /// <summary>Where a player is: the local walker for us, their avatar for a crewmate.</summary>
        public static bool PositionOf(string name, bool self, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (self) { var w = Walker; if (w == null) return false; pos = w.transform.position; return true; }
            var rp = RemotePlayerByName(name);
            if (rp == null) return false;
            pos = rp.transform.position;
            return true;
        }

        // ---- landmarks ------------------------------------------------------------------------------

        /// <summary>Index of the landmark of a kind closest to a point (optionally only unraised ones), or -1.</summary>
        public static int NearestLandmark(Vector3 from, MoonLandmarkKind kind, bool unraisedOnly)
        {
            var w = World; if (w == null) return -1;
            var up = from.normalized;
            int best = -1; float bestDot = -2f;
            int count = w.LandmarkCount;
            for (int i = 0; i < count; i++)
            {
                MoonLandmark lm = null;
                try { lm = w.Landmark(i); } catch (Exception) { }
                if (lm == null || lm.kind != kind) continue;
                if (unraisedOnly && w.IsLandmarkRaised(i)) continue;
                float dot = Vector3.Dot(up, w.LandmarkDirection(i).normalized);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            return best;
        }

        /// <summary>Unraised landmarks of a kind, nearest first.</summary>
        public static List<int> UnraisedLandmarks(MoonLandmarkKind kind)
        {
            var list = new List<int>();
            var w = World; var walker = Walker;
            if (w == null || walker == null) return list;
            var up = walker.transform.position.normalized;
            var dots = new Dictionary<int, float>();
            int count = w.LandmarkCount;
            for (int i = 0; i < count; i++)
            {
                MoonLandmark lm = null;
                try { lm = w.Landmark(i); } catch (Exception) { }
                if (lm == null || lm.kind != kind || w.IsLandmarkRaised(i)) continue;
                list.Add(i);
                dots[i] = Vector3.Dot(up, w.LandmarkDirection(i).normalized);
            }
            list.Sort((a, b) => dots[b].CompareTo(dots[a]));
            return list;
        }

        // ---- world --------------------------------------------------------------------------------

        /// <summary>Ground point under a world position (the Moon's centre is the origin, so "up" is the position's direction).</summary>
        public static Vector3 Ground(Vector3 point, out bool found)
        {
            var up = point.normalized;
            float best = float.MaxValue; Vector3 hitPoint = point; found = false;
            var hits = Physics.RaycastAll(point + up * 60f, -up, 200f, ~0, QueryTriggerInteraction.Ignore);
            var walker = Walker;
            Transform self = walker == null ? null : walker.transform;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null) continue;
                if (self != null && h.collider.transform.IsChildOf(self)) continue;      // our own body isn't ground
                if (h.distance < best) { best = h.distance; hitPoint = h.point; found = true; }
            }
            return hitPoint;
        }
    }
}
