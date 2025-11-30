using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace glassview;

public class GlassViewModSystem : ModSystem
{
    private GlassViewRenderer renderer;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        // Client-side only mod
        return forSide == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        renderer = new GlassViewRenderer(api);
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "glassview");

        api.Logger.Notification("[GlassView] Loaded - glass voxels highlighted when holding chisel");
    }

    public override void Dispose()
    {
        renderer?.Dispose();
        base.Dispose();
    }
}
