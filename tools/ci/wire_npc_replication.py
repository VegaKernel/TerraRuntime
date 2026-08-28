from pathlib import Path

path = Path("src/TerraRuntime/TerrariaServerHost.cs")
text = path.read_text()

replacements = [
    (
        """        var worldItems = new RuntimeWorldItemStore();
        var runtimeConnections = new RuntimeConnectionRegistry(
            runtimeInterestManagement,
            world.Header.Dimensions);
        var vitalsReplication = new RuntimePlayerVitalsReplicator();
        var playerOperations = new RuntimePlayerOperationsTelemetry();
        var playerEvents = new RuntimePlayerEventDispatcher(
            runtimeConnections,
            vitalsReplication,
            playerOperations);
        var state = new ServerRuntimeState(playerEvents, worldTiles: world.Tiles);
""",
        """        var worldItems = new RuntimeWorldItemStore();
        var runtimeConnections = new RuntimeConnectionRegistry(
            runtimeInterestManagement,
            world.Header.Dimensions);
        var npcReplication = new RuntimeNpcReplicationRegistry();
        var npcStore = new RuntimeNpcStore(commitSink: npcReplication);
        var vitalsReplication = new RuntimePlayerVitalsReplicator();
        var playerOperations = new RuntimePlayerOperationsTelemetry();
        var playerNetworkEvents = new RuntimePlayerEventDispatcher(
            runtimeConnections,
            vitalsReplication,
            playerOperations);
        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, npcReplication);
        var state = new ServerRuntimeState(playerEvents, npcs: npcStore, worldTiles: world.Tiles);
""",
    ),
    (
        """                    runtimeConnections,
                    vitalsReplication,
                    worldItems,
""",
        """                    runtimeConnections,
                    npcReplication,
                    vitalsReplication,
                    worldItems,
""",
    ),
    (
        """        RuntimeConnectionRegistry runtimeConnections,
        RuntimePlayerVitalsReplicator vitalsReplication,
""",
        """        RuntimeConnectionRegistry runtimeConnections,
        RuntimeNpcReplicationRegistry npcReplication,
        RuntimePlayerVitalsReplicator vitalsReplication,
""",
    ),
    (
        """            if (!runtimeConnections.TryRegister(source, outbound))
            {
                socket.Dispose();
                return;
            }

            if (!queueTelemetry.TryRegister(connectionId, outbound))
            {
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }
""",
        """            if (!runtimeConnections.TryRegister(source, outbound))
            {
                socket.Dispose();
                return;
            }

            if (!npcReplication.TryRegister(source, outbound))
            {
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!queueTelemetry.TryRegister(connectionId, outbound))
            {
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }
""",
    ),
    (
        """                queueTelemetry.TryUnregister(connectionId);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
""",
        """                queueTelemetry.TryUnregister(connectionId);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
""",
    ),
    (
        """                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
""",
        """                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
""",
    ),
    (
        """                vitalsReplication.TryUnregister(source);
                if (runtimeConnections.TryUnregister(source, out PlayerHandle? playingPlayer) &&
""",
        """                vitalsReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                if (runtimeConnections.TryUnregister(source, out PlayerHandle? playingPlayer) &&
""",
    ),
]

for index, (old, new) in enumerate(replacements, 1):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"replacement {index} expected exactly once, found {count}")
    text = text.replace(old, new, 1)

path.write_text(text)
