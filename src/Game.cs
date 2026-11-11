using System;
using System.Collections.Generic;
using System.Globalization;
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
            w.progressDirty = true;
            try { w.SaveProgress(true); } catch (Exception e) { Plugin.Logger.LogWarning("SaveProgress: " + e.Message); w.RequestSave(); }
            return "Saving... " + (MoonWorld.SaveStatus ?? "");
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

        public static string PlayerIdByName(string name)
        {
            foreach (var m in Members())
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m.Id;
            return null;
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

        static float _backupAt;

        /// <summary>Saves now, then takes one of the game's own full backups a few seconds later once the save is written.</summary>
        public static string BackupNow()
        {
            var w = World; if (w == null) return "Not in a game.";
            if (w.progressStore == null) return "This save has no backup store.";
            SaveNow();
            _backupAt = Time.unscaledTime + 4f;
            return "Saving, then backing up...";
        }

        public static void TickBackups()
        {
            if (_backupAt <= 0 || Time.unscaledTime < _backupAt) return;
            _backupAt = 0;
            var w = World; if (w == null || w.progressStore == null) return;
            try { w.progressStore.TakeFullBackup(); Trainer.Notice("Backup taken."); }
            catch (Exception e) { Trainer.Notice("Backup failed: " + e.Message); Plugin.Logger.LogWarning(e); }
        }

        /// <summary>Full-backup manifest paths for this save, newest first.</summary>
        public static List<string> Backups()
        {
            var list = new List<string>();
            var w = World; if (w == null || w.progressStore == null) return list;
            try
            {
                var raw = w.progressStore.FullBackupManifests();
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
            list.Sort((a, b) => string.CompareOrdinal(System.IO.Path.GetFileName(b), System.IO.Path.GetFileName(a)));
            return list;
        }

        /// <summary>"2026-11-07 16:17" from a backup file name like "...20261107-161729-187...".</summary>
        public static string BackupLabel(string manifest)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(manifest) ?? manifest;
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

        /// <summary>Puts a backup back in place of the current save and reloads the shift. The current state is backed up first.</summary>
        public static string RestoreBackup(string manifest)
        {
            var w = World; if (w == null || w.progressStore == null) return "Not in a game.";
            if (IsCrewmate) return "Only the host can restore the save.";
            try { w.progressStore.TakeFullBackup(); } catch (Exception e) { Plugin.Logger.LogWarning("Pre-restore backup: " + e.Message); }
            try
            {
                w.progressStore.PutBack(manifest);
                Plugin.Logger.LogInfo("Restored backup " + manifest + "; reloading shift.");
                MoonSession.ReloadShift();
                return "Restoring " + BackupLabel(manifest) + "...";
            }
            catch (Exception e) { Plugin.Logger.LogError(e); return "Restore failed: " + e.Message; }
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
