using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class CombatValidatorTests
{
    [Fact]
    public void Use_time_gate_is_global_per_player_but_allows_same_tick_multi_target_swing()
    {
        var validator = new CombatValidator(npcCapacity: 4);
        PlayerStateSnapshot player = CreatePlayer();
        NpcSnapshot first = CreateBlueSlime(slot: 0, 130f, 100f);
        NpcSnapshot second = CreateBlueSlime(slot: 1, 140f, 100f);
        NpcSnapshot third = CreateBlueSlime(slot: 2, 150f, 100f);
        var wire = new TerrariaNpcDamageState(0, 1, 20, 1f, 2, 0);

        AuthoritativeCombatRoll firstRoll = CreateRoll(player.Player, first.Handle);
        Assert.True(validator.TryValidate(100, in player, in first, in wire, in firstRoll, out _));

        AuthoritativeCombatRoll secondRoll = CreateRoll(player.Player, second.Handle);
        Assert.True(validator.TryValidate(100, in player, in second, in wire, in secondRoll, out _));

        AuthoritativeCombatRoll thirdRoll = CreateRoll(player.Player, third.Handle);
        Assert.False(validator.TryValidate(101, in player, in third, in wire, in thirdRoll, out CombatIntegrityDiagnostic rejected));
        Assert.Equal(CombatIntegrityReason.AttackCadence, rejected.Reason);
    }

    [Fact]
    public void New_player_and_npc_generations_do_not_inherit_direct_melee_cooldowns()
    {
        var validator = new CombatValidator(npcCapacity: 1);
        PlayerStateSnapshot firstPlayer = CreatePlayer(generation: 1);
        NpcSnapshot firstNpc = CreateBlueSlime(slot: 0, 130f, 100f, generation: 1);
        var wire = new TerrariaNpcDamageState(0, 1, 20, 1f, 2, 0);
        AuthoritativeCombatRoll firstRoll = CreateRoll(firstPlayer.Player, firstNpc.Handle);
        Assert.True(validator.TryValidate(100, in firstPlayer, in firstNpc, in wire, in firstRoll, out _));

        PlayerStateSnapshot nextPlayer = CreatePlayer(generation: 2);
        NpcSnapshot nextNpc = CreateBlueSlime(slot: 0, 130f, 100f, generation: 2);
        AuthoritativeCombatRoll nextRoll = CreateRoll(nextPlayer.Player, nextNpc.Handle);
        Assert.True(validator.TryValidate(101, in nextPlayer, in nextNpc, in wire, in nextRoll, out _));
    }

    [Fact]
    public void Suspicion_decay_is_applied_once_per_elapsed_tick()
    {
        var validator = new CombatValidator(npcCapacity: 1);
        PlayerStateSnapshot player = CreatePlayer();
        NpcHandle target = new(0, new NpcGeneration(1));

        CombatIntegrityDiagnostic initial = validator.RecordRejection(
            100, player.Player, target, CombatIntegrityReason.AttackCadence, clientDamage: 20, suspicionDelta: 2f);
        CombatIntegrityDiagnostic decayed = validator.RecordRejection(
            150, player.Player, target, CombatIntegrityReason.AttackCadence, clientDamage: 20, suspicionDelta: 0f);

        Assert.Equal(2f, initial.SuspicionScore, 3);
        Assert.Equal(1f, decayed.SuspicionScore, 3);
    }

    private static AuthoritativeCombatRoll CreateRoll(PlayerHandle player, NpcHandle target) =>
        new(
            new AttackContext(player, DamageSource.FromPlayerItem(player), VanillaItemIds.CopperBroadsword, VanillaPrefixIds.None, Pvp: false),
            new NpcDamageRequest(target, DamageSource.FromPlayerItem(player), 20, 0, false, 1f, 1),
            MinimumDamage: 15,
            MaximumDamage: 25,
            AnimationTicks: 18,
            UseTimeTicks: 18,
            ImpossibleCenterDistancePixels: 100f,
            CritChance: 4);

    private static PlayerStateSnapshot CreatePlayer(uint generation = 1)
    {
        var slot = new PlayerSlotId(0);
        return new PlayerStateSnapshot(
            new PlayerHandle(slot, new PlayerSessionGeneration(generation)),
            new PlayerStateRevision(1),
            Team: 0,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            MountType: 0,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            CameraTargetX: 0f,
            CameraTargetY: 0f);
    }

    private static NpcSnapshot CreateBlueSlime(byte slot, float x, float y, uint generation = 1)
    {
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            Life = 25,
            LifeMax = 25,
            Scale = 1f
        };
        return new NpcSnapshot(
            new NpcHandle(slot, new NpcGeneration(generation)),
            new NpcRevision(1),
            Type: 1,
            NetId: 1,
            PositionX: x,
            PositionY: y,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: default,
            Simulation: simulation);
    }
}
