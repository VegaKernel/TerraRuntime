namespace TerraRuntime.Contracts.Runtime;

/// <summary>One thread-safe inbound packet policy shared by all connections, with separate accounting per connection.</summary>
public interface IPacketRateLimitControl
{
    /// <summary>Returns the configured frames per second, or null when no configurable limit is set.</summary>
    int? GetLimit(byte messageId);

    /// <summary>
    /// Sets a positive frames-per-second limit for this packet ID on every existing and future connection.
    /// Null removes the configurable limit. Changing policy does not reset connection counters.
    /// Uses fixed one-second windows; existing hard-abuse limits remain independent.
    /// </summary>
    void SetLimit(byte messageId, int? framesPerSecond);
}
