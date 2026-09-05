using System.Collections.Concurrent;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeConnectionSessionSnapshot(
    long ConnectionId,
    string RemoteAddress,
    int RemotePort,
    DateTimeOffset ConnectedAtUtc);

/// <summary>
/// Process-local, ephemeral connection-session metadata. This intentionally has no persistence path: it exists only
/// to support live operator inspection while a socket is connected and is removed when that connection stops.
/// </summary>
internal sealed class RuntimeConnectionSessionDirectory
{
    private readonly ConcurrentDictionary<long, RuntimeConnectionSessionSnapshot> sessions = new();

    public void Register(long connectionId, string remoteAddress, int remotePort, DateTimeOffset connectedAtUtc)
    {
        if (connectionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectionId));
        sessions[connectionId] = new RuntimeConnectionSessionSnapshot(
            connectionId,
            string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress,
            remotePort,
            connectedAtUtc);
    }

    public bool TryGet(long connectionId, out RuntimeConnectionSessionSnapshot snapshot) =>
        sessions.TryGetValue(connectionId, out snapshot);

    public void Unregister(long connectionId) => sessions.TryRemove(connectionId, out _);
}
