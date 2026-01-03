using System;
using System.Linq;
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
        MultiTextureMeshRef griddleClosedRef;
        MultiTextureMeshRef griddleOpenEmptyRef;
        MultiTextureMeshRef griddleOpenRawRef;
        MultiTextureMeshRef griddleOpenCookedRef;
        BlockPos pos;
        float temp;
        bool hasIngredients;
        bool lastHasIngredients;
        bool hasRecipeMatch;
        bool hasAnyRecipe;
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
            hasCookedOutputCached = false;
            useOpenFullShape = false;
            useOpenEmptyShape = true;

            InFirePitProps renderProps = stack.ItemAttributes?["inFirePitProps"].AsObject<InFirePitProps>();
            if (renderProps == null)
            {
                renderProps = stack.Collectible.Attributes?["inFirePitProps"].AsObject<InFirePitProps>();
            }
            renderTransform = renderProps?.Transform ?? new ModelTransform();
            renderTransform.EnsureDefaultValues();

            openEmptyShapeLoc = ResolveShape(stack, "openEmptyShape");
            openRawShapeLoc = ResolveShape(stack, "openRawShape");
            openCookedShapeLoc = ResolveShape(stack, "openCookedShape");
            closedShapeLoc = ResolveShape(stack, "closedShape") ?? AssetLocation.Create("makingwaffles:block/griddle-waffle-closed", stack.Collectible.Code.Domain).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");

            if (stack?.Collectible is Block block)
            {
                RebuildMeshes(block, stack);
            }
        }

        public void Dispose()
        {
            griddleClosedRef?.Dispose();
            griddleOpenEmptyRef?.Dispose();
            griddleOpenRawRef?.Dispose();
            griddleOpenCookedRef?.Dispose();

            cookingSound?.Stop();
            cookingSound?.Dispose();
            startSound?.Stop();
            startSound?.Dispose();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (griddleClosedRef == null && griddleOpenEmptyRef == null && griddleOpenRawRef == null && griddleOpenCookedRef == null) return;

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
                prog.RgbaTint = new Vec4f(1f, 0.85f - 0.25f * glowIntensity, 0.85f - 0.35f * glowIntensity, 1f);
            }
            else
            {
                prog.RgbaTint = ColorUtil.WhiteArgbVec;
            }
            MultiTextureMeshRef activeRef = griddleClosedRef;
            if (useOpenFullShape)
            {
                // prioritize cooked vs raw
                if (hasCookedOutput && griddleOpenCookedRef != null)
                {
                    activeRef = griddleOpenCookedRef;
                }
                else if (griddleOpenRawRef != null)
                {
                    activeRef = griddleOpenRawRef;
                }
                else if (griddleOpenEmptyRef != null)
                {
                    activeRef = griddleOpenEmptyRef;
                }
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
            hasAnyRecipe = false;
            hasCookedOutput = false;
            if (capi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFirepit be && be.Inventory is InventorySmelting inv)
            {
                ItemStack? outputStack = inv[2]?.Itemstack;
                if (outputStack?.Collectible == null && outputStack != null)
                {
                    outputStack.ResolveBlockOrItem(capi.World);
                }

                ItemStack? inputStack = inv[1]?.Itemstack;
                if (inputStack?.Collectible == null && inputStack != null)
                {
                    inputStack.ResolveBlockOrItem(capi.World);
                }

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
                    hasAnyRecipe = recipe != null;
                    hasRecipeMatch = recipe != null && servings > 0 && servings <= griddle.MaxServingSize;
                    hasCookedOutput |= !hasIngredients && recipe == null && stacks.Any(s => s != null);

                    // If waffles already exist in the firepit output, treat as cooked output for mesh choice
                    ItemStack? firepitOutput = inv[2]?.Itemstack;
                    if (firepitOutput != null && firepitOutput.StackSize > 0)
                    {
                        hasCookedOutput = true;
                    }
                }
                else
                {
                    hasCookedOutput |= hasCookedOutputCached;
                }

                // Look for cooked contents in output
                if (outputStack?.Collectible is BlockGriddlingContainer griddleOut)
                {
                    ItemStack[] baked = griddleOut.GetNonEmptyContents(capi.World, outputStack, false);
                    hasCookedOutput |= baked.Length > 0;
                }
            }

            if (lastHasIngredients && !hasIngredients)
            {
                playedStartSound = false;
            }
            lastHasIngredients = hasIngredients;

            glowIntensity = (!isInOutputSlot && hasIngredients && hasRecipeMatch) ? GameMath.Clamp((temp - 80) / 110, 0, 1) : 0;
            bool isCooking = hasRecipeMatch && glowIntensity > 0f;
            // Show raw/open-full when we have any griddleable recipe (even over/under quantity), or cooked output.
            useOpenFullShape = !isCooking && (hasCookedOutput || hasAnyRecipe);
            // Show empty when nothing griddleable is present.
            useOpenEmptyShape = !isCooking && !hasCookedOutput && !hasAnyRecipe;

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

        void UpdateMeshesFromFirepit()
        {
            if (capi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit be || be.Inventory is not InventorySmelting inv)
            {
                return;
            }

            ItemSlot slot = inv[2];
            if (slot?.Itemstack?.Collectible == null && slot?.Itemstack != null)
            {
                slot.Itemstack.ResolveBlockOrItem(capi.World);
            }
            if (slot?.Itemstack?.Collectible is not BlockGriddlingContainer)
            {
                slot = inv[1];
                if (slot?.Itemstack?.Collectible == null && slot?.Itemstack != null)
                {
                    slot.Itemstack.ResolveBlockOrItem(capi.World);
                }
                if (slot?.Itemstack?.Collectible is not BlockGriddlingContainer)
                {
                    return;
                }
            }
            Block? slotBlock = slot?.Itemstack?.Block ?? slot?.Itemstack?.Collectible as Block;
            ItemStack slotStack = slot.Itemstack;
            if (slotBlock == null || slotBlock.Id == 0) return;
            hasCookedOutputCached = false;

            string nextKey = slotStack?.Collectible?.Code?.ToString() ?? string.Empty;
            if (slotBlock.Id == griddleBlockId && nextKey == griddleCodeKey && griddleClosedRef != null) return;

            RebuildMeshes(slotBlock, slotStack);
        }

        void RebuildMeshes(Block block, ItemStack stack)
        {
            griddleClosedRef?.Dispose();
            griddleOpenEmptyRef?.Dispose();
            griddleOpenRawRef?.Dispose();
            griddleOpenCookedRef?.Dispose();

            griddleBlock = block;
            griddleBlockId = block.Id;
            griddleCodeKey = stack?.Collectible?.Code?.ToString() ?? string.Empty;

            TesselateShapeOrBlock(block, closedShapeLoc, out griddleClosedRef);
            TesselateShapeOrBlock(block, openEmptyShapeLoc, out griddleOpenEmptyRef);
            TesselateShapeOrBlock(block, openRawShapeLoc, out griddleOpenRawRef);
            TesselateShapeOrBlock(block, openCookedShapeLoc, out griddleOpenCookedRef);
        }

        AssetLocation ResolveShape(ItemStack stack, string attrName)
        {
            string? code = stack.ItemAttributes?["inFirePitProps"]?[attrName].AsString();
            if (code == null)
            {
                code = stack.Collectible.Attributes?["inFirePitProps"]?[attrName].AsString();
            }
            return code == null ? null : AssetLocation.Create(code, stack.Collectible.Code.Domain).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
        }

        void TesselateShapeOrBlock(Block block, AssetLocation shapeLoc, out MultiTextureMeshRef meshRef)
        {
            meshRef = null;
            if (shapeLoc == null)
            {
                capi.Tesselator.TesselateBlock(block, out MeshData mesh);
                meshRef = capi.Render.UploadMultiTextureMesh(mesh);
                return;
            }

            Shape shape = Shape.TryGet(capi, shapeLoc);
            if (shape == null) return;

            capi.Tesselator.TesselateShape(block, shape, out MeshData customMesh);
            meshRef = capi.Render.UploadMultiTextureMesh(customMesh);
        }

        AssetLocation openEmptyShapeLoc;
        AssetLocation openRawShapeLoc;
        AssetLocation openCookedShapeLoc;
        AssetLocation closedShapeLoc;
    }
}
