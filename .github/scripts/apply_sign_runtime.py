from pathlib import Path

path = Path("src/TerraRuntime/TerrariaServerHost.cs")
text = path.read_text(encoding="utf-8")

if "var signReplication = new RuntimeSignReplicationRegistry();" in text:
    print("sign runtime wiring already applied")
    raise SystemExit(0)


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one host pattern, found {count}: {old[:120]!r}")
    text = text.replace(old, new, 1)

replace_once(
'''        var chestReplication = new RuntimeChestReplicationRegistry();
        var chestStore = new RuntimeChestStore(world.Chests);
        var chestCommands = new RuntimeChestCommandProcessor(chestStore, chestReplication);
''',
'''        var chestReplication = new RuntimeChestReplicationRegistry();
        var chestStore = new RuntimeChestStore(world.Chests);
        var chestCommands = new RuntimeChestCommandProcessor(chestStore, chestReplication);
        var signReplication = new RuntimeSignReplicationRegistry();
        var signStore = new RuntimeSignStore(world.Signs);
        var signCommands = new RuntimeSignCommandProcessor(signStore, signReplication);
''')

replace_once(
'''        var chestAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            chestReplication,
            tileAndEntityReplicationEvents);
        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, chestAndEntityReplicationEvents);
''',
'''        var chestAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            chestReplication,
            tileAndEntityReplicationEvents);
        var signAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            signReplication,
            chestAndEntityReplicationEvents);
        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, signAndEntityReplicationEvents);
''')

replace_once(
'''            (runtime, command) =>
            {
                if (!chestCommands.TryApply(command))
                    runtime.Apply(command);
            },
''',
'''            (runtime, command) =>
            {
                if (!signCommands.TryApply(command) && !chestCommands.TryApply(command))
                    runtime.Apply(command);
            },
''')

replace_once(
'''        var projectileIngress = new RuntimeProjectileNetworkIngress(commandIngress);
        var chestIngress = new RuntimeChestNetworkIngress(commandIngress);
        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);
''',
'''        var projectileIngress = new RuntimeProjectileNetworkIngress(commandIngress);
        var chestIngress = new RuntimeChestNetworkIngress(commandIngress);
        var signIngress = new RuntimeSignNetworkIngress(commandIngress);
        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);
''')

replace_once(
'''                    projectileIngress,
                    chestIngress,
                    disconnectIngress,
''',
'''                    projectileIngress,
                    chestIngress,
                    signIngress,
                    disconnectIngress,
''')

replace_once(
'''                    tileManipulationReplication,
                    chestReplication,
                    vitalsReplication,
''',
'''                    tileManipulationReplication,
                    chestReplication,
                    signReplication,
                    vitalsReplication,
''')

replace_once(
'''        IProjectileNetworkIngress projectileIngress,
        IChestNetworkIngress chestIngress,
        RuntimePlayerDisconnectIngress disconnectIngress,
''',
'''        IProjectileNetworkIngress projectileIngress,
        IChestNetworkIngress chestIngress,
        ISignNetworkIngress signIngress,
        RuntimePlayerDisconnectIngress disconnectIngress,
''')

replace_once(
'''        RuntimeTileManipulationReplicationRegistry tileManipulationReplication,
        RuntimeChestReplicationRegistry chestReplication,
        RuntimePlayerVitalsReplicator vitalsReplication,
''',
'''        RuntimeTileManipulationReplicationRegistry tileManipulationReplication,
        RuntimeChestReplicationRegistry chestReplication,
        RuntimeSignReplicationRegistry signReplication,
        RuntimePlayerVitalsReplicator vitalsReplication,
''')

replace_once(
'''            if (!tileManipulationReplication.TryRegister(source, outbound))
            {
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            using var bootstrapSink = new PlayerBootstrapFrameSink(
''',
'''            if (!tileManipulationReplication.TryRegister(source, outbound))
            {
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!signReplication.TryRegister(source, outbound))
            {
                tileManipulationReplication.TryUnregister(source);
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            using var bootstrapSink = new PlayerBootstrapFrameSink(
''')

replace_once(
'''            var chestSink = new ChestInteractionFrameSink(
                source,
                bootstrapSink,
                projectileSink,
                chestIngress);

            try
''',
'''            var chestSink = new ChestInteractionFrameSink(
                source,
                bootstrapSink,
                projectileSink,
                chestIngress);
            var signSink = new SignInteractionFrameSink(
                source,
                bootstrapSink,
                chestSink,
                signIngress);

            try
''')

replace_once(
'''                        socket,
                        chestSink,
                        outbound,
''',
'''                        socket,
                        signSink,
                        outbound,
''')

replace_once(
'''                        $"bootstrap={bootstrapSink.StopReason}, vitals={vitalsSink.StopReason}, items={itemSink.StopReason}, projectiles={projectileSink.StopReason}, chests={chestSink.StopReason}, tiles={projectileSink.TileStopReason}, state={bootstrapSink.JoinState}; " +
''',
'''                        $"bootstrap={bootstrapSink.StopReason}, vitals={vitalsSink.StopReason}, items={itemSink.StopReason}, projectiles={projectileSink.StopReason}, chests={chestSink.StopReason}, signs={signSink.StopReason}, tiles={projectileSink.TileStopReason}, state={bootstrapSink.JoinState}; " +
''')

replace_once(
'''                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                tileManipulationReplication.TryUnregister(source);
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                worldItemReplication.TryUnregister(source);
''',
'''                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                signReplication.TryUnregister(source);
                tileManipulationReplication.TryUnregister(source);
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                worldItemReplication.TryUnregister(source);
''')

path.write_text(text, encoding="utf-8")
print("applied authoritative sign production wiring")
