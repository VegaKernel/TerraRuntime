using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldMutationPlan
    {
        private bool CaptureSourceNpcWet(float x, float y, int width, int height)
        {
            if (!IsCurrentOwned) { Fail(); return false; }
            WorldTile Read(int tx, int ty) => Overlay.TryRead(tx, ty, out var tile) ? tile : default;
            bool wet = VanillaWorldCollision.TryGetWetContact(Overlay.Tiles, x, y, width, height, Read, out _);
            if (!Overlay.IsCurrentOwned) Fail();
            return wet;
        }
    }
}
