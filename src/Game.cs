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

        static bool Alive(UnityEngine.Object o) { return o != null && !o.WasCollected && o; }

        public static MoonPrototypeEquipment Equipment
        {
            get
            {
                if (Alive(_equipment)) return _equipment;
                _equipment = null;
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

        public static string SaveNow()
        {
            var w = World; if (w == null) return "Not in a game.";
            w.progressDirty = true;
            try { w.SaveProgress(true); } catch (Exception e) { Plugin.Logger.LogWarning("SaveProgress: " + e.Message); w.RequestSave(); }
            return "Saving... " + (MoonWorld.SaveStatus ?? "");
        }

        // ---- money ---------------------------------------------------------------------------

        public static double ToDouble(Il2CppSystem.Decimal d)
        {
            return double.Parse(d.ToString(), CultureInfo.InvariantCulture);
        }

        public static Il2CppSystem.Decimal Dec(double d) { return new Il2CppSystem.Decimal(d); }

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
            double before = Money(playerId);
            w.SetTeamDollars(Dec(dollars));
            double after = Money(playerId);
            if (Math.Abs(after - dollars) > 1 && Math.Abs(after - before) < 1)
            {
                // Team setter had no effect; use the developer path with the dev switch forced on.
                Cheats.DevScope = true;
                try { w.DevAddMoney(playerId, Dec(dollars - before)); }
                finally { Cheats.DevScope = false; }
                after = Money(playerId);
            }
            MarkDirty();
            return after;
        }

        // ---- crew --------------------------------------------------------------------------------

        public struct Member { public string Id; public string Name; public bool Local; }

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
                            list.Add(new Member { Id = m.id.ToString(), Name = m.name, Local = m.local });
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
                            list.Add(new Member { Id = m.id.ToString(), Name = m.name, Local = m.local });
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
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null) continue;
                if (h.distance < best) { best = h.distance; hitPoint = h.point; found = true; }
            }
            return hitPoint;
        }
    }
}
