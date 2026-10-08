using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class AccessorySlotOwnership1458Tests
{
    [Fact]
    public void Actual_dedicated_ResetEffects_UpdateEquips_match_source_world_Heart_gates()
    {
        using var stream = typeof(AccessorySlotOwnership1458Tests).Assembly.GetManifestResourceStream("AccessorySlots1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",
            document.RootElement.GetProperty("sourceSha256").GetString());
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            var context = new VanillaPlayerCombatEquipmentContext(row.GetProperty("heart").GetBoolean(),
                row.GetProperty("expert").GetBoolean(), row.GetProperty("master").GetBoolean());
            Assert.True(context.TryIsSlotUsable(8, out bool eight));
            Assert.True(context.TryIsSlotUsable(9, out bool nine));
            Assert.Equal(row.GetProperty("slot8").GetBoolean(), eight);
            Assert.Equal(row.GetProperty("slot9").GetBoolean(), nine);
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(8, 491), Equip(9, 1321)], in context, out var actual));
            Assert.Equal(row.GetProperty("rangedDamage").GetSingle(), actual.RangedDamage);
            Assert.Equal(row.GetProperty("arrowDamageAdditiveStack").GetSingle(), actual.ArrowDamageAdditiveStack);
            Assert.Equal(row.GetProperty("magicQuiver").GetBoolean(), actual.MagicQuiver);
            Assert.Equal(906992634, row.GetProperty("next").GetInt32());
        }
        var metadata = document.RootElement.GetProperty("metadata");
        Assert.Equal(14, metadata.GetArrayLength());
        foreach (var item in metadata.EnumerateArray()) Assert.False(item.GetProperty("expertOnly").GetBoolean());
        Assert.True(metadata.EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 4989).GetProperty("expert").GetBoolean());
        foreach (var row in document.RootElement.GetProperty("prefixProfiles").EnumerateArray())
        {
            Assert.True(row.GetProperty("applied").GetBoolean());
            var context = new VanillaPlayerCombatEquipmentContext(false, row.GetProperty("mode").GetInt32() == 1, false);
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(3, 4989, 65)], in context, out var prefixed));
            Assert.Equal(row.GetProperty("defense").GetInt32(), prefixed.Defense);
            Assert.Equal(906992634, row.GetProperty("next").GetInt32());
        }
    }

    [Fact]
    public void Unknown_Heart_is_selected_only_in_Expert_and_locked_items_skip_identity_prefix_validation()
    {
        var classic = new VanillaPlayerCombatEquipmentContext(null, false, false);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(8, 999, 255), Equip(9, 999, 255)], in classic, out var ignored));
        Assert.Equal(VanillaPlayerCombatSnapshot.Baseline, ignored);
        var expert = new VanillaPlayerCombatEquipmentContext(null, true, false);
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(8, 491)], in expert, out _));
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([], in expert, out _));
        var master = new VanillaPlayerCombatEquipmentContext(null, true, true);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(9, 1321)], in master, out var ninth));
        Assert.True(ninth.MagicQuiver);
        var invalid = new VanillaPlayerCombatEquipmentContext(true, false, true);
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([], in invalid, out _));
        var inherited = Equip(8, 491) with { SlotId = (short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 8), ItemFlags = 1 };
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([inherited],
            new VanillaPlayerCombatEquipmentContext(true, true, false), out var inheritedCombat));
        Assert.Equal(1.15f, inheritedCombat.RangedDamage);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(8, 491), inherited],
            new VanillaPlayerCombatEquipmentContext(true, true, false), out _));
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([inherited], in classic, out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild([Equip(8, 999)],
            new VanillaPlayerCombatEquipmentContext(true, true, false), out _));
    }

    [Fact]
    public void Human_packet4_flag_and_bot_owned_appearance_share_the_same_source_combat_builder()
    {
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(4300), session.Handle);
        var players = new PlayerAuthority(null, null);
        players.SetCombatEquipmentWorldModes(true, true); // Effective Expert+GoodWorld, already source Master.
        players.TryApply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
        players.TryApply(new PlayerEquipmentRuntimeCommand(connection, Equip(8, 491) with { PlayerSlot = session.Slot }));
        players.TryApply(new PlayerEquipmentRuntimeCommand(connection, Equip(9, 1321) with { PlayerSlot = session.Slot }));
        Assert.False(players.TryCaptureCombatSnapshot(connection, out _)); // Unreported Heart remains unknown.
        var appearance = Appearance(session.Slot, 4);
        players.TryApply(new PlayerAppearanceRuntimeCommand(connection, appearance));
        Assert.True(players.TryCaptureCombatSnapshot(connection, out var human));
        Assert.Equal(1.15f, human.RangedDamage);
        Assert.True(human.MagicQuiver);
        players.TryApply(new PlayerAppearanceRuntimeCommand(connection, appearance with { DifficultyFlags = 0 }));
        Assert.True(players.TryCaptureCombatSnapshot(connection, out var withoutHeart));
        Assert.Equal(1f, withoutHeart.RangedDamage);
        Assert.True(withoutHeart.MagicQuiver);

        var botSlots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(botSlots);
        var states = new ServerPlayerStateStore(identities, 1);
        var bots = new ServerPlayerAuthority(states, identities);
        bots.SetCombatEquipmentWorldModes(true, true);
        var id = new ServerPlayerId("test:extra-accessory");
        var created = bots.Create(id, 100, 100);
        Assert.True(created.IsCreated);
        Assert.True(bots.SetItem(id, new((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 8), new(491), 1, new(0), 0)));
        Assert.True(bots.SetItem(id, new((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 9), new(1321), 1, new(0), 0)));
        Assert.False(bots.TryCaptureCombatSnapshot(created.Player, out _));
        var botAppearance = new ServerPlayerAppearanceState(0, 0, 0f, 0, "Equipment", 0, 0, 0,
            default, default, default, default, default, default, default, 0, 0, 0);
        Assert.True(bots.SetAppearance(id, botAppearance with { DifficultyFlags = 4 }));
        Assert.True(bots.TryCaptureCombatSnapshot(created.Player, out var bot));
        Assert.Equal(human, bot);
        Assert.True(bots.SetAppearance(id, botAppearance with { DifficultyFlags = 0 }));
        Assert.True(bots.TryCaptureCombatSnapshot(created.Player, out bot));
        Assert.Equal(withoutHeart, bot);
        Assert.True(bots.SetAppearance(id, botAppearance with { DifficultyFlags = 4 }));
        Assert.True(bots.SetItem(id, new((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 8), new(0), 0, new(0), 0)));
        Assert.True(bots.SetItem(id, new((short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 8), new(491), 1, new(0), 1)));
        Assert.True(bots.TryCaptureCombatSnapshot(created.Player, out bot));
        Assert.Equal(human, bot);
        players.TryApply(new PlayerEquipmentRuntimeCommand(connection, Equip(8, 0) with { PlayerSlot = session.Slot, Stack = 0 }));
        players.TryApply(new PlayerEquipmentRuntimeCommand(connection, Equip(8, 491) with {
            PlayerSlot = session.Slot, SlotId = (short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 8), ItemFlags = 1 }));
        players.TryApply(new PlayerAppearanceRuntimeCommand(connection, appearance));
        Assert.True(players.TryCaptureCombatSnapshot(connection, out var inheritedHuman));
        Assert.Equal(human, inheritedHuman);
    }

    private static PlayerEquipmentCommitRequest Equip(int armor, short item, byte prefix = 0) =>
        new(new(0), (short)(VanillaPlayerItemSlotCatalog.ArmorStart + armor), 1, prefix, item, 0);

    private static PlayerAppearanceCommitRequest Appearance(PlayerSlotId slot, byte difficulty) =>
        new(slot, 0, 0, 0f, 0, "Equipment", 0, 0, 0, default, default, default, default, default, default, default,
            difficulty, 0, 0);
}
