from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one replacement target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


host_contract = Path("src/TerraRuntime.HostContracts/ITerraRuntimeHostRuntime.cs")
host_runtime = Path("src/TerraRuntime/TerraRuntimeHostRuntime.cs")
state = Path("src/TerraRuntime/ServerRuntimeState.cs")
server_host = Path("src/TerraRuntime/TerrariaServerHost.cs")
host_tests = Path("tests/TerraRuntime.Tests/TrustedHostModuleLoaderTests.cs")

replace_once(
    host_contract,
    """    IPlayerStateSnapshotReader PlayerStates { get; }\n    INpcActorOperations NpcActors { get; }\n""",
    """    IPlayerStateSnapshotReader PlayerStates { get; }\n    INpcActorOperations NpcActors { get; }\n    IServerPlayerOperations ServerPlayers { get; }\n""",
)

replace_once(
    host_runtime,
    """        NpcActors = new RuntimeNpcActorOperations(runtimePlayerStates.CommandIngress);\n    }\n\n    public TerraRuntimeHostRuntimeInfo Info { get; }\n    public IInterestManagementControl InterestManagement { get; }\n    public IPlayerStateSnapshotReader PlayerStates { get; }\n    public INpcActorOperations NpcActors { get; }\n""",
    """        NpcActors = new RuntimeNpcActorOperations(runtimePlayerStates.CommandIngress);\n        ServerPlayers = new RuntimeServerPlayerOperations(runtimePlayerStates.CommandIngress);\n    }\n\n    public TerraRuntimeHostRuntimeInfo Info { get; }\n    public IInterestManagementControl InterestManagement { get; }\n    public IPlayerStateSnapshotReader PlayerStates { get; }\n    public INpcActorOperations NpcActors { get; }\n    public IServerPlayerOperations ServerPlayers { get; }\n""",
)

replace_once(
    state,
    """    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;\n    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n""",
    """    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;\n    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n    private readonly RuntimeServerPlayerCommandService? _serverPlayerCommands;\n""",
)

replace_once(
    state,
    """        RuntimeProjectileReplicationRegistry? projectileReplication = null,\n        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,\n        RuntimeServerPlayerStateStore? serverPlayerStates = null)\n""",
    """        RuntimeProjectileReplicationRegistry? projectileReplication = null,\n        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,\n        RuntimeServerPlayerStateStore? serverPlayerStates = null,\n        RuntimeServerPlayerSlotRegistry? serverPlayerIdentities = null)\n""",
)

replace_once(
    state,
    """        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs);\n        _serverPlayerStates = serverPlayerStates;\n        _npcActorControls = new RuntimeNpcActorControlRegistry(_npcs);\n""",
    """        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs);\n        _serverPlayerStates = serverPlayerStates;\n        if (serverPlayerIdentities is not null && serverPlayerStates is null)\n            throw new ArgumentException(\"Server-player identities require an authoritative state store.\", nameof(serverPlayerIdentities));\n        _serverPlayerCommands = serverPlayerIdentities is not null && serverPlayerStates is not null\n            ? new RuntimeServerPlayerCommandService(serverPlayerIdentities, serverPlayerStates)\n            : null;\n        _npcActorControls = new RuntimeNpcActorControlRegistry(_npcs);\n""",
)

replace_once(
    state,
    """        AppliedCommands++;\n\n        if (_npcActorCommands.TryApply(command))\n            return;\n""",
    """        AppliedCommands++;\n\n        if (_serverPlayerCommands?.TryApply(command) == true)\n            return;\n        if (_npcActorCommands.TryApply(command))\n            return;\n""",
)

replace_once(
    server_host,
    """            projectileReplication: projectileReplication,\n            tileManipulationReplication: tileManipulationReplication,\n            serverPlayerStates: serverPlayerStates);\n""",
    """            projectileReplication: projectileReplication,\n            tileManipulationReplication: tileManipulationReplication,\n            serverPlayerStates: serverPlayerStates,\n            serverPlayerIdentities: serverPlayerIdentities);\n""",
)

replace_once(
    host_tests,
    """            interestManagement,\n            new TestPlayerStateSnapshotReader(),\n            new TestNpcActorOperations());\n""",
    """            interestManagement,\n            new TestPlayerStateSnapshotReader(),\n            new TestNpcActorOperations(),\n            new TestServerPlayerOperations());\n""",
)

replace_once(
    host_tests,
    """        IInterestManagementControl InterestManagement,\n        IPlayerStateSnapshotReader PlayerStates,\n        INpcActorOperations NpcActors) : ITerraRuntimeHostRuntime;\n""",
    """        IInterestManagementControl InterestManagement,\n        IPlayerStateSnapshotReader PlayerStates,\n        INpcActorOperations NpcActors,\n        IServerPlayerOperations ServerPlayers) : ITerraRuntimeHostRuntime;\n""",
)

replace_once(
    host_tests,
    """    private sealed class TestNpcActorOperations : INpcActorOperations\n""",
    """    private sealed class TestServerPlayerOperations : IServerPlayerOperations\n    {\n        public ValueTask<ServerPlayerCreateResult> CreateAsync(\n            ServerPlayerId id,\n            float positionX,\n            float positionY,\n            CancellationToken cancellationToken = default)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n            return ValueTask.FromResult(new ServerPlayerCreateResult(ServerPlayerCreateStatus.NoAvailableSlot, default));\n        }\n\n        public ValueTask<bool> DespawnAsync(\n            ServerPlayerId id,\n            CancellationToken cancellationToken = default)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n            return ValueTask.FromResult(false);\n        }\n    }\n\n    private sealed class TestNpcActorOperations : INpcActorOperations\n""",
)

print("G6 server-player host API wiring applied")
