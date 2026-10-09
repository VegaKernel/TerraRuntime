using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    private void ExpirePendingBulletVolleys()
    {
        long tick = 0;
        bool capturedTick = false;
        for (int slot = 0; slot < pendingBulletVolleys.Length; slot++)
        {
            if (pendingBulletVolleys[slot] is not { } pending)
                continue;
            if (!capturedTick)
            {
                tick = tickProvider();
                capturedTick = true;
            }
            if (!TryRefreshPendingBulletCapture(pending))
            {
                pendingBulletVolleys[slot] = null;
                RejectedClientProjectileProvenance++;
                continue;
            }
            var use = pending.Use;
            if (tick >= use.UseTick && tick - use.UseTick <= PendingBulletWindow(use) &&
                use.PlayerCapture is { } capture && players.IsCurrentProjectileUse(capture) &&
                use.RandomBefore is { } before && projectileRandom.HasSameState(before))
                continue;
            pendingBulletVolleys[slot] = null;
            RejectedClientProjectileProvenance++;
        }
    }

    // Packet27 carries one client-owned key, not the remaining volley keys. Retain at most one bounded
    // proposal per player until those reports arrive; guessing keys would create duplicate peer actors.
    private sealed class PendingClientBulletVolley(AuthoritativeClientProjectileSpawn use)
    {
        internal AuthoritativeClientProjectileSpawn Use = use;
        internal readonly TerrariaProjectileUpdateState[] Reports = new TerrariaProjectileUpdateState[use.VolleyStates!.Length];
        internal int Count = 1;

        internal void SetFirst(in TerrariaProjectileUpdateState first) => Reports[0] = first;
    }

    private bool TryRefreshPendingBulletCapture(PendingClientBulletVolley pending)
    {
        var use = pending.Use;
        if (use.ManaCost != 0 || use.PlayerCapture is not { } capture ||
            !players.TryRefreshPendingBulletUseCapture(capture, out var current)) return false;
        if (!ReferenceEquals(current, capture))
        {
            if (use.AlternativeBulletUse is { } alternative)
                use = use with { AlternativeBulletUse = new(alternative.Use with { PlayerCapture = current }) };
            pending.Use = use with { PlayerCapture = current };
        }
        return true;
    }

    private bool TryBeginClientBulletVolley(ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet, in AuthoritativeClientProjectileSpawn use)
    {
        if (use.VolleyStates is not { Length: > 0 and <= VanillaBulletWeaponLaunch1458.MaximumShotCount } states ||
            use.PlayerCapture is not { } capture || capture.Connection != connection)
            return false;
        var pending = new PendingClientBulletVolley(use);
        pending.SetFirst(in packet);
        if (states.Length == 1)
            return TryCommitAuthoritativeBulletVolley(pending);
        pendingBulletVolleys[connection.Player.Slot.Value] = pending;
        return true;
    }

    private bool TryContinuePendingBulletVolley(ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet)
    {
        int slot = connection.Player.Slot.Value;
        if (pendingBulletVolleys[slot] is not { } pending)
            return false;
        long tick = tickProvider();
        if (!TryRefreshPendingBulletCapture(pending))
        {
            Reject();
            return true;
        }
        var use = pending.Use;
        if (use.PlayerCapture is not { } capture || capture.Connection != connection ||
            tick < use.UseTick || tick - use.UseTick > PendingBulletWindow(use) ||
            !players.IsCurrentProjectileUse(capture) || use.RandomBefore is not { } before ||
            !projectileRandom.HasSameState(before))
        {
            Reject();
            return true;
        }
        for (int index = 0; index < pending.Count; index++)
        {
            if (pending.Reports[index].Key != packet.Key)
                continue;
            // TCP/application duplicates are idempotent; the same full key cannot denote two births.
            if (pending.Reports[index] != packet) Reject();
            return true;
        }
        ProjectileStateUpdate state = use.VolleyStates![pending.Count];
        bool primaryMatches = tick - use.UseTick <= use.UseTimeTicks && MatchesBulletReport(in packet, in state);
        var alternative = use.AlternativeBulletUse;
        bool alternativeMatches = alternative is not null && tick - use.UseTick <= alternative.Use.UseTimeTicks &&
            MatchesBulletReport(in packet, in alternative.Use.VolleyStates![pending.Count]);
        if (replication!.WireIdentities.TryResolve(packet.Key, out _) || (!primaryMatches && !alternativeMatches))
        {
            Reject();
            return true;
        }
        if (!primaryMatches) pending.Use = alternative!.Use;
        else if (!alternativeMatches) pending.Use = use with { AlternativeBulletUse = null };
        pending.Reports[pending.Count++] = packet;
        if (pending.Count == use.VolleyStates.Length)
        {
            pendingBulletVolleys[slot] = null;
            if (!TryCommitAuthoritativeBulletVolley(pending))
            {
                RejectedSpawns++;
                RejectedClientProjectileProvenance++;
                RejectedClientUpdates++;
            }
        }
        return true;

        void Reject()
        {
            pendingBulletVolleys[slot] = null;
            RejectedClientProjectileProvenance++;
            RejectedClientUpdates++;
        }
    }

    private static int PendingBulletWindow(in AuthoritativeClientProjectileSpawn use) =>
        Math.Max(use.UseTimeTicks, use.AlternativeBulletUse?.Use.UseTimeTicks ?? 0);

    private static bool MatchesBulletReport(in TerrariaProjectileUpdateState packet, in ProjectileStateUpdate state)
    {
        const float positionTolerance = 0.001f;
        const float velocityTolerance = 0.002f;
        return packet.ProjectileType == state.Type.Value && packet.Key.Spawner == state.Spawner &&
            packet.Damage == state.Damage && packet.OriginalDamage == state.OriginalDamage &&
            MathF.Abs(packet.KnockBack - state.KnockBack) <= 0.001f &&
            MathF.Abs(packet.PositionX - state.PositionX) <= positionTolerance &&
            MathF.Abs(packet.PositionY - state.PositionY) <= positionTolerance &&
            MathF.Abs(packet.VelocityX - state.VelocityX) <= velocityTolerance &&
            MathF.Abs(packet.VelocityY - state.VelocityY) <= velocityTolerance &&
            packet.Ai0 == state.Ai.Ai0 && packet.Ai1 == state.Ai.Ai1 && packet.Ai2 == state.Ai.Ai2 &&
            packet.BannerIdToRespondTo == state.BannerIdToRespondTo;
    }

    private bool TryCommitAuthoritativeBulletVolley(PendingClientBulletVolley pending)
    {
        if (!TryRefreshPendingBulletCapture(pending)) return false;
        var use = pending.Use;
        // Representation-equivalent reports cannot identify client arithmetic. Prefer the
        // complete surviving source profile with the shorter clock, so a valid shorter
        // Windows/Linux period is not rejected because of the host's deterministic tie-break.
        if (use.AlternativeBulletUse is { } alternative && alternative.Use.UseTimeTicks < use.UseTimeTicks)
            use = alternative.Use;
        if (replication is null || use.VolleyStates is not { } states || pending.Count != states.Length ||
            use.PlayerCapture is not { } capture || use.RandomBefore is not { } before ||
            use.RandomAfter is not { } after || !projectileRandom.HasSameState(before) ||
            !players.CanCommitProjectileUse(capture, use.InventoryMutation, use.ManaCost) ||
            trustedClientUseCadence.IsOnCooldown(capture.Connection.Player, use.UseTick))
            return false;
        Span<RuntimeProjectileStore.SpawnRequest> requests = stackalloc RuntimeProjectileStore.SpawnRequest[states.Length];
        Span<TerrariaProjectileKeyState> keys = stackalloc TerrariaProjectileKeyState[states.Length];
        for (int index = 0; index < states.Length; index++)
        {
            requests[index] = new(states[index]);
            keys[index] = pending.Reports[index].Key;
            // An earlier report was unresolved when received, but another accepted spawn can bind it
            // while the remaining reports are pending. Never steal that exact retained generation.
            if (replication.WireIdentities.TryResolve(in keys[index], out _))
                return false;
        }
        if (!projectiles.TryPrepareVanillaSpawnBatch(requests, out var prepared) || prepared is null)
            return false;
        using (prepared)
        {
            if (!replication.TryPrepareSpawnBirthJournal(prepared.Births, keys, capture.Connection.Source, out var journal) ||
                journal is null || !journal.IsCurrentOwned || !prepared.IsCurrentOwned ||
                !players.CanCommitProjectileUse(capture, use.InventoryMutation, use.ManaCost) ||
                !projectileRandom.HasSameState(before) || !prepared.TryCommitUnpublished())
                return false;
            // Adoption remains callback-free. All children, one debit, cursor and cadence become owned
            // before an item-use event or peer queue can observe the accepted volley.
            if (!players.TryCommitProjectileUseUnpublished(capture, use.InventoryMutation, use.ManaCost))
                throw new InvalidOperationException("Validated bullet volley changed during callback-free adoption.");
            foreach (var final in prepared.FinalBirths)
                if (!projectiles.TryMarkCombatTrusted(final.Handle, capture.Connection.Player))
                    throw new InvalidOperationException("Validated bullet volley lost its final generation.");
            projectileRandom.CopyStateFrom(after);
            trustedClientUseCadence.MarkUse(capture.Connection.Player, use.UseTick, use.UseTimeTicks);
            if (!journal.TryAdoptBaselines(projectiles))
                throw new InvalidOperationException("Validated bullet volley lost its publication ownership.");
            AppliedSpawns += states.Length;
            PromotedClientProjectileSpawns += states.Length;
            players.PublishProjectileUse(capture, use.InventoryMutation, use.ManaCost);
            if (journal.TryPublish(projectiles))
            {
                using var observers = journal.EnterObserverPublication();
                prepared.TryPublishBirthJournal();
            }
            return true;
        }
    }
}
