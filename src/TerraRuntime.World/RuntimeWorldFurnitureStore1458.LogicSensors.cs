namespace TerraRuntime.World;

public sealed partial class RuntimeWorldFurnitureStore1458
{
    public bool TrySetLogicSensorState(WorldTileEntity expected, in WorldTile before, bool on)
    {
        if ((uint)expected.X >= (uint)Tiles.Dimensions.WidthTiles || (uint)expected.Y >= (uint)Tiles.Dimensions.HeightTiles ||
            expected.Kind != WorldTileEntityKind.LogicSensor || expected.Payload is not WorldLogicSensorPayload payload ||
            !before.IsActive || before.Type != 423 || !Tiles.Get(expected.X, expected.Y).Equals(before) ||
            !TryPrepare(1, out var preparation) || preparation is null ||
            !preparation.TryGetAt(expected.X, expected.Y, out var current) || current != expected ||
            !preparation.TryStageLogicSensorState(expected.X, expected.Y, payload, on) || !preparation.TrySeal()) return false;
        var after = before; after.FrameX = on ? (short)18 : (short)0;
        Span<WorldSectionId> sections = [TerrariaSectionGeometry.FromTile(Tiles.Dimensions, expected.X, expected.Y)];
        Tiles.BeginMetadataMutation(sections);
        try
        {
            Tiles.SetWithinMetadataMutation(expected.X, expected.Y, in after);
            if (!preparation.TryAdoptUnpublished()) throw new InvalidOperationException("Prepared sensor state lost metadata adoption.");
        }
        finally { Tiles.EndMetadataMutation(sections); }
        return true;
    }

}
