using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>One owner-published packet-7/optional-78 pair. Socket readers never access live owners.</summary>
internal sealed record WorldBootstrapSnapshot1458(
    WorldFileData World,
    WorldInfoRuntimeState Runtime,
    WorldInfoTransientState Transient,
    TerrariaInvasionProgressState? Progress)
{
    // The mapper reads only immutable Header/RuntimeMetadata, never the retained mutable tile store.
    internal ReadOnlyMemory<byte> WorldInfoFrame => PlayerJoinFrameEncoder.EncodeWorldInfo(World, Transient, Runtime);
    internal ReadOnlyMemory<byte> ProgressFrame => Progress is { } value &&
        TerrariaInvasionProgressCodec.TryEncode(in value, out var frame) ? frame : default;

    internal (ReadOnlyMemory<byte> WorldInfo, ReadOnlyMemory<byte> Progress) EncodeFrames() =>
        (WorldInfoFrame, ProgressFrame);
}

public sealed partial class WorldRuntime
{
    private WorldBootstrapSnapshot1458? bootstrapSnapshot;
    private readonly bool bootstrapSkyblockLowTiles;

    internal WorldBootstrapSnapshot1458 CaptureWorldBootstrap() =>
        Volatile.Read(ref bootstrapSnapshot) ?? throw new InvalidOperationException("World bootstrap is not initialized.");

    // Transfers execute on the owner thread; ordinary packet-6/8 readers use CaptureWorldBootstrap only.
    internal WorldBootstrapSnapshot1458 CaptureFreshWorldBootstrap()
    {
        RefreshWorldBootstrap();
        return CaptureWorldBootstrap();
    }

    internal void RefreshWorldBootstrap()
    {
        bool invasionKnown = Invasion.TryCapture(out var invasion);
        var runtime = new WorldInfoRuntimeState(
            checked((int)Math.Clamp(WorldClock.Time, 0d, int.MaxValue)),
            WorldClock.DayTime, checked((byte)WorldClock.MoonPhase),
            WorldClock.BloodMoonActive, WorldClock.SlimeRainActive)
        {
            WindSpeedTarget = WorldClock.WindSpeedTarget,
            Rain = WorldClock.NetworkRain,
            InvasionType = invasionKnown ? checked((sbyte)invasion.State.Type) : null,
            ProgressionMutations = WorldProgression.CaptureSnapshot()
        };
        var transient = new WorldInfoTransientState(WorldClock.PumpkinMoonActive, WorldClock.SnowMoonActive,
            false, false, bootstrapSkyblockLowTiles, 0);
        TerrariaInvasionProgressState? progress = null;
        // Main.SyncAnInvasion prioritizes Snow Moon, Pumpkin Moon, DD2, then ordinary invasions.
        // DD2 and unsupported loaded invasion identities have no fabricated progress projection here.
        if (WorldClock.MoonEventActive)
        {
            progress = new(checked((int)WorldClock.MoonEventWaveKills), WorldClock.MoonEventWaveRequirement,
                WorldClock.SnowMoonActive ? (sbyte)1 : (sbyte)2, checked((sbyte)WorldClock.MoonEventWaveNumber));
        }
        else if (invasionKnown && invasion.State.Type > 0)
        {
            long value = (long)invasion.State.SizeStart - invasion.State.Size;
            if (value is >= int.MinValue and <= int.MaxValue)
                progress = new((int)value, invasion.State.SizeStart == 0 ? 1 : invasion.State.SizeStart,
                    checked((sbyte)(invasion.State.Type + 3)), 0);
        }
        var previous = Volatile.Read(ref bootstrapSnapshot);
        if (previous is not null && previous.Runtime == runtime && previous.Transient == transient && previous.Progress == progress)
            return;
        var snapshot = new WorldBootstrapSnapshot1458(World, runtime, transient, progress);
        Volatile.Write(ref bootstrapSnapshot, snapshot);
    }
}
