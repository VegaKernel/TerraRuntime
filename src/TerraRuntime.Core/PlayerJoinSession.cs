using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Connection-owned bootstrap state for one leased Terraria player slot.
/// State values intentionally match the observable server-side Netplay states used by Terraria 1.4.5.8.
/// </summary>
public enum PlayerJoinState : sbyte
{
    AwaitingWorldRequest = 1,
    AwaitingSectionRequest = 2,
    AwaitingSpawn = 3,
    Playing = 10,
    Closed = -2
}

public enum PlayerJoinTransition : byte
{
    None = 0,
    WorldRequestAccepted = 1,
    SectionRequestAccepted = 2,
    EnteredPlayingState = 3,
    Closed = 4
}

/// <summary>
/// Owns a player-slot lease from the moment a valid Hello is accepted until the connection closes.
/// It models vanilla bootstrap transitions but intentionally contains no packet IDs or socket concerns.
/// </summary>
public sealed class PlayerJoinSession : IDisposable
{
    private readonly object _gate = new();
    private PlayerSlotPool.PlayerSlotLease? _slotLease;
    private PlayerJoinState _state;

    public PlayerJoinSession(PlayerSlotPool.PlayerSlotLease slotLease)
    {
        ArgumentNullException.ThrowIfNull(slotLease);
        if (slotLease.IsReleased)
        {
            throw new ArgumentException("A released player-slot lease cannot start a join session.", nameof(slotLease));
        }

        _slotLease = slotLease;
        _state = PlayerJoinState.AwaitingWorldRequest;
    }

    public PlayerSlotId Slot
    {
        get
        {
            lock (_gate)
            {
                ThrowIfClosed();
                return _slotLease!.Slot;
            }
        }
    }

    public PlayerJoinState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Mirrors vanilla packet-6 handling: the response is valid in later states too, while only state 1 advances to 2.
    /// </summary>
    public PlayerJoinTransition ObserveWorldRequest()
    {
        lock (_gate)
        {
            if (_state == PlayerJoinState.Closed)
            {
                return PlayerJoinTransition.None;
            }

            if (_state == PlayerJoinState.AwaitingWorldRequest)
            {
                _state = PlayerJoinState.AwaitingSectionRequest;
                return PlayerJoinTransition.WorldRequestAccepted;
            }

            return PlayerJoinTransition.None;
        }
    }

    /// <summary>
    /// Mirrors vanilla packet-8 handling: sections may be requested repeatedly, while only state 2 advances to 3.
    /// </summary>
    public PlayerJoinTransition ObserveSectionRequest()
    {
        lock (_gate)
        {
            if (_state == PlayerJoinState.Closed)
            {
                return PlayerJoinTransition.None;
            }

            if (_state == PlayerJoinState.AwaitingSectionRequest)
            {
                _state = PlayerJoinState.AwaitingSpawn;
                return PlayerJoinTransition.SectionRequestAccepted;
            }

            return PlayerJoinTransition.None;
        }
    }

    /// <summary>
    /// Mirrors vanilla packet-12 handling: only the first spawn received in state 3 commits the client to state 10.
    /// Earlier spawn packets do not skip bootstrap stages; later spawn packets do not re-enter the session.
    /// </summary>
    public PlayerJoinTransition ObserveSpawn()
    {
        lock (_gate)
        {
            if (_state == PlayerJoinState.AwaitingSpawn)
            {
                _state = PlayerJoinState.Playing;
                return PlayerJoinTransition.EnteredPlayingState;
            }

            return PlayerJoinTransition.None;
        }
    }

    public PlayerJoinTransition Close()
    {
        PlayerSlotPool.PlayerSlotLease? lease;
        lock (_gate)
        {
            if (_state == PlayerJoinState.Closed)
            {
                return PlayerJoinTransition.None;
            }

            _state = PlayerJoinState.Closed;
            lease = _slotLease;
            _slotLease = null;
        }

        lease?.Dispose();
        return PlayerJoinTransition.Closed;
    }

    public void Dispose() => Close();

    private void ThrowIfClosed()
    {
        if (_state == PlayerJoinState.Closed || _slotLease is null)
        {
            throw new ObjectDisposedException(nameof(PlayerJoinSession));
        }
    }
}
