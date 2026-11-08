using System;
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

        // Money
        public static float PayoutMult = 1f;
        public static bool FreeUpgrades;
        public static bool FreeTravel;

        // Vehicles
        public static float VehicleSpeedMult = 1f;
        public static float BrushWidthMult = 1f;

        // World
        public static float MachineSpeed = 1f;
        public static bool FreezeTime;
        public static int CleanRings = 3;

        /// <summary>While true, the game's MoonDevTools.Enabled reads as true (see Patches.DevTools). Fly keeps it on.</summary>
        public static bool DevScope;

        private static bool _flyApplied, _noclipApplied, _bootsApplied, _nvApplied;
        private static float _savedClockSpeed = 1f;
        private static bool _timeFrozen;
        private static float _landUntil;

        public static bool IsLocal(string playerId)
        {
            return playerId == null || playerId == Game.PlayerId || playerId == MoonWorld.LocalPlayer;
        }

        public static void SetFly(bool on)
        {
            Fly = on;
            Apply(Game.Walker);
        }

        public static void Tick()
        {
            var walker = Game.Walker;
            if (walker == null) { _flyApplied = _noclipApplied = false; return; }
            Apply(walker);

            if (InfiniteJetpack && walker.BoostsLeft < 50) walker.BoostsLeft = 99;

            if (GravityBoots != _bootsApplied)
            {
                _bootsApplied = GravityBoots;
                if (GravityBoots) walker.SetGravityBoots(true);
            }
            if (NightVision != _nvApplied)
            {
                _nvApplied = NightVision;
                if (NightVision) walker.SetNightVision(true);
            }

            if (FreezeTime != _timeFrozen)
            {
                _timeFrozen = FreezeTime;
                if (FreezeTime) { _savedClockSpeed = MoonSolarSystem.clockSpeed; SetClockSpeed(0f); }
                else SetClockSpeed(_savedClockSpeed <= 0 ? 1f : _savedClockSpeed);
            }

            // Landing after a teleport: hover until the ground has streamed in, then drop.
            if (_landUntil > 0)
            {
                bool found;
                Game.Ground(walker.transform.position, out found);
                if (found || Time.unscaledTime > _landUntil)
                {
                    _landUntil = 0;
                    if (!Fly) { walker.SetFlying(false, false); _flyApplied = false; DevScope = false; }
                }
            }
        }

        private static void Apply(MoonWalker walker)
        {
            if (walker == null) return;
            if (Fly != _flyApplied || Noclip != _noclipApplied)
            {
                _flyApplied = Fly; _noclipApplied = Noclip;
                DevScope = true;
                try { walker.SetFlying(Fly, Fly && Noclip); } finally { DevScope = Fly; }
                MoonDevTools.Noclip = Fly && Noclip;
            }
            if (Fly) { DevScope = true; walker.FlySpeed = FlySpeed; }
        }

        /// <summary>Changes the lunar clock speed without the time of day jumping (clock = (Clock*speed + offset)).</summary>
        public static void SetClockSpeed(float speed)
        {
            var sky = Game.Find<MoonSolarSystem>();
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
            var sky = Game.Find<MoonSolarSystem>();
            if (sky == null) return "No sky.";
            double daySeconds = sky.lunarDaySeconds > 0 ? sky.lunarDaySeconds : 3600;
            MoonSolarSystem.clockOffset += hours / 24.0 * daySeconds;
            return (hours > 0 ? "+" : "") + hours + " h";
        }

        public static string TimeText()
        {
            var sky = Game.Find<MoonSolarSystem>();
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

        public static string TeleportTo(Vector3 position, string label)
        {
            var walker = Game.Walker;
            if (walker == null) return "Not in a game.";
            bool found;
            var ground = Game.Ground(position, out found);
            var up = position.normalized;
            var target = (found ? ground : position) + up * (found ? 1.0f : 3.0f);
            // Hover in fly mode until the terrain under us exists, so we don't fall through unloaded ground.
            DevScope = true;
            walker.SetFlying(true, false);
            _flyApplied = true;
            walker.Teleport(target, true);
            _landUntil = Time.unscaledTime + 8f;
            return "Teleported to " + label;
        }

        public static string TeleportToWaypointByNumber(string arg)
        {
            int n;
            if (!int.TryParse(arg, out n)) return "Usage: tp <marker number>";
            return TeleportToWaypoint(n - 1);
        }

        // ---- world ---------------------------------------------------------------------------------

        public static string CleanAround(int rings)
        {
            var world = Game.World; var walker = Game.Walker;
            if (world == null || walker == null) return "Not in a game.";
            DevScope = true;
            int n;
            try { n = world.DevCompleteRegions(walker.transform.position, rings); } finally { DevScope = Fly; }
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
            finally { DevScope = Fly; }
            Game.MarkDirty();
            return leaveOne
                ? n + " regions completed. One section left - a marker points at it; sweep it to finish the Moon."
                : n + " regions completed. The Moon is clean.";
        }

        /// <summary>Raises every unraised landmark of the given kind (null = both flags and sites) for the local player.</summary>
        public static string RaiseLandmarks(MoonLandmarkKind? kind)
        {
            var world = Game.World;
            if (world == null) return "Not in a game.";
            int count = world.LandmarkCount, done = 0, already = 0;
            string me = Game.PlayerId;
            for (int i = 0; i < count; i++)
            {
                MoonLandmark lm = null;
                try { lm = world.Landmark(i); } catch (Exception) { }
                if (lm == null) continue;
                if (kind.HasValue && lm.kind != kind.Value) continue;
                if (world.IsLandmarkRaised(i)) { already++; continue; }
                try { world.MarkLandmarkRaised(i, me); done++; }
                catch (Exception e) { Plugin.Logger.LogWarning("MarkLandmarkRaised(" + i + "): " + e.Message); }
            }
            Game.MarkDirty();
            string what = kind == null ? "landmarks" : kind.Value == MoonLandmarkKind.Flag ? "flags" : "sites";
            return done + " " + what + " raised" + (already > 0 ? " (" + already + " were already done)" : "") + ".";
        }

        public static string RefillAll()
        {
            var walker = Game.Walker; var eq = Game.Equipment;
            if (walker == null) return "Not in a game.";
            walker.RefillBoosts();
            if (eq != null) eq._Energy_k__BackingField = 1f;
            return "Jetpack and suit energy refilled.";
        }

        public static string Reset()
        {
            Fly = Noclip = InfiniteJetpack = GravityBoots = NightVision = FreeUpgrades = FreeTravel = FreezeTime = false;
            FlySpeed = 20f; SpeedMult = JumpMult = PayoutMult = VehicleSpeedMult = BrushWidthMult = MachineSpeed = 1f;
            Tick();
            return "Everything off.";
        }
    }
}
