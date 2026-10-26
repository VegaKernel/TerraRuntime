using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Fishing_potion_metadata_matches_actual_official_item_defaults()
    {
        using Stream compressed = typeof(FishingAuthority1458Tests).Assembly.GetManifestResourceStream("FishingRulesLinux1458")!;
        using var zip = new GZipStream(compressed, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(zip);
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("potions").EnumerateArray())
        {
            Assert.True(VanillaSelectedConsumableCatalog1458.TryGet(new(row.GetProperty("id").GetInt32()), out var item));
            Assert.Equal(row.GetProperty("buffType").GetInt32(), item.BuffType);
            Assert.Equal(row.GetProperty("buffTime").GetInt32(), item.BuffTime);
            Assert.Equal(row.GetProperty("time").GetInt32(), item.UseTime);
            Assert.Equal(row.GetProperty("animation").GetInt32(), item.UseAnimation);
            compared++;
        }
        Assert.Equal(3, compared);
    }

    [Fact]
    public void Paid_fishing_potions_keep_source_durations_effects_and_last_positive_tick()
    {
        foreach (short potion in new short[] { 2354, 2355, 2356 })
        {
            using var f = new FishingFixture(8);
            short buff = checked((short)(potion - 2354 + 121));
            int duration = potion == 2356 ? 14400 : 28800;
            var weather = new FishingPowerContext1458(true, 10000, 2, false, 0, false, 0);
            Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out var baseline));
            Assert.NotNull(baseline);
            f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, new[] { new BuffTypeId(buff) })));
            Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out var unfunded));
            Assert.Equal(baseline.Conditions, unfunded!.Conditions);
            Assert.False(unfunded.CratePotion); Assert.False(unfunded.SonarPotion);
            f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, Array.Empty<BuffTypeId>())));
            Assert.True(f.Items.TryAllocateSourceDrop(new(f.Member.PositionX, f.Member.PositionY, 0, 0, 1, 0,
                WorldItemOwnershipMode.None, potion, false, 0, 0), default, out var dropped, out _));
            Assert.True(f.Items.TryApplyOwner(dropped.Handle.Slot,
                new(f.Connection.Player.Slot.Value, 15, 1, 0, f.Member.PositionX, f.Member.PositionY), out dropped));
            f.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, dropped.Handle));
            short slot = Enumerable.Range(0, 58).Select(i => (short)i).Single(i => f.Item(i).ItemType.Value == potion);
            Assert.Equal(1, f.Funded(slot));
            f.Control(false, (byte)slot);
            for (int tick = 0; tick < 20; tick++) f.Tick();
            Assert.Equal((byte)slot, f.Member.SelectedItem);
            Assert.NotNull(f.Member.ItemPhase);
            Assert.True(f.Member.ItemPhase!.Value.Selected.ReleaseUseItem);
            Assert.Equal(0, f.Member.ItemPhase.Value.Selected.Animation);
            Assert.Equal(0, f.Member.ItemPhase.Value.Selected.ItemTime);
            Assert.Equal(1, f.Funded(slot));
            f.Control(true, (byte)slot); f.Tick(); f.Control(false, (byte)slot);
            Assert.True(f.Item(slot).IsEmpty,
                $"potion={potion} selected={f.Member.SelectedItem} stack={f.Item(slot).Stack} funded={f.Funded(slot)} phase={f.Member.ItemPhase?.Selected}");
            Assert.Equal(0, f.Funded(slot));
            var owned = Profiles(f.Players).CaptureBuffState(f.Connection)!;
            Assert.Equal(duration, owned.GetDuration(new(buff)));
            f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, new[] { new BuffTypeId(buff) })));
            Assert.Equal(duration, Profiles(f.Players).CaptureBuffState(f.Connection)!.GetDuration(new(buff)));
            f.Control(false, 0);
            for (int tick = 0; tick < 20; tick++) f.Tick();
            Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out var active),
                $"after-potion={potion} selected={f.Member.SelectedItem}/{f.Item(f.Member.SelectedItem).ItemType.Value} phase={f.Member.ItemPhase?.Selected} local={f.Member.ItemPhase?.ToolUse?.LocalClocksKnown} health={f.Member.NpcHealth}");
            Assert.Equal(baseline.Conditions.FinalFishingLevel + (buff == 121 ? 15 : 0), active!.Conditions.FinalFishingLevel);
            Assert.Equal(buff == 122, active.SonarPotion); Assert.Equal(buff == 123, active.CratePotion);
            bool paidCatch = false;
            for (int cast = 0; cast < 6 && !paidCatch; cast++)
            {
                f.Cast(); var bite = f.WaitForBite(); f.Pull();
                bool paid = f.Authority.CaptureFishingBobber(f.Connection)?.PaidCatch == true;
                f.Return();
                if (paid) { Assert.True(f.Count(bite.Result.ItemType) > 0); paidCatch = true; }
            }
            Assert.True(paidCatch);
            Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out var afterCatch));
            Assert.Equal(active.Conditions, afterCatch!.Conditions);
            Assert.Equal(active.SonarPotion, afterCatch.SonarPotion); Assert.Equal(active.CratePotion, afterCatch.CratePotion);
            // Exercise the full original duration on a detached copy of the
            // actual paid owner. World/NPC/liquid simulation is independent
            // of this bounded clock and need not run seventy thousand ticks.
            var clock = Profiles(f.Players).CaptureBuffState(f.Connection)!.Clone();
            int remaining = clock.GetDuration(new(buff));
            long clockTick = 1_000_000;
            for (int tick = 0; tick < remaining; tick++) clock.AdvanceOwnedFoodClock(++clockTick, true);
            Assert.Equal(0, clock.GetDuration(new(buff)));
            Assert.Equal((byte)(1 << (buff - 121)), clock.CaptureFishingEffects(clockTick));
            clock.AdvanceOwnedFoodClock(++clockTick, true);
            Assert.Equal((byte)0, clock.CaptureFishingEffects(clockTick));
        }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority players);
}
