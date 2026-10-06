using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeInvasionCapture1458(long Revision, InvasionState1458 State);

/// <summary>Authoritative scalar owner; planning and publication remain separate synchronous phases.</summary>
internal sealed class RuntimeWorldInvasion1458
{
    private InvasionState1458? state;
    private long revision;

    internal RuntimeWorldInvasion1458(InvasionState1458? initial, int maximumTilesX = 0, int spawnTileX = 0)
    {
        // Unsupported loaded metadata is preserved by the raw world image, not reconstructed here.
        MaximumTilesX = maximumTilesX;
        SpawnTileX = spawnTileX;
        state = initial is { } value && VanillaInvasionLifecycle1458.CanOwnLive(in value) ? value : null;
    }

    internal int MaximumTilesX { get; }
    internal int SpawnTileX { get; }

    internal static RuntimeWorldInvasion1458 FromMetadata(WorldFileRuntimeMetadata metadata, int maximumTilesX) => new(
        new InvasionState1458(metadata.InvasionType, metadata.InvasionSize, metadata.InvasionSizeStart,
            metadata.InvasionDelay, metadata.InvasionX, 0, 0, 0, 0, 0,
            metadata.DownedGoblins, metadata.DownedPirates, metadata.DownedMartians, metadata.LanternNightNextNight),
        maximumTilesX, metadata.SpawnX);

    internal bool TryCapture(out RuntimeInvasionCapture1458 capture)
    {
        if (state is not { } value)
        {
            capture = default;
            return false;
        }
        capture = new(revision, value);
        return true;
    }

    internal bool IsCurrent(in RuntimeInvasionCapture1458 capture) =>
        state is { } value && revision == capture.Revision && value == capture.State;

    internal bool CanAdopt(in RuntimeInvasionCapture1458 before, in InvasionTransition1458 transition) =>
        IsCurrent(in before) && revision != long.MaxValue &&
        // The later Lantern-night consumer is not owned by this scalar invasion lifecycle.
        (!before.State.LanternNextNight || transition.State.LanternNextNight) &&
        VanillaInvasionLifecycle1458.CanOwnLive(transition.State);

    internal bool TryAdopt(in RuntimeInvasionCapture1458 before, in InvasionTransition1458 transition,
        out RuntimeInvasionCapture1458 accepted)
    {
        accepted = default;
        if (!CanAdopt(in before, in transition)) return false;
        if (before.State != transition.State)
        {
            state = transition.State;
            revision++;
        }
        accepted = new(revision, transition.State);
        return true;
    }

    internal WorldInvasionSaveState1458? CaptureSaveState() => state is { } value
        ? new(value.Delay, value.Size, checked((sbyte)value.Type), value.X, value.SizeStart, value.LanternNextNight)
        : null;
}
