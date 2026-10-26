using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class AnglerAuthority1458Tests
{
    [Fact]
    public void Original_world_quest_selection_and_whole_rewards_match_inventory_and_next_random()
    {
        using var resource = typeof(AnglerAuthority1458Tests).Assembly.GetManifestResourceStream("AnglerOriginal1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",
            source.RootElement.GetProperty("sourceSha256").GetString());
        int choices = 0;
        foreach (var row in source.RootElement.GetProperty("choices").EnumerateArray())
        {
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var world = new AnglerQuestWorld1458(row.GetProperty("hard").GetBoolean(), row.GetProperty("crimson").GetBoolean(),
                row.GetProperty("boss").GetBoolean(), row.GetProperty("surface").GetBoolean());
            Assert.True(VanillaAnglerQuest1458.TryChoose(new SourceRandom(random), world, out int quest));
            Assert.Equal(row.GetProperty("quest").GetInt32(), quest);
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            choices++;
        }
        Assert.Equal(256, choices);
        int rewards = 0;
        foreach (var row in source.RootElement.GetProperty("rewards").EnumerateArray())
        {
            using var f = new FishingCrateAuthority1458Tests.Fixture();
            var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
            random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
            var context = new AnglerRewardContext1458(row.GetProperty("done").GetInt32(), row.GetProperty("fish").GetInt32(),
                row.GetProperty("hard").GetBoolean(), row.GetProperty("expert").GetBoolean(), row.GetProperty("price").GetSingle(),
                row.GetProperty("owned").EnumerateArray().Select(static value => value.GetInt32()).ToArray());
            Assert.True(f.Players.TryFishingRewards(f.Connection, -1, 0, false, session =>
            {
                foreach (var reward in VanillaAnglerReward1458.Roll(new SourceRandom(session.Random), context))
                    if (!session.Grant(reward.Type, reward.Stack, canGoIntoVoidVault: false)) return false;
                return true;
            }), $"Angler{context.QuestsDone}/{context.QuestFish}/{row.GetProperty("seed")}");
            var expected = new RuntimePlayerInventoryItem[59];
            foreach (var item in row.GetProperty("items").EnumerateArray())
                expected[item[0].GetInt32()] = new(new(item[1].GetInt32()), checked((short)item[2].GetInt32()), new(item[3].GetInt32()), 0);
            Assert.Equal(expected, f.ItemImage());
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            rewards++;
        }
        Assert.Equal(480, rewards);
    }

    [Fact]
    public void Paid_real_fish_completion_rejects_flag_duplicates_and_survives_day_save_reload()
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        var metadata = new WorldFileRuntimeMetadata { WorldSurface = 80, AnglerQuest = new(0, []) };
        f.State.BindAnglerWorld(metadata);
        SetAppearance(f, "Fisher");
        ConfigureAngler(f);
        f.Set(0, 2450, 1);
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone();
        Assert.False(f.State.TryCompleteAnglerQuest(f.Connection)); // packet5 cannot fund fish.
        Assert.True(beforeRandom.HasSameState(random));
        Assert.True(f.State.TryCaptureAnglerState(f.Connection, out _, out bool finished, out int count));
        Assert.False(finished); Assert.Equal(0, count);
        f.Set(0, 0, 0);
        var fish = f.Drop(2450, 1);
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, fish.Handle));
        Assert.True(f.State.TryCompleteAnglerQuest(f.Connection));
        Assert.True(f.State.TryCaptureAnglerState(f.Connection, out byte quest, out finished, out count));
        Assert.Equal(0, quest); Assert.True(finished); Assert.Equal(1, count);
        Assert.DoesNotContain(f.ItemImage(), item => item.ItemType.Value == 2450);
        beforeRandom = random.Clone();
        Assert.False(f.State.TryCompleteAnglerQuest(f.Connection));
        Assert.True(beforeRandom.HasSameState(random));
        SetAppearance(f, "Renamed");
        Assert.True(f.State.TryCaptureAnglerState(f.Connection, out _, out finished, out count));
        Assert.True(finished);
        Assert.False(f.State.TryCompleteAnglerQuest(f.Connection));

        // A replacement runtime loads the canonical per-name daily block. Imported
        // client completion/count packets cannot recreate a payment or reward.
        var save = Assert.IsType<WorldAnglerQuestData1458>(f.State.CaptureAnglerSaveState());
        using var replacement = new FishingCrateAuthority1458Tests.Fixture();
        replacement.State.BindAnglerWorld(new() { WorldSurface = 80, AnglerQuest = save });
        SetAppearance(replacement, "Fisher");
        ConfigureAngler(replacement);
        var extra = replacement.Drop(2450, 1);
        replacement.State.Apply(new WorldItemRemoveRuntimeCommand(replacement.Connection, extra.Handle));
        Assert.False(replacement.State.TryCompleteAnglerQuest(replacement.Connection));
        Assert.True(replacement.State.TryCaptureAnglerState(replacement.Connection, out _, out finished, out count));
        Assert.True(finished); Assert.Equal(0, count); // lifetime player count is a separate existing transfer field.
        Assert.True(replacement.Players.SwapAnglerQuestAtDawn());
        Assert.Empty(replacement.State.CaptureAnglerSaveState()!.CompletedNames);

        // Advance the same real player through the first guaranteed reward.
        // Each day consumes the currently selected real fish, never an imported count.
        for (int completedCount = 2; completedCount <= 5; completedCount++)
        {
            Assert.True(f.Players.SwapAnglerQuestAtDawn());
            Assert.True(f.State.TryCaptureAnglerState(f.Connection, out quest, out finished, out count));
            Assert.False(finished);
            short fishType = checked((short)VanillaAnglerQuest1458.ItemForQuest(quest));
            var nextFish = f.Drop(fishType, 1);
            f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, nextFish.Handle));
            Assert.True(f.State.TryCompleteAnglerQuest(f.Connection));
            Assert.True(f.State.TryCaptureAnglerState(f.Connection, out _, out finished, out count));
            Assert.True(finished); Assert.Equal(completedCount, count);
        }
        Assert.Contains(f.ItemImage(), item => item.ItemType.Value == 2428 && item.Stack == 1);
    }

    [Fact]
    public void Canonical_Angler_header_replacement_retains_following_fields_and_roundtrips_cache()
    {
        MethodInfo fixture = typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", BindingFlags.Static | BindingFlags.NonPublic)!;
        byte[] file = (byte[])fixture.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(file, limits, out var original).IsLoaded);
        Assert.NotNull(original);
        Assert.True(WorldFilePreservedSections.TryCapture(file, original.Envelope, out var preserved));
        Assert.NotNull(preserved);
        var angler = new WorldAnglerQuestData1458(40, ["Fisher", "Рыбак"]);
        Assert.True(WorldFileAnglerHeaderPatcher1458.TryPatch(preserved.Header.Span, original.Header, angler, out var changed));
        var envelope = new WorldFileEnvelope(WorldFileFormatPolicy.CurrentVersion, 0, 0, [0, changed.Length], 0, []);
        var parserLimits = new WorldFileRuntimeMetadataLimits(4096, 65536, 64, 1024, 64, 65536);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(changed, envelope, original.Header, parserLimits, out var metadata, out _));
        Assert.NotNull(metadata);
        Assert.Equal(40, metadata.AnglerQuest.Quest);
        Assert.Equal(angler.CompletedNames, metadata.AnglerQuest.CompletedNames);
        Assert.Equal(original.RuntimeMetadata.SavedAngler, metadata.SavedAngler);
        Assert.Equal(original.RuntimeMetadata.BannerSectionOffset + changed.Length - preserved.Header.Length, metadata.BannerSectionOffset);
        Assert.Equal(original.RuntimeMetadata.InvasionSizeStart, metadata.InvasionSizeStart);
        Assert.Equal(original.RuntimeMetadata.DownedFishron, metadata.DownedFishron);
        var beforeTail = preserved.Header.Span[(original.RuntimeMetadata.AnglerSectionOffset + original.RuntimeMetadata.AnglerSectionLength)..].ToArray();
        Assert.Equal(beforeTail, changed.AsSpan(metadata.AnglerSectionOffset + metadata.AnglerSectionLength).ToArray());

        var synchronizer = new WorldTileSaveShadowSynchronizer(original.Tiles, 1);
        while (!synchronizer.IsBootstrapped) synchronizer.CaptureBootstrap(16);
        Assert.True(synchronizer.TryCaptureImage(out var image));
        using var rewritten = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.Rewritten,
            WorldFileTileChestRewriter.TryRewrite(original.Envelope, original.Header, changed, preserved,
                image!, original.Chests, rewritten, out _));
        Assert.True(WorldFileLoader.TryLoad(rewritten.ToArray(), limits, out var reloaded).IsLoaded);
        Assert.NotNull(reloaded);
        Assert.Equal(40, reloaded.RuntimeMetadata.AnglerQuest.Quest);
        Assert.Equal(angler.CompletedNames, reloaded.RuntimeMetadata.AnglerQuest.CompletedNames);
        Assert.Equal(original.Chests, reloaded.Chests);
        Assert.Equal(original.Signs, reloaded.Signs);

        // A variable length is not authority to copy arbitrary trailing bytes.
        using var rejected = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.InvalidHeaderSection,
            WorldFileTileChestRewriter.TryRewrite(original.Envelope, original.Header, changed[..^1], preserved,
                image!, original.Chests, rejected, out _));
        Assert.Equal(0, rejected.Length);

        var loaded = new WorldFileData(original.Envelope, original.Header, metadata, original.Tiles, original.Chests, original.Signs,
            original.Npcs, original.TileEntities, original.PressurePlates, original.TownRooms, original.Bestiary, original.CreativePowers);
        byte[] payload = RuntimeWorldPreparedStateCodec.Encode(loaded);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(payload, original.Tiles, out var cached));
        Assert.NotNull(cached);
        Assert.Equal(40, cached.RuntimeMetadata.AnglerQuest.Quest);
        Assert.Equal(angler.CompletedNames, cached.RuntimeMetadata.AnglerQuest.CompletedNames);
        Assert.Equal(metadata.AnglerSectionOffset, cached.RuntimeMetadata.AnglerSectionOffset);
    }

    [Fact]
    public void Fished_Red_Slime_unlock_uses_existing_progression_save_owner()
    {
        var journal = new RuntimeWorldProgressionMutations();
        Assert.True(journal.MarkSlimeRedSpawnUnlocked());
        Assert.False(journal.MarkSlimeRedSpawnUnlocked());
        Assert.True(journal.CaptureSnapshot().HasAny);
        Assert.True(journal.CaptureSnapshot().UnlockSlimeRedSpawn);
        var baseline = new RuntimeWorldProgressionMutations();
        baseline.SetSlimeRedSpawnBaseline(true);
        Assert.False(baseline.MarkSlimeRedSpawnUnlocked());
        Assert.False(baseline.CaptureSnapshot().HasAny);

        byte[] file = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(file, limits, out var world).IsLoaded);
        Assert.NotNull(world);
        Assert.True(WorldFilePreservedSections.TryCapture(file, world.Envelope, out var preserved));
        Assert.NotNull(preserved);
        var state = journal.CaptureSnapshot();
        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(preserved.Header.Span, world.Header, state, out var patched));
        Assert.Equal(1, patched.Zip(preserved.Header.ToArray()).Count(pair => pair.First != pair.Second));
        patched.CopyTo(file.AsSpan(world.Envelope.SectionOffsets[0]));
        Assert.True(WorldFileLoader.TryLoad(file, limits, out var loaded).IsLoaded);
        Assert.NotNull(loaded);
        Assert.True(loaded.RuntimeMetadata.UnlockedSlimeRedSpawn);
        Assert.Equal(world.RuntimeMetadata.UnlockedSlimeYellowSpawn, loaded.RuntimeMetadata.UnlockedSlimeYellowSpawn);

        var sourceMetadata = world.RuntimeMetadata;
        var runtime = new WorldInfoRuntimeState(sourceMetadata.Time, sourceMetadata.DayTime, sourceMetadata.MoonPhase,
            sourceMetadata.BloodMoon, sourceMetadata.SlimeRainTime > 0d) { ProgressionMutations = state };
        var initial = WorldInfoPacketMapper.Create(world.Header, world.RuntimeMetadata);
        var published = WorldInfoPacketMapper.Create(world.Header, world.RuntimeMetadata, runtime: runtime);
        // Official NetMessage.SendData7 writes NPC.unlockedSlimeRedSpawn at
        // bitsByte12[4], the EventInfo9 byte. Preserve every neighboring bit.
        Assert.Equal((byte)(initial.EventInfo9 | 16), published.EventInfo9);
        Assert.Equal(initial.EventInfo8, published.EventInfo8);
        Assert.Equal(initial.EventInfo10, published.EventInfo10);
        using var beforeWire = new MemoryStream();
        using var afterWire = new MemoryStream();
        initial.ToStream(beforeWire, includeHeader: true);
        published.ToStream(afterWire, includeHeader: true);
        byte[] oldBytes = beforeWire.ToArray(), newBytes = afterWire.ToArray();
        var differences = oldBytes.Zip(newBytes).Where(pair => pair.First != pair.Second).ToArray();
        var difference = Assert.Single(differences);
        Assert.Equal(16, difference.First ^ difference.Second);
    }

    [Fact]
    public void More_than_one_connection_roster_of_daily_names_survives_all_scalar_header_patch_owners()
    {
        byte[] file = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(file, limits, out var world).IsLoaded);
        Assert.NotNull(world);
        Assert.True(WorldFilePreservedSections.TryCapture(file, world.Envelope, out var preserved));
        Assert.NotNull(preserved);
        var names = Enumerable.Range(0, 256).Select(static value => $"F{value}").ToArray();
        Assert.True(WorldFileAnglerHeaderPatcher1458.TryPatch(preserved.Header.Span, world.Header, new(39, names), out var header));
        var mutations = new RuntimeWorldProgressionMutationSnapshot(0) { UnlockSlimeRedSpawn = true };
        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(header, world.Header, mutations, out var progressed));
        Assert.Equal(WorldFileSeasonHeaderPatchResult1458.Patched,
            WorldFileSeasonHeaderPatcher1458.TryPatch(progressed, world.Envelope, world.Header, out var seasonal, forceHalloweenForToday: true));
        var invasion = new WorldInvasionSaveState1458(3, 10, 1, 2d, 100);
        Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
            WorldFileInvasionHeaderPatcher1458.TryPatch(seasonal, world.Envelope, world.Header, invasion, out var complete));
        var shadow = new WorldTileSaveShadowSynchronizer(world.Tiles, 1);
        while (!shadow.IsBootstrapped) shadow.CaptureBootstrap(16);
        Assert.True(shadow.TryCaptureImage(out var tiles));
        using var output = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.Rewritten,
            WorldFileTileChestRewriter.TryRewrite(world.Envelope, world.Header, complete, preserved, tiles!, world.Chests, output, out _));
        Assert.True(WorldFileLoader.TryLoad(output.ToArray(), limits, out var loaded).IsLoaded);
        Assert.NotNull(loaded);
        Assert.Equal(names, loaded.RuntimeMetadata.AnglerQuest.CompletedNames);
        Assert.True(loaded.RuntimeMetadata.UnlockedSlimeRedSpawn);
        Assert.True(loaded.RuntimeMetadata.ForceHalloweenForToday);
        Assert.Equal(10, loaded.RuntimeMetadata.InvasionSize);
    }

    private static void SetAppearance(FishingCrateAuthority1458Tests.Fixture f, string name) =>
        f.State.Apply(new PlayerAppearanceRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 0, 0, 0, name,
            0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));

    private static void ConfigureAngler(FishingCrateAuthority1458Tests.Fixture f)
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawnVanilla(new(369, 369, 1610, 1606, 0, 0, 255, default, NpcSimulationState.Initial), out var vendor));
        Assert.True(f.Players.TrySetTalkNpc(f.Connection, checked((short)vendor.Handle.Slot)));
        var session = new RuntimeTownShopSession1458(checked((short)vendor.Handle.Slot), new(369),
            RuntimeTownShopSessionKind1458.TalkOnly, 1f, false, default, [], RuntimeTownCommerceMissingFacts1458.None)
        { VendorHandle = vendor.Handle, VendorStore = npcs };
        Assert.True(f.Players.TrySetTownShopSession(f.Connection, session));
    }

    private sealed class SourceRandom(VanillaUnifiedRandom1458 source) : Random
    {
        public override int Next() => source.Next();
        public override int Next(int maxValue) => source.Next(maxValue);
        public override int Next(int minValue, int maxValue) => source.Next(minValue, maxValue);
        public override double NextDouble() => source.NextDouble();
    }
}
