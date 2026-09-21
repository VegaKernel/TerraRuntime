using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MotherSlimeDeathSplitPipelineTests
{
    [Fact]
    public void Authoritative_player_kill_materializes_baby_slimes_before_parent_despawn()
    {
        var items = new RuntimeWorldItemStore();
        var npcs = new RuntimeNpcStore();
        var player = new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1));
        var pipeline = new RuntimeNpcNetworkCombatPipeline(
            npcs,
            items,
            new Players(player),
            new PlayerAuthority(events: null, worldTiles: null),
            static () => 0,
            npcReplication: null,
            new RuntimeWorldItemInstancedLeaseStore(items),
            worldItemReplication: null,
            worldClock: null,
            new RuntimeWorldProgressionMutations(),
            expertMode: false,
            masterMode: false);

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.MotherSlime, out VanillaNpcDefinition definition));
        var update = new NpcStateUpdate(
            VanillaNpcIds.MotherSlime.Value,
            checked((short)VanillaNpcIds.MotherSlime.Value),
            100f,
            200f,
            2f,
            3f,
            0,
            default,
            NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax });
        Assert.True(npcs.TrySpawnVanilla(in update, out NpcSnapshot mother));

        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
            pipeline.TryStrikeServerPlayerMelee(player, mother.Handle, 100_000, 0, false, 0f, 1));
        Assert.False(npcs.TryGet(mother.Handle, out _));

        int children = 0;
        for (int slot = 0; slot < npcs.Capacity; slot++)
        {
            if (!npcs.TryGetActive(checked((byte)slot), out NpcSnapshot child))
                continue;
            children++;
            Assert.Equal(VanillaNpcIds.BlueSlime.Value, child.Type);
            Assert.Equal(VanillaNpcNetVariantCatalog.BabySlime.Value, child.NetId);
        }

        Assert.InRange(children, 2, 3);
    }

    private sealed class Players(PlayerHandle player) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with
            {
                Player = player,
                Revision = new PlayerStateRevision(1),
                PositionX = 100f,
                PositionY = 200f,
                HasHealth = true,
                Life = 500,
                MaxLife = 500
            };
            return slot == player.Slot;
        }
    }
}
