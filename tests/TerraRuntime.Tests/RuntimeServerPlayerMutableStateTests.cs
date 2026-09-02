using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeServerPlayerMutableStateTests
{
    [Fact]
    public void Appearance_vitals_and_item_state_are_normalized_and_generation_safe()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);
        var id = new ServerPlayerId("test:mutable-fake");
        ServerPlayerCreateResult created = service.Create(id, 100f, 200f);
        Assert.True(created.IsCreated);

        ServerPlayerAppearanceState appearance = CreateAppearance("  Merchant  ") with
        {
            SkinVariant = byte.MaxValue,
            VoiceVariant = 0,
            VoicePitchOffset = float.PositiveInfinity,
            Hair = byte.MaxValue,
            HideVisibleAccessory = ushort.MaxValue,
            HideMisc = byte.MaxValue,
            DifficultyFlags = byte.MaxValue,
            TorchAndCartFlags = byte.MaxValue,
            ConsumableUnlockFlags = byte.MaxValue
        };
        Assert.True(service.SetAppearance(id, in appearance));
        Assert.True(states.TryGetAppearance(created.Player, out ServerPlayerAppearanceState normalizedAppearance));
        Assert.Equal("Merchant", normalizedAppearance.Name);
        Assert.Equal((byte)11, normalizedAppearance.SkinVariant);
        Assert.Equal((byte)1, normalizedAppearance.VoiceVariant);
        Assert.Equal(1f, normalizedAppearance.VoicePitchOffset);
        Assert.Equal((byte)0, normalizedAppearance.Hair);
        Assert.Equal((ushort)0x03ff, normalizedAppearance.HideVisibleAccessory);
        Assert.Equal((byte)0x03, normalizedAppearance.HideMisc);
        Assert.Equal((byte)0x0c, normalizedAppearance.DifficultyFlags);
        Assert.Equal((byte)0x1f, normalizedAppearance.TorchAndCartFlags);
        Assert.Equal((byte)0x7f, normalizedAppearance.ConsumableUnlockFlags);

        var vitals = new ServerPlayerVitalsState(Life: 0, MaxLife: 1, Mana: 37, MaxMana: 80);
        Assert.True(service.SetVitals(id, in vitals));
        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot player));
        Assert.True(player.HasHealth);
        Assert.Equal((short)0, player.Life);
        Assert.Equal(VanillaPlayerVitalsRules.MinimumMaxLife, player.MaxLife);
        Assert.True(player.IsDead);
        Assert.True(player.HasMana);
        Assert.Equal((short)37, player.Mana);
        Assert.Equal((short)80, player.MaxMana);

        var item = new ServerPlayerItemState(
            Slot: 0,
            VanillaItemIds.DirtBlock,
            Stack: 3,
            new PrefixId(2),
            ItemFlags: byte.MaxValue);
        Assert.True(service.SetItem(id, in item));
        Assert.True(states.TryGetItem(created.Player, 0, out ServerPlayerItemState normalizedItem));
        Assert.Equal(VanillaItemIds.DirtBlock, normalizedItem.ItemType);
        Assert.Equal((short)3, normalizedItem.Stack);
        Assert.Equal(new PrefixId(2), normalizedItem.Prefix);
        Assert.Equal((byte)1, normalizedItem.ItemFlags);
        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot afterItem));
        Assert.Equal(4UL, afterItem.Revision.Value);

        var empty = new ServerPlayerItemState(0, VanillaItemIds.None, 0, default, 0);
        Assert.True(service.SetItem(id, in empty));
        Assert.True(states.TryGetItem(created.Player, 0, out ServerPlayerItemState cleared));
        Assert.True(cleared.IsEmpty);

        PlayerHandle stale = created.Player;
        Assert.True(service.Despawn(id));
        Assert.False(states.TryGetAppearance(stale, out _));
        Assert.False(states.TryGetItem(stale, 0, out _));
    }

    [Fact]
    public void Invalid_presentation_and_item_state_do_not_advance_revision()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var service = new ServerPlayerAuthority(states, identities);
        var id = new ServerPlayerId("test:invalid-fake-state");
        ServerPlayerCreateResult created = service.Create(id, 0f, 0f);
        Assert.True(created.IsCreated);

        ServerPlayerAppearanceState appearance = CreateAppearance("   ");
        var invalidItem = new ServerPlayerItemState(
            Slot: VanillaPlayerItemSlotCatalog.Count,
            VanillaItemIds.DirtBlock,
            Stack: 1,
            Prefix: default,
            ItemFlags: 0);
        Assert.False(service.SetAppearance(id, in appearance));
        Assert.False(service.SetItem(id, in invalidItem));
        Assert.True(states.TryGet(created.Player, out PlayerStateSnapshot retained));
        Assert.Equal(1UL, retained.Revision.Value);
    }

    private static ServerPlayerAppearanceState CreateAppearance(string name) =>
        new(
            SkinVariant: 0,
            VoiceVariant: 1,
            VoicePitchOffset: 0f,
            Hair: 0,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: new PlayerRgbColor(1, 2, 3),
            SkinColor: new PlayerRgbColor(4, 5, 6),
            EyeColor: new PlayerRgbColor(7, 8, 9),
            ShirtColor: new PlayerRgbColor(10, 11, 12),
            UnderShirtColor: new PlayerRgbColor(13, 14, 15),
            PantsColor: new PlayerRgbColor(16, 17, 18),
            ShoeColor: new PlayerRgbColor(19, 20, 21),
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
}
