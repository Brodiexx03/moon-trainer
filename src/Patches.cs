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

        // The bag hooks leave a crewmate's bag alone. Game 1.0.5 only ever asks these about our own bag (through the
        // local player's MoonDustInventory), so this guards against a game update asking for someone else's. They
        // test "not a crewmate's" rather than "ours": an id we fail to recognise must never switch the cheat off.

        [HarmonyPatch(typeof(MoonDustInventory), nameof(MoonDustInventory.CapacityKg), MethodType.Getter)]
        [HarmonyPostfix]
        static void BagCapacity(MoonDustInventory __instance, ref double __result)
        {
            if (Cheats.InfiniteBag && Cheats.NotCrewmate(Game.BagOwner(__instance))) __result = Math.Max(__result, 1000000);
        }

        [HarmonyPatch(typeof(MoonDustInventory), nameof(MoonDustInventory.IsFull), MethodType.Getter)]
        [HarmonyPostfix]
        static void BagFull(MoonDustInventory __instance, ref bool __result)
        {
            if (__result && Cheats.InfiniteBag && Cheats.NotCrewmate(Game.BagOwner(__instance))) __result = false;
        }

        [HarmonyPatch(typeof(MoonWorld), nameof(MoonWorld.InventoryFull))]
        [HarmonyPostfix]
        static void WorldBagFull(string playerId, ref bool __result)
        {
            if (__result && Cheats.InfiniteBag && Cheats.NotCrewmate(playerId)) __result = false;
        }

        // The hopper, vehicle speed and vehicle brush hooks are NOT limited to one player or vehicle, on purpose.
        // The game only runs them for a MoonPrototypeVehicle, which is a vehicle this game is driving itself: ours,
        // or a crewmate's we have taken over. A vehicle a crewmate drives is a MoonRemoteVehicle here and never comes
        // through (checked against game 1.0.5). Narrowing them to "the one I'm sitting in" would only switch the
        // cheat off for a vehicle we parked or were handed. The menu says so (Trainer.DustPage).

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

        // ---- vehicles (every vehicle this game is driving - see the note above the hopper hooks) ----

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

        // ---- machines (world-wide by nature: one clock steps every machine, so a host speeds up the whole crew's) ----

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
