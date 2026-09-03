using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime;

/// <summary>
/// Post-simulation trusted-projectile/PvP pass. It never consumes packet-117 damage. Only exact generations already
/// marked CombatTrusted can reach player HP, so speed/damage/AI are the server-simulated values from the projectile
/// store. Unsupported projectile families and armored/unmodeled targets fail closed out of this strict slice.
/// </summary>
internal sealed class RuntimeProjectilePlayerCombatPass
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly PlayerAuthority players;
    private readonly Func<long> tickProvider;
    private readonly ProjectileSnapshot[] projectileBuffer;

    public RuntimeProjectilePlayerCombatPass(
        RuntimeProjectileStore projectiles,
        PlayerAuthority players,
        Func<long> tickProvider)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        projectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
    }

    public long CommittedHits { get; private set; }
    public long Kills { get; private set; }
    public long ConsumedProjectiles { get; private set; }

    public void Tick()
    {
        long tick = tickProvider();
        int projectileCount = projectiles.CopyActive(projectileBuffer);
        for (int i = 0; i < projectileCount; i++)
        {
            ProjectileSnapshot projectile = projectileBuffer[i];
            if (!projectiles.IsCombatTrusted(projectile.Handle) ||
                !projectiles.TryGetCombatTrustedOwner(projectile.Handle, out PlayerHandle trustedOwner) ||
                !IsEligible(in projectile, out VanillaProjectileDefinition definition) ||
                !players.TryGet(trustedOwner, out RuntimePlayerMember? owner) ||
                owner.Connection.Player != trustedOwner ||
                owner.Slot.Value != projectile.Spawner ||
                !owner.Hostile || owner.IsDead)
            {
                continue;
            }

            bool ended = false;
            foreach (RuntimePlayerMember target in players.Members)
            {
                if (target.Slot.Value == projectile.Spawner || !target.Hostile || target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    (owner.Team != 0 && owner.Team == target.Team) ||
                    !Intersects(in projectile, in definition, target))
                {
                    continue;
                }

                int direction = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
                bool killedBefore = target.IsDead;
                if (!players.TryCommitAuthoritativePvpDamage(
                        tick,
                        owner.Connection.Player,
                        target.Connection.Player,
                        DamageSource.FromPlayerProjectile(owner.Connection.Player, projectile.Handle),
                        projectile.Damage,
                        critical: false,
                        direction,
                        out PlayerStateSnapshot committed))
                {
                    continue;
                }

                CommittedHits++;
                if (!killedBefore && committed.IsDead)
                    Kills++;

                if (!projectiles.TryConsumeCombatHitPenetration(projectile.Handle, out bool despawned, out ProjectileSnapshot current))
                    break;
                if (despawned)
                {
                    ConsumedProjectiles++;
                    ended = true;
                    break;
                }
                projectile = current;
            }

            if (ended)
                continue;
        }
    }

    private static bool IsEligible(in ProjectileSnapshot projectile, out VanillaProjectileDefinition definition)
    {
        if (!projectile.IsActive || projectile.Damage <= 0 || !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
            VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
            !profile.BehaviorImplemented ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _))
        {
            definition = default;
            return false;
        }
        return profile.Family is VanillaProjectileBehaviorFamily.BasicArrow or
            VanillaProjectileBehaviorFamily.Thrown or
            VanillaProjectileBehaviorFamily.Boomerang;
    }

    private static bool Intersects(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        RuntimePlayerMember player)
    {
        float left = projectile.PositionX + definition.CollisionOffsetX;
        float top = projectile.PositionY + definition.CollisionOffsetY;
        float right = left + definition.CollisionWidth;
        float bottom = top + definition.CollisionHeight;
        float playerRight = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth;
        float playerBottom = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight;
        return left < playerRight && right > player.PositionX && top < playerBottom && bottom > player.PositionY;
    }
}
