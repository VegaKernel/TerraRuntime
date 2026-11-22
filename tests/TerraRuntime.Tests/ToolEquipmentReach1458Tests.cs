using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class ToolEquipmentReach1458Tests
{
    [Fact]
    public void Good_functional_accessories_have_source_combat_projection_without_remote_phase_admission()
    {
        var normal = new VanillaPlayerCombatEquipmentContext(false, false, false);
        PlayerEquipmentCommitRequest[] equipment =
        [
            new(default, 62, 1, 74, 4954, 0),
            new(default, 63, 1, 0, 2423, 0),
            new(default, 64, 1, 75, 216, 0),
            new(default, 65, 1, 0, 4989, 0),
            new(default, 66, 1, 0, 3061, 0),
            new(default, 67, 1, 0, 5000, 0),
            new(default, 68, 1, 0, 407, 0)
        ];

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in normal,
            out var combat));
        Assert.Equal(1, combat.Defense); // Shackle's Item.SetDefaults defense.
        Assert.Equal(1f, combat.MoveSpeed); // Mobility prefixes need the separate phase.
        Assert.False(combat.LavaRose); // Slot 67 is unavailable outside Expert.
        Assert.False(combat.WaterWalk);
        Assert.Equal(0, combat.LavaProtectionTicks);

        var master = new VanillaPlayerCombatEquipmentContext(true, true, true);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, in master,
            out var masterCombat));
        Assert.True(masterCombat.LavaRose);
        Assert.True(masterCombat.WaterWalk);
        Assert.Equal(420, masterCombat.LavaProtectionTicks);
        foreach (var accessory in equipment)
            Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild([accessory],
                in master, out _), $"Unsupported functional accessory {accessory.ItemNetId}");

        var wrongSlot = equipment.ToArray();
        wrongSlot[0] = wrongSlot[0] with { SlotId = 59 };
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(wrongSlot, in normal,
            out _));
    }

    [Fact]
    public void Architect_range_applies_once_to_mining_tools_only_from_functional_slots()
    {
        var world = new RuntimePlayerUpdateWorld1458(4200, 1200, 400, false, false)
        {
            JourneyMode = true
        };
        Assert.True(VanillaToolCatalog1458.TryGet(new ItemTypeId(1), out var tool));
        var context = new VanillaPlayerCombatEquipmentContext(false, false, false);
        var builder = new PlayerEquipmentCommitRequest(default, 66, 1, 0, 3061, 0);
        var social = builder with { SlotId = 76 };

        Assert.True(PlayerAuthority.TryResolveToolReach(in world, in tool,
            [social], in context, out var socialReach));
        Assert.Equal(new RuntimePlayerToolReach1458(5, 3, tool.TileBoost), socialReach);

        Assert.True(PlayerAuthority.TryResolveToolReach(in world, in tool,
            [builder, builder with { SlotId = 67, ItemNetId = 2215 }], in context, out var extended));
        Assert.Equal(new RuntimePlayerToolReach1458(8, 5, tool.TileBoost), extended);

        Assert.True(PlayerAuthority.TryResolveToolReach(in world, in tool,
            [builder with { Stack = 0 }], in context, out var emptyReach));
        Assert.Equal(socialReach, emptyReach);
    }

    [Fact]
    public void Architect_and_toolbelt_extend_paid_placement_reach_from_functional_slots()
    {
        var world = new RuntimePlayerUpdateWorld1458(4200, 1200, 400, false, false)
        {
            JourneyMode = true
        };
        Assert.True(VanillaLocalPlacementCatalog1458.TryGet(new ItemTypeId(2), out var dirt));
        var normal = new VanillaPlayerCombatEquipmentContext(false, false, false);
        var master = new VanillaPlayerCombatEquipmentContext(true, true, true);
        var builder = new PlayerEquipmentCommitRequest(default, 66, 1, 0, 3061, 0);
        var toolbelt = builder with { SlotId = 68, ItemNetId = 407 };

        Assert.True(PlayerAuthority.TryResolvePlacementReach(in world, in dirt,
            [builder with { SlotId = 76 }, toolbelt with { Stack = 0 }], in normal, out var baseline));
        Assert.Equal(new RuntimePlayerToolReach1458(5, 3, dirt.TileBoost), baseline);

        Assert.True(PlayerAuthority.TryResolvePlacementReach(in world, in dirt,
            [builder, toolbelt, builder with { SlotId = 67, ItemNetId = 2215 }], in master, out var equipped));
        Assert.Equal(new RuntimePlayerToolReach1458(8, 5, dirt.TileBoost + 1), equipped);

        Assert.True(PlayerAuthority.TryResolvePlacementReach(in world, in dirt,
            [builder, toolbelt], in normal, out var ordinary));
        Assert.Equal(new RuntimePlayerToolReach1458(8, 5, dirt.TileBoost), ordinary);
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(
            [builder, toolbelt], in normal, out var effects));
        Assert.True(effects.TileRange);
        Assert.Equal(0, effects.BlockRange);

        var unknownWorld = world with { JourneyMode = null };
        Assert.False(PlayerAuthority.TryResolvePlacementReach(in unknownWorld,
            in dirt, [builder, toolbelt], in master, out _));
    }

    [Fact]
    public void Architect_builder_effects_follow_effective_functional_slot_selection()
    {
        var normal = new VanillaPlayerCombatEquipmentContext(false, false, false);
        var master = new VanillaPlayerCombatEquipmentContext(true, true, true);
        var architect = new PlayerEquipmentCommitRequest(default, 66, 1, 0, 3061, 0);

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(
            [architect], in normal, out var functional));
        Assert.True(functional.TileSpeed);
        Assert.True(functional.WallSpeed);
        Assert.True(functional.AutoPaint);
        Assert.Equal((1.5f, 1.5f), functional.ResolveRawSpeeds(new ItemTypeId(2)));
        Assert.Equal((1f, 1.5f), functional.ResolveRawSpeeds(new ItemTypeId(8)));
        Assert.Equal((1f, 1.5f), functional.ResolveRawSpeeds(new ItemTypeId(26)));
        Assert.Equal((1f, 1.5f), functional.ResolveRawSpeeds(new ItemTypeId(1)));
        var raw = functional.ResolveRawSpeeds(new ItemTypeId(2));
        var baseBoosters = new PlayerPermanentBoosterState1458(0, 0, 1f, raw.Tile, raw.Wall);
        var noUnlocks = new PlayerPermanentUnlockState1458(0);
        Assert.True(VanillaSelectedConsumable1458.TryApplyPermanentBoosters(
            in baseBoosters, in noUnlocks, out var boosters));
        Assert.True(VanillaSelectedConsumable1458.TryFinalizePermanentSpeeds(in boosters, out var speeds));
        Assert.Equal(2f / 3f, speeds.TileAnimationMultiplier);
        Assert.Equal(2f / 3f, speeds.WallAnimationMultiplier);

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(
            [architect with { SlotId = 76 }], in normal, out var social));
        Assert.False(social.TileSpeed);
        Assert.False(social.WallSpeed);
        Assert.False(social.AutoPaint);
        Assert.Equal((1f, 1f), social.ResolveRawSpeeds(new ItemTypeId(2)));

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(
            [architect with { SlotId = 67 }], in normal, out var locked));
        Assert.False(locked.TileSpeed);
        Assert.False(locked.WallSpeed);
        Assert.False(locked.AutoPaint);
        Assert.Equal((1f, 1f), locked.ResolveRawSpeeds(new ItemTypeId(2)));

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureToolAccessories(
            [architect with { SlotId = 67 }], in master, out var unlocked));
        Assert.True(unlocked.TileSpeed);
        Assert.True(unlocked.WallSpeed);
        Assert.True(unlocked.AutoPaint);
    }
}
