using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    internal static class GriddlingSlotHelpers
    {
        public static bool IsGriddleContainer(InventorySmelting inventory)
        {
            ItemSlot? inputSlot = inventory?[1];
            if (inputSlot?.Itemstack?.Collectible is BlockGriddlingContainer)
            {
                return true;
            }

            return false;
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
            if (inventory.Api?.Side != EnumAppSide.Server) return false;

            ItemSlot inputSlot = inventory[1];

            bool removingFromInput = slot == inputSlot && slot.Itemstack?.Collectible is BlockGriddlingContainer;
            if (!removingFromInput) return false;

            ItemStack? cookedStack = slot.Itemstack;
            if (cookedStack?.Collectible is null) return false;

            if (quantity <= 0) return false;

            AssetLocation emptyLoc = cookedStack.Collectible.Code;
            Block? emptyBlock = inventory.Api.World.GetBlock(emptyLoc);
            if (emptyBlock == null || emptyBlock.BlockId == 0) return false;

            ItemStack emptyStack = new ItemStack(emptyBlock);
            float temp = cookedStack.Collectible.GetTemperature(inventory.Api.World, cookedStack);
            emptyStack.Collectible.SetTemperature(inventory.Api.World, emptyStack, temp);

            Vec3d? dropPos = inventory.pos?.ToVec3d().Add(0.5, 0.5, 0.5);
            ItemSlot[] cookingSlots = inventory.CookingSlots;
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

            if (cookedStack.Collectible is BlockGriddlingContainer cookedBlock)
            {
                ItemStack[] bakedStacks = cookedBlock.GetNonEmptyContents(inventory.Api.World, cookedStack);
                if (dropPos != null)
                {
                    foreach (var stack in bakedStacks)
                    {
                        if (stack == null) continue;
                        inventory.Api.World.SpawnItemEntity(stack, dropPos);
                    }
                }
            }

            slot.Itemstack = null;
            slot.OnItemSlotModified(cookedStack);
            takenStack = emptyStack;
            return true;
        }

        public static bool HasGriddleOutput(InventorySmelting inventory)
        {
            ItemSlot? outputSlot = inventory?[2];
            if (outputSlot == null || outputSlot.Empty) return false;

            if (inventory.Api?.World != null && outputSlot.Itemstack?.Collectible is BlockGriddlingContainer griddle)
            {
                ItemStack[] baked = griddle.GetNonEmptyContents(inventory.Api.World, outputSlot.Itemstack, false);
                if (baked.Length == 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryGetAnyGriddleRecipe(InventorySmelting inventory, out BlockGriddlingContainer? griddle, out CookingRecipe? recipe)
        {
            griddle = null;
            recipe = null;

            ItemSlot? inputSlot = inventory?[1];
            if (inputSlot?.Itemstack?.Collectible is not BlockGriddlingContainer griddleContainer) return false;
            if (inventory.Api?.World == null) return false;

            ItemStack[] stacks = griddleContainer.GetCookingStacks(inventory, false);
            recipe = griddleContainer.GetMatchingCookingRecipe(inventory.Api.World, stacks, out _);
            if (recipe == null) return false;

            griddle = griddleContainer;
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

            // If cooked waffles are still in the output, prompt the player to clear them first
            if (__instance[2]?.Empty == false)
            {
                __result = Lang.Get("makingwaffles:griddle-finished");
                return;
            }

            __result = griddle.GetOutputText(__instance.Api.World, __instance, inputSlot);
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), "get_HaveCookingContainer")]
    public static class GriddlingHaveCookingContainerPatch
    {
        public static void Postfix(InventorySmelting __instance, ref bool __result)
        {
            if (__result) return;

            ItemSlot? inputSlot = __instance?[1];
            if (inputSlot?.Itemstack?.Collectible is BlockGriddlingContainer)
            {
                __result = true;
            }
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
        public static bool Prefix(InventorySmelting __instance, ItemSlot sinkSlot, ItemSlot sourceSlot, ref bool __result)
        {
            if (!GriddlingSlotHelpers.IsGriddleContainer(__instance)) return true;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            bool isCookingSlot = Array.IndexOf(cookingSlots, sinkSlot) >= 0;

            // Only allow first cooking slot; block others
            if (cookingSlots.Length > 0 && sinkSlot == cookingSlots[0])
            {
                if (GriddlingSlotHelpers.HasGriddleOutput(__instance))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
            if (isCookingSlot)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.GetSuitability))]
    public static class GriddlingSingleSlotSuitabilityPatch
    {
        public static void Postfix(InventorySmelting __instance, ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge, ref float __result)
        {
            if (!GriddlingSlotHelpers.IsGriddleContainer(__instance)) return;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            bool isCookingSlot = Array.IndexOf(cookingSlots, targetSlot) >= 0;

            // Only first cooking slot is suitable
            if (isCookingSlot && (cookingSlots.Length == 0 || targetSlot != cookingSlots[0]))
            {
                __result = 0f;
                return;
            }

            if (isCookingSlot && GriddlingSlotHelpers.HasGriddleOutput(__instance))
            {
                __result = 0f;
                return;
            }
        }
    }

    [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.OnItemSlotModified))]
    public static class GriddlingEjectOutputOnPourPatch
    {
        public static void Postfix(InventorySmelting __instance, ItemSlot slot)
        {
            if (!GriddlingSlotHelpers.IsGriddleContainer(__instance)) return;
            if (__instance.Api?.Side != EnumAppSide.Server) return;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            bool isCookingSlot = Array.IndexOf(cookingSlots, slot) >= 0;
            if (!isCookingSlot) return;

            // Only eject if there is cooked output and the new contents form any griddle recipe
            if (__instance[2]?.Empty != false) return;
            if (!GriddlingSlotHelpers.TryGetAnyGriddleRecipe(__instance, out _, out _)) return;

            ItemSlot outputSlot = __instance[2];
            ItemStack? dropStack = outputSlot.Itemstack;
            if (dropStack == null) return;

            Vec3d dropPos = __instance.pos?.ToVec3d().Add(0.5, 0.5, 0.5) ?? new Vec3d();
            __instance.Api.World.SpawnItemEntity(dropStack.Clone(), dropPos);

            AssetLocation dropSound = new AssetLocation("sounds/player/throw");
            if (dropSound != null)
            {
                __instance.Api.World.PlaySoundAt(dropSound, dropPos.X, dropPos.Y, dropPos.Z, null, true, 18, 1.8f);
            }

            outputSlot.Itemstack = null;
            outputSlot.MarkDirty();
        }
    }
}
