using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

/// <summary>Source-stage reflection and cannon offers, guarded by exact owned peers and retained allocation claims.</summary>
internal sealed class RuntimeBigMimicEffects1458 : IVanillaBigMimicEffects1458
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly IRuntimePlayerSlotSnapshotLookup players;

    internal RuntimeBigMimicEffects1458(RuntimeProjectileStore projectiles,
        RuntimeWorldItemStore worldItems, IRuntimePlayerSlotSnapshotLookup players)
    {
        this.projectiles = projectiles;
        this.worldItems = worldItems;
        this.players = players;
    }
    private readonly record struct Peer(ProjectileSnapshot Snapshot, ProjectileLifecycleState Lifecycle);
    private readonly record struct Reflection(Peer Before, VanillaProjectileReflectionResult After);

    private sealed class RandomAdapter(IVanillaNpcRandom source) : IVanillaProjectileReflectionRandom, INpcLootRollSource
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => source.NextInt32(inclusiveMin, exclusiveMax);
        public int RollLuck(int chanceDenominator) => throw new InvalidOperationException("Stuff-cannon materials have no luck roll.");
    }

    public bool TryPlanReflection(in NpcSnapshot source, IVanillaNpcRandom random,
        out IVanillaBigMimicEffectPlan1458 plan)
    {
        plan = null!;
        if (!VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var npcDefinition) ||
            !npcDefinition.TryResolveHitbox(source.Simulation, out var npcBody))
            return false;
        PlayerStateSnapshot[] playerViews = CapturePlayers();
        Peer[] peers = CaptureProjectiles();
        var changes = new List<Reflection>();
        var draws = new RandomAdapter(random);
        foreach (Peer peer in peers)
        {
            var projectile = peer.Snapshot;
            if (peer.Lifecycle.Reflected || projectile.Damage <= 0 ||
                !VanillaProjectileReflection1458.CanBeReflectedAtDefaults(projectile.Type))
                continue;
            if (!TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(projectile.Type, out var definition))
                return false;
            if (!Intersects((int)source.PositionX, (int)source.PositionY, npcBody.Width, npcBody.Height,
                (int)projectile.PositionX, (int)projectile.PositionY, definition.Width, definition.Height))
                continue;
            bool found = false;
            PlayerStateSnapshot owner = default;
            foreach (var player in playerViews)
                if (player.Player.Slot.Value == projectile.Spawner)
                {
                    owner = player;
                    found = true;
                    break;
                }
            if (!found || projectile.Revision.Value == ulong.MaxValue)
                return false;
            var body = Body(in owner);
            var reflected = VanillaProjectileReflection1458.ResolveSource(in projectile, in definition,
                peer.Lifecycle.OldVelocityX, peer.Lifecycle.OldVelocityY,
                owner.PositionX + body.Width * .5f, owner.PositionY + body.Height * .5f, draws);
            if (!float.IsFinite(reflected.VelocityX) || !float.IsFinite(reflected.VelocityY))
                return false;
            changes.Add(new(peer, reflected));
        }
        plan = new EffectPlan(this, playerViews, peers, changes.ToArray(), null);
        return plan.IsCurrent;
    }

    public bool TryPlanCannon(in NpcSnapshot source, ReadOnlySpan<VanillaBigMimicCannonItem1458> items,
        IVanillaNpcRandom random, out IVanillaBigMimicEffectPlan1458 plan)
    {
        plan = null!;
        if (items.Length != 10 || !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body))
            return false;
        PlayerStateSnapshot[] playerViews = CapturePlayers();
        var allocationPlayers = new WorldItemAllocationPlayer1458[playerViews.Length];
        for (int index = 0; index < playerViews.Length; index++)
        {
            var player = playerViews[index];
            var playerBody = Body(in player);
            allocationPlayers[index] = new(player.Player.Slot.Value, player.PositionX, player.PositionY,
                (int)playerBody.Width, (int)playerBody.Height);
        }
        var allocation = worldItems.CreateAllocationPreview(allocationPlayers);
        var origin = new NpcLootWorldItemOrigin((int)source.PositionX + body.Width / 2,
            (int)source.PositionY + body.Height / 2);
        var draws = new RandomAdapter(random);
        foreach (var item in items)
        {
            var drop = new NpcLootDrop(new ItemTypeId(item.Item), 1);
            if (!VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, draws,
                new NpcLootWorldItemVelocity1458(item.VelocityX, item.VelocityY), out var materialized))
            {
                allocation.Dispose();
                return false;
            }
            materialized = materialized with { Ownership = WorldItemOwnershipMode.GrabDelayForAllPlayers };
            if (!allocation.TrySpawnSource(in materialized, 0, out _))
            {
                allocation.Dispose();
                return false;
            }
        }
        plan = new EffectPlan(this, playerViews, null, [], allocation);
        return plan.IsCurrent;
    }

    private PlayerStateSnapshot[] CapturePlayers()
    {
        var result = new List<PlayerStateSnapshot>();
        for (int slot = 0; slot < 255; slot++)
            if (players.TryGetPlayer(new PlayerSlotId((byte)slot), out var player))
                result.Add(player);
        return result.ToArray();
    }

    private Peer[] CaptureProjectiles()
    {
        Span<ProjectileSnapshot> scratch = stackalloc ProjectileSnapshot[1000];
        int count = projectiles.CopyActive(scratch);
        var result = new Peer[count];
        for (int index = 0; index < count; index++)
        {
            var projectile = scratch[index];
            projectiles.TryGetLifecycle(projectile.Handle, out var lifecycle);
            result[index] = new(projectile, lifecycle);
        }
        return result;
    }

    private static (float Width, float Height) Body(in PlayerStateSnapshot player) => player.HasMount
        ? VanillaPlayerMountHitbox1458.Resolve(player.MountType)
        : (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);

    private static bool Intersects(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
        bx < ax + aw && ax < bx + bw && by < ay + ah && ay < by + bh;

    private sealed class EffectPlan(RuntimeBigMimicEffects1458 owner, PlayerStateSnapshot[] players,
        Peer[]? projectiles, Reflection[] changes, RuntimeWorldItemStore.AllocationPreview? allocation) : IVanillaBigMimicEffectPlan1458
    {
        private bool claimed;
        private bool committed;
        private bool disposed;

        public bool IsCurrent => !disposed && !committed && owner.CapturePlayers().AsSpan().SequenceEqual(players) &&
            (projectiles is null || owner.CaptureProjectiles().AsSpan().SequenceEqual(projectiles)) &&
            (allocation?.IsCurrent ?? true);

        public bool TryClaim()
        {
            if (!IsCurrent || (allocation is not null && !allocation.TryClaim()))
                return false;
            claimed = true;
            return true;
        }

        public bool TryCommit()
        {
            if (!claimed || !IsCurrent)
                return false;
            foreach (var change in changes)
            {
                var before = change.Before.Snapshot;
                var lifecycle = change.Before.Lifecycle;
                if (!owner.projectiles.TryReflect(in before, in lifecycle,
                    change.After.VelocityX, change.After.VelocityY, change.After.Damage, out _))
                    return false;
            }
            if (allocation is not null)
                for (int index = 0; index < allocation.Count; index++)
                    if (!allocation.TryCommitNext(out _, out _))
                        return false;
            committed = true;
            return true;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            allocation?.Dispose();
        }
    }
}
