namespace TerraRuntime.Operations;

internal readonly record struct RuntimeNpcSnapshot(
    byte Slot,
    ulong Generation,
    ulong Revision,
    int Type,
    short NetId,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    float Ai0,
    float Ai1,
    float Ai2,
    float Ai3,
    int DirectionX,
    int DirectionY,
    bool CollideX,
    bool CollideY,
    bool Wet,
    bool NoGravity,
    bool NoTileCollide);

internal readonly record struct RuntimeNpcsSnapshot(
    ReadOnlyMemory<RuntimeNpcSnapshot> Npcs,
    long CommittedSpawns,
    long CommittedUpdates,
    long CommittedDespawns,
    DateTimeOffset CapturedAtUtc);
