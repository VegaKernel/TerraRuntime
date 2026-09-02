using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DeerclopsDeathProgressionTests
{
    [Fact]
    public void Progression_header_patcher_sets_downed_deerclops_and_preserves_adjacent_late_flags()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.DownedDeerclops);
        Assert.True(WorldFilePreservedSections.TryCapture(sourceFile, source.Envelope, out WorldFilePreservedSections? preserved));

        var mutations = new RuntimeWorldProgressionMutations();
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.Deerclops));
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();

        Assert.Equal(
            WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(
                originalHeader,
                source.Header,
                in snapshot,
                out byte[] patchedHeader));
        Assert.Equal(1, originalHeader.Zip(patchedHeader).Count(static pair => pair.First != pair.Second));

        byte[] patchedFile = sourceFile.ToArray();
        patchedHeader.CopyTo(patchedFile.AsSpan(source.Envelope.SectionOffsets[0], patchedHeader.Length));
        Assert.True(WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? loadedWorld).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);

        Assert.True(loaded.RuntimeMetadata.DownedDeerclops);
        Assert.Equal(source.RuntimeMetadata.DownedEmpressOfLight, loaded.RuntimeMetadata.DownedEmpressOfLight);
        Assert.Equal(source.RuntimeMetadata.DownedQueenSlime, loaded.RuntimeMetadata.DownedQueenSlime);
        Assert.Equal(source.RuntimeMetadata.UnlockedSlimeBlueSpawn, loaded.RuntimeMetadata.UnlockedSlimeBlueSpawn);
        Assert.Equal(source.RuntimeMetadata.UnlockedTruffleSpawn, loaded.RuntimeMetadata.UnlockedTruffleSpawn);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
