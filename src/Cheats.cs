using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MoonTrainer
{
    /// <summary>Trainer state. Toggles are applied every frame by Tick(); the Harmony patches read the multipliers.</summary>
    public static class Cheats
    {
        // Movement
        public static bool Fly;
        public static bool Noclip;
        public static float FlySpeed = 20f;
        public static float SpeedMult = 1f;
        public static float JumpMult = 1f;
        public static bool InfiniteJetpack;
        public static bool GravityBoots;
        public static bool NightVision;
        public static bool InfiniteBag;
        public static bool AutoSell;

        // Money
        public static float PayoutMult = 1f;
        public static bool FreeUpgrades;
        public static bool FreeTravel;

        // Vehicles
        public static float VehicleSpeedMult = 1f;
        public static float BrushWidthMult = 1f;
        public static bool InfiniteHopper;

        // World
        public static float MachineSpeed = 1f;
        public static bool FreezeTime;
        public static float TimeSpeed = 1f;
        public static int CleanRings = 3;
        public static bool FlagsOnMap;

        /// <summary>While true, the game's MoonDevTools.Enabled reads as true (see Patches.DevTools). Fly keeps it on.</summary>
        public static bool DevScope;

        private static bool _flyApplied, _noclipApplied, _bootsApplied, _nvApplied;
        private static float _appliedTimeSpeed = -1f;
        private static bool _clockTouched;
        private static IntPtr _walkerPtr;
        private static float _nextAutoSell;
        private static bool _sellFailLogged;
        private static float _speedBeforeBoost = 1f;

        // After a teleport we hover in fly mode until the ground under us has loaded (even when Fly itself is off).
        private static bool _hover, _hoverSnap;
        private static float _hoverUntil;

        /// <summary>What DevScope should fall back to after a one-off developer call: on while flying or hovering.</summary>
        public static bool KeepDev { get { return Fly || _hover; } }

        public static bool IsLocal(string playerId)
        {
            return Game.IsLocalId(playerId);
        }

        /// <summary>
        /// For hooks that must never switch off for us: true unless the id is a crewmate's. An id nobody recognises
        /// counts as ours, so playing alone is exactly as before and only a known crewmate is left out.
        /// </summary>
        public static bool NotCrewmate(string playerId)
        {
            return Game.IsLocalId(playerId) || !Game.IsCrewmateId(playerId);
        }

        public static void SetFly(bool on)
        {
            Fly = on;
            Apply(Game.Walker);
        }

        public static void ToggleSpeedBoost()
        {
            if (SpeedMult == 1f) { SpeedMult = _speedBeforeBoost > 1f ? _speedBeforeBoost : 3f; }
            else { _speedBeforeBoost = SpeedMult; SpeedMult = 1f; }
        }

        public static void Tick()
        {
            var walker = Game.Walker;
            if (walker == null) { ForgetApplied(); _walkerPtr = IntPtr.Zero; return; }
            // A different walker (new session, reloaded shift): nothing has been applied to this one yet.
            if (walker.Pointer != _walkerPtr) { _walkerPtr = walker.Pointer; ForgetApplied(); }
            Apply(walker);

            if (InfiniteJetpack && walker.BoostsLeft < 50) walker.BoostsLeft = 99;

            if (GravityBoots != _bootsApplied)
            {
                _bootsApplied = GravityBoots;
                if (GravityBoots) walker.SetGravityBoots(true);
                else if (walker.GravityBootsOn && !walker.OwnsGravityBoots)
                {
                    // Switched off and the player doesn't really own them: take them off again.
                    try { walker.SetGravityBoots(false); } catch (Exception) { }
                    if (walker.GravityBootsOn) walker.GravityBootsOn = false;
                }
            }
            if (NightVision != _nvApplied)
            {
                _nvApplied = NightVision;
                if (NightVision) walker.SetNightVision(true);
                else if (walker.NightVisionOn && !walker.OwnsNightVision)
                {
                    try { walker.SetNightVision(false); } catch (Exception) { }
                    if (walker.NightVisionOn) walker.NightVisionOn = false;
                }
            }

            // The lunar clock is only touched once a non-normal speed has been asked for, and left alone again after
            // it's back to normal - the trainer no longer resets the game's clock at the start of every session.
            float wantSpeed = FreezeTime ? 0f : TimeSpeed;
            if (wantSpeed != 1f) _clockTouched = true;
            if (_clockTouched && wantSpeed != _appliedTimeSpeed)
            {
                _appliedTimeSpeed = wantSpeed;
                SetClockSpeed(wantSpeed);
                if (wantSpeed == 1f) _clockTouched = false;
            }

            if (AutoSell && Time.unscaledTime >= _nextAutoSell)
            {
                _nextAutoSell = Time.unscaledTime + 2f;
                SellCarried(false);
            }

            // Landing after a teleport: hover until the ground has streamed in, then drop.
            if (_hover)
            {
                bool found;
                var pos = walker.transform.position;
                var ground = Game.Ground(pos, out found);
                if (found)
                {
                    if (_hoverSnap)
                    {
                        // We arrived before the terrain existed, at a guessed height. Now that it's there, stand on it
                        // if we ended up under it or well above it.
                        var up = pos.normalized;
                        float height = Vector3.Dot(pos - ground, up);
                        if (height < 0.3f || height > 6f)
                        {
                            DevScope = true;
                            try { walker.Teleport(ground + up * 1.0f, false); }
                            catch (Exception e) { Plugin.Logger.LogWarning("Surface snap failed: " + e.Message); }
                        }
                    }
                    EndHover(walker);
                }
                else if (Time.unscaledTime > _hoverUntil) EndHover(walker);
            }
        }

        private static void ForgetApplied()
        {
            _flyApplied = _noclipApplied = _bootsApplied = _nvApplied = false;
            _appliedTimeSpeed = -1f;
            _hover = false;
        }

        private static void EndHover(MoonWalker walker)
        {
            _hover = false;
            Apply(walker);
            DevScope = Fly;
        }

        private static void Apply(MoonWalker walker)
        {
            if (walker == null) return;
            bool wantFly = Fly || _hover;
            bool wantNoclip = Fly && Noclip;
            if (wantFly != _flyApplied || wantNoclip != _noclipApplied)
            {
                _flyApplied = wantFly; _noclipApplied = wantNoclip;
                DevScope = true;
                try { walker.SetFlying(wantFly, wantNoclip); } finally { DevScope = wantFly; }
                MoonDevTools.Noclip = wantNoclip;
            }
            if (wantFly) DevScope = true;
            if (Fly) walker.FlySpeed = FlySpeed;
        }

        // ---- time --------------------------------------------------------------------------------

        /// <summary>Changes the lunar clock speed without the time of day jumping (clock = (Clock*speed + offset)).</summary>
        public static void SetClockSpeed(float speed)
        {
            var sky = Game.Sky;
            if (sky == null) { MoonSolarSystem.clockSpeed = speed; return; }
            double daySeconds = sky.lunarDaySeconds > 0 ? sky.lunarDaySeconds : 3600;
            double before = sky.LunarTime;
            MoonSolarSystem.clockSpeed = speed;
            double delta = before - sky.LunarTime;
            delta -= Math.Floor(delta);
            MoonSolarSystem.clockOffset += delta * daySeconds;
        }

        public static string ShiftTime(float hours)
        {
            var sky = Game.Sky;
            if (sky == null) return "No sky.";
            double daySeconds = sky.lunarDaySeconds > 0 ? sky.lunarDaySeconds : 3600;
            MoonSolarSystem.clockOffset += hours / 24.0 * daySeconds;
            return (hours > 0 ? "+" : "") + hours + " h";
        }

        public static string TimeText()
        {
            var sky = Game.Sky;
            if (sky == null) return "-";
            double h = sky.LunarTime * 24;
            return ((int)h).ToString("00") + ":" + ((int)(h % 1 * 60)).ToString("00") + (MoonSolarSystem.clockSpeed == 0 ? " (frozen)" : "");
        }

        // ---- teleport --------------------------------------------------------------------------------

        public static string TeleportToWaypoint(int index)
        {
            var world = Game.World; var walker = Game.Walker;
            if (world == null || walker == null) return "Not in a game.";
            var marks = world.waypoints;
            if (marks == null || index < 0 || index >= marks.Count) return "No such marker.";
            var mark = marks[index];
            var dir = mark.direction.normalized;
            float radius = walker.transform.position.magnitude;
            return TeleportTo(dir * radius, "marker " + (index + 1));
        }

        /// <summary>
        /// Teleports the local player. With exact, the height of the position is trusted (a saved spot, a crewmate);
        /// otherwise the height is a guess (a landmark or marker direction) and we stand on the surface once it loads.
        /// </summary>
        public static string TeleportTo(Vector3 position, string label, bool exact = false)
        {
            var walker = Game.Walker;
            if (walker == null) return "Not in a game.";
            bool found;
            var ground = Game.Ground(position, out found);
            var up = position.normalized;
            Vector3 target;
            if (exact) target = position + up * 0.25f;          // someone stood exactly here (may be indoors, under a roof)
            else if (found) target = ground + up * 1.0f;
            else target = position + up * 3.0f;
            // Hover in fly mode until the terrain under us exists, so we don't fall through unloaded ground. This keeps
            // noclip as it was, and works with Fly off too (the hover flag holds fly mode until we land).
            _hover = true;
            _hoverSnap = !found && !exact;
            _hoverUntil = Time.unscaledTime + 8f;
            Apply(walker);
            DevScope = true;
            walker.Teleport(target, true);
            return "Teleported to " + label;
        }

        public static string TeleportToDirection(Vector3 dir, string label)
        {
            var walker = Game.Walker;
            if (walker == null) return "Not in a game.";
            return TeleportTo(dir.normalized * walker.transform.position.magnitude, label);
        }

        public static string TeleportToWaypointByNumber(string arg)
        {
            int n;
            if (!int.TryParse(arg, out n)) return "Usage: tp <marker number>";
            return TeleportToWaypoint(n - 1);
        }

        public static string TeleportToLandmark(int index)
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            var lm = world.Landmark(index);
            return TeleportToDirection(world.LandmarkDirection(index), lm != null ? lm.name : "landmark");
        }

        public static string TeleportToNearestLandmark(MoonLandmarkKind kind)
        {
            var walker = Game.Walker;
            if (walker == null) return "Not in a game.";
            int i = Game.NearestLandmark(walker.transform.position, kind, true);
            if (i < 0) return "No unraised " + (kind == MoonLandmarkKind.Flag ? "flags" : "sites") + " left.";
            return TeleportToLandmark(i);
        }

        public static string TeleportToPlayer(string name)
        {
            var rp = Game.RemotePlayerByName(name);
            if (rp == null) return "Can't see " + name + " right now.";
            return TeleportTo(rp.transform.position, rp.DisplayName, true);
        }

        // ---- world ---------------------------------------------------------------------------------

        public static string CleanAround(int rings)
        {
            var world = Game.World; var walker = Game.Walker;
            if (world == null || walker == null) return "Not in a game.";
            DevScope = true;
            int n;
            try { n = world.DevCompleteRegions(walker.transform.position, rings); } finally { DevScope = KeepDev; }
            Game.MarkDirty();
            return n + " regions completed.";
        }

        /// <summary>
        /// Completes every region on the Moon. With leaveOne, the game's own dev helper keeps one section dirty so you
        /// can sweep it yourself for the ending; a marker is dropped on it. Without, that one is finished too.
        /// </summary>
        public static string FinishMoon(bool leaveOne)
        {
            var world = Game.World; var walker = Game.Walker;
            if (world == null || walker == null) return "Not in a game.";
            int n;
            Vector3 lastPoint;
            DevScope = true;
            try
            {
                n = world.DevFinishAllButOne(walker.transform.position, out lastPoint);
                if (lastPoint.sqrMagnitude > 0.001f)
                {
                    var dir = lastPoint.normalized;
                    if (leaveOne)
                    {
                        try { world.AddWaypoint(dir); } catch (Exception e) { Plugin.Logger.LogWarning("AddWaypoint: " + e.Message); }
                    }
                    else
                    {
                        n += world.DevCompleteRegions(dir * walker.transform.position.magnitude, 1);
                    }
                }
            }
            finally { DevScope = KeepDev; }
            Game.MarkDirty();
            return leaveOne
                ? n + " regions completed. One section left - a marker points at it; sweep it to finish the Moon."
                : n + " regions completed. The Moon is clean.";
        }

        public static string ResetDust()
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            bool ok;
            DevScope = true;
            try { ok = world.DevResetDust(); } finally { DevScope = KeepDev; }
            Game.MarkDirty();
            return ok ? "Dust reset - the Moon is dirty again." : "The game refused to reset the dust.";
        }

        /// <summary>Raises every unraised landmark of the given kind (null = both flags and sites) for a player.</summary>
        public static string RaiseLandmarks(MoonLandmarkKind? kind, string playerId = null)
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            int count = world.LandmarkCount, done = 0, already = 0;
            string who = playerId ?? Game.PlayerId;
            for (int i = 0; i < count; i++)
            {
                MoonLandmark lm = null;
                try { lm = world.Landmark(i); } catch (Exception) { }
                if (lm == null) continue;
                if (kind.HasValue && lm.kind != kind.Value) continue;
                if (world.IsLandmarkRaised(i)) { already++; continue; }
                try { world.MarkLandmarkRaised(i, who); done++; }
                catch (Exception e) { Plugin.Logger.LogWarning("MarkLandmarkRaised(" + i + "): " + e.Message); }
            }
            Game.MarkDirty();
            string what = kind == null ? "landmarks" : kind.Value == MoonLandmarkKind.Flag ? "flags" : "sites";
            return done + " " + what + " raised" + (already > 0 ? " (" + already + " were already done)" : "") + ".";
        }

        /// <summary>Raises the unraised landmark nearest to a point, credited to a player.</summary>
        public static string RaiseNearest(Vector3 from, MoonLandmarkKind kind, string playerId)
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            int i = Game.NearestLandmark(from, kind, true);
            if (i < 0) return "No unraised " + (kind == MoonLandmarkKind.Flag ? "flags" : "sites") + " left.";
            world.MarkLandmarkRaised(i, playerId);
            Game.MarkDirty();
            var lm = world.Landmark(i);
            return (lm != null ? lm.name : "Landmark") + " raised.";
        }

        // ---- dust, vehicles and buildings ----------------------------------------------------------

        public static string SetCarried(bool full)
        {
            var world = Game.World; var eq = Game.Equipment;
            if (world == null || eq == null) return "Not in a game.";
            var inv = eq.Inventory;
            double kg = full ? (inv != null ? inv.CapacityKg : 100) : 0;
            if (InfiniteBag && full) kg = 5000;
            DevScope = true;
            try { world.DevSetCarriedDust(Game.PlayerId, kg); } finally { DevScope = KeepDev; }
            return full ? "Bag filled (" + kg.ToString("N0") + " kg)." : "Bag emptied.";
        }

        public static string SetHopper(bool full)
        {
            var eq = Game.Equipment;
            if (eq == null) return "Not in a game.";
            var vehicle = eq.Vehicle;
            if (vehicle == null) return "Get in a vehicle first.";
            // ContainsKey + indexer: TryGetValue's out parameter on a game dictionary crashes the runtime.
            var hoppers = eq.World.vehicleStorage;
            if (vehicle.StorageId == null || !hoppers.ContainsKey(vehicle.StorageId)) return "This vehicle has no hopper.";
            hoppers[vehicle.StorageId].kilograms = full ? vehicle.DustCapacityKg : 0;
            return full ? "Hopper filled." : "Hopper emptied.";
        }

        /// <summary>Hands the dust you're carrying in for money, wherever you are.</summary>
        public static string SellCarried(bool report)
        {
            var world = Game.World; var eq = Game.Equipment;
            if (world == null || eq == null || eq.Inventory == null) return "Not in a game.";
            double carried = eq.Inventory.Kilograms;
            if (carried <= 0.01) return report ? "Your bag is empty." : null;
            double before = report ? Game.Money(Game.PlayerId) : 0;
            double sold;
            try { sold = world.DepositAtStation(Game.PlayerId, 1e9); }
            catch (Exception e)
            {
                // Auto-sell retries every 2 seconds: say it once, not forever.
                if (report || !_sellFailLogged) { _sellFailLogged = true; Plugin.Logger.LogWarning("Selling failed: " + e.Message); }
                return "Selling failed: " + e.Message;
            }
            if (!report)
            {
                if (sold > 0) Game.MarkDirtyQuiet();
                return null;
            }
            if (sold > 0) Game.MarkDirty();
            double earned = Game.Money(Game.PlayerId) - before;
            string msg = "Sold " + sold.ToString("N1") + " kg" + (earned > 0 ? " for $" + earned.ToString("N0") : "") + ".";
            Plugin.Logger.LogInfo(msg);
            return msg;
        }

        /// <summary>Point on the ground the camera is looking at, within 250 m.</summary>
        public static bool AimPoint(out Vector3 point)
        {
            point = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 250f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool found = false;
            var walker = Game.Walker;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null) continue;
                if (walker != null && h.collider.transform.IsChildOf(walker.transform)) continue;
                if (h.distance < best) { best = h.distance; point = h.point; found = true; }
            }
            return found;
        }

        static float AimYaw()
        {
            var cam = Camera.main;
            return cam == null ? 0f : cam.transform.eulerAngles.y;
        }

        public static string PlaceCatcher()
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            Vector3 p;
            if (!AimPoint(out p)) return "Look at the ground where you want it (within 250 m).";
            int id;
            DevScope = true;
            try { id = world.DevPlaceCatcher(Game.PlayerId, p, AimYaw()); } finally { DevScope = KeepDev; }
            if (id < 0) return "The game couldn't place a catcher there.";
            Game.MarkDirty();
            return "Catcher placed.";
        }

        public static string PlaceRobotDock(int kind, string label)
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            Vector3 p;
            if (!AimPoint(out p)) return "Look at the ground where you want it (within 250 m).";
            int id;
            DevScope = true;
            try { id = world.DevPlaceRobotDock(Game.PlayerId, kind, p, AimYaw()); } finally { DevScope = KeepDev; }
            if (id < 0) return "The game couldn't place a " + label + " dock there.";
            Game.MarkDirty();
            return label + " robot dock placed.";
        }

        // ---- misc ---------------------------------------------------------------------------------

        public static string UnlockAchievements()
        {
            int n = 0;
            try
            {
                var all = MoonAchievements.All;
                var e = all.GetEnumerator();
                var plain = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (plain.MoveNext())
                {
                    var id = e.Current;
                    if (string.IsNullOrEmpty(id)) continue;
                    MoonAchievements.Unlock(id);
                    n++;
                }
                MoonAchievements.Flush();
            }
            catch (Exception ex) { Plugin.Logger.LogWarning("Achievements: " + ex.Message); return "Achievements failed: " + ex.Message; }
            return n + " achievements unlocked.";
        }

        public static string RefillAll()
        {
            var walker = Game.Walker; var eq = Game.Equipment;
            if (walker == null) return "Not in a game.";
            walker.RefillBoosts();
            if (eq != null) eq._Energy_k__BackingField = 1f;
            return "Jetpack and suit energy refilled.";
        }

        // ---- presets ------------------------------------------------------------------------------

        public static string Preset(string name)
        {
            switch (name)
            {
                case "explore":
                    Off(false);
                    Fly = true; Noclip = true; FlySpeed = 60f; NightVision = true; InfiniteJetpack = true; FreezeTime = true; FlagsOnMap = true;
                    break;
                case "grind":
                    Off(false);
                    PayoutMult = 5f; BrushWidthMult = 3f; VehicleSpeedMult = 2f; SpeedMult = 2f; InfiniteBag = true; InfiniteHopper = true; MachineSpeed = 5f;
                    break;
                case "build":
                    Off(false);
                    Fly = true; FreeUpgrades = true; FreeTravel = true; MachineSpeed = 10f; InfiniteJetpack = true;
                    break;
            }
            Tick();
            return "Preset: " + name;
        }

        public static string Off(bool tick = true)
        {
            Fly = Noclip = InfiniteJetpack = GravityBoots = NightVision = FreeUpgrades = FreeTravel = FreezeTime = false;
            InfiniteBag = AutoSell = InfiniteHopper = FlagsOnMap = false;
            FlySpeed = 20f; SpeedMult = JumpMult = PayoutMult = VehicleSpeedMult = BrushWidthMult = MachineSpeed = TimeSpeed = 1f;
            if (tick) Tick();
            return "Everything off.";
        }

        /// <summary>One-line summary of what's switched on, for the HUD. Empty when nothing is.</summary>
        public static string ActiveSummary()
        {
            var parts = new List<string>();
            if (Fly) parts.Add(Noclip ? "FLY+NOCLIP" : "FLY");
            if (SpeedMult != 1f) parts.Add(SpeedMult.ToString("0.#") + "x SPEED");
            if (JumpMult != 1f) parts.Add(JumpMult.ToString("0.#") + "x JUMP");
            if (InfiniteJetpack) parts.Add("INF JET");
            if (GravityBoots) parts.Add("BOOTS");
            if (NightVision) parts.Add("NV");
            if (InfiniteBag) parts.Add("INF BAG");
            if (AutoSell) parts.Add("AUTO-SELL");
            if (PayoutMult != 1f) parts.Add(PayoutMult.ToString("0.#") + "x $");
            if (FreeUpgrades) parts.Add("FREE UPG");
            if (FreeTravel) parts.Add("FREE TRAVEL");
            if (VehicleSpeedMult != 1f) parts.Add(VehicleSpeedMult.ToString("0.#") + "x CAR");
            if (BrushWidthMult != 1f) parts.Add(BrushWidthMult.ToString("0.#") + "x BRUSH");
            if (InfiniteHopper) parts.Add("INF HOPPER");
            if (MachineSpeed != 1f) parts.Add(MachineSpeed.ToString("0.#") + "x MACHINES");
            if (FreezeTime) parts.Add("TIME FROZEN");
            else if (TimeSpeed != 1f) parts.Add(TimeSpeed.ToString("0.##") + "x TIME");
            if (FlagsOnMap) parts.Add("MAP POI");
            return string.Join("  |  ", parts);
        }

        // ---- persistence: "Name=value;Name=value" ----------------------------------------------------

        public static string Serialize()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            Action<string, object> add = (k, v) => sb.Append(k).Append('=').Append(Convert.ToString(v, c)).Append(';');
            add("Fly", Fly); add("Noclip", Noclip); add("FlySpeed", FlySpeed); add("SpeedMult", SpeedMult); add("JumpMult", JumpMult);
            add("InfiniteJetpack", InfiniteJetpack); add("GravityBoots", GravityBoots); add("NightVision", NightVision);
            add("InfiniteBag", InfiniteBag); add("AutoSell", AutoSell);
            add("PayoutMult", PayoutMult); add("FreeUpgrades", FreeUpgrades); add("FreeTravel", FreeTravel);
            add("VehicleSpeedMult", VehicleSpeedMult); add("BrushWidthMult", BrushWidthMult); add("InfiniteHopper", InfiniteHopper);
            add("MachineSpeed", MachineSpeed); add("FreezeTime", FreezeTime); add("TimeSpeed", TimeSpeed); add("CleanRings", CleanRings);
            add("FlagsOnMap", FlagsOnMap);
            return sb.ToString();
        }

        public static void SetByName(string key, string value)
        {
            var c = CultureInfo.InvariantCulture;
            bool b = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
            float f;
            bool ok = float.TryParse(value, NumberStyles.Float, c, out f) && !float.IsNaN(f) && !float.IsInfinity(f);
            switch (key)
            {
                case "Fly": Fly = b; break;
                case "Noclip": Noclip = b; break;
                case "FlySpeed": FlySpeed = ok ? Mathf.Clamp(f, 1f, 500f) : 20f; break;
                case "SpeedMult": SpeedMult = ok ? Mathf.Clamp(f, 0.25f, 20f) : 1f; break;
                case "JumpMult": JumpMult = ok ? Mathf.Clamp(f, 0.25f, 20f) : 1f; break;
                case "InfiniteJetpack": InfiniteJetpack = b; break;
                case "GravityBoots": GravityBoots = b; break;
                case "NightVision": NightVision = b; break;
                case "InfiniteBag": InfiniteBag = b; break;
                case "AutoSell": AutoSell = b; break;
                case "PayoutMult": PayoutMult = ok ? Mathf.Clamp(f, 0.25f, 100f) : 1f; break;
                case "FreeUpgrades": FreeUpgrades = b; break;
                case "FreeTravel": FreeTravel = b; break;
                case "VehicleSpeedMult": VehicleSpeedMult = ok ? Mathf.Clamp(f, 0.25f, 20f) : 1f; break;
                case "BrushWidthMult": BrushWidthMult = ok ? Mathf.Clamp(f, 0.25f, 20f) : 1f; break;
                case "InfiniteHopper": InfiniteHopper = b; break;
                case "MachineSpeed": MachineSpeed = ok ? Mathf.Clamp(f, 0.25f, 50f) : 1f; break;
                case "FreezeTime": FreezeTime = b; break;
                case "TimeSpeed": TimeSpeed = ok ? Mathf.Clamp(f, 0.25f, 50f) : 1f; break;
                case "CleanRings": CleanRings = ok ? Mathf.Clamp((int)f, 1, 60) : 3; break;
                case "FlagsOnMap": FlagsOnMap = b; break;
            }
        }
    }
}
