namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // Accepted player phases adopt births before world work, then publish the
    // source birth/status/world sequence. Packet16 is an already adopted HP
    // baseline; a later canonical Hurt can supersede its deferred notification.
    internal void DrainOrderedLocalMagicBirths() => toolProjectileAuthority?.DrainOrderedLocalMagicBirths();

    internal ToolActionPublicationBatch1458? PrepareToolBirthsAfterOwnedPhase()
    {
        if (toolProjectileAuthority is null) return null;
        ToolActionPublicationBatch1458? batch = null;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!players.TryGet((byte)slot, out var member) ||
                member.ItemPhase?.ToolUse?.Input is not { IsCurrent: true } input) continue;
            batch ??= new();
            if (input.ShootReport is null) continue;
            try
            {
                if (toolProjectileAuthority.TryCommitPendingToolBirth(member.Connection, out var birth) && birth is not null)
                    batch.AddBirth(birth);
            }
            catch (Exception exception) { batch.RecordFailure(exception); }
        }
        return batch;
    }

    internal void DrainToolActionsAfterOwnedPhase(ToolActionPublicationBatch1458 batch)
    {
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!players.TryGet((byte)slot, out var member) ||
                member.ItemPhase?.ToolUse?.Input is not { IsCurrent: true } input) continue;
            try { TryCommitObservedToolAction(input, deferred: batch); }
            catch (Exception exception) { batch.RecordFailure(exception); }
        }
    }

    internal sealed class ToolActionPublicationBatch1458 : IDisposable
    {
        private readonly List<ToolActionPreparation1458> actions = [];
        private readonly List<ToolBirthPublication1458> births = [];
        private readonly List<PlacementPreparation1458> placements = [];
        private readonly List<bool> actionOrder = [];
        private int nextPlacement, nextOrder;
        internal List<ToolHealthPublication1458> HealthPublications { get; } = [];
        private Exception? failure;
        private int next;
        private int nextBirth;
        private int nextHealth;
        private bool publishing;
        private bool disposed;

        internal void Add(ToolActionPreparation1458 action)
        {
            if (disposed || actions.Count + placements.Count == byte.MaxValue)
                throw new InvalidOperationException("A tool phase has at most one action per human slot.");
            actions.Add(action);
            actionOrder.Add(false);
        }

        internal void AddPlacement(PlacementPreparation1458 placement)
        {
            if (disposed || actions.Count + placements.Count == byte.MaxValue)
                throw new InvalidOperationException("A player phase has at most one world action per human slot.");
            placements.Add(placement);
            actionOrder.Add(true);
        }

        internal void RecordFailure(Exception exception) => failure ??= exception;

        internal void AddBirth(ToolBirthPublication1458 birth)
        {
            if (disposed || births.Count == byte.MaxValue)
                throw new InvalidOperationException("A tool phase has at most one birth per human slot.");
            births.Add(birth);
        }

        internal bool TryPublishAll()
        {
            if (disposed || publishing) return false;
            publishing = true;
            Exception? first = failure;
            failure = null;
            try
            {
                while (nextBirth < births.Count)
                {
                    var birth = births[nextBirth++];
                    try { birth.TryPublish(); }
                    catch (Exception exception) { first ??= exception; }
                }
                while (nextHealth < HealthPublications.Count)
                {
                    var health = HealthPublications[nextHealth++];
                    try { health.TryPublish(); }
                    catch (Exception exception) { first ??= exception; }
                }
                while (nextOrder < actionOrder.Count)
                {
                    if (actionOrder[nextOrder++])
                    {
                        var placement = placements[nextPlacement++];
                        try { placement.TryPublishAll(); }
                        catch (Exception exception) { first ??= exception; }
                        finally { placement.Dispose(); }
                    }
                    else
                    {
                        var action = actions[next++];
                        try { action.TryPublishAll(); }
                        catch (Exception exception) { first ??= exception; }
                        finally { action.Dispose(); }
                    }
                }
            }
            finally { publishing = false; }
            if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            while (next < actions.Count) actions[next++].Dispose();
            while (nextPlacement < placements.Count) placements[nextPlacement++].Dispose();
        }
    }
}
