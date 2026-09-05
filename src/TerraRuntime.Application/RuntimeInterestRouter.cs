using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime-owned routing boundary between authoritative entity state and outbound fan-out.
/// External hosts can only toggle the control surface; the active policy and spatial indexes remain
/// TerraRuntime implementation details.
/// </summary>
internal sealed class RuntimeInterestRouter
{
    private const int DefaultPlayerEnterRadiusSections = 3;
    private const int DefaultPlayerLeaveRadiusSections = 4;

    private readonly IInterestManagementControl _control;
    private readonly RuntimePlayerSpatialIndex? _players;
    private readonly RuntimePlayerVisibilityTracker? _playerVisibility;

    public RuntimeInterestRouter(
        IInterestManagementControl control,
        WorldDimensions? dimensions = null,
        int playerEnterRadiusSections = DefaultPlayerEnterRadiusSections,
        int playerLeaveRadiusSections = DefaultPlayerLeaveRadiusSections)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentOutOfRangeException.ThrowIfNegative(playerEnterRadiusSections);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLeaveRadiusSections, playerEnterRadiusSections);

        _control = control;

        if (dimensions is not null)
        {
            _players = new RuntimePlayerSpatialIndex(dimensions);
            _playerVisibility = new RuntimePlayerVisibilityTracker(
                _players,
                playerEnterRadiusSections,
                playerLeaveRadiusSections);
        }
    }

    public RuntimePlayerSpatialIndexSnapshot? PlayerSpatialSnapshot => _players?.Snapshot;

    public RuntimePlayerVisibilitySnapshot? PlayerVisibilitySnapshot => _playerVisibility?.Snapshot;

    public RuntimePlayerVisibilityUpdate TrackPlayer(
        PlayerSlotId slot,
        float positionX,
        float positionY)
    {
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[256];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[256];
        return TrackPlayer(slot, positionX, positionY, entered, left);
    }

    public RuntimePlayerVisibilityUpdate TrackPlayer(
        PlayerSlotId slot,
        float positionX,
        float positionY,
        Span<PlayerSlotId> entered,
        Span<PlayerSlotId> left)
    {
        if (_players is null || _playerVisibility is null)
            return default;

        _players.Update(slot, positionX, positionY);
        return _playerVisibility.Refresh(slot, entered, left);
    }

    public RuntimePlayerVisibilityUpdate RemovePlayer(PlayerSlotId slot)
    {
        if (_players is null || _playerVisibility is null)
            return default;

        Span<PlayerSlotId> left = stackalloc PlayerSlotId[256];
        RuntimePlayerVisibilityUpdate update = _playerVisibility.Remove(slot, left);
        _players.Remove(slot);
        return update;
    }

    public int CollectNearbyPlayers(
        PlayerSlotId subject,
        int radiusSections,
        Span<PlayerSlotId> destination,
        bool includeSubject = false) =>
        _players?.CollectNearbyPlayers(subject, radiusSections, destination, includeSubject) ?? 0;

    public bool IsPlayerVisible(PlayerSlotId observer, PlayerSlotId subject) =>
        _playerVisibility?.IsVisible(observer, subject) ?? false;

    public bool ShouldRelayPlayerMovement(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject)
    {
        _ = observer;
        _ = subject;
        return true;
    }
}

/// <summary>
/// Internal spatial input prepared by the synchronization layer. Unknown positions deliberately
/// remain representable so a future spatial policy can fail open until it has enough state to make
/// a safe visibility decision.
/// </summary>
internal readonly record struct RuntimePlayerInterestState(
    PlayerSlotId Slot,
    bool HasPosition,
    float PositionX,
    float PositionY);
