using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private readonly NpcRawPlayerSlotSnapshot1458[] invasionPlayerCensus = new NpcRawPlayerSlotSnapshot1458[255];

    private void ApplyInvasionRequest(short request)
    {
        if (invasion is null || invasionStartPublisher is null || !invasion.TryCapture(out var before) ||
            invasion.MaximumTilesX <= 0 || invasion.SpawnTileX < 0 ||
            invasion.SpawnTileX >= invasion.MaximumTilesX) return;
        int type = request switch { -1 => 1, -3 => 3, -7 => 4, _ => 0 };
        if (type == 0) return;
        var initial = before.State;
        var transition = new InvasionTransition1458(initial, default);
        var random = combat.SourceRandom;
        var randomBefore = random.Clone();
        var randomAfter = randomBefore.Clone();
        bool censusUsed = false;
        // Generic negative61 calls StartInvasion only while idle; -7 always clears delay and calls it.
        if (request == -7 || initial.Type == 0)
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
        // Complete dependency recapture before the final direct owner/RNG checks and callback-free adoption.
        if (censusUsed)
            for (int slot = 0; slot < invasionPlayerCensus.Length; slot++)
                if (!rawPlayerSlots.IsCurrent(in invasionPlayerCensus[slot])) return;
        if (!random.HasSameState(randomBefore) || !invasion.CanAdopt(in before, in transition)) return;
        random.CopyStateFrom(randomAfter);
        if (!invasion.TryAdopt(in before, in transition, out var accepted))
            throw new InvalidOperationException("A validated invasion start changed without a callback.");
        invasionStartPublisher(accepted);
    }
}
