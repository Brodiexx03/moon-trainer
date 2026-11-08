using System;
using System.Collections.Generic;
using System.Globalization;
using MoonCleaners;
using UnityEngine;

namespace MoonTrainer
{
    /// <summary>
    /// Named spots you can teleport back to. Stored in the config per save slot, as "name|x,y,z" entries.
    /// Only the direction from the Moon's centre matters; teleporting re-finds the ground there.
    /// </summary>
    public static class Locations
    {
        public struct Spot { public string Name; public Vector3 Position; }

        static string Slot
        {
            get
            {
                try { var k = MoonSaveSlots.ActiveKey; return string.IsNullOrEmpty(k) ? "default" : k; }
                catch (Exception) { return "default"; }
            }
        }

        public static List<Spot> All()
        {
            var list = new List<Spot>();
            string raw = Settings.LocationsFor(Slot);
            foreach (var entry in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var bar = entry.LastIndexOf('|');
                if (bar <= 0) continue;
                var xyz = entry.Substring(bar + 1).Split(',');
                float x, y, z;
                if (xyz.Length != 3
                    || !float.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    || !float.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                    || !float.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;
                list.Add(new Spot { Name = entry.Substring(0, bar), Position = new Vector3(x, y, z) });
            }
            return list;
        }

        static void Write(List<Spot> spots)
        {
            var parts = new List<string>();
            var c = CultureInfo.InvariantCulture;
            foreach (var s in spots)
                parts.Add(s.Name.Replace(";", "").Replace("|", "") + "|" + s.Position.x.ToString("R", c) + "," + s.Position.y.ToString("R", c) + "," + s.Position.z.ToString("R", c));
            Settings.SetLocationsFor(Slot, string.Join(";", parts));
        }

        /// <summary>Saves where you're standing. With no name, it's named after the nearest flag or site.</summary>
        public static string SaveHere(string name)
        {
            var walker = Game.Walker;
            if (walker == null) return "Not in a game.";
            var pos = walker.transform.position;
            if (string.IsNullOrWhiteSpace(name)) name = AutoName(pos);
            name = name.Trim();
            var spots = All();
            spots.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            spots.Add(new Spot { Name = name, Position = pos });
            Write(spots);
            return "Saved location \"" + name + "\".";
        }

        public static string Go(string name)
        {
            foreach (var s in All())
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return Cheats.TeleportTo(s.Position, s.Name);
            // Allow a prefix ("!go base" finds "Base camp").
            foreach (var s in All())
                if (s.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return Cheats.TeleportTo(s.Position, s.Name);
            return "No saved location called \"" + name + "\".";
        }

        public static string Delete(string name)
        {
            var spots = All();
            int removed = spots.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            Write(spots);
            return removed > 0 ? "Deleted \"" + name + "\"." : "No saved location called \"" + name + "\".";
        }

        static string AutoName(Vector3 pos)
        {
            var world = Game.World;
            string near = null;
            if (world != null)
            {
                int best = -1; float bestDot = -2f;
                var up = pos.normalized;
                int count = world.LandmarkCount;
                for (int i = 0; i < count; i++)
                {
                    float dot = Vector3.Dot(up, world.LandmarkDirection(i).normalized);
                    if (dot > bestDot) { bestDot = dot; best = i; }
                }
                if (best >= 0) { var lm = world.Landmark(best); if (lm != null) near = lm.name; }
            }
            string baseName = near != null ? "Near " + near : "Spot";
            var spots = All();
            string name = baseName; int n = 2;
            while (spots.Exists(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) name = baseName + " " + n++;
            return name;
        }
    }
}
