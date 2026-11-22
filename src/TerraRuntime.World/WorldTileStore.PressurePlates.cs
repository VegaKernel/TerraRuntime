namespace TerraRuntime.World;

public sealed partial class WorldTileStore
{
    public WorldWeightedPressurePlates1458? RuntimePressurePlates { get; private set; }

    public bool TryAttachPressurePlates(ReadOnlySpan<WorldPressurePlate> source, int maximum, bool fromWorldFile = false)
    {
        if (RuntimePressurePlates is not null || maximum < 0 || source.Length > maximum ||
            Dimensions.WidthTiles > short.MaxValue || Dimensions.HeightTiles > short.MaxValue) return false;
        var unique = new HashSet<(int, int)>();
        foreach (var point in source)
            if ((uint)point.X >= (uint)Dimensions.WidthTiles || (uint)point.Y >= (uint)Dimensions.HeightTiles ||
                !unique.Add((point.X, point.Y))) return false;
        RuntimePressurePlates = new(this, source, fromWorldFile);
        return true;
    }
}
