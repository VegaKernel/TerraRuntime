using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime;

internal enum RuntimeNpcSyncKind : byte
{
    Spawn = 0,
    Update = 1,
    Despawn = 2
}

/// <summary>
/// Converts generation-safe runtime NPC state into the protocol-neutral packet-23 projection.
/// Runtime generations remain monotonic ulongs; the vanilla wire generation is 1..255 and wraps while skipping zero.
/// Current verified types 1 and 2 retain the default spriteDirection=-1 through their supported AI paths and use
/// NPCID.Sets.SyncAnchor == Vector2.Zero in TerrariaServer 1.4.5.8.
/// </summary>
internal static class RuntimeNpcPacketProjection
{
    public static bool TryCreate(
        in NpcSnapshot npc,
        RuntimeNpcSyncKind kind,
        out TerrariaNpcUpdateState state)
    {
        if (!npc.IsActive ||
            npc.Type is not (1 or 2) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.Type, out VanillaNpcDefinition definition) ||
            npc.Target == ushort.MaxValue)
        {
            state = default;
            return false;
        }

        int life = kind == RuntimeNpcSyncKind.Despawn ? 0 : definition.LifeMax;
        NpcSimulationState simulation = npc.Simulation;
        NpcAiState ai = npc.Ai;
        state = new TerrariaNpcUpdateState(
            NpcSlot: npc.Handle.Slot,
            Generation: ToProtocolGeneration(npc.Handle.Generation),
            NpcType: npc.Type,
            PositionX: npc.PositionX,
            PositionY: npc.PositionY,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            Target: npc.Target,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            SpriteDirection: -1,
            Ai0: ai.Ai0,
            Ai1: ai.Ai1,
            Ai2: ai.Ai2,
            Ai3: ai.Ai3,
            NpcNetId: npc.NetId,
            Life: life,
            LifeMax: definition.LifeMax,
            SpawnNeedsSyncing: kind == RuntimeNpcSyncKind.Spawn);
        return state.IsValid;
    }

    internal static byte ToProtocolGeneration(NpcGeneration generation)
    {
        ulong zeroBased = (generation.Value - 1UL) % byte.MaxValue;
        return checked((byte)(zeroBased + 1UL));
    }
}
