namespace TerraRuntime.Application;

public sealed partial class PlayerBootstrapPacketSet
{
    private SectionRebuildGlobalBudget _sectionRebuildGlobalBudget =
        new(SectionRebuildGlobalBudgetOptions.HardAbuse);

    internal SectionRebuildGlobalBudgetSnapshot CaptureSectionRebuildGlobalBudgetSnapshot() =>
        _sectionRebuildGlobalBudget.Snapshot;

    internal void ConfigureSectionRebuildGlobalBudget(
        SectionRebuildGlobalBudgetOptions options,
        TimeProvider? timeProvider = null)
    {
        if (Volatile.Read(ref _sectionRebuildRequester) is not null)
        {
            throw new InvalidOperationException(
                "Section rebuild global budget must be configured before the rebuild pipeline is attached.");
        }

        _sectionRebuildGlobalBudget = new SectionRebuildGlobalBudget(options, timeProvider);
    }
}
