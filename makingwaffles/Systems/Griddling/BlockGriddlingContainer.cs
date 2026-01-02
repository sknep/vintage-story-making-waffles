using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    public class BlockGriddlingContainer : Block, IInFirepitRendererSupplier, IAttachableToEntity, IContainedCustomName
    {
        public int MaxServingSize = 8;
        Cuboidi? attachmentArea;
        IAttachableToEntity? attrAtta;

        #region IAttachableToEntity

        public int RequiresBehindSlots { get; set; } = 0;
        public bool IsAttachable(Entity toEntity, ItemStack itemStack) => attrAtta != null;
        string? IAttachableToEntity.GetCategoryCode(ItemStack stack) => attrAtta?.GetCategoryCode(stack);
        CompositeShape? IAttachableToEntity.GetAttachedShape(ItemStack stack, string slotCode) => attrAtta?.GetAttachedShape(stack, slotCode);
        string[]? IAttachableToEntity.GetDisableElements(ItemStack stack) => attrAtta?.GetDisableElements(stack);
        string[]? IAttachableToEntity.GetKeepElements(ItemStack stack) => attrAtta?.GetKeepElements(stack);
        string? IAttachableToEntity.GetTexturePrefixCode(ItemStack stack) => attrAtta?.GetTexturePrefixCode(stack);

        void IAttachableToEntity.CollectTextures(ItemStack itemstack, Shape intoShape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict)
        {
            attrAtta?.CollectTextures(itemstack, intoShape, texturePrefixCode, intoDict);
        }

        #endregion

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            attachmentArea = Attributes?["attachmentArea"].AsObject<Cuboidi?>(null);

            MaxServingSize = Attributes?["maxServingSize"].AsInt(8) ?? 8;

            attrAtta = IAttachableToEntity.FromAttributes(this);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (!hotbarSlot.Empty && hotbarSlot.Itemstack.Collectible.Attributes?.IsTrue("handleCookingContainerInteract") == true)
            {
                EnumHandHandling handling = EnumHandHandling.NotHandled;
                hotbarSlot.Itemstack.Collectible.OnHeldInteractStart(hotbarSlot, byPlayer.Entity, blockSel, null, true, ref handling);
                if (handling == EnumHandHandling.PreventDefault || handling == EnumHandHandling.PreventDefaultAction) return true;
            }

            ItemStack stack = OnPickBlock(world, blockSel.Position);

            if (byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            {
                world.BlockAccessor.SetBlock(0, blockSel.Position);
                world.PlaySoundAt(this.Sounds.Place, byPlayer, byPlayer);
                return true;
            }
            return false;
        }


        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!byPlayer.Entity.Controls.ShiftKey)
            {
                failureCode = "onlywhensneaking";
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (blockSel?.Position == null) return;

            BlockEntityFirepit? firepit = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityFirepit;
            if (firepit?.Inventory is not InventorySmelting inv) return;

            ItemSlot inputSlot = inv[1];
            if (inputSlot == null || !inputSlot.Empty) return;

            if (byEntity.World.Side == EnumAppSide.Client)
            {
                handling = EnumHandHandling.PreventDefaultAction;
                return;
            }

            ItemStack? placeStack = slot.TakeOut(1);
            if (placeStack == null) return;

            inputSlot.Itemstack = placeStack;
            inputSlot.MarkDirty();
            slot.MarkDirty();
            firepit.MarkDirty(true);

            IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;
            byEntity.World.PlaySoundAt(Sounds.Place, blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5, byPlayer);

            handling = EnumHandHandling.PreventDefaultAction;
        }


        public override float GetMeltingDuration(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot)
        {
            ItemStack[] matchStacks = GetCookingStacks(cookingSlotsProvider, false);
            CookingRecipe? matchRecipe = GetMatchingCookingRecipe(world, matchStacks, out int matchServings);
            if (matchRecipe != null && matchServings > 0 && matchServings <= MaxServingSize)
            {
                float baseSeconds = Attributes?["cookBaseSeconds"].AsFloat(7f) ?? 7f;
                float secondsPerServing = Attributes?["cookSecondsPerServing"].AsFloat(11f) ?? 11f;
                return baseSeconds + secondsPerServing * matchServings;
            }

            float duration = 0;

            ItemStack[] stacks = GetCookingStacks(cookingSlotsProvider, false);
            for (int i = 0; i < stacks.Length; i++)
            {
                var stack = stacks[i];
                int portionSize = stack.StackSize;

                if (stack.Collectible?.CombustibleProps == null)
                {
                    if (stack.Collectible?.Attributes?["waterTightContainerProps"].Exists == true)
                    {
                        var props = BlockLiquidContainerBase.GetContainableProps(stack);
                        portionSize = (int)(stack.StackSize / props?.ItemsPerLitre ?? 1);
                    }

                    duration += 20 * portionSize;
                    continue;
                }

                float singleDuration = stack.Collectible.GetMeltingDuration(world, cookingSlotsProvider, inputSlot);
                duration += singleDuration * portionSize / stack.Collectible.CombustibleProps.SmeltedRatio;
            }

            duration = Math.Max(8, duration / 6);

            return duration;
        }


        public override float GetMeltingPoint(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot)
        {
            float meltpoint = 0;

            ItemStack[] stacks = GetCookingStacks(cookingSlotsProvider, false);
            for (int i = 0; i < stacks.Length; i++)
            {
                meltpoint = Math.Max(meltpoint, stacks[i].Collectible.GetMeltingPoint(world, cookingSlotsProvider, inputSlot));
            }

            return Math.Max(100, meltpoint);
        }


        public override bool CanSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemStack inputStack, ItemStack outputStack)
        {
            GetMatchingCookingRecipe(world, GetCookingStacks(cookingSlotsProvider, false), out int quantityServings);

            return quantityServings > 0 && quantityServings <= MaxServingSize;
        }


        public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
        {
            ItemStack[] stacks = GetCookingStacks(cookingSlotsProvider);
            CookingRecipe? recipe = GetMatchingCookingRecipe(world, stacks, out int quantityServings);

            Block block = world.GetBlock(CodeWithVariant("state", "cooked"));

            if (recipe == null) return;

            if (quantityServings < 1 || quantityServings > MaxServingSize) return;

            if (recipe.CooksInto != null)
            {
                var outstack = recipe.CooksInto.ResolvedItemstack?.Clone();
                if (outstack != null)
                {
                    outstack.StackSize *= quantityServings;
                    stacks = [outstack];
                    if (!recipe.IsFood) block = world.GetBlock(new AssetLocation(Attributes["dirtiedBlockCode"].AsString()));
                }
            }
            else
            {
                for (int i = 0; i < stacks.Length; i++)
                {
                    stacks[i].StackSize = stacks[i].StackSize / quantityServings;
                    CookingRecipeIngredient? ingred = recipe.GetIngrendientFor(stacks[i]);
                    ItemStack? cookedStack = ingred?.GetMatchingStack(stacks[i])?.CookedStack?.ResolvedItemstack.Clone();
                    if (cookedStack != null)
                    {
                        stacks[i] = cookedStack;
                    }
                }
            }

            ItemStack outputStack = new ItemStack(block);
            outputStack.Collectible.SetTemperature(world, outputStack, GetIngredientsTemperature(world, stacks));

            // Carry over and set perishable properties
            TransitionableProperties? cookedPerishProps = recipe.PerishableProps?.Clone();
            cookedPerishProps?.TransitionedStack.Resolve(world, "cooking container perished stack");

            if (cookedPerishProps != null) CarryOverFreshness(api, cookingSlotsProvider.Slots, stacks, cookedPerishProps);


            if (recipe.CooksInto != null)
            {
                for (int i = 0; i < cookingSlotsProvider.Slots.Length; i++)
                {
                    cookingSlotsProvider.Slots[i].Itemstack = i == 0 ? stacks[0] : null;
                }
                ((BlockGriddledContainer)block).SetContents(recipe.Code, 1, outputStack, stacks);
                outputSlot.Itemstack = outputStack;
                inputSlot.Itemstack = null;
                outputSlot.MarkDirty();
                inputSlot.MarkDirty();
            }
            else
            {
                for (int i = 0; i < cookingSlotsProvider.Slots.Length; i++)
                {
                    cookingSlotsProvider.Slots[i].Itemstack = null;
                }
                ((BlockGriddledContainer)block).SetContents(recipe.Code, quantityServings, outputStack, stacks);

                outputSlot.Itemstack = outputStack;
                inputSlot.Itemstack = null;
            }
        }

        public string? GetOutputText(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot)
        {
            if (inputSlot.Itemstack == null) return null;
            if (inputSlot.Itemstack.Collectible is not BlockGriddlingContainer) return null;

            ItemStack[] stacks = GetCookingStacks(cookingSlotsProvider);

            CookingRecipe? recipe = GetMatchingCookingRecipe(world, stacks, out int quantity);

            if (recipe != null)
            {
                string message;
                string? outputName = recipe.GetOutputName(world, stacks);

                if (recipe.CooksInto != null)
                {
                    ItemStack outStack = recipe.CooksInto.ResolvedItemstack;

                    message = "mealcreation-nonfood";
                    outputName = outStack?.GetName();

                    if (quantity == -1) return Lang.Get("makingwaffles:griddle-recipeerror", outputName?.ToLower() ?? Lang.Get("unknown"));
                    quantity *= recipe.CooksInto.Quantity;

                    if (outStack?.Collectible.Attributes?["waterTightContainerProps"].Exists == true)
                    {
                        float litreFloat = quantity / BlockLiquidContainerBase.GetContainableProps(outStack)?.ItemsPerLitre ?? 1;
                        string litres;

                        if (litreFloat < 0.1)
                        {
                            litres = Lang.Get("{0} mL", (int)(litreFloat * 1000));
                        }
                        else
                        {
                            litres = Lang.Get("{0:0.##} L", litreFloat);
                        }

                        return Lang.Get("mealcreation-nonfood-liquid", litres, outputName?.ToLower() ?? Lang.Get("unknown"));
                    }
                }
                else
                {
                    message = quantity == 1 ? "mealcreation-makesingular" : "mealcreation-makeplural";
                    // We need to use language plural format instead, here and all similar code!
                }
                if (quantity == -1) return Lang.Get("makingwaffles:griddle-recipeerror", outputName?.ToLower() ?? Lang.Get("unknown"));
                else if (quantity > MaxServingSize) return Lang.Get("mealcreation-toomuch", inputSlot.GetStackName(), quantity, outputName?.ToLower() ?? Lang.Get("unknown"));
                return Lang.Get(message, quantity, outputName?.ToLower() ?? Lang.Get("unknown"));
            }

            if (!stacks.All(stack => stack == null)) return Lang.Get("mealcreation-norecipe");
            return null;

        }





        public CookingRecipe? GetMatchingCookingRecipe(IWorldAccessor world, ItemStack[] stacks, out int quantityServings)
        {
            quantityServings = 0;
            List<CookingRecipe> recipes = world.Api.ModLoader
                .GetModSystem<GriddlingRecipeRegistrySystem>()
                .GriddlingRecipes;
            if (recipes == null || recipes.Count == 0) return null;

            foreach (var recipe in recipes)
            {
                quantityServings = 0;
                if (recipe.Matches(stacks, ref quantityServings) || quantityServings == -1) return recipe;
            }

            return null;
        }


        public static float GetIngredientsTemperature(IWorldAccessor world, ItemStack[] ingredients)
        {
            bool haveStack = false;
            float lowestTemp = 0;
            for (int i = 0; i < ingredients.Length; i++)
            {
                if (ingredients[i] != null)
                {
                    float stackTemp = ingredients[i].Collectible.GetTemperature(world, ingredients[i]);
                    lowestTemp = haveStack ? Math.Min(lowestTemp, stackTemp) : stackTemp;
                    haveStack = true;
                }

            }

            return lowestTemp;
        }





        public ItemStack[] GetCookingStacks(ISlotProvider cookingSlotsProvider, bool clone = true)
        {
            List<ItemStack> stacks = new List<ItemStack>(4);

            for (int i = 0; i < cookingSlotsProvider.Slots.Length; i++)
            {
                ItemStack? stack = cookingSlotsProvider.Slots[i].Itemstack;
                if (stack == null) continue;
                stacks.Add(clone ? stack.Clone() : stack);
            }

            return stacks.ToArray();
        }

        public IInFirepitRenderer GetRendererWhenInFirepit(ItemStack stack, BlockEntityFirepit firepit, bool forOutputSlot)
        {
            return new GriddleInFirepitRenderer(api as ICoreClientAPI, stack, firepit.Pos, forOutputSlot);
        }

        public EnumFirepitModel GetDesiredFirepitModel(ItemStack stack, BlockEntityFirepit firepit, bool forOutputSlot)
        {
            return GetDesiredFirepitModelFromAttributes();
        }

        EnumFirepitModel GetDesiredFirepitModelFromAttributes()
        {
            string? model = Attributes?["inFirePitProps"]?["useFirepitModel"].AsString();
            if (model == null) return EnumFirepitModel.Wide;

            switch (model.ToLowerInvariant())
            {
                case "spit":
                    return EnumFirepitModel.Spit;
                case "normal":
                    return EnumFirepitModel.Normal;
                case "wide":
                    return EnumFirepitModel.Wide;
                default:
                    return EnumFirepitModel.Wide;
            }
        }



        public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1)
        {
            return capi.BlockTextureAtlas.GetRandomColor(Textures["ceramic"].Baked.TextureSubId, rndIndex);
        }

        public override int GetRandomColor(ICoreClientAPI capi, ItemStack stack)
        {
            return capi.BlockTextureAtlas.GetRandomColor(Textures["ceramic"].Baked.TextureSubId);
        }

        public string? GetContainedName(ItemSlot inSlot, int quantity)
        {
            return null;
        }

        public string GetContainedInfo(ItemSlot inSlot)
        {
            return Lang.GetWithFallback("contained-empty-container", "{0} (Empty)", inSlot.GetStackName());
        }
    }
}
