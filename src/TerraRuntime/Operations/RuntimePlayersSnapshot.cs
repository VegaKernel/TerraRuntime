namespace TerraRuntime.Operations;

internal readonly record struct RuntimePlayerSnapshot(
    long ConnectionId,
    byte Slot,
    ulong Generation,
    string Name,
    byte Team,
    float PositionX,
    float PositionY,
    bool HasHealth,
    short Life,
    short MaxLife,
    bool HasMana,
    short Mana,
    short MaxMana);

internal readonly record struct RuntimePlayersSnapshot(
    ReadOnlyMemory<RuntimePlayerSnapshot> Players,
    DateTimeOffset CapturedAtUtc);
