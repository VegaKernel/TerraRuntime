using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum ProjectileLifecycleFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedUpdate = 2,
    MalformedDestroy = 3,
    GameIngressBackpressure = 4
}

/// <summary>
/// Connection-owned packet 27/29 ingress. Multiplicity performs protocol decoding; the socket thread only
/// applies cheap client-authority guards and queues immutable decoded state. The production gameplay ingress also
/// composes packet 17 through <see cref="TileManipulationFrameSink"/>, preserving the existing host sink chain.
/// Exact entity lookup, ownership validation and every world mutation are authoritative-thread responsibilities.
/// </summary>
public sealed class ProjectileLifecycleFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly IProjectileNetworkIngress ingress;
    private readonly TileManipulationFrameSink? tileManipulation;
    private long droppedAuthorityUpdates;

    internal ProjectileLifecycleFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        IProjectileNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("Projectile ingress requires a connection command source.", nameof(source));
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ingress);

        this.source = source;
        this.bootstrap = bootstrap;
        this.inner = inner;
        this.ingress = ingress;
        tileManipulation = ingress is ITileNetworkIngress tileIngress
            ? new TileManipulationFrameSink(source, bootstrap, inner, tileIngress)
            : null;
    }

    public ProjectileLifecycleFrameStopReason StopReason { get; private set; }

    public TileManipulationFrameStopReason TileStopReason =>
        tileManipulation?.StopReason ?? TileManipulationFrameStopReason.None;

    public TerrariaFrameRejectionCategory RejectionCategory
    {
        get
        {
            TerrariaFrameRejectionCategory own = StopReason switch
            {
                ProjectileLifecycleFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
                ProjectileLifecycleFrameStopReason.MalformedUpdate or ProjectileLifecycleFrameStopReason.MalformedDestroy => TerrariaFrameRejectionCategory.MalformedProtocol,
                ProjectileLifecycleFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
                _ => TerrariaFrameRejectionCategory.None
            };
            if (own != TerrariaFrameRejectionCategory.None)
                return own;

            if (tileManipulation is ITerrariaFrameRejectionSource tileSource &&
                tileSource.RejectionCategory != TerrariaFrameRejectionCategory.None)
            {
                return tileSource.RejectionCategory;
            }

            return inner is ITerrariaFrameRejectionSource source
                ? source.RejectionCategory
                : TerrariaFrameRejectionCategory.None;
        }
    }

    public long DroppedAuthorityUpdates => Interlocked.Read(ref droppedAuthorityUpdates);

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != ProjectileLifecycleFrameStopReason.None ||
            TileStopReason != TileManipulationFrameStopReason.None)
        {
            return TerrariaFrameSinkResult.Stop;
        }

        return (TerrariaMessageId)frame.MessageId switch
        {
            TerrariaMessageId.ProjectileNew => HandleUpdate(in frame),
            TerrariaMessageId.ProjectileDestroy => HandleDestroy(in frame),
            _ => tileManipulation?.OnFrame(in frame) ?? inner.OnFrame(in frame)
        };
    }

    private TerrariaFrameSinkResult HandleUpdate(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(ProjectileLifecycleFrameStopReason.InvalidJoinState);

        TerrariaProjectileDecodeResult decode = TerrariaProjectileDecoder.TryDecodeUpdate(
            in frame,
            out TerrariaProjectileUpdateState state);
        if (decode != TerrariaProjectileDecodeResult.Decoded)
            return Stop(ProjectileLifecycleFrameStopReason.MalformedUpdate);

        if (!VanillaProjectileIds.TryCreate(state.ProjectileType, out var type) ||
            !VanillaProjectileIds.IsLiveWireType(type) ||
            VanillaProjectileFacts.IsHostile(type) ||
            state.Key.Spawner != connection.Player.Slot.Value)
        {
            Interlocked.Increment(ref droppedAuthorityUpdates);
            return TerrariaFrameSinkResult.Continue;
        }

        return ingress.TryPostUpdate(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(ProjectileLifecycleFrameStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult HandleDestroy(in TerrariaFrame frame)
    {
        if (!TryGetPlayingConnection(out ConnectionHandle connection))
            return Stop(ProjectileLifecycleFrameStopReason.InvalidJoinState);

        TerrariaProjectileDecodeResult decode = TerrariaProjectileDecoder.TryDecodeDestroy(
            in frame,
            out TerrariaProjectileDestroyState state);
        if (decode != TerrariaProjectileDecodeResult.Decoded)
            return Stop(ProjectileLifecycleFrameStopReason.MalformedDestroy);

        return ingress.TryPostDestroy(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(ProjectileLifecycleFrameStopReason.GameIngressBackpressure);
    }

    private bool TryGetPlayingConnection(out ConnectionHandle connection)
    {
        if (bootstrap.JoinState == PlayerJoinState.Playing &&
            bootstrap.AssignedPlayerHandle is PlayerHandle player)
        {
            connection = new ConnectionHandle(source, player);
            return true;
        }

        connection = default;
        return false;
    }

    private TerrariaFrameSinkResult Stop(ProjectileLifecycleFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
