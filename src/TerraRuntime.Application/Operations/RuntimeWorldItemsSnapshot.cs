namespace TerraRuntime.Application.Operations;

internal readonly record struct RuntimeWorldItemGroupSnapshot(
    short ItemNetId,
    int DropCount,
    long TotalStack,
    int ReservedDrops,
    int ShimmeredDrops,
    short MaxStack,
    float AveragePositionX,
    float AveragePositionY);

internal readonly record struct RuntimeWorldItemsSnapshot(
    int ActiveItems,
    ReadOnlyMemory<RuntimeWorldItemGroupSnapshot> Groups,
    DateTimeOffset CapturedAtUtc);
