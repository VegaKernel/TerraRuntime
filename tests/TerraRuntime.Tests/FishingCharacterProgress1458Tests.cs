using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class FishingCharacterProgress1458Tests
{
    [Fact]
    public void Imported_character_counters_do_not_complete_quests_and_paid_increment_retains_golf_and_milestone()
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        var published = new List<(int Count, int Golf)>();
        var relayed = new List<(int Count, int Golf)>();
        f.State.BindAnglerWorld(new WorldFileRuntimeMetadata { WorldSurface = 80, AnglerQuest = new(0, []) },
            (_, _, _, count, golf) => published.Add((count, golf)),
            (_, count, golf) => relayed.Add((count, golf)));
        AnglerAuthority1458Tests.SetAppearance(f, "CharacterFisher");
        AnglerAuthority1458Tests.ConfigureAngler(f);
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone();
        var before = f.ItemImage();

        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 29, 1234));
        Assert.Equal(29, member.AnglerQuestsFinished);
        Assert.Equal(1234, member.GolferScoreAccumulated);
        Assert.False(member.OwnsAnglerQuestsFinished);
        Assert.Equal(before, f.ItemImage());
        Assert.True(beforeRandom.HasSameState(random));
        Assert.Empty(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.False(f.State.TryCompleteAnglerQuest(f.Connection));

        var fish = f.Drop(2450, 1);
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, fish.Handle));
        Assert.True(f.State.TryCompleteAnglerQuest(f.Connection));
        Assert.Equal(30, member.AnglerQuestsFinished);
        Assert.True(member.OwnsAnglerQuestsFinished);
        Assert.Contains(f.ItemImage(), item => item.ItemType.Value == 2294 && item.Stack == 1);
        Assert.Equal((30, 1234), published[^1]);
        Assert.Equal((30, 1234), relayed[^1]);
        var accepted = f.ItemImage(); var paidRandom = random.Clone();

        // Subsequent76 is not another paid completion or permission to choose
        // a new milestone. The sibling golf field still belongs to the character.
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 999, 2345));
        Assert.Equal(30, member.AnglerQuestsFinished);
        Assert.Equal(2345, member.GolferScoreAccumulated);
        Assert.Equal((30, 2345), published[^1]);
        Assert.Equal((30, 2345), relayed[^1]);
        Assert.Equal(accepted, f.ItemImage()); Assert.True(paidRandom.HasSameState(random));
        Assert.False(f.State.TryCompleteAnglerQuest(f.Connection));
    }

    [Fact]
    public async Task Progress_ownership_and_golf_transfer_while_new_lifetime_can_import_its_reported_counter()
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.State.BindAnglerWorld(new() { WorldSurface = 80, AnglerQuest = new(0, []) });
        AnglerAuthority1458Tests.SetAppearance(f, "TransferredFisher");
        AnglerAuthority1458Tests.ConfigureAngler(f);
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 4, 4321));
        var fish = f.Drop(2450, 1);
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, fish.Handle));
        Assert.True(f.State.TryCompleteAnglerQuest(f.Connection));
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = await detached.Task;
        Assert.NotNull(transfer);
        Assert.Equal(5, transfer.AnglerQuestsFinished);
        Assert.Equal(4321, transfer.GolferScoreAccumulated);
        Assert.True(transfer.OwnsAnglerQuestsFinished);
        var attached = new TaskCompletionSource<bool>();
        f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attached));
        Assert.True(await attached.Task);
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 25, 8765));
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        Assert.Equal(5, member.AnglerQuestsFinished); Assert.Equal(8765, member.GolferScoreAccumulated);

        using var replacement = new FishingCrateAuthority1458Tests.Fixture();
        replacement.State.BindAnglerWorld(new() { WorldSurface = 80, AnglerQuest = f.State.CaptureAnglerSaveState()! });
        AnglerAuthority1458Tests.SetAppearance(replacement, "TransferredFisher");
        replacement.State.Apply(new PlayerCharacterProgressRuntimeCommand(replacement.Connection, 5, 8765));
        Assert.True(replacement.State.TryCaptureAnglerState(replacement.Connection, out _, out bool complete, out int count));
        Assert.True(complete); Assert.Equal(5, count);
        Assert.True(replacement.Players.TryGet(replacement.Connection, out var rejoined));
        Assert.Equal(8765, rejoined.GolferScoreAccumulated);
        Assert.False(rejoined.OwnsAnglerQuestsFinished);
        // This deliberately reports76; source evidence does not prove that an
        // unmodified vanilla client automatically transmits it at join.
    }

    [Fact]
    public void Stale_occupation_and_negative_progress_do_not_change_current_character()
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 29, 1234));
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        var stale = f.Connection with { Player = new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)) };
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(stale, 99, 888));
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, -1, 888));
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 99, -1));
        Assert.Equal(29, member.AnglerQuestsFinished); Assert.Equal(1234, member.GolferScoreAccumulated);
        f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 99, 888));
        Assert.False(f.Players.TryGet(f.Connection, out _));
    }
}
