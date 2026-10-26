using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using Xunit;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Retired_paid_ammo_delivery_does_not_authorize_fishing_or_a_fresh_crate_open()
    {
        var failures = new List<string>();
        foreach (bool paidAmmo in new[] { false, true })
        {
            try { CheckRetiredBobber(paidAmmo); }
            catch (Exception error) { failures.Add($"bobber/paidAmmo={paidAmmo}: {error.Message}"); }
        }
        foreach (bool retire in new[] { false, true })
        {
            try { CheckRetiredCrate(retire); }
            catch (Exception error) { failures.Add($"crate/retire={retire}: {error.Message}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static FishingFixture PaidAmmoFishingFixture(bool pay)
    {
        var f = new FishingFixture(3, initialItems: new Dictionary<short, RuntimePlayerInventoryItem>
        {
            [1] = new(new(39), 1, default, 0),
            [54] = new(new(40), 3, default, 0)
        });
        if (pay)
        {
            var member = f.Member;
            f.Apply(new PlayerMovementRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot, 96, 16, 0, 64, 1, member.PositionX, member.PositionY,
                    false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0))
                { ItemUseCaller = RuntimeItemUseCaller1458.LocalOwnerProjection });
            f.Tick();
            Assert.Equal(2, f.Item(54).Stack);
            Assert.Single(f.Member.ItemPhase!.Value.ToolUse!.WeaponShootHistory!.Uses);
            Assert.False(f.Member.ItemPhase!.Value.ToolUse!.WeaponShootHistory!.Uses[0].Born);
        }
        f.Control(false, 0);
        for (int tick = 0; tick < 40; tick++) f.Tick();
        return f;
    }

    private static void RetireFishingPhase(FishingFixture f, bool paidAmmo)
    {
        // An unsupported imported buff really retires the shared player phase;
        // no fake receipt or direct assignment of a delivery shell is used.
        f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot, new BuffTypeId[] { new(2) })));
        f.Tick();
        if (paidAmmo)
        {
            Assert.NotNull(f.Member.ItemPhase);
            var phase = f.Member.ItemPhase!.Value;
            Assert.False(phase.ToolUse!.LocalClocksKnown);
            Assert.Null(phase.DerivedCombat);
            Assert.Single(phase.ToolUse.WeaponShootHistory!.Uses);
        }
        else Assert.Null(f.Member.ItemPhase);
    }

    private static void CheckRetiredBobber(bool paidAmmo)
    {
        using var f = PaidAmmoFishingFixture(paidAmmo);
        f.Cast();
        Assert.True(f.Runtime.Npcs.TryCaptureFishingWorld(f.Member, out _, out var weather, out _));
        Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out _));
        RetireFishingPhase(f, paidAmmo);
        Assert.True(f.Players.TryCaptureFishingPlayer(f.Member, weather, out _, ending: true));
        var before = Assert.IsType<FishingBobber1458>(f.Authority.CaptureFishingBobber(f.Connection));
        var random = f.Random.Clone();
        Assert.Equal(ProjectileActorSimulationResult.Rejected,
            StepRetiredBobber(f.Authority, before.Snapshot));
        Assert.Equal(before, f.Authority.CaptureFishingBobber(f.Connection));
        Assert.True(f.Random.HasSameState(random));
        Assert.False(f.Players.TryCaptureFishingPlayer(f.Member, weather, out _));
    }

    private static void CheckRetiredCrate(bool retire)
    {
        using var f = PaidAmmoFishingFixture(true);
        Assert.True(f.Items.TryAllocateSourceDrop(new(f.Member.PositionX, f.Member.PositionY, 0, 0,
            1, 0, WorldItemOwnershipMode.None, 2334, false, 0, 0), default, out var crate, out _));
        Assert.True(f.Items.TryApplyOwner(crate.Handle.Slot,
            new(f.Connection.Player.Slot.Value, 15, 1, 0, f.Member.PositionX, f.Member.PositionY), out crate));
        f.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, crate.Handle));
        short slot = Enumerable.Range(0, 58).Select(i => (short)i)
            .Single(i => f.Item(i).ItemType.Value == 2334);
        Assert.Equal(1, f.Funded(slot));
        f.Players.BindFishingRewards(f.Runtime.WorldItems);
        if (retire) RetireFishingPhase(f, true);
        var random = f.Random.Clone(); var image = f.InventoryImage();
        bool opened = f.Players.TryOpenFishingCrate(f.Connection, slot, Inventory(f.Players).Serial);
        Assert.Equal(!retire, opened);
        if (retire)
        {
            Assert.Equal(image, f.InventoryImage());
            Assert.Equal(1, f.Funded(slot));
            Assert.True(f.Random.HasSameState(random));
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TrySimulateFishingBobber")]
    private static extern ProjectileActorSimulationResult StepRetiredBobber(
        ProjectileAuthority owner, ProjectileSnapshot before);
}
