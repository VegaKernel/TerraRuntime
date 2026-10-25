using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private readonly NpcRawPlayerSlotSnapshot1458[] invasionPlayerCensus = new NpcRawPlayerSlotSnapshot1458[255];

    private void ApplyInvasionRequest(ConnectionHandle connection, short request)
    {
        if (invasion is null || invasionStartPublisher is null || !invasion.TryCapture(out var before) ||
            invasion.MaximumTilesX <= 0 || invasion.SpawnTileX < 0 ||
            invasion.SpawnTileX >= invasion.MaximumTilesX) return;
        int type = request switch { -1 => 1, -2 => 2, -3 => 3, _ => 0 };
        int itemType = request switch { -1 => 361, -2 => 602, -3 => 1315, _ => 0 };
        // Do not spend a Snow Globe on an event whose AI38 cohort cannot enter play.
        if (type == 2 && !RuntimeInvasionSpawn1458.IsFrostCohortReady()) return;
        // Martian Madness starts from the server-owned escaping probe, never a paid item.
        if (type == 0 || before.State.Type != 0 ||
            !players.TryPrepareEventSummon(connection, itemType, out var payment)) return;
        var initial = before.State;
        var transition = new InvasionTransition1458(initial, default);
        var random = combat.SourceRandom;
        var randomBefore = random.Clone();
        var randomAfter = randomBefore.Clone();
        bool censusUsed = false;
        if (initial.Type == 0)
        {
            initial = initial with { Delay = 0 };
            int eligible = 0;
            if (initial.Type == 0 || initial.Size == 0)
            {
                censusUsed = true;
                for (int slot = 0; slot < invasionPlayerCensus.Length; slot++)
                {
                    if (!rawPlayerSlots.TryCapture((byte)slot, out var captured)) return;
                    invasionPlayerCensus[slot] = captured;
                    if (!captured.Facts.Active) continue;
                    // The source uses statLifeMax, including dead players, never derived statLifeMax2.
                    if (captured.LivePlayer?.BaseLifeMax is not { } baseline) return;
                    if (baseline >= 200) eligible++;
                }
            }
            if (!VanillaInvasionLifecycle1458.TryStart(in initial, type, eligible,
                    invasion.MaximumTilesX, invasion.SpawnTileX, randomAfter.Next, out transition)) return;
        }
        if (transition.State.Type != type || transition.State.Size <= 0) return;
        // Complete dependency recapture before the final direct owner/RNG checks and callback-free adoption.
        if (censusUsed)
            for (int slot = 0; slot < invasionPlayerCensus.Length; slot++)
                if (!rawPlayerSlots.IsCurrent(in invasionPlayerCensus[slot])) return;
        if (!random.HasSameState(randomBefore) || !invasion.CanAdopt(in before, in transition) ||
            !payment!.IsCurrent) return;
        if (!payment.TryAdoptUnpublished()) return;
        random.CopyStateFrom(randomAfter);
        if (!invasion.TryAdopt(in before, in transition, out var accepted))
            throw new InvalidOperationException("A validated invasion start changed without a callback.");
        try { invasionStartPublisher(accepted); }
        finally { payment.Publish(); }
    }

    private void ApplyMoonEventRequest(ConnectionHandle connection, short request)
    {
        if (worldClock is null) return;
        int itemType = request switch { -4 => 1844, -5 => 1958, -6 => 2767, -10 => 4271, _ => 0 };
        bool allowed = request switch
        {
            -4 or -5 => !worldClock.DayTime && !worldClock.MoonEventActive,
            -6 => worldClock.DayTime && !worldClock.EclipseActive &&
                (naturalSpawnWorldFacts?.HardMode == true || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Hardmode)),
            -10 => !worldClock.DayTime && !worldClock.BloodMoonActive,
            _ => false
        };
        if (!allowed || !players.TryPrepareEventSummon(connection, itemType, out var payment) ||
            !payment!.TryAdoptUnpublished()) return;
        bool started = request switch
        {
            -4 => worldClock.TryStartPumpkinMoon(),
            -5 => worldClock.TryStartSnowMoon(),
            -6 => worldClock.TryStartSolarEclipse(),
            -10 => worldClock.TryStartBloodMoon(),
            _ => false
        };
        if (!started) throw new InvalidOperationException("A validated event start changed without a callback.");
        // Both payment and event scalars are committed before public notifications.
        try
        {
            npcReplication?.EventInvocation(request, naturalSpawnWorldFacts?.RemixWorld == true);
            if (request is -4 or -5) npcReplication?.MoonFirstWave(request == -5);
            if (request == -10) npcReplication?.ProgressionEvent(4);
        }
        finally { payment.Publish(); }
    }
}
