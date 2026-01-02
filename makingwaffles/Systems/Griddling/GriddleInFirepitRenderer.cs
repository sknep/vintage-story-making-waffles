using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace MakingWaffles.Systems.Griddling
{
    public class GriddleInFirepitRenderer : IInFirepitRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 20;

        ICoreClientAPI capi;
        MultiTextureMeshRef griddleRef;
        MultiTextureMeshRef griddleOpenEmptyRef;
        MultiTextureMeshRef griddleOpenFullRef;
        BlockPos pos;
        float temp;
        bool hasIngredients;
        bool lastHasIngredients;
        bool hasRecipeMatch;
        bool hasCookedOutput;
        bool useOpenFullShape;
        bool useOpenEmptyShape;
        float glowIntensity;

        ILoadedSound cookingSound;
        ILoadedSound startSound;
        bool playedStartSound;
        float lastVolume;

        bool isInOutputSlot;
        Matrixf ModelMat = new Matrixf();
        ModelTransform renderTransform;
        Block griddleBlock;
        int griddleBlockId;
        string griddleCodeKey;
        bool hasCookedOutputCached;

        public GriddleInFirepitRenderer(ICoreClientAPI capi, ItemStack stack, BlockPos pos, bool isInOutputSlot)
        {
            this.capi = capi;
            this.pos = pos;
            this.isInOutputSlot = isInOutputSlot;
            bool stackIsCooked = stack?.Collectible is BlockGriddledContainer;
            hasCookedOutputCached = stackIsCooked || isInOutputSlot;
            useOpenFullShape = stackIsCooked;
            useOpenEmptyShape = !stackIsCooked;

            if (stack?.Collectible is Block block)
            {
                RebuildMeshes(block, stack);
            }

            InFirePitProps renderProps = stack.ItemAttributes?["inFirePitProps"].AsObject<InFirePitProps>();
            if (renderProps == null)
            {
                renderProps = stack.Collectible.Attributes?["inFirePitProps"].AsObject<InFirePitProps>();
            }
            renderTransform = renderProps?.Transform ?? new ModelTransform();
            renderTransform.EnsureDefaultValues();

            string? openEmptyCode = stack.ItemAttributes?["inFirePitProps"]?["openEmptyShape"].AsString();
            if (openEmptyCode == null)
            {
                openEmptyCode = stack.Collectible.Attributes?["inFirePitProps"]?["openEmptyShape"].AsString();
            }

            if (openEmptyCode != null)
            {
                openEmptyShapeLoc = AssetLocation.Create(openEmptyCode, stack.Collectible.Code.Domain)
                    .WithPathPrefixOnce("shapes/")
                    .WithPathAppendixOnce(".json");
            }

            string? openFullCode = stack.ItemAttributes?["inFirePitProps"]?["openFullShape"].AsString();
            if (openFullCode == null)
            {
                openFullCode = stack.Collectible.Attributes?["inFirePitProps"]?["openFullShape"].AsString();
            }

            if (openFullCode != null)
            {
                openFullShapeLoc = AssetLocation.Create(openFullCode, stack.Collectible.Code.Domain)
                    .WithPathPrefixOnce("shapes/")
                    .WithPathAppendixOnce(".json");
            }
        }

        public void Dispose()
        {
            griddleRef?.Dispose();
            griddleOpenEmptyRef?.Dispose();
            griddleOpenFullRef?.Dispose();

            cookingSound?.Stop();
            cookingSound?.Dispose();
            startSound?.Stop();
            startSound?.Dispose();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (griddleRef == null) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);

            prog.DontWarpVertices = 0;
            prog.AddRenderFlags = 0;
            prog.RgbaAmbientIn = rpi.AmbientColor;
            prog.RgbaFogIn = rpi.FogColor;
            prog.FogMinIn = rpi.FogMin;
            prog.FogDensityIn = rpi.FogDensity;
            prog.RgbaTint = ColorUtil.WhiteArgbVec;
            prog.NormalShaded = 1;
            prog.ExtraGodray = 0;
            prog.SsaoAttn = 0;
            prog.AlphaTest = 0.05f;
            prog.OverlayOpacity = 0;

            prog.ModelMatrix = ModelMat
                .Identity()
                .Translate(pos.X - camPos.X + renderTransform.Translation.X, pos.Y - camPos.Y + renderTransform.Translation.Y, pos.Z - camPos.Z + renderTransform.Translation.Z)
                .Translate(renderTransform.Origin.X, renderTransform.Origin.Y, renderTransform.Origin.Z)
                .RotateX(renderTransform.Rotation.X * GameMath.DEG2RAD)
                .RotateY(renderTransform.Rotation.Y * GameMath.DEG2RAD)
                .RotateZ(renderTransform.Rotation.Z * GameMath.DEG2RAD)
                .Scale(renderTransform.ScaleXYZ.X, renderTransform.ScaleXYZ.Y, renderTransform.ScaleXYZ.Z)
                .Translate(-renderTransform.Origin.X, -renderTransform.Origin.Y, -renderTransform.Origin.Z)
                .Values
            ;

            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            prog.ExtraGlow = (int)(glowIntensity * 60);
            if (glowIntensity > 0f)
            {
                // red, green, blue, alpha adjusted by glow intensity as a factor
                prog.RgbaTint = new Vec4f(1f, 0.85f - 0.25f * glowIntensity, 0.85f - 0.35f * glowIntensity, 1f);
            }
            else
            {
                prog.RgbaTint = ColorUtil.WhiteArgbVec;
            }
            MultiTextureMeshRef activeRef = griddleRef;
            if (useOpenFullShape && griddleOpenFullRef != null)
            {
                activeRef = griddleOpenFullRef;
            }
            else if (useOpenEmptyShape && griddleOpenEmptyRef != null)
            {
                activeRef = griddleOpenEmptyRef;
            }
            rpi.RenderMultiTextureMesh(activeRef, "tex");

            prog.ExtraGlow = 0;
            prog.RgbaTint = ColorUtil.WhiteArgbVec;

            prog.Stop();
        }

        public void OnUpdate(float temperature)
        {
            temp = temperature;
            UpdateMeshesFromFirepit();

            hasIngredients = false;
            hasRecipeMatch = false;
            if (capi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFirepit be && be.Inventory is InventorySmelting inv)
            {
                bool cookedInOutput = inv[2]?.Itemstack?.Collectible is BlockGriddledContainer;
                bool emptyInput = inv[1]?.Itemstack?.Collectible is BlockGriddlingContainer;
                if (cookedInOutput)
                {
                    hasCookedOutputCached = true;
                }
                else if (emptyInput)
                {
                    hasCookedOutputCached = false;
                }
                hasCookedOutput = hasCookedOutputCached;
                ItemSlot[] cookingSlots = inv.CookingSlots;
                for (int i = 0; i < cookingSlots.Length; i++)
                {
                    if (!cookingSlots[i].Empty)
                    {
                        hasIngredients = true;
                        break;
                    }
                }

                if (!isInOutputSlot && inv[1]?.Itemstack?.Collectible is BlockGriddlingContainer griddle)
                {
                    ItemStack[] stacks = griddle.GetCookingStacks(inv, false);
                    CookingRecipe? recipe = griddle.GetMatchingCookingRecipe(capi.World, stacks, out int servings);
                    hasRecipeMatch = recipe != null && servings > 0 && servings <= griddle.MaxServingSize;
                }
            }

            if (lastHasIngredients && !hasIngredients)
            {
                playedStartSound = false;
            }
            lastHasIngredients = hasIngredients;

            glowIntensity = (!isInOutputSlot && hasIngredients) ? GameMath.Clamp((temp - 80) / 110, 0, 1) : 0;
            bool isCooking = glowIntensity > 0f;
            useOpenFullShape = hasCookedOutput || (!isCooking && hasRecipeMatch);
            useOpenEmptyShape = !hasCookedOutput && !isCooking && !hasRecipeMatch;

            float soundIntensity = glowIntensity;
            SetCookingSoundVolume(isInOutputSlot ? 0 : soundIntensity);
        }

        public void OnCookingComplete()
        {
            isInOutputSlot = true;
            hasCookedOutputCached = true;
            useOpenFullShape = true;
            useOpenEmptyShape = false;
            SetCookingSoundVolume(0);
        }

        public void SetCookingSoundVolume(float volume)
        {
            if (isInOutputSlot)
            {
                volume = 0;
            }

            if (volume > 0)
            {
                if (hasIngredients && lastVolume <= 0 && !playedStartSound)
                {
                    startSound = capi.World.LoadSound(new SoundParams()
                    {
                        Location = new AssetLocation("sounds/sizzle.ogg"),
                        ShouldLoop = false,
                        DisposeOnFinish = true,
                        Position = pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                        Range = 6f,
                        ReferenceDistance = 3f,
                        Pitch = 0.9f,
                        Volume = Math.Min(1f, volume)
                    });
                    startSound?.Start();
                    playedStartSound = true;
                }

                if (cookingSound == null)
                {
                    cookingSound = capi.World.LoadSound(new SoundParams()
                    {
                        Location = new AssetLocation("sounds/moltenmetal.ogg"),
                        ShouldLoop = true,
                        Position = pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                        DisposeOnFinish = false,
                        Range = 5f,
                        ReferenceDistance = 3f,
                        Pitch = 0.6f,
                        Volume = volume * 0.4f
                    });
                    cookingSound.Start();
                }
                else
                {
                    cookingSound.SetVolume(volume);
                }
            }
            else
            {
                if (cookingSound != null)
                {
                    cookingSound.Stop();
                    cookingSound.Dispose();
                    cookingSound = null;
                }

                startSound?.Stop();
                startSound?.Dispose();
                startSound = null;
                playedStartSound = false;
            }

            lastVolume = volume;
        }

        AssetLocation openEmptyShapeLoc;
        AssetLocation openFullShapeLoc;

        void UpdateMeshesFromFirepit()
        {
            if (capi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit be || be.Inventory is not InventorySmelting inv)
            {
                return;
            }

            ItemSlot slot = inv[2];
            if (slot?.Itemstack?.Collectible is not BlockGriddledContainer)
            {
                slot = inv[1];
                if (slot?.Itemstack?.Collectible is not BlockGriddlingContainer)
                {
                    return;
                }
            }
            Block? slotBlock = slot?.Itemstack?.Block;
            ItemStack slotStack = slot.Itemstack;
            if (slotBlock == null || slotBlock.Id == 0) return;
            if (slotStack?.Collectible is BlockGriddledContainer)
            {
                hasCookedOutputCached = true;
            }
            else if (slotStack?.Collectible is BlockGriddlingContainer)
            {
                hasCookedOutputCached = false;
            }

            string nextKey = slotStack?.Collectible?.Code?.ToString() ?? string.Empty;
            if (slotBlock.Id == griddleBlockId && nextKey == griddleCodeKey) return;

            RebuildMeshes(slotBlock, slotStack);
        }

        void RebuildMeshes(Block block, ItemStack stack)
        {
            griddleRef?.Dispose();
            griddleOpenEmptyRef?.Dispose();
            griddleOpenFullRef?.Dispose();

            griddleBlock = block;
            griddleBlockId = block.Id;
            griddleCodeKey = stack?.Collectible?.Code?.ToString() ?? string.Empty;

            capi.Tesselator.TesselateBlock(block, out MeshData griddleMesh);
            griddleRef = capi.Render.UploadMultiTextureMesh(griddleMesh);

            string? openEmptyCode = stack?.ItemAttributes?["inFirePitProps"]?["openEmptyShape"].AsString();
            if (openEmptyCode == null)
            {
                openEmptyCode = block.Attributes?["inFirePitProps"]?["openEmptyShape"].AsString();
            }

            if (openEmptyCode != null)
            {
                openEmptyShapeLoc = AssetLocation.Create(openEmptyCode, block.Code.Domain)
                    .WithPathPrefixOnce("shapes/")
                    .WithPathAppendixOnce(".json");
            }
            else
            {
                openEmptyShapeLoc = null;
            }

            if (openEmptyShapeLoc != null)
            {
                Shape openShape = Shape.TryGet(capi, openEmptyShapeLoc);
                if (openShape != null)
                {
                    capi.Tesselator.TesselateShape(block, openShape, out MeshData openMesh);
                    griddleOpenEmptyRef = capi.Render.UploadMultiTextureMesh(openMesh);
                }
            }

            string? openFullCode = stack?.ItemAttributes?["inFirePitProps"]?["openFullShape"].AsString();
            if (openFullCode == null)
            {
                openFullCode = block.Attributes?["inFirePitProps"]?["openFullShape"].AsString();
            }

            if (openFullCode != null)
            {
                openFullShapeLoc = AssetLocation.Create(openFullCode, block.Code.Domain)
                    .WithPathPrefixOnce("shapes/")
                    .WithPathAppendixOnce(".json");
            }
            else
            {
                openFullShapeLoc = null;
            }

            if (openFullShapeLoc != null)
            {
                Shape openShape = Shape.TryGet(capi, openFullShapeLoc);
                if (openShape != null)
                {
                    capi.Tesselator.TesselateShape(block, openShape, out MeshData openMesh);
                    griddleOpenFullRef = capi.Render.UploadMultiTextureMesh(openMesh);
                }
            }
        }
    }
}
