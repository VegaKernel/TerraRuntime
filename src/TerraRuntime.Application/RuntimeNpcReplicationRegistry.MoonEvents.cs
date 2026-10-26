using System.Runtime.ExceptionServices;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private RuntimeWorldClock? moonEventClock;

    // Owner-thread scalar acknowledgement, including accepted deaths for which vanilla sends no78.
    internal long HandledMoonEventProgressRevision { get; private set; }

    internal void BindMoonEventClock(RuntimeWorldClock? clock)
    {
        if (clock is null) return;
        if (moonEventClock is not null && !ReferenceEquals(moonEventClock, clock))
            throw new InvalidOperationException("NPC replication is already bound to a different Moon-event clock.");
        moonEventClock = clock;
    }

    internal void AcknowledgeMoonEventProgress(long revision) =>
        HandledMoonEventProgressRevision = Math.Max(HandledMoonEventProgressRevision, revision);

    /// <summary>
    /// Drains one immutable accepted effect after loot and before terminal NPC23. The retained death journal
    /// marks its publication attempt before calling here. Its earned98 survives a newer nested death, while
    /// obsolete wave text/progress cannot overwrite the newer visible state.
    /// </summary>
    internal void PublishMoonEventDeathEffects(in MoonEventDeathEffects1458 effects)
    {
        if (effects.MoonIcon == 0) return;
        bool unhandled = effects.ProgressRevision > HandledMoonEventProgressRevision;
        AcknowledgeMoonEventProgress(effects.ProgressRevision);
        ExceptionDispatchInfo? failure = null;
        if (unhandled && moonEventClock is { } clock && effects.IsCurrent(clock) && effects.WaveAdvanced)
        {
            try { Broadcast(TerrariaMoonEventAnnouncementCodec1458.EncodeWave(effects.MoonIcon == 1, effects.Wave)); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
        }
        short achievement = effects.EarnedProgressionEvent;
        if (achievement > 0)
        {
            try { ProgressionEvent(achievement); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        if (unhandled && moonEventClock is { } current && effects.IsCurrent(current) && effects.ShouldReportProgress)
        {
            try
            {
                var progress = new TerrariaInvasionProgressState(checked((int)effects.WaveKills),
                    effects.WaveRequirement, effects.MoonIcon, checked((sbyte)effects.Wave));
                if (TerrariaInvasionProgressCodec.TryEncode(in progress, out var frame)) Broadcast(frame);
            }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        failure?.Throw();
    }
}
