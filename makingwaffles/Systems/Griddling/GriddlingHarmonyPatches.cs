using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    internal static class GriddlingSlotHelpers
    {
        public static bool IsGriddleContainer(InventorySmelting inventory)
        {
            ItemSlot? inputSlot = inventory?[1];
            if (inputSlot?.Itemstack?.Collectible is BlockGriddlingContainer
                || inputSlot?.Itemstack?.Collectible is BlockGriddledContainer)
            {
                return true;
            }

            ItemSlot? outputSlot = inventory?[2];
            return outputSlot?.Itemstack?.Collectible is BlockGriddledContainer;
        }

        public static int GetCookingSlotRows(int quantityCookingSlots)
        {
            if (quantityCookingSlots <= 0) return 0;
            return Math.Max(1, quantityCookingSlots / 4);
        }

        public static bool TryTakeOutGriddledOutput(ItemSlot slot, int quantity, out ItemStack? takenStack)
        {
            takenStack = null;
            if (slot?.Inventory is not InventorySmelting inventory) return false;

            ItemSlot outputSlot = inventory[2];
            if (outputSlot != slot) return false;

            ItemStack? cookedStack = slot.Itemstack;
            if (cookedStack?.Collectible is not BlockGriddledContainer cookedBlock) return false;

            if (quantity <= 0) return false;

            string? emptyCode = cookedBlock.Attributes?["emptiedBlockCode"]?.AsString();
            AssetLocation emptyLoc = AssetLocation.CreateOrNull(emptyCode) ?? cookedBlock.CodeWithVariant("state", "empty");
            Block? emptyBlock = inventory.Api.World.GetBlock(emptyLoc);
            if (emptyBlock == null || emptyBlock.BlockId == 0) return false;

            ItemStack emptyStack = new ItemStack(emptyBlock);
            float temp = cookedStack.Collectible.GetTemperature(inventory.Api.World, cookedStack);
            emptyStack.Collectible.SetTemperature(inventory.Api.World, emptyStack, temp);

            Vec3d? dropPos = inventory.pos?.ToVec3d().Add(0.5, 0.5, 0.5);
            ItemSlot[] cookingSlots = inventory.Slots;
            for (int i = 0; i < cookingSlots.Length; i++)
            {
                ItemStack? stack = cookingSlots[i].Itemstack;
                if (stack == null) continue;

                if (dropPos != null)
                {
                    inventory.Api.World.SpawnItemEntity(stack, dropPos);
                }
                cookingSlots[i].Itemstack = null;
                cookingSlots[i].MarkDirty();
            }

            slot.Itemstack = null;
            slot.OnItemSlotModified(cookedStack);
            takenStack = emptyStack;
            return true;
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.GetOutputText))]
    public static class GriddlingOutputTextPatch
    {
        public static void Postfix(InventorySmelting __instance, ref string __result)
        {
            ItemSlot? inputSlot = __instance?[1];
            if (inputSlot?.Itemstack?.Collectible is not BlockGriddlingContainer griddle) return;

            if (__instance.Api == null)
            {
                __result = null;
                return;
            }

            __result = griddle.GetOutputText(__instance.Api.World, __instance, inputSlot);
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.OnItemSlotModified))]
    public static class GriddlingEmptySwapPatch
    {
        public static void Postfix(InventorySmelting __instance, ItemSlot slot)
        {
            if (__instance.Api?.Side != EnumAppSide.Server) return;

            ItemSlot? inputSlot = __instance?[1];
            ItemSlot? outputSlot = __instance?[2];

            ItemSlot? cookedSlot = inputSlot?.Itemstack?.Collectible is BlockGriddledContainer
                ? inputSlot
                : outputSlot?.Itemstack?.Collectible is BlockGriddledContainer
                    ? outputSlot
                    : null;

            if (cookedSlot == null) return;
            BlockGriddledContainer cookedBlock = (BlockGriddledContainer)cookedSlot.Itemstack.Collectible;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            for (int i = 0; i < cookingSlots.Length; i++)
            {
                if (!cookingSlots[i].Empty) return;
            }

            string? emptyCode = cookedBlock.Attributes?["emptiedBlockCode"]?.AsString();
            AssetLocation emptyLoc = AssetLocation.CreateOrNull(emptyCode) ?? cookedBlock.CodeWithVariant("state", "empty");
            Block? emptyBlock = __instance.Api.World.GetBlock(emptyLoc);
            if (emptyBlock == null || emptyBlock.BlockId == 0) return;

            ItemStack oldStack = cookedSlot.Itemstack;
            ItemStack newStack = new ItemStack(emptyBlock);
            float temp = oldStack.Collectible.GetTemperature(__instance.Api.World, oldStack);
            newStack.Collectible.SetTemperature(__instance.Api.World, newStack, temp);

            cookedSlot.Itemstack = newStack;
            cookedSlot.MarkDirty();
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), "get_HaveCookingContainer")]
    public static class GriddlingHaveCookingContainerPatch
    {
        public static void Postfix(InventorySmelting __instance, ref bool __result)
        {
            if (__result) return;

            ItemSlot? outputSlot = __instance?[2];
            if (outputSlot?.Itemstack?.Collectible is BlockGriddledContainer)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), "get_CookingSlots")]
    public static class GriddlingCookingSlotsPatch
    {
        public static void Postfix(InventorySmelting __instance, ref ItemSlot[] __result)
        {
            if (__result.Length > 0) return;

            ItemSlot? outputSlot = __instance?[2];
            if (outputSlot?.Itemstack?.Collectible is BlockGriddledContainer)
            {
                __result = __instance.Slots;
            }
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.discardCookingSlots))]
    public static class GriddlingDiscardCookingSlotsPatch
    {
        public static bool Prefix(InventorySmelting __instance)
        {
            ItemSlot? outputSlot = __instance?[2];
            if (outputSlot?.Itemstack?.Collectible is BlockGriddledContainer)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.TakeOutWhole))]
    public static class GriddlingTakeOutWholePatch
    {
        public static bool Prefix(ItemSlot __instance, ref ItemStack __result)
        {
            if (!GriddlingSlotHelpers.TryTakeOutGriddledOutput(__instance, 1, out ItemStack? takenStack)) return true;

            __result = takenStack!;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.TakeOut))]
    public static class GriddlingTakeOutPatch
    {
        public static bool Prefix(ItemSlot __instance, int quantity, ref ItemStack __result)
        {
            if (!GriddlingSlotHelpers.TryTakeOutGriddledOutput(__instance, quantity, out ItemStack? takenStack)) return true;

            __result = takenStack!;
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockEntityFirepit), "SetDialogValues")]
    public static class GriddlingFirepitDialogValuesPatch
    {
        public static void Postfix(BlockEntityFirepit __instance, ITreeAttribute dialogTree)
        {
            if (__instance?.Inventory is not InventorySmelting inventory) return;
            if (!GriddlingSlotHelpers.IsGriddleContainer(inventory)) return;

            dialogTree.SetInt("haveCookingContainer", 1);
            dialogTree.SetInt("quantityCookingSlots", 1);
        }
    }

    [HarmonyPatch(typeof(GuiDialogBlockEntityFirepit), "SetupDialog")]
    public static class GriddlingFirepitDialogRowsPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions);
            matcher.MatchStartForward(
                new CodeMatch(instruction => instruction.IsLdloc()),
                new CodeMatch(OpCodes.Ldc_I4_4),
                new CodeMatch(OpCodes.Div)
            );

            if (matcher.IsValid)
            {
                matcher.Advance(1)
                    .RemoveInstructions(2)
                    .Insert(
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GriddlingSlotHelpers), nameof(GriddlingSlotHelpers.GetCookingSlotRows)))
                    );
            }

            return matcher.InstructionEnumeration();
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.CanContain))]
    public static class GriddlingSingleSlotContainPatch
    {
        public static bool Prefix(InventorySmelting __instance, ItemSlot sinkSlot, ref bool __result)
        {
            if (!GriddlingSlotHelpers.IsGriddleContainer(__instance)) return true;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            if (cookingSlots.Length == 0) return true;

            if (sinkSlot == cookingSlots[0]) return true;

            for (int i = 1; i < cookingSlots.Length; i++)
            {
                if (sinkSlot == cookingSlots[i])
                {
                    __result = false;
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.GetSuitability))]
    public static class GriddlingSingleSlotSuitabilityPatch
    {
        public static void Postfix(InventorySmelting __instance, ItemSlot targetSlot, ref float __result)
        {
            if (!GriddlingSlotHelpers.IsGriddleContainer(__instance)) return;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            if (cookingSlots.Length == 0) return;

            for (int i = 1; i < cookingSlots.Length; i++)
            {
                if (targetSlot == cookingSlots[i])
                {
                    __result = 0f;
                    return;
                }
            }
        }
    }
}
