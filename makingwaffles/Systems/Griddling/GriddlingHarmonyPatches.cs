using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
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
            if (inputSlot?.Itemstack?.Collectible is not BlockGriddledContainer cookedBlock) return;

            ItemSlot[] cookingSlots = __instance.CookingSlots;
            for (int i = 0; i < cookingSlots.Length; i++)
            {
                if (!cookingSlots[i].Empty) return;
            }

            string? emptyCode = cookedBlock.Attributes?["emptiedBlockCode"]?.AsString();
            AssetLocation emptyLoc = AssetLocation.CreateOrNull(emptyCode) ?? cookedBlock.CodeWithVariant("state", "empty");
            Block? emptyBlock = __instance.Api.World.GetBlock(emptyLoc);
            if (emptyBlock == null || emptyBlock.BlockId == 0) return;

            ItemStack oldStack = inputSlot.Itemstack;
            ItemStack newStack = new ItemStack(emptyBlock);
            float temp = oldStack.Collectible.GetTemperature(__instance.Api.World, oldStack);
            newStack.Collectible.SetTemperature(__instance.Api.World, newStack, temp);

            inputSlot.Itemstack = newStack;
            inputSlot.MarkDirty();
        }
    }
}
