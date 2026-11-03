namespace TerraRuntime.WorldGeneration.Runtime;

public sealed partial class Workspace
{
    internal int VanillaSurfaceJungleMinX { get; private set; }
    internal int VanillaSurfaceJungleMaxX { get; private set; }
    internal void SetVanillaSurfaceJungleBounds(int minimum, int maximum)
    {
        VanillaSurfaceJungleMinX = minimum;
        VanillaSurfaceJungleMaxX = maximum;
    }
}
