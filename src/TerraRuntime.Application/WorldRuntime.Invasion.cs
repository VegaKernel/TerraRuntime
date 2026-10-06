using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

public sealed partial class WorldRuntime
{
    private void PublishInvasionProgress(RuntimeInvasionCapture1458 accepted)
    {
        if (!Invasion.IsCurrent(in accepted)) return;
        RefreshWorldBootstrap();
        var state = accepted.State;
        if (state.Type <= 0) return;
        // NPC.checkDead -> ReportInvasionProgress retains Maximum0; only join SyncAnInvasion uses1.
        var frameState = new TerrariaInvasionProgressState(state.Progress, state.ProgressMax,
            checked((sbyte)state.ProgressIcon), checked((sbyte)state.ProgressWave));
        if (TerrariaInvasionProgressCodec.TryEncode(in frameState, out var frame))
            RuntimeConnections.BroadcastToPlaying(frame);
    }

    private void PublishInvasionStart(RuntimeInvasionCapture1458 accepted)
    {
        if (!Invasion.IsCurrent(in accepted)) return;
        RefreshWorldBootstrap();
        RuntimeConnections.BroadcastToPlaying(CreateLiveWorldInfoFrame());
        if (!Invasion.IsCurrent(in accepted)) return;
        // MessageBuffer case61 publishes an initial 0/1 indicator, distinct from SyncAnInvasion.
        var state = new TerrariaInvasionProgressState(0, 1, checked((sbyte)(accepted.State.Type + 3)), 0);
        if (TerrariaInvasionProgressCodec.TryEncode(in state, out var frame))
            RuntimeConnections.BroadcastToPlaying(frame);
    }

    private void AdvanceInvasion()
    {
        if (!Invasion.TryCapture(out var before) || before.State.Type == 0 ||
            !VanillaInvasionLifecycle1458.TryAdvance(before.State, Invasion.SpawnTileX, WorldClock.DayRate,
                out var transition) || !Invasion.TryAdopt(in before, in transition, out var accepted)) return;
        var effects = transition.Effects;
        if (effects.ProgressionEvent != 0)
        {
            var milestone = before.State.Type switch
            {
                1 => VanillaWorldProgressionId.GoblinArmy,
                3 => VanillaWorldProgressionId.PirateInvasion,
                _ => VanillaWorldProgressionId.MartianMadness
            };
            WorldProgression.MarkCompleted(milestone);
            RefreshWorldBootstrap();
            RuntimeConnections.BroadcastToPlaying(TerrariaProgressionEventCodec1458.Encode(
                checked((short)effects.ProgressionEvent)));
        }
        if (!Invasion.IsCurrent(in accepted)) return;
        RefreshWorldBootstrap();
        if (effects.Warnings > 0) PublishInvasionWarning(effects.FirstWarning);
        if (!Invasion.IsCurrent(in accepted)) return;
        if (effects.WorldInfo) RuntimeConnections.BroadcastToPlaying(CreateLiveWorldInfoFrame());
        if (!Invasion.IsCurrent(in accepted)) return;
        if (effects.Warnings > 1) PublishInvasionWarning(effects.SecondWarning);
        if (!Invasion.IsCurrent(in accepted)) return;
        if (effects.Warnings > 2) PublishInvasionWarning(effects.ThirdWarning);
    }

    private void PublishInvasionWarning(InvasionWarning1458 warning)
    {
        int key = (warning.Type, warning.Phase) switch
        {
            (1, InvasionWarningPhase1458.Ended) => 0,
            (1, InvasionWarningPhase1458.West) => 1,
            (1, InvasionWarningPhase1458.East) => 2,
            (1, InvasionWarningPhase1458.Arrived) => 3,
            (3, InvasionWarningPhase1458.Ended) => 24,
            (3, InvasionWarningPhase1458.West) => 25,
            (3, InvasionWarningPhase1458.East) => 26,
            (3, InvasionWarningPhase1458.Arrived) => 27,
            (4, InvasionWarningPhase1458.Ended) => 42,
            (4, InvasionWarningPhase1458.Arrived) => 41,
            _ => -1
        };
        if (key >= 0) RuntimeConnections.BroadcastToPlaying(
            TerrariaBossAnnouncementCodec1458.Encode($"LegacyMisc.{key}"));
    }
}
