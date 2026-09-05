using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal enum RuntimeNpcSyncKind : byte
{
    Spawn = 0,
    Update = 1,
    Despawn = 2
}

/// <summary>
/// Converts generation-safe runtime NPC state into the protocol-neutral packet-23 projection.
/// Runtime generations remain monotonic ulongs; vanilla wire generation is 1..255 and skips zero.
/// Each admitted definition supplies the TerrariaServer 1.4.5.8 NPCID.Sets.SyncAnchor used by the wire position.
/// </summary>
internal static class RuntimeNpcPacketProjection
{
    public static bool TryCreate(
        in NpcSnapshot npc,
        RuntimeNpcSyncKind kind,
        out TerrariaNpcUpdateState state)
    {
        if (!npc.IsActive)
        {
            state = default;
            return false;
        }

        NpcTypeId npcType = npc.TypeIdentity;
        if (!VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.SyncAnchor.IsValid ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            npc.Target == ushort.MaxValue)
        {
            state = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        int lifeMax = simulation.LifeMax > 0 ? simulation.LifeMax : definition.LifeMax;
        int life = kind == RuntimeNpcSyncKind.Despawn
            ? 0
            : simulation.LifeMax > 0 ? simulation.Life : definition.LifeMax;
        NpcAiState ai = npc.Ai;
        NpcNetId netIdentity = npc.NetIdentity;
        state = new TerrariaNpcUpdateState(
            NpcSlot: npc.Handle.Slot,
            Generation: ToProtocolGeneration(npc.Handle.Generation),
            NpcType: npcType.Value,
            PositionX: npc.PositionX + hitbox.Width * definition.SyncAnchor.X,
            PositionY: npc.PositionY + hitbox.Height * definition.SyncAnchor.Y,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            Target: npc.Target,
            DirectionX: simulation.DirectionX,
            DirectionY: simulation.DirectionY,
            SpriteDirection: simulation.SpriteDirection,
            Ai0: ai.Ai0,
            Ai1: ai.Ai1,
            Ai2: ai.Ai2,
            Ai3: ai.Ai3,
            NpcNetId: checked((short)netIdentity.Value),
            Life: life,
            LifeMax: lifeMax,
            SpawnNeedsSyncing: kind == RuntimeNpcSyncKind.Spawn);
        return state.IsValid;
    }

    internal static byte ToProtocolGeneration(NpcGeneration generation)
    {
        ulong zeroBased = (generation.Value - 1UL) % byte.MaxValue;
        return checked((byte)(zeroBased + 1UL));
    }
}
