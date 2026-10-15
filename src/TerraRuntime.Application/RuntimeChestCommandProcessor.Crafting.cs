using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestCommandProcessor
{
    private CraftingWork? craftingWork;
    private readonly WorldTileStore? craftingTiles;
    private readonly Func<ConnectionHandle, bool>? currentCraftingActor;

    internal int LastCraftingWorkSteps { get; private set; }
    internal long AppliedCraftingRequests { get; private set; }
    internal long RejectedCraftingRequests { get; private set; }

    // The existing bounded per-source mailbox retains subsequent requests. There
    // is one detached operation for the whole world, not a new request queue.
    internal bool CanApplyCraftingCommand(RuntimeCommand command) =>
        craftingWork is null || command is not ClientChestCraftingRuntimeCommand;

    private void ApplyCrafting(ClientChestCraftingRuntimeCommand command)
    {
        if (craftingWork is not null || craftingTiles is null ||
            currentCraftingActor?.Invoke(command.Connection) != true ||
            !replication.IsCurrentCraftingActor(command.Connection))
        {
            RejectedCraftingRequests++;
            return;
        }

        int maximumChanges = replication.GetMaximumCraftingChanges(command.Connection);
        if (maximumChanges <= 0)
        {
            RejectedCraftingRequests++;
            replication.PublishCrafting(command.Connection, [], false);
            return;
        }
        var preparation = store.PrepareCrafting(command.Connection, command.Request, craftingTiles, maximumChanges);
        var costs = store.OwnsLoadedWorldStock ? captureRemoteCosts?.Invoke(command.Connection) : null;
        if (store.OwnsLoadedWorldStock && costs is null)
        {
            RejectedCraftingRequests++;
            replication.PublishCrafting(command.Connection, [], false);
            return;
        }
        craftingWork = new(command.Connection, preparation, preparation.Prepare().GetEnumerator(), costs);
    }

    internal int TickCraftingWork(int budget)
    {
        if (budget is < 0 or > PlayerAuthority.ShopWorkStepsPerTick)
            throw new ArgumentOutOfRangeException(nameof(budget));
        LastCraftingWorkSteps = 0;
        while (craftingWork is { } work && LastCraftingWorkSteps < budget)
        {
            LastCraftingWorkSteps++;
            if (!work.Preparation.IsCurrent || work.Costs is { IsCurrent: false } || currentCraftingActor?.Invoke(work.Connection) != true ||
                !replication.IsCurrentCraftingActor(work.Connection))
            {
                FinishCrafting(work, false, []);
                break;
            }
            if (work.Steps.MoveNext()) continue;

            TerrariaChestItemState[] changes = work.Preparation.Accepted
                ? work.Preparation.CaptureChanges() : [];
            PlayerAuthority.RemoteCraftCostCommit? costCommit = null;
            bool costsAccepted = work.Costs is null || work.Costs.TryPrepareCommit(work.Preparation, out costCommit);
            bool accepted = costsAccepted && work.Preparation.Accepted &&
                currentCraftingActor?.Invoke(work.Connection) == true &&
                replication.CanPublishCrafting(work.Connection, changes) &&
                (costCommit is null || costCommit.IsCurrent) &&
                work.Preparation.TryAdoptUnpublished();
            if (accepted) costCommit?.AdoptValidatedUnpublished();
            FinishCrafting(work, accepted, accepted ? changes : []);
        }
        return LastCraftingWorkSteps;
    }

    private void FinishCrafting(CraftingWork work, bool accepted, TerrariaChestItemState[] changes)
    {
        // Retire before publication/reentry. Accepted world items are adopted in
        // full before any packet is visible. Later transport failure does not
        // refund accepted costs, matching original HandleRequest/SendToClient.
        work.Steps.Dispose();
        work.Costs?.FinishUnpublished();
        if (ReferenceEquals(craftingWork, work)) craftingWork = null;
        if (accepted) AppliedCraftingRequests++;
        else RejectedCraftingRequests++;
        replication.PublishCrafting(work.Connection, changes, accepted);
    }

    private sealed record CraftingWork(ConnectionHandle Connection,
        RuntimeChestStore.CraftingPreparation Preparation, IEnumerator<int> Steps, PlayerAuthority.RemoteCraftCostCapture? Costs);
}
