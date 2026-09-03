using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core;

/// <summary>
/// World-query seam for the server-authoritative parts of TerrariaServer 1.4.5.8 AI_027/028/029 and the
/// Good-World Fire Imp support path. Presentation-only dust/sound/light state deliberately does not cross it.
/// </summary>
public interface IVanillaWallOfFleshEnvironment
{
    int WorldWidthTiles { get; }
    int WorldHeightTiles { get; }
    int UnderworldLayerTiles { get; }

    bool TryResolveCorridor(float positionX, float positionY, int width, int height, out float topPixels, out float bottomPixels);

    bool CanHit(float sourceX, float sourceY, int sourceWidth, int sourceHeight, float targetX, float targetY, int targetWidth, int targetHeight);

    bool TryFindGroundSpawn(int tileX, int startTileY, out int bottomX, out int bottomY);

    bool TryFindTeleportSpot(int targetTileX, int targetTileY, int npcWidth, int npcHeight, out int tileX, out int tileY);
}

internal sealed class VanillaWallOfFleshNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaWallOfFleshEnvironment? environment;

    public void SetEnvironment(IVanillaWallOfFleshEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (environment is null ||
            definition.AiStyle != VanillaNpcAiStyles.WallOfFlesh ||
            npc.TypeIdentity != VanillaNpcIds.WallOfFlesh ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        float velocityX = npc.VelocityX;
        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        NpcAiState localAi = simulation.LocalAi;

        if (positionX < 160f || positionX > (environment.WorldWidthTiles - 10) * 16f)
        {
            simulation = simulation with { TimeLeft = 0, LocalAi = localAi, JustHit = false };
            next = new NpcStateUpdate(npc.Type, npc.NetId, positionX, positionY, 0f, 0f, npc.Target, ai, simulation);
            return true;
        }

        bool firstTick = localAi.Ai0 == 0f;
        if (firstTick)
            localAi = localAi with { Ai0 = 2f };

        ai = ai with { Ai1 = ai.Ai1 + 1f };
        if (simulation.LifeMax > 0 && simulation.Life < simulation.LifeMax * 0.5f)
            ai = ai with { Ai1 = ai.Ai1 + 1f };
        if (simulation.LifeMax > 0 && simulation.Life < simulation.LifeMax * 0.2f)
            ai = ai with { Ai1 = ai.Ai1 + 1f };

        if (ai.Ai2 == 0f && ai.Ai1 > 2700f)
            ai = ai with { Ai2 = 1f };
        if (ai.Ai2 > 0f && ai.Ai1 > 60f)
        {
            int bursts = simulation.LifeMax > 0 && simulation.Life < simulation.LifeMax * 0.3f ? 4 : 3;
            float phase = ai.Ai2 + 1f;
            ai = ai with { Ai1 = 0f, Ai2 = phase > bursts ? 0f : phase };
        }

        float top = localAi.Ai2;
        float bottom = localAi.Ai3;
        if (environment.TryResolveCorridor(positionX, positionY, hitbox.Width, hitbox.Height, out float desiredTop, out float desiredBottom))
        {
            if (top == 0f && bottom == 0f)
            {
                top = desiredTop;
                bottom = desiredBottom;
            }
            else
            {
                top = MoveTowards(top, desiredTop, 1f);
                bottom = MoveTowards(bottom, desiredBottom, 1f);
            }
        }
        else
        {
            float defaultTop = (environment.UnderworldLayerTiles + 10) * 16f;
            float defaultBottom = (environment.UnderworldLayerTiles + 80) * 16f;
            top = top == 0f ? defaultTop : top;
            bottom = bottom == 0f ? defaultBottom : bottom;
        }

        float minimumTop = (environment.UnderworldLayerTiles + 10) * 16f;
        float maximumBottom = (environment.UnderworldLayerTiles + 80) * 16f;
        top = Math.Clamp(top, minimumTop, maximumBottom);
        bottom = Math.Clamp(bottom, minimumTop, maximumBottom);
        if (top > bottom - 160f)
            top = bottom - 160f;
        if (bottom < top + 160f)
            bottom = top + 160f;
        localAi = localAi with { Ai2 = top, Ai3 = bottom };

        positionY = (top + bottom) * 0.5f - hitbox.Height * 0.5f;

        float centerX = positionX + hitbox.Width * 0.5f;
        float centerY = positionY + hitbox.Height * 0.5f;
        float minimumTargetY = (environment.WorldHeightTiles - 250) * 16f;
        ushort targetSlot = npc.Target;
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = targetSlot < byte.MaxValue &&
                         context.TryFindCandidate((byte)targetSlot, out target) &&
                         target.Active && !target.Dead && !target.Ghost && target.CenterY >= minimumTargetY;
        if (!hasTarget && context.TrySelectClosestWallOfFleshTarget(centerX, centerY, minimumTargetY, out target))
        {
            targetSlot = target.Slot;
            hasTarget = true;
        }

        float speed = ComputeHorizontalSpeed(simulation.Life, simulation.LifeMax, context.ExpertMode, context.GoodWorld);
        int direction = simulation.DirectionX;
        if (velocityX == 0f)
        {
            if (hasTarget)
                direction = target.CenterX >= centerX ? 1 : -1;
            if (direction == 0)
                direction = 1;
            velocityX = direction;
        }
        if (velocityX < 0f)
        {
            velocityX = -speed;
            direction = -1;
        }
        else
        {
            velocityX = speed;
            direction = 1;
        }

        if (!hasTarget)
        {
            float retreat = localAi.Ai1 + 1f / 180f;
            localAi = localAi with { Ai1 = retreat };
            if (retreat >= 1f)
                simulation = simulation with { TimeLeft = 0 };
        }
        else
        {
            localAi = localAi with { Ai1 = Math.Clamp(localAi.Ai1 - 1f / 30f, 0f, 1f) };
        }

        simulation = simulation with
        {
            DirectionX = direction,
            SpriteDirection = direction,
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = localAi,
            JustHit = false
        };
        next = new NpcStateUpdate(npc.Type, npc.NetId, positionX, positionY, velocityX, 0f, targetSlot, ai, simulation);
        return true;
    }

    internal static float ComputeHorizontalSpeed(int life, int lifeMax, bool expertMode, bool goodWorld)
    {
        float speed = 1.5f;
        if (lifeMax > 0)
        {
            if (life < lifeMax * 0.75f) speed += 0.25f;
            if (life < lifeMax * 0.50f) speed += 0.40f;
            if (life < lifeMax * 0.25f) speed += 0.50f;
            if (life < lifeMax * 0.10f) speed += 0.60f;
            if (expertMode && life < lifeMax * 0.66f) speed += 0.30f;
            if (expertMode && life < lifeMax * 0.33f) speed += 0.30f;
            if (expertMode && life < lifeMax * 0.05f) speed += 0.60f;
            if (expertMode && life < lifeMax * 0.035f) speed += 0.60f;
            if (expertMode && life < lifeMax * 0.025f) speed += 0.60f;
        }
        if (expertMode)
            speed = speed * 1.35f + 0.35f;
        if (goodWorld)
            speed = speed * 1.1f + 0.2f;
        return speed;
    }

    private static float MoveTowards(float value, float target, float amount) =>
        value < target ? Math.Min(value + amount, target) : Math.Max(value - amount, target);
}

internal sealed class VanillaWallOfFleshEyeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaWallOfFleshEnvironment? environment;
    public void SetEnvironment(IVanillaWallOfFleshEnvironment value) => environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (environment is null || definition.AiStyle != VanillaNpcAiStyles.WallOfFleshEye || npc.TypeIdentity != VanillaNpcIds.WallOfFleshEye ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox) || !TryResolveRoot(in npc, context, out NpcSnapshot root))
        {
            if (definition.AiStyle == VanillaNpcAiStyles.WallOfFleshEye && npc.TypeIdentity == VanillaNpcIds.WallOfFleshEye)
            {
                NpcSimulationState gone = npc.Simulation with { TimeLeft = 0, JustHit = false };
                next = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, 0f, 0f, npc.Target, npc.Ai, gone);
                return true;
            }
            next = default;
            return false;
        }

        float centerX = root.PositionX + ResolveWidth(VanillaNpcIds.WallOfFlesh, root.Simulation.Scale) * 0.5f;
        float centerY = npc.PositionY + hitbox.Height * 0.5f;
        ushort targetSlot = npc.Target;
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = targetSlot < byte.MaxValue && context.TryFindCandidate((byte)targetSlot, out target) && target.Active && !target.Dead && !target.Ghost;
        if (!hasTarget && context.TrySelectClosestWallOfFleshTarget(centerX, centerY, (environment.WorldHeightTiles - 250) * 16f, out target))
        {
            targetSlot = target.Slot;
            hasTarget = true;
        }

        NpcAiState rootLocal = root.Simulation.LocalAi;
        float top = rootLocal.Ai2;
        float bottom = rootLocal.Ai3;
        float middle = (top + bottom) * 0.5f;
        float desiredCenterY = npc.Ai.Ai0 > 0f ? (middle + top) * 0.5f : (middle + bottom) * 0.5f;
        float desiredY = desiredCenterY - hitbox.Height * 0.5f;
        float velocityY = npc.VelocityY;
        float positionY = npc.PositionY;
        if (positionY > desiredY + 1f) velocityY = -1f;
        else if (positionY < desiredY - 1f) velocityY = 1f;
        else { velocityY = 0f; positionY = desiredY; }
        velocityY = Math.Clamp(velocityY, -5f, 5f);

        NpcAiState localAi = npc.Simulation.LocalAi with { Ai0 = 0f };
        if (hasTarget)
        {
            int life = root.Simulation.Life;
            int lifeMax = Math.Max(1, root.Simulation.LifeMax);
            int volley = 4;
            float timer = localAi.Ai1 + 1f;
            if (life < lifeMax * .75f) { timer += 1f; volley++; }
            if (life < lifeMax * .50f) { timer += 1f; volley++; }
            if (life < lifeMax * .25f) { timer += 1f; volley += 2; }
            if (life < lifeMax * .10f) { timer += 2f; volley += 3; }
            if (context.ExpertMode) { timer += .5f; volley++; if (life < lifeMax * .10f) { timer += 2f; volley += 3; } }

            float phase = localAi.Ai2;
            if (phase == 0f)
            {
                if (timer > 600f) { phase = 1f; timer = 0f; }
            }
            else if (timer > 45f)
            {
                float targetX = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f;
                float targetY = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f;
                if (environment.CanHit(npc.PositionX, positionY, hitbox.Width, hitbox.Height, targetX, targetY,
                        (int)VanillaPlayerHitboxFacts.BaseWidth, (int)VanillaPlayerHitboxFacts.BaseHeight))
                {
                    timer = 0f;
                    phase++;
                    if (phase >= volley) phase = 0f;
                    int direction = root.Simulation.DirectionX == 0 ? (root.VelocityX < 0f ? -1 : 1) : root.Simulation.DirectionX;
                    bool facing = direction > 0 ? target.CenterX > npc.PositionX + hitbox.Width * .5f : target.CenterX < npc.PositionX + hitbox.Width * .5f;
                    if (facing)
                        localAi = localAi with { Ai0 = 1f };
                }
            }
            localAi = localAi with { Ai1 = timer, Ai2 = phase };
        }

        int rootDirection = root.Simulation.DirectionX == 0 ? 1 : root.Simulation.DirectionX;
        NpcSimulationState simulation = npc.Simulation with
        {
            Life = root.Simulation.Life > 0 ? root.Simulation.Life : npc.Simulation.Life,
            LifeMax = root.Simulation.LifeMax,
            DirectionX = rootDirection,
            SpriteDirection = rootDirection,
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = localAi,
            JustHit = false
        };
        next = new NpcStateUpdate(npc.Type, npc.NetId, root.PositionX, positionY, 0f, velocityY, targetSlot, npc.Ai, simulation);
        return true;
    }

    internal static bool TryResolveRoot(in NpcSnapshot npc, VanillaNpcBehaviorContext context, out NpcSnapshot root)
    {
        if (float.IsFinite(npc.Ai.Ai3) && npc.Ai.Ai3 >= 0f && npc.Ai.Ai3 < byte.MaxValue && context.TryFindNpcPeer((byte)npc.Ai.Ai3, out root) && root.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            return true;
        return context.TryFindFirstNpcPeer(VanillaNpcIds.WallOfFlesh, out root);
    }

    private static int ResolveWidth(NpcTypeId type, float scale) =>
        VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition) && definition.TryResolveHitbox(scale, out VanillaNpcHitboxSize hitbox) ? hitbox.Width : 120;
}

internal sealed class VanillaWallOfFleshHungryNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.TheHungry || npc.TypeIdentity != VanillaNpcIds.TheHungry)
        {
            next = default;
            return false;
        }
        if (!VanillaWallOfFleshEyeNpcBehaviorStrategy.TryResolveRoot(in npc, context, out NpcSnapshot root))
        {
            next = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, 0f, 0f, npc.Target, npc.Ai, npc.Simulation with { TimeLeft = 0, JustHit = false });
            return true;
        }

        ushort targetSlot = npc.Target;
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = targetSlot < byte.MaxValue && context.TryFindCandidate((byte)targetSlot, out target) && target.Active && !target.Dead && !target.Ghost;
        if (!hasTarget && context.TrySelectClosestWallOfFleshTarget(npc.PositionX, npc.PositionY, float.MinValue, out target))
        {
            targetSlot = target.Slot;
            hasTarget = true;
        }

        NpcAiState ai = npc.Ai;
        if (npc.Simulation.JustHit)
            ai = ai with { Ai1 = 10f };
        float accel = .1f;
        float tether = 300f;
        int? damage = null;
        int? defense = null;
        int rootLife = root.Simulation.Life;
        int rootMax = Math.Max(1, root.Simulation.LifeMax);
        if (rootLife < rootMax * .5f)
        {
            damage = 60;
            defense = context.ExpertMode ? definition.Defense : 30;
            if (!context.ExpertMode) tether = 700f; else accel += .066f;
        }
        else if (rootLife < rootMax * .75f)
        {
            damage = 45;
            defense = context.ExpertMode ? definition.Defense : 20;
            if (!context.ExpertMode) tether = 500f; else accel += .033f;
        }
        if (context.ExpertMode)
        {
            int slot = npc.Handle.Slot;
            if (slot % 4 == 0) tether *= 1.75f;
            if (slot % 4 == 1) tether *= 1.5f;
            if (slot % 4 == 2) tether *= 1.25f;
            if (slot % 3 == 0) tether *= 1.5f;
            if (slot % 3 == 1) tether *= 1.25f;
            tether *= .75f;
        }

        float anchorX = root.PositionX + 60f;
        float top = root.Simulation.LocalAi.Ai2;
        float bottom = root.Simulation.LocalAi.Ai3;
        float anchorY = top + (bottom - top) * ai.Ai0;
        float timer = ai.Ai2 + 1f;
        if (timer > 100f) tether *= 1.3f;
        if (timer > 200f) timer = 0f;
        ai = ai with { Ai2 = timer };

        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        if (hasTarget && ai.Ai1 == 0f)
        {
            float dx = target.CenterX - 15f - anchorX;
            float dy = target.CenterY - 15f - anchorY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > tether && distance > 0f) { float k = tether / distance; dx *= k; dy *= k; }
            Accelerate(ref velocityX, npc.PositionX, anchorX + dx, accel);
            Accelerate(ref velocityY, npc.PositionY, anchorY + dy, accel);
            float cap = 4f;
            if (context.ExpertMode)
            {
                float extra = 1.5f;
                float ratio = rootLife / (float)rootMax;
                if (ratio < .75f) extra += .7f;
                if (ratio < .50f) extra += .7f;
                if (ratio < .25f) extra += .9f;
                if (ratio < .10f) extra += .9f;
                extra = extra * 1.25f + .3f;
                cap += extra * .35f;
                float centerX = npc.PositionX + 15f;
                float rootCenterX = root.PositionX + 60f;
                if ((centerX < rootCenterX && root.VelocityX > 0f) || (centerX > rootCenterX && root.VelocityX < 0f)) cap += 6f;
            }
            velocityX = Math.Clamp(velocityX, -cap, cap);
            velocityY = Math.Clamp(velocityY, -cap, cap);
        }
        else if (ai.Ai1 > 0f)
            ai = ai with { Ai1 = ai.Ai1 - 1f };
        else if (ai.Ai1 < 0f)
            ai = ai with { Ai1 = 0f };

        int sprite = target.CenterX >= npc.PositionX + 15f ? 1 : -1;
        NpcSimulationState simulation = npc.Simulation with
        {
            SpriteDirection = sprite,
            NoGravity = true,
            NoTileCollide = true,
            DamageOverride = damage,
            DefenseOverride = defense,
            JustHit = false
        };
        next = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY, targetSlot, ai, simulation);
        return true;
    }

    private static void Accelerate(ref float velocity, float position, float target, float acceleration)
    {
        if (position < target) { velocity += acceleration; if (velocity < 0f) velocity += acceleration * 2.5f; }
        else if (position > target) { velocity -= acceleration; if (velocity > 0f) velocity -= acceleration * 2.5f; }
    }
}

internal sealed class VanillaFireImpNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaWallOfFleshEnvironment? environment;
    public void SetEnvironment(IVanillaWallOfFleshEnvironment value) => environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (environment is null || npc.TypeIdentity != VanillaNpcIds.FireImp || definition.AiStyle != VanillaNpcAiStyles.Caster)
        {
            next = default;
            return false;
        }

        ushort targetSlot = npc.Target;
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = targetSlot < byte.MaxValue && context.TryFindCandidate((byte)targetSlot, out target) && target.Active && !target.Dead && !target.Ghost;
        if (!hasTarget && context.TrySelectClosestWallOfFleshTarget(npc.PositionX + definition.Width * .5f, npc.PositionY + definition.Height * .5f, float.MinValue, out target))
        {
            targetSlot = target.Slot;
            hasTarget = true;
        }

        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        float velocityX = npc.VelocityX * .93f;
        if (velocityX is > -0.1f and < 0.1f) velocityX = 0f;
        float velocityY = npc.VelocityY;
        NpcAiState ai = npc.Ai;
        if (ai.Ai2 != 0f && ai.Ai3 != 0f)
        {
            positionX = ai.Ai2 * 16f - definition.Width * .5f + 8f;
            positionY = ai.Ai3 * 16f - definition.Height;
            velocityX = 0f;
            velocityY = 0f;
            ai = ai with { Ai2 = 0f, Ai3 = 0f };
        }
        float timer = ai.Ai0 == 0f ? 500f : ai.Ai0;
        timer += 1f;
        bool wofAlive = context.CountNpcPeers(VanillaNpcIds.WallOfFlesh) > 0;
        if (context.GoodWorld && wofAlive)
        {
            timer += 1f;
            if (timer % 2f == 1f) timer -= 1f;
        }
        float attackTimer = ai.Ai1;
        if (timer is 100f or 200f or 300f)
            attackTimer = 30f;
        if (timer >= 650f && hasTarget)
        {
            timer = 1f;
            int tx = (int)(target.CenterX / 16f);
            int ty = (int)(target.CenterY / 16f);
            if (environment.TryFindTeleportSpot(tx, ty, definition.Width, definition.Height, out int tileX, out int tileY))
            {
                attackTimer = 5f;
                ai = ai with { Ai2 = tileX, Ai3 = tileY };
            }
        }
        if (attackTimer > 0f)
            attackTimer -= 1f;
        ai = ai with { Ai0 = timer, Ai1 = attackTimer };

        NpcSimulationState simulation = npc.Simulation with { JustHit = false };
        next = new NpcStateUpdate(npc.Type, npc.NetId, positionX, positionY, velocityX, velocityY, targetSlot, ai, simulation);
        return true;
    }
}

internal sealed class VanillaBurningSphereNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (npc.TypeIdentity != VanillaNpcIds.BurningSphere || definition.AiStyle != VanillaNpcAiStyles.BurningSphere)
        {
            next = default;
            return false;
        }

        ushort targetSlot = npc.Target;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        if (targetSlot >= byte.MaxValue || !context.TryFindCandidate((byte)targetSlot, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead || target.Ghost)
        {
            if (context.TrySelectClosestWallOfFleshTarget(npc.PositionX + definition.Width * .5f, npc.PositionY + definition.Height * .5f, float.MinValue, out target))
            {
                targetSlot = target.Slot;
                float dx = target.CenterX - (npc.PositionX + definition.Width * .5f);
                float dy = target.CenterY - (npc.PositionY + definition.Height * .5f);
                float speed = context.GoodWorld && context.CountNpcPeers(VanillaNpcIds.WallOfFlesh) > 0 ? 14f : 5f;
                float length = MathF.Sqrt(dx * dx + dy * dy);
                if (length <= 0f) length = 1f;
                velocityX = dx * speed / length;
                velocityY = dy * speed / length;
            }
        }

        bool protectedByWof = context.GoodWorld && context.CountNpcPeers(VanillaNpcIds.WallOfFlesh) > 0;
        int timeLeft = npc.Simulation.TimeLeft < 0 ? 100 : Math.Min(npc.Simulation.TimeLeft, 100);
        NpcSimulationState simulation = npc.Simulation with
        {
            DontTakeDamage = protectedByWof,
            NoGravity = true,
            NoTileCollide = true,
            TimeLeft = timeLeft,
            JustHit = false
        };
        next = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, velocityX, velocityY, targetSlot, npc.Ai, simulation);
        return true;
    }
}
