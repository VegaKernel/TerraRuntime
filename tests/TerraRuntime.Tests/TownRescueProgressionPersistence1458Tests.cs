using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TownRescueProgressionPersistence1458Tests
{
    [Fact]
    public void All_rescue_saved_flags_patch_losslessly_and_reload_from_wld_header()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.SavedGoblin);
        Assert.False(source.RuntimeMetadata.SavedWizard);
        Assert.False(source.RuntimeMetadata.SavedMechanic);
        Assert.False(source.RuntimeMetadata.SavedAngler);
        Assert.False(source.RuntimeMetadata.SavedStylist);
        Assert.False(source.RuntimeMetadata.SavedTaxCollector);
        Assert.False(source.RuntimeMetadata.SavedGolfer);
        Assert.False(source.RuntimeMetadata.SavedBartender);
        Assert.True(WorldFilePreservedSections.TryCapture(sourceFile, source.Envelope, out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var mutations = new RuntimeWorldProgressionMutations();
        foreach (RuntimeTownRescueFacts1458 fact in new[]
        {
            RuntimeTownRescueFacts1458.Goblin,
            RuntimeTownRescueFacts1458.Wizard,
            RuntimeTownRescueFacts1458.Mechanic,
            RuntimeTownRescueFacts1458.Stylist,
            RuntimeTownRescueFacts1458.Angler,
            RuntimeTownRescueFacts1458.Bartender,
            RuntimeTownRescueFacts1458.Golfer,
            RuntimeTownRescueFacts1458.TaxCollector
        })
        {
            Assert.True(mutations.MarkTownNpcRescued(fact));
            Assert.False(mutations.MarkTownNpcRescued(fact));
        }

        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();
        Assert.Equal(RuntimeTownRescueFacts1458.All, snapshot.RescuedTownNpcs);
        Assert.Equal(
            WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(originalHeader, source.Header, in snapshot, out byte[] patchedHeader));
        Assert.Equal(8, originalHeader.Zip(patchedHeader).Count(static pair => pair.First != pair.Second));

        byte[] patchedFile = sourceFile.ToArray();
        int headerStart = source.Envelope.SectionOffsets[0];
        patchedHeader.CopyTo(patchedFile.AsSpan(headerStart, patchedHeader.Length));
        Assert.True(WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? loadedWorld).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
        Assert.True(loaded.RuntimeMetadata.SavedGoblin);
        Assert.True(loaded.RuntimeMetadata.SavedWizard);
        Assert.True(loaded.RuntimeMetadata.SavedMechanic);
        Assert.True(loaded.RuntimeMetadata.SavedAngler);
        Assert.True(loaded.RuntimeMetadata.SavedStylist);
        Assert.True(loaded.RuntimeMetadata.SavedTaxCollector);
        Assert.True(loaded.RuntimeMetadata.SavedGolfer);
        Assert.True(loaded.RuntimeMetadata.SavedBartender);
        Assert.Equal(source.RuntimeMetadata.Time, loaded.RuntimeMetadata.Time);
        Assert.Equal(source.RuntimeMetadata.UnlockedSlimeBlueSpawn, loaded.RuntimeMetadata.UnlockedSlimeBlueSpawn);
        Assert.Equal(source.RuntimeMetadata.UnlockedTruffleSpawn, loaded.RuntimeMetadata.UnlockedTruffleSpawn);
        Assert.Equal(source.Chests, loaded.Chests);
        Assert.Equal(source.Signs, loaded.Signs);
    }

    [Fact]
    public void Persisted_rescue_baseline_is_not_reported_as_new_save_mutation()
    {
        var mutations = new RuntimeWorldProgressionMutations();
        mutations.SetTownRescueBaseline(RuntimeTownRescueFacts1458.Goblin | RuntimeTownRescueFacts1458.Golfer);
        Assert.False(mutations.MarkTownNpcRescued(RuntimeTownRescueFacts1458.Goblin));
        Assert.False(mutations.MarkTownNpcRescued(RuntimeTownRescueFacts1458.Golfer));
        Assert.Equal(RuntimeTownRescueFacts1458.None, mutations.CaptureSnapshot().RescuedTownNpcs);
        Assert.False(mutations.CaptureSnapshot().HasAny);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
