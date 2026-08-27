using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed class ServerRuntimeState
{
    private int lastWorkerResult;
    private int lastSpawnCommitResult = -1;

    public long AppliedCommands { get; private set; }

    public long Updates { get; private set; }

    public long CommittedPlayerSpawns { get; private set; }

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult
    {
        get
        {
            int value = Volatile.Read(ref lastSpawnCommitResult);
            return value < 0 ? null : (PlayerSpawnCommitResult)value;
        }
    }

    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;

        switch (command)
        {
            case WorkerResultCommand result:
                Volatile.Write(ref lastWorkerResult, result.Value);
                break;

            case PlayerSpawnRuntimeCommand spawn:
                PlayerSpawnCommitResult commit = spawn.Session.TryCommitSpawn(spawn.Request.ClaimedSlot);
                Volatile.Write(ref lastSpawnCommitResult, (int)commit);
                if (commit == PlayerSpawnCommitResult.Committed)
                    CommittedPlayerSpawns++;
                break;
        }
    }

    public void Tick()
    {
        Updates++;
    }
}

internal abstract record RuntimeCommand;

internal sealed record ProbeCommand : RuntimeCommand;

internal sealed record WorkerResultCommand(int Value) : RuntimeCommand;
