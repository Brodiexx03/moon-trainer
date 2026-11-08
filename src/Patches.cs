using System;
using HarmonyLib;
using MoonCleaners;

namespace MoonTrainer
{
    /// <summary>Harmony hooks into the game's own balance code. Each one only changes anything while its cheat is on.</summary>
    public static class Patches
    {
        // ---- the game's own developer switch: on only while the trainer is using a Dev* function or flying ----

        [HarmonyPatch(typeof(MoonDevTools), nameof(MoonDevTools.Enabled), MethodType.Getter)]
        [HarmonyPostfix]
        static void DevTools(ref bool __result)
        {
            if (Cheats.DevScope) __result = true;
        }

        // ---- player multipliers (local player only) ----

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.MoveSpeedMultiplier))]
        [HarmonyPostfix]
        static void MoveSpeed(string player, ref float __result)
        {
            if (Cheats.SpeedMult != 1f && Cheats.IsLocal(player)) __result *= Cheats.SpeedMult;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.JumpMultiplier))]
        [HarmonyPostfix]
        static void Jump(string player, ref float __result)
        {
            if (Cheats.JumpMult != 1f && Cheats.IsLocal(player)) __result *= Cheats.JumpMult;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.JetpackBoosts))]
        [HarmonyPostfix]
        static void Jetpack(string player, ref int __result)
        {
            if (Cheats.InfiniteJetpack && Cheats.IsLocal(player) && __result < 4) __result = 4;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.HasGravityBoots))]
        [HarmonyPostfix]
        static void Boots(string player, ref bool __result)
        {
            if (Cheats.GravityBoots && Cheats.IsLocal(player)) __result = true;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.HasNightVision))]
        [HarmonyPostfix]
        static void NightVision(string player, ref bool __result)
        {
            if (Cheats.NightVision && Cheats.IsLocal(player)) __result = true;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.CleaningWidthMultiplier))]
        [HarmonyPostfix]
        static void Width(string player, ref float __result)
        {
            if (Cheats.BrushWidthMult != 1f && Cheats.IsLocal(player)) __result *= Cheats.BrushWidthMult;
        }

        // ---- unlimited bag and hopper ----

        [HarmonyPatch(typeof(MoonDustInventory), nameof(MoonDustInventory.CapacityKg), MethodType.Getter)]
        [HarmonyPostfix]
        static void BagCapacity(ref double __result)
        {
            if (Cheats.InfiniteBag) __result = Math.Max(__result, 1000000);
        }

        [HarmonyPatch(typeof(MoonDustInventory), nameof(MoonDustInventory.IsFull), MethodType.Getter)]
        [HarmonyPostfix]
        static void BagFull(ref bool __result)
        {
            if (Cheats.InfiniteBag) __result = false;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.InventoryFull))]
        [HarmonyPostfix]
        static void WorldBagFull(ref bool __result)
        {
            if (Cheats.InfiniteBag) __result = false;
        }

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.DustCapacityKg), MethodType.Getter)]
        [HarmonyPostfix]
        static void HopperCapacity(ref double __result)
        {
            if (Cheats.InfiniteHopper) __result = Math.Max(__result, 1000000);
        }

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.DustFull), MethodType.Getter)]
        [HarmonyPostfix]
        static void HopperFull(ref bool __result)
        {
            if (Cheats.InfiniteHopper) __result = false;
        }

        // ---- map ----

        [HarmonyPatch(typeof(MoonMapScreen), nameof(MoonMapScreen.FlagsOnMap))]
        [HarmonyPostfix]
        static void MapFlags(ref bool __result)
        {
            if (Cheats.FlagsOnMap) __result = true;
        }

        // ---- money ----

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PayoutMultiplier))]
        [HarmonyPostfix]
        static void Payout(string player, ref Il2CppSystem.Decimal __result)
        {
            if (Cheats.PayoutMult != 1f && Cheats.IsLocal(player))
                __result = Game.Dec(Game.ToDouble(__result) * Cheats.PayoutMult);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.Discounted))]
        [HarmonyPostfix]
        static void Discounted(string player, ref Il2CppSystem.Decimal __result)
        {
            if (Cheats.FreeUpgrades && Cheats.IsLocal(player)) __result = Game.Dec(0);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.PriceMultiplier))]
        [HarmonyPostfix]
        static void PriceMult(string player, ref Il2CppSystem.Decimal __result)
        {
            if (Cheats.FreeUpgrades && Cheats.IsLocal(player)) __result = Game.Dec(0);
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.TryPayTeleport))]
        [HarmonyPrefix]
        static bool FreeTravel(string player, ref bool __result)
        {
            if (!Cheats.FreeTravel || !Cheats.IsLocal(player)) return true;
            __result = true;
            return false;
        }

        // ---- vehicles ----

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.VehicleSpeedFactor))]
        [HarmonyPostfix]
        static void VehicleSpeed(ref float __result)
        {
            if (Cheats.VehicleSpeedMult != 1f) __result *= Cheats.VehicleSpeedMult;
        }

        [HarmonyPatch(typeof(MoonPrototypeVehicle), nameof(MoonPrototypeVehicle.CleaningWidth), MethodType.Getter)]
        [HarmonyPostfix]
        static void VehicleWidth(ref float __result)
        {
            if (Cheats.BrushWidthMult != 1f) __result *= Cheats.BrushWidthMult;
        }

        // ---- machines ----

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.StepProduction))]
        [HarmonyPrefix]
        static void Production(ref float dt)
        {
            if (Cheats.MachineSpeed != 1f) dt *= Cheats.MachineSpeed;
        }
    }

    /// <summary>Chat commands. Crewmates' messages arrive through MoonTalk.Heard; our own go out through MoonTalk.Say.</summary>
    public static class ChatPatches
    {
        [HarmonyPatch(typeof(MoonTalk), nameof(MoonTalk.Heard))]
        [HarmonyPostfix]
        static void Heard(string name, string said)
        {
            try { Chat.Handle(name, said, false); }
            catch (Exception e) { Plugin.Logger.LogError("Chat command failed: " + e); }
        }

        [HarmonyPatch(typeof(MoonTalk), nameof(MoonTalk.Say))]
        [HarmonyPrefix]
        static void Say(string said)
        {
            try { Chat.Handle(null, said, true); }
            catch (Exception e) { Plugin.Logger.LogError("Chat command failed: " + e); }
        }
    }
}
