using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>Read-only physical NPC slot data, retained after despawn for source-compatible parent reads.</summary>
internal readonly record struct VanillaNpcRetainedSlot(byte Slot, bool IsActive, NpcStateUpdate State)
{
    public int Type => State.Type;
    public short NetId => State.NetId;
    public NpcTypeId TypeIdentity => new(Type);
    public float PositionX => State.PositionX;
    public float PositionY => State.PositionY;
    public float VelocityX => State.VelocityX;
    public float VelocityY => State.VelocityY;
    public NpcAiState Ai => State.Ai;
    public NpcSimulationState Simulation => State.Simulation;
}
