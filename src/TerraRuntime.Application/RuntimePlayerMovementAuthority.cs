using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Generation-safe server-owned movement history and conservative packet-13 validation.
/// The production boundary hard-rejects impossible velocity and stale generations. Position
/// discontinuities are observed by default until every legitimate teleport/respawn producer
/// can grant an explicit server permit; tests can enable strict position enforcement.
/// </summary>
internal sealed class RuntimePlayerMovementAuthority
{
    internal const float MaximumAbsoluteVelocity = 128f;
    internal const float PositionGracePixels = 384f;
    internal const float MaximumTravelPixelsPerSecond = 4096f;
    internal static readonly TimeSpan MaximumTravelWindow = TimeSpan.FromSeconds(2);

    private readonly object _generationGate = new();
    private readonly Dictionary<byte, ulong> _latestGenerationBySlot = [];
    private readonly Dictionary<PlayerHandle, MovementTrack> _tracks = [];
    private readonly TimeProvider _timeProvider;
    private readonly bool _enforcePositionDiscontinuities;

    private long _accepted;
    private long _queueRejected;
    private long _staleGenerationRejected;
    private long _velocityRejected;
    private long _positionViolations;
    private long _positionRejected;
    private long _exceptionalAccepted;

    public RuntimePlayerMovementAuthority(
        bool enforcePositionDiscontinuities = false,
        TimeProvider? timeProvider = null)
    {
        _enforcePositionDiscontinuities = enforcePositionDiscontinuities;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RuntimePlayerMovementAuthoritySnapshot CaptureSnapshot() =>
        new(
            Accepted: Interlocked.Read(ref _accepted),
            QueueRejected: Interlocked.Read(ref _queueRejected),
            StaleGenerationRejected: Interlocked.Read(ref _staleGenerationRejected),
            VelocityRejected: Interlocked.Read(ref _velocityRejected),
            PositionViolations: Interlocked.Read(ref _positionViolations),
            PositionRejected: Interlocked.Read(ref _positionRejected),
            ExceptionalAccepted: Interlocked.Read(ref _exceptionalAccepted),
            TrackedPlayers: CaptureTrackedCount(),
            PositionEnforcementEnabled: _enforcePositionDiscontinuities);

    public bool TryValidateAndPost(
        ConnectionHandle connection,
        in PlayerMovementCommitRequest request,
        Func<bool> post)
    {
        ArgumentNullException.ThrowIfNull(post);

        if (!TryGetCurrentTrack(connection.Player, out MovementTrack track))
        {
            Interlocked.Increment(ref _staleGenerationRejected);
            return false;
        }

        lock (track.Gate)
        {
            if (!IsVelocityPlausible(in request))
            {
                Interlocked.Increment(ref _velocityRejected);
                return false;
            }

            long now = _timeProvider.GetTimestamp();
            bool positionViolation = track.HasAccepted &&
                IsPositionDiscontinuity(track, in request, now);
            bool exceptional = positionViolation &&
                TryConsumeMatchingPermit(track, in request, now);

            if (positionViolation)
            {
                Interlocked.Increment(ref _positionViolations);
                if (_enforcePositionDiscontinuities && !exceptional)
                {
                    Interlocked.Increment(ref _positionRejected);
                    return false;
                }
            }

            if (!post())
            {
                Interlocked.Increment(ref _queueRejected);
                return false;
            }

            track.HasAccepted = true;
            track.PositionX = request.PositionX;
            track.PositionY = request.PositionY;
            track.VelocityX = request.HasVelocity ? request.VelocityX : 0f;
            track.VelocityY = request.HasVelocity ? request.VelocityY : 0f;
            track.MountType = request.HasMount ? request.MountType : (ushort)0;
            track.AcceptedTimestamp = now;
            Interlocked.Increment(ref _accepted);
            if (exceptional)
                Interlocked.Increment(ref _exceptionalAccepted);
            return true;
        }
    }

    /// <summary>
    /// Grants one short-lived server-owned exception for a known movement discontinuity.
    /// A target may be supplied for teleports/respawns; permits are single-use.
    /// </summary>
    public bool TryGrantException(
        ConnectionHandle connection,
        RuntimePlayerMovementExceptionKind kind,
        TimeSpan validity,
        float? targetX = null,
        float? targetY = null,
        float targetRadiusPixels = 512f)
    {
        if (!connection.IsAssigned ||
            kind == RuntimePlayerMovementExceptionKind.None ||
            validity <= TimeSpan.Zero ||
            validity > TimeSpan.FromSeconds(10) ||
            !float.IsFinite(targetRadiusPixels) ||
            targetRadiusPixels < 0f ||
            targetX.HasValue != targetY.HasValue ||
            targetX is float x && !float.IsFinite(x) ||
            targetY is float y && !float.IsFinite(y))
        {
            return false;
        }

        if (!TryGetCurrentTrack(connection.Player, out MovementTrack track))
            return false;

        lock (track.Gate)
        {
            long now = _timeProvider.GetTimestamp();
            long expiry;
            try
            {
                expiry = checked(now + ToTimestampDelta(validity));
            }
            catch (OverflowException)
            {
                return false;
            }

            track.Permit = new MovementPermit(
                kind,
                expiry,
                targetX,
                targetY,
                targetRadiusPixels);
            return true;
        }
    }

    public bool TryForget(ConnectionHandle connection)
    {
        if (!connection.Player.IsAssigned)
            return false;

        lock (_generationGate)
            return _tracks.Remove(connection.Player);
    }

    private bool TryGetCurrentTrack(PlayerHandle player, out MovementTrack track)
    {
        track = null!;
        if (!player.IsAssigned)
            return false;

        lock (_generationGate)
        {
            byte slot = player.Slot.Value;
            ulong generation = player.Generation.Value;
            if (_latestGenerationBySlot.TryGetValue(slot, out ulong latest))
            {
                if (generation < latest)
                    return false;

                if (generation > latest)
                {
                    RemoveSlotTracks(slot);
                    _latestGenerationBySlot[slot] = generation;
                }
            }
            else
            {
                _latestGenerationBySlot.Add(slot, generation);
            }

            if (_tracks.TryGetValue(player, out MovementTrack? existing))
            {
                track = existing;
                return true;
            }

            track = new MovementTrack();
            _tracks.Add(player, track);
            return true;
        }
    }

    private void RemoveSlotTracks(byte slot)
    {
        if (_tracks.Count == 0)
            return;

        var stale = new List<PlayerHandle>();
        foreach (PlayerHandle player in _tracks.Keys)
        {
            if (player.Slot.Value == slot)
                stale.Add(player);
        }

        foreach (PlayerHandle player in stale)
            _tracks.Remove(player);
    }

    private int CaptureTrackedCount()
    {
        lock (_generationGate)
            return _tracks.Count;
    }

    private bool IsPositionDiscontinuity(
        MovementTrack track,
        in PlayerMovementCommitRequest request,
        long now)
    {
        TimeSpan elapsed = now > track.AcceptedTimestamp
            ? _timeProvider.GetElapsedTime(track.AcceptedTimestamp, now)
            : TimeSpan.Zero;
        double seconds = Math.Clamp(
            elapsed.TotalSeconds,
            0d,
            MaximumTravelWindow.TotalSeconds);
        float allowed = PositionGracePixels +
            (float)(MaximumTravelPixelsPerSecond * seconds);

        float dx = request.PositionX - track.PositionX;
        float dy = request.PositionY - track.PositionY;
        float distanceSquared = (dx * dx) + (dy * dy);
        if (!float.IsFinite(distanceSquared))
            return true;

        return distanceSquared > allowed * allowed;
    }

    private static bool IsVelocityPlausible(in PlayerMovementCommitRequest request)
    {
        if (!request.HasVelocity)
            return true;

        return MathF.Abs(request.VelocityX) <= MaximumAbsoluteVelocity &&
            MathF.Abs(request.VelocityY) <= MaximumAbsoluteVelocity;
    }

    private bool TryConsumeMatchingPermit(
        MovementTrack track,
        in PlayerMovementCommitRequest request,
        long now)
    {
        MovementPermit? permit = track.Permit;
        if (permit is null)
            return false;

        track.Permit = null;
        MovementPermit value = permit.Value;
        if (now > value.ExpiresTimestamp)
            return false;

        if (value.TargetX is not float targetX || value.TargetY is not float targetY)
            return true;

        float dx = request.PositionX - targetX;
        float dy = request.PositionY - targetY;
        float distanceSquared = (dx * dx) + (dy * dy);
        return float.IsFinite(distanceSquared) &&
            distanceSquared <= value.TargetRadiusPixels * value.TargetRadiusPixels;
    }

    private long ToTimestampDelta(TimeSpan duration)
    {
        double ticks = duration.TotalSeconds * _timeProvider.TimestampFrequency;
        if (!double.IsFinite(ticks) || ticks <= 0d || ticks > long.MaxValue)
            throw new OverflowException();
        return checked((long)Math.Ceiling(ticks));
    }

    private sealed class MovementTrack
    {
        public object Gate { get; } = new();
        public bool HasAccepted { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public ushort MountType { get; set; }
        public long AcceptedTimestamp { get; set; }
        public MovementPermit? Permit { get; set; }
    }

    private readonly record struct MovementPermit(
        RuntimePlayerMovementExceptionKind Kind,
        long ExpiresTimestamp,
        float? TargetX,
        float? TargetY,
        float TargetRadiusPixels);
}

internal enum RuntimePlayerMovementExceptionKind : byte
{
    None = 0,
    Teleport = 1,
    Respawn = 2,
    MountTransition = 3,
    ServerCorrection = 4
}

internal readonly record struct RuntimePlayerMovementAuthoritySnapshot(
    long Accepted,
    long QueueRejected,
    long StaleGenerationRejected,
    long VelocityRejected,
    long PositionViolations,
    long PositionRejected,
    long ExceptionalAccepted,
    int TrackedPlayers,
    bool PositionEnforcementEnabled);
