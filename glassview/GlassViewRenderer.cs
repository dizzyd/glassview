using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace glassview;

/// <summary>
/// Renders wireframe highlights for glass voxels when holding a chisel.
/// </summary>
public class GlassViewRenderer : IRenderer
{
    private static readonly Vec3f ZeroOrigin = new(0, 0, 0);

    // VS default line width (API doesn't provide a getter to save/restore)
    private const float DefaultLineWidth = 1.6f;
    private const float WireframeLineWidth = 4.0f;

    // Fully qualified names of chisel item types to detect
    private static readonly string[] ChiselTypeNames =
    {
        "Vintagestory.GameContent.ItemChisel",
        "chisel.src.ItemHandPlaner",
        "chisel.src.ItemLadderMaker",
        "chisel.src.ItemPantograph",
        "chisel.src.ItemPathMaker",
        "chisel.src.ItemWedge",
        "chiseltools.src.TrueChisel"
    };

    private static HashSet<Type> _chiselItemTypes;

    public static void DiscoverChiselTypes(ICoreClientAPI api)
    {
        _chiselItemTypes = new HashSet<Type>();

        foreach (var name in ChiselTypeNames)
        {
            var type = AccessTools.TypeByName(name);
            if (type != null)
            {
                _chiselItemTypes.Add(type);
                api.Logger.Notification("[GlassView] Loaded chisel type: {0}", name);
            }
        }
    }

    private static bool IsChiselItem(CollectibleObject item)
    {
        return _chiselItemTypes.Contains(item.GetType());
    }

    private readonly ICoreClientAPI capi;
    private readonly Matrixf mvMat = new();
    private readonly BlockPos tempPos = new(0);
    private readonly List<int> glassMaterialIndices = new();
    private readonly Vec4f wireframeColor = new(0.2f, 1.0f, 1.0f, 1.0f);

    private MeshRef wireframeMeshRef;

    // Render with debug wireframes (0.5), after terrain (0.37) and entities (0.4)
    public double RenderOrder => 0.5;
    public int RenderRange => 8;

    public GlassViewRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        CreateWireframeMesh();
    }

    private void CreateWireframeMesh()
    {
        var wireframe = LineMeshUtil.GetCube(ColorUtil.WhiteArgb);
        wireframe.Scale(new Vec3f(0, 0, 0), 0.5f, 0.5f, 0.5f);
        wireframe.Translate(0.5f, 0.5f, 0.5f);

        wireframe.Flags = new int[wireframe.VerticesCount];
        for (int i = 0; i < wireframe.Flags.Length; i++)
        {
            wireframe.Flags[i] = 1 << 8;
        }

        wireframeMeshRef = capi.Render.UploadMesh(wireframe);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var player = capi.World.Player;
        if (player == null) return;

        var heldItem = player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible;
        if (heldItem == null || !IsChiselItem(heldItem)) return;

        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null) return;

        var selectedBe = capi.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityMicroBlock;
        if (selectedBe == null) return;

        // Skip if block is too far away
        var playerPos = player.Entity.CameraPos;
        double distSq = blockSel.Position.DistanceSqTo(playerPos.X, playerPos.Y, playerPos.Z);
        if (distSq > RenderRange * RenderRange) return;

        var prog = capi.Render.GetEngineShader(EnumShaderProgram.Wireframe);
        prog.Use();
        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        prog.Uniform("origin", ZeroOrigin);
        prog.Uniform("colorIn", wireframeColor);

        // Modify GL state for wireframe rendering
        capi.Render.GLDepthMask(false);
        capi.Render.LineWidth = WireframeLineWidth;

        RenderGlassWireframes(prog, blockSel.Position, selectedBe, playerPos);

        foreach (var facing in BlockFacing.ALLFACES)
        {
            tempPos.Set(blockSel.Position).Add(facing);
            if (capi.World.BlockAccessor.GetBlockEntity(tempPos) is BlockEntityMicroBlock neighborBe)
            {
                RenderGlassWireframes(prog, tempPos, neighborBe, playerPos);
            }
        }

        prog.Stop();

        // Restore GL state (Opaque stage defaults)
        capi.Render.GLDepthMask(true);
        capi.Render.LineWidth = DefaultLineWidth;
    }

    private void RenderGlassWireframes(IShaderProgram prog, BlockPos pos, BlockEntityMicroBlock be, Vec3d playerPos)
    {
        if (be.BlockIds == null || be.VoxelCuboids == null) return;

        // Find glass material indices
        glassMaterialIndices.Clear();
        for (int i = 0; i < be.BlockIds.Length; i++)
        {
            var block = capi.World.GetBlock(be.BlockIds[i]);
            if (block?.RenderPass == EnumChunkRenderPass.Transparent)
            {
                glassMaterialIndices.Add(i);
            }
        }

        if (glassMaterialIndices.Count == 0) return;

        // Render each glass voxel cuboid
        foreach (var voxelCuboid in be.VoxelCuboids)
        {
            BlockEntityMicroBlock.FromUint(voxelCuboid, out int x1, out int y1, out int z1,
                out int x2, out int y2, out int z2, out int material);

            if (!glassMaterialIndices.Contains(material)) continue;

            const float voxelScale = 1f / 16f;
            float posX = pos.X + x1 * voxelScale;
            float posY = pos.Y + y1 * voxelScale;
            float posZ = pos.Z + z1 * voxelScale;

            mvMat.Identity();
            mvMat.Set(capi.Render.CameraMatrixOriginf);
            mvMat.Translate((float)(posX - playerPos.X), (float)(posY - playerPos.Y), (float)(posZ - playerPos.Z));
            mvMat.Scale((x2 - x1) * voxelScale, (y2 - y1) * voxelScale, (z2 - z1) * voxelScale);

            prog.UniformMatrix("modelViewMatrix", mvMat.Values);
            capi.Render.RenderMesh(wireframeMeshRef);
        }
    }

    public void Dispose()
    {
        wireframeMeshRef?.Dispose();
    }
}
