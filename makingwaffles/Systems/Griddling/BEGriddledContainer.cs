using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    public class BlockEntityGriddledContainer : BlockEntityContainer, IBlockEntityMealContainer
    {
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "cookedcontainer";

        internal InventoryGeneric inventory;
        public float QuantityServings { get; set; }
        public string? RecipeCode { get; set; }

        internal BlockGriddledContainer? ownBlock;

        InventoryBase IBlockEntityMealContainer.inventory => inventory;

        public bool Rotten
        {
            get
            {
                bool rotten = false;
                for (int i = 0; i < inventory.Count; i++)
                {
                    rotten |= inventory[i].Itemstack?.Collectible.Code.Path == "rot";
                }

                return rotten;
            }
        }

        public CookingRecipe? FromRecipe
        {
            get
            {
                return Api?.ModLoader
                    .GetModSystem<GriddlingRecipeRegistrySystem>()
                    .GriddlingRecipes
                    .FirstOrDefault(r => r.Code == RecipeCode);
            }
        }

        public BlockEntityGriddledContainer()
        {
            inventory = new InventoryGeneric(4, null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            ownBlock = Block as BlockGriddledContainer;
            inventory.SlotModified += OnSlotModified;

            if (Api.Side == EnumAppSide.Client)
            {
                RegisterGameTickListener(Every100ms, 200);
            }
        }

        private void OnSlotModified(int slotId)
        {
            if (Api?.Side != EnumAppSide.Server) return;
            if (inventory?.Count == 0) return;

            for (int i = 0; i < inventory.Count; i++)
            {
                if (!inventory[i].Empty) return;
            }

            AssetLocation emptyLoc = AssetLocation.CreateOrNull(Block?.Attributes?["emptiedBlockCode"]?.AsString()) ?? Block?.CodeWithVariant("state", "empty");
            Block? emptyBlock = emptyLoc != null ? Api.World.GetBlock(emptyLoc) : null;
            if (emptyBlock == null || emptyBlock.BlockId == 0) return;

            Api.World.BlockAccessor.SetBlock(emptyBlock.BlockId, Pos);
        }

        private void Every100ms(float dt)
        {
            float temp = GetTemperature();
            if (Api.World.Rand.NextDouble() < (temp - 50) / 160)
            {
                BlockGriddledContainer.smokeHeld.MinPos = Pos.ToVec3d().AddCopy(0.5 - 0.05, 0.3125, 0.5 - 0.05);
                Api.World.SpawnParticles(BlockGriddledContainer.smokeHeld);
            }
        }

        private int GetTemperature()
        {
            ItemStack[] stacks = GetNonEmptyContentStacks(false);
            if (stacks.Length == 0 || stacks[0] == null) return 0;

            return (int)stacks[0].Collectible.GetTemperature(Api.World, stacks[0]);
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            BlockGriddledContainer? blockpot = byItemStack?.Block as BlockGriddledContainer;
            if (blockpot != null)
            {
                TreeAttribute? tempTree = byItemStack?.Attributes?["temperature"] as TreeAttribute;

                if (blockpot.GetNonEmptyContents(Api.World, byItemStack) is ItemStack[] stacks)
                {
                    for (int i = 0; i < stacks.Length; i++)
                    {
                        ItemStack stack = stacks[i].Clone();
                        Inventory[i].Itemstack = stack;

                        if (tempTree != null) stack.Attributes["temperature"] = tempTree.Clone();
                    }
                }

                RecipeCode = blockpot.GetRecipeCode(Api.World, byItemStack);
                QuantityServings = byItemStack?.Attributes?.GetFloat("quantityServings", 1) ?? 1;
            }
        }

        public override void OnBlockBroken(IPlayer? byPlayer = null)
        {
            // Don't drop contents
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            QuantityServings = (float)tree.GetDecimal("quantityServings", 1);
            RecipeCode = tree.GetString("recipeCode");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetFloat("quantityServings", QuantityServings);
            tree.SetString("recipeCode", RecipeCode == null ? "" : RecipeCode);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            ItemStack[] contentStacks = GetNonEmptyContentStacks();
            if (contentStacks.Length == 0) return;

            int temp = GetTemperature();
            string temppretty = temp < 20 ? Lang.Get("Cold") : Lang.Get("{0}A?C", temp);
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

