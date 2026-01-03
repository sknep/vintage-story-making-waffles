using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    public class BlockEntityGriddleContainer : BlockEntityContainer, IBlockEntityMealContainer
    {
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "griddlecontainer";

        internal InventoryGeneric inventory;
        public float QuantityServings { get; set; } = 0;
        public string? RecipeCode { get; set; }

        InventoryBase IBlockEntityMealContainer.inventory => inventory;

        public BlockEntityGriddleContainer()
        {
            inventory = new InventoryGeneric(4, null, null);
            inventory.SlotModified += OnSlotModified;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

            if (Api?.Side == EnumAppSide.Client && inventory.Count > 0)
            {
                RegisterGameTickListener(dt =>
                {
                    float temp = GetTemperature();
                    if (Api.World.Rand.NextDouble() < (temp - 50) / 160)
                    {
                        BlockGriddlingContainer.smokeHeld.MinPos = Pos.ToVec3d().AddCopy(0.5 - 0.05, 0.3125, 0.5 - 0.05);
                        Api.World.SpawnParticles(BlockGriddlingContainer.smokeHeld);
                    }
                }, 200);
            }
        }

        void OnSlotModified(int slotId)
        {
            if (Api?.Side != EnumAppSide.Server) return;
            if (inventory.Count == 0) return;
            for (int i = 0; i < inventory.Count; i++)
            {
                if (!inventory[i].Empty) return;
            }

            AssetLocation emptyLoc = AssetLocation.CreateOrNull(Block?.Attributes?["emptiedBlockCode"]?.AsString()) ?? Block?.Code;
            Block? emptyBlock = emptyLoc != null ? Api.World.GetBlock(emptyLoc) : null;
            if (emptyBlock == null || emptyBlock.BlockId == 0) return;
            Api.World.BlockAccessor.SetBlock(emptyBlock.BlockId, Pos);
        }

        int GetTemperature()
        {
            ItemStack[] stacks = GetNonEmptyContentStacks(false);
            if (stacks.Length == 0 || stacks[0] == null) return 0;
            return (int)stacks[0].Collectible.GetTemperature(Api.World, stacks[0]);
        }

        public ItemStack[] GetNonEmptyContentStacks(bool clone = true)
        {
            List<ItemStack> stacks = new List<ItemStack>(inventory.Count);
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemStack? stack = inventory[i].Itemstack;
                if (stack == null) continue;
                stacks.Add(clone ? stack.Clone() : stack);
            }
            return stacks.ToArray();
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            if (byItemStack == null) return;

            RecipeCode = byItemStack.Attributes.GetString("recipeCode", null);
            QuantityServings = byItemStack.Attributes.GetFloat("quantityServings", 0);

            for (int i = 0; i < inventory.Count; i++)
            {
                ItemStack? stack = byItemStack.Attributes.GetItemstack("contents" + i);
                inventory[i].Itemstack = stack?.Clone();
            }
        }

        public void WriteToItem(ItemStack stack)
        {
            ItemStack[] contents = GetNonEmptyContentStacks();
            stack.Attributes.SetString("recipeCode", RecipeCode ?? string.Empty);
            stack.Attributes.SetFloat("quantityServings", QuantityServings);
            for (int i = 0; i < inventory.Count; i++)
            {
                stack.Attributes.SetItemstack("contents" + i, i < contents.Length ? contents[i] : null);
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
            inventory.AfterBlocksLoaded(Api.World);
            RecipeCode = tree.GetString("recipeCode");
            QuantityServings = (float)tree.GetDecimal("quantityServings", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invTree = new TreeAttribute();
            inventory.ToTreeAttributes(invTree);
            tree["inventory"] = invTree;
            tree.SetString("recipeCode", RecipeCode ?? string.Empty);
            tree.SetFloat("quantityServings", QuantityServings);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            ItemStack[] contentStacks = GetNonEmptyContentStacks();
            if (contentStacks.Length == 0) return;

            int temp = GetTemperature();
            string temppretty = temp < 20 ? Lang.Get("Cold") : Lang.Get("{0}°C", temp);
            string outputName = contentStacks[0].GetName();
            dsc.AppendLine(outputName + " (" + temppretty + ")");

            foreach (var slot in inventory)
            {
                if (slot.Empty) continue;
                TransitionableProperties[]? propsm = slot.Itemstack.Collectible.GetTransitionableProperties(Api.World, slot.Itemstack, null);
                if (propsm != null && propsm.Length > 0)
                {
                    slot.Itemstack.Collectible.AppendPerishableInfoText(slot, dsc, Api.World);
                    break;
                }
            }
        }
    }
}
