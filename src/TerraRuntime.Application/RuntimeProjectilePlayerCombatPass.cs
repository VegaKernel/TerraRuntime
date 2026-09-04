using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime;

/// <summary>
/// Post-simulation trusted-projectile/PvP pass. It never consumes packet-117 damage. Only exact generations already
/// marked CombatTrusted can reach player HP, so speed/damage/AI are the server-simulated values from the projectile
/// store. The admitted source-backed slice also owns Projectile.playerImmune[target] semantics generation-safely.
/// </summary>
internal sealed class RuntimeProjectilePlayerCombatPass
{
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private readonly RuntimeProjectileStore projectiles;
    private readonly RuntimeNpcStore npcs;
    private readonly PlayerAuthority players;
    private readonly Func<long> tickProvider;
    private readonly Random random;
    private readonly ProjectileSnapshot[] projectileBuffer;
    private readonly long[] lastProjectilePlayerHitTick;
    private readonly ProjectileGeneration[] lastProjectileHitGeneration;
    private readonly PlayerSessionGeneration[] lastTargetGeneration;

    public RuntimeProjectilePlayerCombatPass(
        RuntimeProjectileStore projectiles,
        RuntimeNpcStore npcs,
        PlayerAuthority players,
        Func<long> tickProvider,
        Random? random = null)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.random = random ?? Random.Shared;
        projectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
        int immunityCells = checked(projectiles.Capacity * PlayerSlotCount);
        lastProjectilePlayerHitTick = new long[immunityCells];
        lastProjectileHitGeneration = new ProjectileGeneration[immunityCells];
        lastTargetGeneration = new PlayerSessionGeneration[immunityCells];
        Array.Fill(lastProjectilePlayerHitTick, long.MinValue);
    }

    public long CommittedHits { get; private set; }
    public long Kills { get; private set; }
    public long ConsumedProjectiles { get; private set; }
    public long HostileCommittedHits { get; private set; }
    public long HostileGodModeAvoidances { get; private set; }
    public long HostileKills { get; private set; }

    public void Tick(ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions)
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
                !players.TryCaptureCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot ownerCombat) ||
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
                    IsPlayerOnProjectileCooldown(projectile.Handle, target.Connection.Player, tick) ||
                    !Intersects(in projectile, in definition, target))
                {
                    continue;
                }

                int meleeCritRoll = VanillaProjectileCombatFacts.UsesMeleePvpCrit(projectile.Type)
                    ? random.Next(1, 101)
                    : 100;
                int damageVariation = random.Next(-15, 16);
                if (!VanillaProjectileCombatFacts.TryResolvePvpHit(
                        projectile.Type,
                        projectile.Damage,
                        in ownerCombat,
                        meleeCritRoll,
                        damageVariation,
                        out VanillaProjectileResolvedHit hit))
                {
                    continue;
                }

                int direction = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult commitResult = players.TryCommitAuthoritativePvpDamage(
                        tick,
                        owner.Connection.Player,
                        target.Connection.Player,
                        DamageSource.FromPlayerProjectile(owner.Connection.Player, projectile.Handle),
                        hit.Damage,
                        hit.Critical,
                        direction,
                        out PlayerStateSnapshot committed);
                if (commitResult == PlayerDamageCommitResult.Rejected)
                    continue;

                MarkPlayerProjectileCooldown(projectile.Handle, target.Connection.Player, tick);
                if (commitResult == PlayerDamageCommitResult.Committed)
                {
                    CommittedHits++;
                    if (!killedBefore && committed.IsDead)
                        Kills++;
                }

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

        TickServerHostilePve(projectileBuffer.AsSpan(0, projectileCount), tick);
        TickExplosions(explosions, tick);
    }

    private void TickServerHostilePve(ReadOnlySpan<ProjectileSnapshot> activeProjectiles, long tick)
    {
        for (int i = 0; i < activeProjectiles.Length; i++)
        {
            ProjectileSnapshot projectile = activeProjectiles[i];
            if (!projectile.IsActive || projectile.Damage <= 0 ||
                !VanillaProjectileFacts.IsHostile(projectile.Type) ||
                !projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle sourceNpc) ||
                !projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle) ||
                !CanDealHostileProjectileDamage(projectile.Type, in lifecycle) ||
                !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition) ||
                !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
                !profile.BehaviorImplemented)
            {
                continue;
            }

            VanillaPlayerImmunityChannel1458 immunityChannel =
                VanillaIncomingPlayerDamageFacts1458.GetHostileProjectileImmunityChannel(projectile.Type);
            foreach (RuntimePlayerMember target in players.Members)
            {
                PlayerHandle targetHandle = target.Connection.Player;
                if (target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    (target.GodMode && IsPlayerOnProjectileCooldown(projectile.Handle, targetHandle, tick)) ||
                    !IntersectsHostile(in projectile, in definition, in lifecycle, sourceNpc, target))
                {
                    continue;
                }

                int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                    projectile.Damage,
                    random.Next(-15, 16));
                if (damage <= 0)
                    continue;

                float projectileCenterX = GetHostileProjectileCenterX(in projectile, in definition, in lifecycle);
                float targetCenterX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
                int hitDirection = targetCenterX < projectileCenterX ? -1 : 1;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult result = players.TryCommitAuthoritativeNpcProjectileDamage(
                    tick,
                    sourceNpc,
                    projectile.Handle,
                    targetHandle,
                    damage,
                    hitDirection,
                    immunityChannel,
                    out PlayerStateSnapshot committed);
                if (result == PlayerDamageCommitResult.Rejected)
                    continue;

                if (result == PlayerDamageCommitResult.AvoidedByGodMode)
                {
                    HostileGodModeAvoidances++;
                    // Creative god mode returns before vanilla Hurt mutates immunity. This is presentation-only
                    // throttling so a projectile overlapping for many ticks does not flood packet 119.
                    MarkPlayerProjectileCooldown(projectile.Handle, targetHandle, tick);
                    continue;
                }

                HostileCommittedHits++;
                if (!killedBefore && committed.IsDead)
                    HostileKills++;

                // Projectile.Damage_EVP does not generically decrement penetrate on player contact. Only a small
                // explicit type set does so; none is admitted here until those per-type side effects are modeled.
            }
        }
    }


    private void TickExplosions(ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions, long tick)
    {
        for (int explosionIndex = 0; explosionIndex < explosions.Length; explosionIndex++)
        {
            RuntimeProjectileExplosionEvent explosion = explosions[explosionIndex];
            ProjectileSnapshot projectile = explosion.Projectile;
            if (explosion.SourceNpc.IsAssigned)
            {
                TickHostileNpcExplosion(in explosion, tick);
                continue;
            }

            PlayerHandle trustedOwner = explosion.TrustedOwner;
            if (!projectile.IsActive || projectile.Damage <= 0 ||
                !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
                VanillaProjectileFacts.IsHostile(projectile.Type) ||
                !VanillaProjectileExplosionFacts.TryGetOnKillExplosion(projectile.Type, out _) ||
                !VanillaProjectileCombatFacts.TryGetDamageClass(projectile.Type, out _) ||
                !players.TryGet(trustedOwner, out RuntimePlayerMember? owner) ||
                !players.TryCaptureCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot ownerCombat) ||
                owner.Connection.Player != trustedOwner || owner.Slot.Value != projectile.Spawner ||
                !owner.Hostile || owner.IsDead)
            {
                continue;
            }

            foreach (RuntimePlayerMember target in players.Members)
            {
                if (target.Slot.Value == projectile.Spawner || !target.Hostile || target.IsDead ||
                    !target.HasHealth || target.Life <= 0 ||
                    (owner.Team != 0 && owner.Team == target.Team) ||
                    IsPlayerOnProjectileCooldown(projectile.Handle, target.Connection.Player, tick) ||
                    !Intersects(in explosion, target))
                {
                    continue;
                }

                int meleeCritRoll = VanillaProjectileCombatFacts.UsesMeleePvpCrit(projectile.Type)
                    ? random.Next(1, 101)
                    : 100;
                int damageVariation = random.Next(-15, 16);
                if (!VanillaProjectileCombatFacts.TryResolvePvpHit(
                        projectile.Type,
                        projectile.Damage,
                        in ownerCombat,
                        meleeCritRoll,
                        damageVariation,
                        out VanillaProjectileResolvedHit hit))
                {
                    continue;
                }

                int direction = ResolveExplosionDirection(in explosion, target);
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult commitResult = players.TryCommitAuthoritativePvpDamage(
                        tick,
                        trustedOwner,
                        target.Connection.Player,
                        DamageSource.FromPlayerProjectile(trustedOwner, projectile.Handle),
                        hit.Damage,
                        hit.Critical,
                        direction,
                        out PlayerStateSnapshot committed);
                if (commitResult == PlayerDamageCommitResult.Rejected)
                    continue;

                MarkPlayerProjectileCooldown(projectile.Handle, target.Connection.Player, tick);
                if (commitResult == PlayerDamageCommitResult.Committed)
                {
                    CommittedHits++;
                    if (!killedBefore && committed.IsDead)
                        Kills++;
                }
            }
        }
    }

    private void TickHostileNpcExplosion(in RuntimeProjectileExplosionEvent explosion, long tick)
    {
        ProjectileSnapshot projectile = explosion.Projectile;
        if (!projectile.IsActive || projectile.Damage <= 0 ||
            !VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !VanillaProjectileExplosionFacts.TryGetOnKillExplosion(projectile.Type, out _))
        {
            return;
        }

        VanillaPlayerImmunityChannel1458 immunityChannel =
            VanillaIncomingPlayerDamageFacts1458.GetHostileProjectileImmunityChannel(projectile.Type);
        foreach (RuntimePlayerMember target in players.Members)
        {
            if (target.IsDead || !target.HasHealth || target.Life <= 0 || !Intersects(in explosion, target))
                continue;

            int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                projectile.Damage,
                random.Next(-15, 16));
            if (damage <= 0)
                continue;

            int hitDirection = ResolveExplosionDirection(in explosion, target);
            bool killedBefore = target.IsDead;
            PlayerDamageCommitResult result = players.TryCommitAuthoritativeNpcProjectileDamage(
                tick,
                explosion.SourceNpc,
                projectile.Handle,
                target.Connection.Player,
                damage,
                hitDirection,
                immunityChannel,
                out PlayerStateSnapshot committed);
            if (result == PlayerDamageCommitResult.Rejected)
                continue;

            if (result == PlayerDamageCommitResult.AvoidedByGodMode)
            {
                HostileGodModeAvoidances++;
                continue;
            }

            HostileCommittedHits++;
            if (!killedBefore && committed.IsDead)
                HostileKills++;
        }
    }

    private bool IsPlayerOnProjectileCooldown(ProjectileHandle projectile, PlayerHandle target, long tick)
    {
        if (!projectile.IsAssigned || !target.IsAssigned)
            return true;
        int index = checked(projectile.Slot * PlayerSlotCount + target.Slot.Value);
        if (lastProjectileHitGeneration[index] != projectile.Generation ||
            lastTargetGeneration[index] != target.Generation)
        {
            return false;
        }
        long previous = lastProjectilePlayerHitTick[index];
        return previous != long.MinValue && tick - previous < VanillaProjectileCombatFacts.PvpPlayerImmunityTicks;
    }

    private void MarkPlayerProjectileCooldown(ProjectileHandle projectile, PlayerHandle target, long tick)
    {
        int index = checked(projectile.Slot * PlayerSlotCount + target.Slot.Value);
        lastProjectileHitGeneration[index] = projectile.Generation;
        lastTargetGeneration[index] = target.Generation;
        lastProjectilePlayerHitTick[index] = tick;
    }

    private static bool IsEligible(in ProjectileSnapshot projectile, out VanillaProjectileDefinition definition)
    {
        if (!projectile.IsActive || projectile.Damage <= 0 || !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
            VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
            !profile.BehaviorImplemented ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _) ||
            !VanillaProjectileCombatFacts.TryGetDamageClass(projectile.Type, out _))
        {
            definition = default;
            return false;
        }
        return profile.Family is VanillaProjectileBehaviorFamily.BasicArrow or
            VanillaProjectileBehaviorFamily.Thrown or
            VanillaProjectileBehaviorFamily.Boomerang or
            VanillaProjectileBehaviorFamily.Bomb or
            VanillaProjectileBehaviorFamily.ControlledMagicMissile;
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


    private static bool CanDealHostileProjectileDamage(ProjectileTypeId type, in ProjectileLifecycleState lifecycle)
    {
        // Projectile.Damage_CanDealDamage: Empress lance/sun-dance are harmless through localAI[0] == 60.
        if ((type == VanillaProjectileIds.FairyQueenLance || type == VanillaProjectileIds.FairyQueenSunDance) &&
            lifecycle.LocalAi.Ai0 <= 60f)
        {
            return false;
        }
        return true;
    }

    private bool IntersectsHostile(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        in ProjectileLifecycleState lifecycle,
        NpcHandle sourceNpc,
        RuntimePlayerMember player)
    {
        float playerLeft = player.PositionX;
        float playerTop = player.PositionY;
        float playerWidth = PlayerAuthority.VanillaBasePlayerWidth;
        float playerHeight = PlayerAuthority.VanillaBasePlayerHeight;

        // Projectile.Colliding source gates: the eye is harmless before its firing phase, while a sphere
        // that is still attached to its source cannot hurt until ai[0] reaches 60 (unless ai[1] == -1).
        if (projectile.Type == VanillaProjectileIds.PhantasmalEye && projectile.Ai.Ai0 < 2f)
            return false;
        if (projectile.Type == VanillaProjectileIds.PhantasmalSphere &&
            projectile.Ai.Ai0 >= 0f && projectile.Ai.Ai0 < 60f && projectile.Ai.Ai1 != -1f)
        {
            return false;
        }

        if (projectile.Type == VanillaProjectileIds.QueenSlimeSmash)
        {
            float size = lifecycle.LocalAi.Ai1 > 0f ? lifecycle.LocalAi.Ai1 : definition.Width;
            return IntersectsDynamicAabb(projectile.PositionX, projectile.PositionY, size, size, playerLeft, playerTop, playerWidth, playerHeight);
        }

        if (projectile.Type == VanillaProjectileIds.Sharknado || projectile.Type == VanillaProjectileIds.Cthulunado)
        {
            float width = lifecycle.LocalAi.Ai1 > 0f ? lifecycle.LocalAi.Ai1 : definition.Width;
            float height = lifecycle.LocalAi.Ai2 > 0f ? lifecycle.LocalAi.Ai2 : definition.Height;
            return IntersectsDynamicAabb(projectile.PositionX, projectile.PositionY, width, height, playerLeft, playerTop, playerWidth, playerHeight);
        }

        if (projectile.Type == VanillaProjectileIds.PhantasmalDeathray)
        {
            if (lifecycle.LocalAi.Ai0 < 20f || !npcs.TryGet(sourceNpc, out NpcSnapshot source))
                return false;

            float maxScale = source.TypeIdentity == VanillaNpcIds.MoonLordHead
                ? 1f
                : source.TypeIdentity == VanillaNpcIds.MoonLordFreeEye
                    ? 0.4f
                    : 0f;
            if (!(maxScale > 0f))
                return false;

            float scale = MathF.Sin(lifecycle.LocalAi.Ai0 * (MathF.PI / 180f)) * 10f * maxScale;
            scale = MathF.Min(scale, maxScale);
            if (!(scale > 0f) || !float.IsFinite(scale) || !(lifecycle.LocalAi.Ai1 > 0f) ||
                !float.IsFinite(lifecycle.LocalAi.Ai1))
            {
                return false;
            }

            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float endX = centerX + projectile.VelocityX * lifecycle.LocalAi.Ai1;
            float endY = centerY + projectile.VelocityY * lifecycle.LocalAi.Ai1;
            return CheckAabbVsLine(
                playerLeft, playerTop, playerWidth, playerHeight,
                centerX, centerY, endX, endY, definition.Width * scale);
        }

        if (projectile.Type == VanillaProjectileIds.FairyQueenLance)
        {
            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float cos = MathF.Cos(projectile.Ai.Ai0);
            float sin = MathF.Sin(projectile.Ai.Ai0);
            return CheckAabbVsLine(
                playerLeft, playerTop, playerWidth, playerHeight,
                centerX - cos * 40f, centerY - sin * 40f,
                centerX + cos * 40f, centerY + sin * 40f,
                8f);
        }

        if (projectile.Type == VanillaProjectileIds.FairyQueenSunDance)
        {
            float localAi0 = lifecycle.LocalAi.Ai0;
            float scaleIn = Math.Clamp(localAi0 / 20f, 0f, 1f);
            float scaleOut = Math.Clamp((180f - localAi0) / 60f, 0f, 1f);
            float scale = scaleIn * scaleOut;
            if (!(scale > 0f))
                return false;

            float rotationLerp = Math.Clamp((localAi0 - 50f) / 130f, 0f, 1f);
            float rotation = projectile.Ai.Ai0 + rotationLerp * (MathF.PI / 9f);
            float dirX = MathF.Cos(rotation);
            float dirY = MathF.Sin(rotation);
            float centerX = projectile.PositionX + definition.Width * 0.5f;
            float centerY = projectile.PositionY + definition.Height * 0.5f;
            float rayX = dirX * scale;
            float rayY = dirY * scale;
            float widthScale = scale * 0.7f;

            return CheckAabbVsLine(playerLeft, playerTop, playerWidth, playerHeight, centerX, centerY, centerX + rayX * 510f, centerY + rayY * 510f, widthScale * 100f) ||
                   CheckAabbVsLine(playerLeft, playerTop, playerWidth, playerHeight, centerX, centerY, centerX + rayX * 660f, centerY + rayY * 660f, widthScale * 60f) ||
                   CheckAabbVsLine(playerLeft, playerTop, playerWidth, playerHeight, centerX, centerY, centerX + rayX * 800f, centerY + rayY * 800f, widthScale * 10f);
        }

        return Intersects(in projectile, in definition, player);
    }

    private static float GetHostileProjectileCenterX(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        in ProjectileLifecycleState lifecycle)
    {
        if (projectile.Type == VanillaProjectileIds.QueenSlimeSmash && lifecycle.LocalAi.Ai1 > 0f)
            return projectile.PositionX + lifecycle.LocalAi.Ai1 * 0.5f;
        if ((projectile.Type == VanillaProjectileIds.Sharknado || projectile.Type == VanillaProjectileIds.Cthulunado) &&
            lifecycle.LocalAi.Ai1 > 0f)
        {
            return projectile.PositionX + lifecycle.LocalAi.Ai1 * 0.5f;
        }
        return projectile.PositionX + definition.Width * 0.5f;
    }

    private static bool IntersectsDynamicAabb(
        float left,
        float top,
        float width,
        float height,
        float playerLeft,
        float playerTop,
        float playerWidth,
        float playerHeight) =>
        left < playerLeft + playerWidth &&
        left + width > playerLeft &&
        top < playerTop + playerHeight &&
        top + height > playerTop;

    // Allocation-free port of Terraria Collision.CheckAABBvLineCollision's gameplay geometry. The rectangle
    // is transformed into line-local coordinates and tested against the finite strip [0,length] x +/-width/2.
    private static bool CheckAabbVsLine(
        float rectX, float rectY, float rectWidth, float rectHeight,
        float lineStartX, float lineStartY, float lineEndX, float lineEndY, float lineWidth)
    {
        float halfWidth = lineWidth * 0.5f;
        float broadLeft = MathF.Min(lineStartX, lineEndX) - halfWidth;
        float broadTop = MathF.Min(lineStartY, lineEndY) - halfWidth;
        float broadRight = MathF.Max(lineStartX, lineEndX) + halfWidth;
        float broadBottom = MathF.Max(lineStartY, lineEndY) + halfWidth;
        if (!(rectX < broadRight && rectX + rectWidth > broadLeft && rectY < broadBottom && rectY + rectHeight > broadTop))
            return false;

        float dx = lineEndX - lineStartX;
        float dy = lineEndY - lineStartY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0f) || !float.IsFinite(length))
            return false;
        float cos = dx / length;
        float sin = dy / length;

        Span<float> xs = stackalloc float[4];
        Span<float> ys = stackalloc float[4];
        TransformLineLocal(rectX, rectY, lineStartX, lineStartY, cos, sin, out xs[0], out ys[0]);
        TransformLineLocal(rectX + rectWidth, rectY, lineStartX, lineStartY, cos, sin, out xs[1], out ys[1]);
        TransformLineLocal(rectX + rectWidth, rectY + rectHeight, lineStartX, lineStartY, cos, sin, out xs[2], out ys[2]);
        TransformLineLocal(rectX, rectY + rectHeight, lineStartX, lineStartY, cos, sin, out xs[3], out ys[3]);

        for (int i = 0; i < 4; i++)
        {
            if (MathF.Abs(ys[i]) < halfWidth && xs[i] >= 0f && xs[i] < length)
                return true;
        }

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) & 3;
            if (SegmentCrossesHorizontalStripEdge(xs[i], ys[i], xs[j], ys[j], halfWidth, length) ||
                SegmentCrossesHorizontalStripEdge(xs[i], ys[i], xs[j], ys[j], -halfWidth, length))
            {
                return true;
            }
        }
        return false;
    }

    private static void TransformLineLocal(
        float x, float y, float originX, float originY, float cos, float sin,
        out float localX, out float localY)
    {
        float rx = x - originX;
        float ry = y - originY;
        localX = rx * cos + ry * sin;
        localY = -rx * sin + ry * cos;
    }

    private static bool SegmentCrossesHorizontalStripEdge(
        float x0, float y0, float x1, float y1, float edgeY, float length)
    {
        float dy = y1 - y0;
        if (dy == 0f)
            return y0 == edgeY && MathF.Max(MathF.Min(x0, x1), 0f) <= MathF.Min(MathF.Max(x0, x1), length);
        float t = (edgeY - y0) / dy;
        if (t < 0f || t > 1f)
            return false;
        float x = x0 + (x1 - x0) * t;
        return x >= 0f && x <= length;
    }

    private static bool Intersects(in RuntimeProjectileExplosionEvent explosion, RuntimePlayerMember player)
    {
        float right = explosion.Left + explosion.Width;
        float bottom = explosion.Top + explosion.Height;
        float playerRight = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth;
        float playerBottom = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight;
        return explosion.Left < playerRight && right > player.PositionX &&
               explosion.Top < playerBottom && bottom > player.PositionY;
    }

    private static int ResolveExplosionDirection(
        in RuntimeProjectileExplosionEvent explosion,
        RuntimePlayerMember target)
    {
        if (explosion.Projectile.VelocityX > 0.01f)
            return 1;
        if (explosion.Projectile.VelocityX < -0.01f)
            return -1;
        float targetCenter = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        return targetCenter > explosion.CenterX ? 1 : targetCenter < explosion.CenterX ? -1 : 0;
    }
}
