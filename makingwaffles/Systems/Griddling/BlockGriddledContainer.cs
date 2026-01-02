using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MakingWaffles.Systems.Griddling
{
    public class BlockGriddledContainer : BlockCookedContainerBase, IInFirepitRendererSupplier, IGroundStoredParticleEmitter, IAttachableToEntity
    {
        public static SimpleParticleProperties smokeHeld;
        public static SimpleParticleProperties foodSparks;

        Vec3d gsSmokePos = new Vec3d(0.5, 0.3125, 0.5);

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

            if (CollisionBoxes[0] != null) gsSmokePos.Y = CollisionBoxes[0].MaxY;

            attrAtta = IAttachableToEntity.FromAttributes(this);
        }

        static BlockGriddledContainer()
        {
            smokeHeld = new SimpleParticleProperties(
                1, 1,
                ColorUtil.ToRgba(50, 220, 220, 220),
                new Vec3d(),
                new Vec3d(),
                new Vec3f(-0.05f, 0.1f, -0.05f),
                new Vec3f(0.05f, 0.15f, 0.05f),
                1.5f,
                0,
                0.25f,
                0.35f,
                EnumParticleModel.Quad
            );
            smokeHeld.SelfPropelled = true;
            smokeHeld.AddPos.Set(0.1, 0.1, 0.1);

            foodSparks = new SimpleParticleProperties(
                1, 1,
                ColorUtil.ToRgba(255, 83, 233, 255),
                new Vec3d(), new Vec3d(),
                new Vec3f(-3f, 1f, -3f),
                new Vec3f(3f, 8f, 3f),
                0.5f,
                1f,
                0.25f, 0.25f
            );
            foodSparks.VertexFlags = 0;
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            if (byEntity.World.Side == EnumAppSide.Client && GetTemperature(byEntity.World, slot.Itemstack) > 50 && byEntity.World.Rand.NextDouble() < 0.07)
            {
                float sideWays = 0.35f;

                if ((byEntity as EntityPlayer)?.Player is IClientPlayer byPlayer && byPlayer.CameraMode != EnumCameraMode.FirstPerson)
                {
                    sideWays = 0f;
                }

                Vec3d pos =
                    byEntity.Pos.XYZ.Add(0, byEntity.LocalEyePos.Y - 0.5f, 0)
                    .Ahead(0.33f, byEntity.Pos.Pitch, byEntity.Pos.Yaw)
                    .Ahead(sideWays, 0, byEntity.Pos.Yaw + GameMath.PIHALF)
                ;

                smokeHeld.MinPos = pos.AddCopy(-0.05, 0.1, -0.05);
                byEntity.World.SpawnParticles(smokeHeld);
            }

            if (byEntity.World.Side != EnumAppSide.Server) return;
            if (slot?.Itemstack == null) return;

            ItemStack[] contents = GetNonEmptyContents(byEntity.World, slot.Itemstack);
            if (contents != null && contents.Length > 0) return;

            AssetLocation emptyLoc = AssetLocation.CreateOrNull(Attributes?["emptiedBlockCode"]?.AsString()) ?? CodeWithVariant("state", "empty");
            Block? emptyBlock = byEntity.World.GetBlock(emptyLoc);
            if (emptyBlock == null || emptyBlock.BlockId == 0) return;

            float temp = slot.Itemstack.Collectible.GetTemperature(byEntity.World, slot.Itemstack);
            ItemStack newStack = new ItemStack(emptyBlock);
            newStack.Collectible.SetTemperature(byEntity.World, newStack, temp);
            slot.Itemstack = newStack;
            slot.MarkDirty();
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack stack = base.OnPickBlock(world, pos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGriddledContainer bec)
            {
                ItemStack[] contentStacks = bec.GetNonEmptyContentStacks();
                SetContents(bec.RecipeCode, bec.QuantityServings, stack, contentStacks);
                float temp = contentStacks.Length > 0 ? contentStacks[0].Collectible.GetTemperature(world, contentStacks[0]) : 0;
                SetTemperature(world, stack, temp, false);
            }

            return stack;
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return [OnPickBlock(world, pos)];
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (TryTakeGriddledResult(world, byPlayer, blockSel.Position)) return true;

            ItemStack stack = OnPickBlock(world, blockSel.Position);

            if (byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            {
                world.BlockAccessor.SetBlock(0, blockSel.Position);
                world.PlaySoundAt(Sounds.Place, byPlayer, byPlayer);
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        bool TryTakeGriddledResult(IWorldAccessor world, IPlayer byPlayer, BlockPos pos)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGriddledContainer be) return false;
            if (be.RecipeCode == null) return false;

            CookingRecipe? recipe = world.Api.ModLoader
                .GetModSystem<GriddlingRecipeRegistrySystem>()
                .GriddlingRecipes
                .FirstOrDefault(r => r.Code == be.RecipeCode);

            if (recipe?.CooksInto == null) return false;

            ItemStack[] stacks = be.GetNonEmptyContentStacks();
            if (stacks.Length == 0) return false;

            foreach (var stack in stacks)
            {
                if (stack == null) continue;
                ItemStack giveStack = stack.Clone();
                if (!byPlayer.InventoryManager.TryGiveItemstack(giveStack, true))
                {
                    world.SpawnItemEntity(giveStack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            AssetLocation emptyLoc = AssetLocation.CreateOrNull(Attributes?["emptiedBlockCode"]?.AsString()) ?? CodeWithVariant("state", "empty");
            Block? emptyBlock = world.GetBlock(emptyLoc);
            world.BlockAccessor.SetBlock(emptyBlock?.Id ?? 0, pos);
            world.PlaySoundAt(Sounds.Place, byPlayer, byPlayer);

            return true;
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

        public virtual bool ShouldSpawnGSParticles(IWorldAccessor world, ItemStack stack) => world.Rand.NextDouble() < (GetTemperature(world, stack) - 50) / 160 / 8;

        public virtual void DoSpawnGSParticles(IAsyncParticleManager manager, BlockPos pos, Vec3f offset)
        {
            smokeHeld.MinPos = pos.ToVec3d().AddCopy(gsSmokePos).AddCopy(offset);
            manager.Spawn(smokeHeld);
        }
    }
}


