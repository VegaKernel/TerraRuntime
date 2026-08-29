using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileClockHeaderPatcherTests
{
    [Fact]
    public void Patches_only_clock_state_and_keeps_complete_world_loadable()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        byte[] originalHeader = preserved!.Header.ToArray();
        Assert.Equal(
            WorldFileClockHeaderPatchResult.Patched,
            WorldFileClockHeaderPatcher.TryPatch(
                originalHeader,
                source.Header,
                time: 43_210d,
                dayTime: false,
                moonPhase: 6,
                slimeRainTime: -1_234d,
                out byte[] patchedHeader));
        Assert.Equal(originalHeader.Length, patchedHeader.Length);
        Assert.Equal(originalHeader, preserved.Header.ToArray());

        byte[] patchedFile = sourceFile.ToArray();
        int headerStart = source.Envelope.SectionOffsets[0];
        int headerEnd = source.Envelope.SectionOffsets[1];
        Assert.Equal(headerEnd - headerStart, patchedHeader.Length);
        patchedHeader.CopyTo(patchedFile.AsSpan(headerStart, patchedHeader.Length));

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
            patchedFile,
            limits,
            out WorldFileData? loadedWorld);
        Assert.True(diagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);

        Assert.Equal(43_210, loaded.RuntimeMetadata.Time);
        Assert.False(loaded.RuntimeMetadata.DayTime);
        Assert.Equal((byte)6, loaded.RuntimeMetadata.MoonPhase);
        Assert.Equal(-1_234d, loaded.RuntimeMetadata.SlimeRainTime);

        Assert.Equal(source.RuntimeMetadata.GameMode, loaded.RuntimeMetadata.GameMode);
        Assert.Equal(source.RuntimeMetadata.SpawnX, loaded.RuntimeMetadata.SpawnX);
        Assert.Equal(source.RuntimeMetadata.SpawnY, loaded.RuntimeMetadata.SpawnY);
        Assert.Equal(source.RuntimeMetadata.DungeonX, loaded.RuntimeMetadata.DungeonX);
        Assert.Equal(source.RuntimeMetadata.DungeonY, loaded.RuntimeMetadata.DungeonY);
        Assert.Equal(source.RuntimeMetadata.OreTiers, loaded.RuntimeMetadata.OreTiers);
        Assert.Equal(source.Chests, loaded.Chests);
        Assert.Equal(source.Signs, loaded.Signs);
        Assert.Equal(source.Npcs, loaded.Npcs);
        Assert.Equal(source.TileEntities, loaded.TileEntities);
        Assert.Equal(source.PressurePlates, loaded.PressurePlates);
        Assert.Equal(source.TownRooms, loaded.TownRooms);
        Assert.Equal(source.Bestiary, loaded.Bestiary);
        Assert.Equal(source.CreativePowers, loaded.CreativePowers);
    }

    [Theory]
    [InlineData(double.NaN, false, 0, 0d)]
    [InlineData(-1d, false, 0, 0d)]
    [InlineData(1.5d, false, 0, 0d)]
    [InlineData(0d, false, 8, 0d)]
    [InlineData(0d, false, 0, double.PositiveInfinity)]
    public void Rejects_invalid_clock_state_without_returning_partial_header(
        double time,
        bool dayTime,
        byte moonPhase,
        double slimeRainTime)
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));

        Assert.Equal(
            WorldFileClockHeaderPatchResult.InvalidClockState,
            WorldFileClockHeaderPatcher.TryPatch(
                preserved!.Header.Span,
                source.Header,
                time,
                dayTime,
                moonPhase,
                slimeRainTime,
                out byte[] patchedHeader));
        Assert.Empty(patchedHeader);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
