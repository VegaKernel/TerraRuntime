using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EaterOfWorldsDeathProgressionTests
{
    [Fact]
    public void Progression_header_patcher_sets_downed_boss2_and_keeps_world_loadable()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.DownedBoss2);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var mutations = new RuntimeWorldProgressionMutations();
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.EvilBoss));
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();

        Assert.Equal(
            WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(
                originalHeader,
                source.Header,
                in snapshot,
                out byte[] patchedHeader));
        Assert.Equal(1, originalHeader.Zip(patchedHeader).Count(pair => pair.First != pair.Second));

        byte[] patchedFile = sourceFile.ToArray();
        int headerStart = source.Envelope.SectionOffsets[0];
        patchedHeader.CopyTo(patchedFile.AsSpan(headerStart, patchedHeader.Length));

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? loadedWorld);
        Assert.True(diagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
        Assert.True(loaded.RuntimeMetadata.DownedBoss2);
        Assert.Equal(source.RuntimeMetadata.DownedBoss1, loaded.RuntimeMetadata.DownedBoss1);
        Assert.Equal(source.RuntimeMetadata.DownedBoss3, loaded.RuntimeMetadata.DownedBoss3);
        Assert.Equal(source.RuntimeMetadata.DownedSlimeKing, loaded.RuntimeMetadata.DownedSlimeKing);
        Assert.Equal(source.Chests, loaded.Chests);
        Assert.Equal(source.Signs, loaded.Signs);
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
