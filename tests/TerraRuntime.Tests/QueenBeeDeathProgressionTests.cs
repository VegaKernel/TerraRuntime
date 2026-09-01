using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class QueenBeeDeathProgressionTests
{
    [Fact]
    public void Progression_header_patcher_sets_downed_queen_bee_and_keeps_world_loadable()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.DownedQueenBee);
        Assert.True(WorldFilePreservedSections.TryCapture(sourceFile, source.Envelope, out WorldFilePreservedSections? preserved));
        var mutations = new RuntimeWorldProgressionMutations();
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.QueenBee));
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();
        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(originalHeader, source.Header, in snapshot, out byte[] patchedHeader));
        Assert.Equal(1, originalHeader.Zip(patchedHeader).Count(pair => pair.First != pair.Second));
        byte[] patchedFile = sourceFile.ToArray();
        patchedHeader.CopyTo(patchedFile.AsSpan(source.Envelope.SectionOffsets[0], patchedHeader.Length));
        Assert.True(WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? loadedWorld).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
        Assert.True(loaded.RuntimeMetadata.DownedQueenBee);
        Assert.Equal(source.RuntimeMetadata.DownedBoss3, loaded.RuntimeMetadata.DownedBoss3);
        Assert.Equal(source.RuntimeMetadata.DownedMechBoss1, loaded.RuntimeMetadata.DownedMechBoss1);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
