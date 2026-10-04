using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcCombat1458
{
    internal TownRandomScope? BeginRandomScope(IRuntimeTownNpcScheduleRandom1458 schedule)
    {
        if (random is not NpcRuntimeTownCombatRandom1458 combatAdapter ||
            schedule is not NpcRuntimeTownScheduleRandom1458 scheduleAdapter ||
            combatAdapter.Current is not SystemVanillaNpcRandom original ||
            !ReferenceEquals(combatAdapter.Current, scheduleAdapter.Current)) return null;
        return new TownRandomScope(combatAdapter, scheduleAdapter, original);
    }

    // A resident's complete AI/contact phase uses one cloned source stream. A rejected generation or
    // capacity/admission result cannot consume the live stream or publish a partial AI transition.
    internal sealed class TownRandomScope : IDisposable
    {
        private readonly NpcRuntimeTownCombatRandom1458 combat;
        private readonly NpcRuntimeTownScheduleRandom1458 schedule;
        private readonly SystemVanillaNpcRandom original;
        private readonly VanillaUnifiedRandom1458 baseline;
        internal VanillaUnifiedRandom1458 Random { get; }

        internal TownRandomScope(NpcRuntimeTownCombatRandom1458 combat,
            NpcRuntimeTownScheduleRandom1458 schedule, SystemVanillaNpcRandom original)
        {
            this.combat = combat; this.schedule = schedule; this.original = original;
            baseline = original.SourceRandom.Clone();
            Random = original.SourceRandom.Clone();
            combat.Current = schedule.Current = new SystemVanillaNpcRandom(Random);
        }

        internal bool IsCurrent => original.SourceRandom.HasSameState(baseline);
        internal void Accept() => original.SourceRandom.CopyStateFrom(Random);
        public void Dispose() => combat.Current = schedule.Current = original;
    }
}
