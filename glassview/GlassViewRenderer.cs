using System.Collections.Generic;
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
    private readonly ICoreClientAPI capi;
    private MeshRef wireframeMeshRef;
    private readonly Matrixf mvMat = new Matrixf();

    // Bright cyan color for wireframe - fully opaque for visibility
    private readonly Vec4f wireframeColor = new Vec4f(0.2f, 1.0f, 1.0f, 1.0f);

    public double RenderOrder => 0.45; // After terrain, before particles
    public int RenderRange => 24;

    public GlassViewRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        CreateWireframeMesh();
    }

    private void CreateWireframeMesh()
    {
        // Create a wireframe cube using lines
        MeshData wireframe = LineMeshUtil.GetCube(ColorUtil.WhiteArgb);

        // Scale from -1..1 to 0..1 range and translate to unit cube position
        wireframe.Scale(new Vec3f(0, 0, 0), 0.5f, 0.5f, 0.5f);
        wireframe.Translate(0.5f, 0.5f, 0.5f);

        // Set flags
        wireframe.Flags = new int[wireframe.VerticesCount];
        for (int i = 0; i < wireframe.Flags.Length; i++)
        {
            wireframe.Flags[i] = 1 << 8; // Required for wireframe shader
        }

        wireframeMeshRef = capi.Render.UploadMesh(wireframe);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var player = capi.World.Player;
        if (player == null) return;

        var activeSlot = player.InventoryManager?.ActiveHotbarSlot;
        var heldItem = activeSlot?.Itemstack?.Collectible;
        if (heldItem == null) return;

        bool holdingChisel = heldItem.Tool == EnumTool.Chisel;
        if (!holdingChisel) return;

        var blockSel = player.CurrentBlockSelection;
        if (blockSel == null) return;

        // Set up rendering using wireframe shader
        var prog = capi.Render.GetEngineShader(EnumShaderProgram.Wireframe);
        prog.Use();

        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        capi.Render.GLDepthMask(false);
        capi.Render.GLEnableDepthTest();
        capi.Render.LineWidth = 4.0f;

        prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        prog.Uniform("origin", new Vec3f(0, 0, 0));
        prog.Uniform("colorIn", wireframeColor);

        var playerPos = player.Entity.CameraPos;
        RenderGlassHighlights(prog, blockSel, playerPos);

        prog.Stop();
        capi.Render.LineWidth = 1.6f;

        // Restore state
        capi.Render.GLDepthMask(true);
        capi.Render.GlEnableCullFace();
    }

    private void RenderGlassHighlights(IShaderProgram prog, BlockSelection blockSel, Vec3d playerPos)
    {
        // Check if the selected block is a microblock
        var selectedBe = capi.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityMicroBlock;
        if (selectedBe == null) return;

        // Track which positions we've already rendered to avoid duplicates
        var renderedPositions = new HashSet<BlockPos>();

        // Render the selected block and all neighboring microblocks
        RenderGlassWireframes(prog, blockSel.Position, playerPos, renderedPositions);

        // Check all 6 neighboring positions for microblocks with glass
        foreach (var facing in BlockFacing.ALLFACES)
        {
            var neighborPos = blockSel.Position.AddCopy(facing);
            RenderGlassWireframes(prog, neighborPos, playerPos, renderedPositions);
        }
    }

    private void RenderGlassWireframes(IShaderProgram prog, BlockPos pos, Vec3d playerPos, HashSet<BlockPos> renderedPositions)
    {
        // Skip if we've already rendered this position
        if (renderedPositions.Contains(pos)) return;
        renderedPositions.Add(pos);

        // Check if it's a chisel block entity
        var be = capi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityMicroBlock;
        if (be?.BlockIds == null || be.VoxelCuboids == null) return;

        // Find which material indices are glass (transparent render pass)
        var glassMaterialIndices = new HashSet<int>();
        for (int i = 0; i < be.BlockIds.Length; i++)
        {
            var block = capi.World.GetBlock(be.BlockIds[i]);
            if (block != null && block.RenderPass == EnumChunkRenderPass.Transparent)
            {
                glassMaterialIndices.Add(i);
            }
        }

        if (glassMaterialIndices.Count == 0) return;

        // Render each glass voxel cuboid
        foreach (var voxelCuboid in be.VoxelCuboids)
        {
            // Decode the voxel cuboid
            int x1, y1, z1, x2, y2, z2, material;
            BlockEntityMicroBlock.FromUint(voxelCuboid, out x1, out y1, out z1, out x2, out y2, out z2, out material);

            // Skip if not a glass material
            if (!glassMaterialIndices.Contains(material)) continue;

            // Calculate world position and scale for this voxel cuboid
            float voxelScale = 1f / 16f;
            float posX = pos.X + x1 * voxelScale;
            float posY = pos.Y + y1 * voxelScale;
            float posZ = pos.Z + z1 * voxelScale;
            float scaleX = (x2 - x1) * voxelScale;
            float scaleY = (y2 - y1) * voxelScale;
            float scaleZ = (z2 - z1) * voxelScale;

            // Build modelview matrix - start with camera, then translate and scale
            mvMat.Identity();
            mvMat.Set(capi.Render.CameraMatrixOriginf);
            mvMat.Translate((float)(posX - playerPos.X), (float)(posY - playerPos.Y), (float)(posZ - playerPos.Z));
            mvMat.Scale(scaleX, scaleY, scaleZ);

            prog.UniformMatrix("modelViewMatrix", mvMat.Values);
            capi.Render.RenderMesh(wireframeMeshRef);
        }
    }

    public void Dispose()
    {
        wireframeMeshRef?.Dispose();
    }
}
