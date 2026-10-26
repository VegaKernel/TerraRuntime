using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    internal void BindAnglerWorld(WorldFileRuntimeMetadata metadata,
        Action<ConnectionHandle, byte, bool, int, int>? publisher = null,
        Action<ConnectionHandle, int, int>? progressPublisher = null)
    {
        if (_runtime.WorldItems.SpawnRandom is not SystemWorldItemSpawnRandom source)
            throw new InvalidOperationException("Angler authority requires the existing source Main.rand owner.");
        _runtime.Players.BindAnglerWorld(metadata, _runtime.WorldProgression, source.SourceRandom, publisher, progressPublisher);
        _runtime.WorldProgression.SetSlimeRedSpawnBaseline(metadata.UnlockedSlimeRedSpawn);
        _runtime.WorldClock?.BindAnglerOwner(_runtime.Players);
    }

    internal WorldAnglerQuestData1458? CaptureAnglerSaveState() => _runtime.Players.CaptureAnglerSaveState();
    internal void BindFishingWorldInfoPublisher(Action publisher) => _runtime.Npcs.BindFishingWorldInfoPublisher(publisher);
    internal bool TryCompleteAnglerQuest(ConnectionHandle connection) => _runtime.Players.TryCompleteAnglerQuest(connection);
    internal void PublishAnglerState(ConnectionHandle connection) => _runtime.Players.PublishAnglerState(connection);
    internal bool TryCaptureAnglerState(ConnectionHandle connection, out byte quest, out bool completed, out int count) =>
        _runtime.Players.TryCaptureAnglerState(connection, out quest, out completed, out count);
}
