using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

internal sealed partial class VanillaGroundFighterNpcBehaviorStrategy
{
    private static bool TryPrepareRangedPrelude(in NpcSnapshot before, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, IVanillaNpcPhaseWorld1458 world,
        RuntimeNpcStore.AiSpawnPreview transaction, IVanillaNpcRandom random,
        Span<NpcAiProjectileIntent> shots, out NpcSnapshot prepared, out bool returnedFromAi,
        out bool force, out int count)
    {
        prepared = before; returnedFromAi = false; force = false; count = 0;
        if (before.Type is 379 or 380)
        {
            // The ritual branch writes a separate NPC438. Refuse the complete
            // phase until that counterpart participates in this transaction.
            if (before.Ai.Ai3 < 0f) return false;
            prepared = before with { Ai = before.Ai with { Ai0 = before.Ai.Ai0 >= 1000f ? 0f : before.Ai.Ai0 },
                Simulation = before.Simulation with { DamageOverride = before.Simulation.BaseDamage ?? definition.Damage } };
        }
        if (before.Type == 383)
        {
            int shieldSlot;
            if (before.Ai.Ai2 == 0f && before.Simulation.LocalAi.Ai0 == 0f)
            {
                if (!definition.TryResolveHitbox(before.Simulation, out var body)) return false;
                var intent = new NpcAiSpawnIntent(VanillaNpcIds.ForceBubble,
                    (int)(before.PositionX + body.Width * .5f), (int)(before.PositionY + body.Height * .5f),
                    0f, 0f, byte.MaxValue) { StartSlot = before.Handle.Slot };
                if (!transaction.TryStageMartianChild(in intent, out var birth)) return false;
                var linked = birth.Ai with { Ai0 = before.Handle.Slot };
                if (!world.TryFinishMartianChild(transaction, in birth, birth.PositionX, birth.PositionY,
                        in linked, out _)) return false;
                shieldSlot = birth.Handle.Slot;
                prepared = prepared with { Ai = prepared.Ai with { Ai2 = shieldSlot + 1 },
                    Simulation = prepared.Simulation with { LocalAi = prepared.Simulation.LocalAi with { Ai0 = -1f },
                        DontTakeDamage = true } };
                force = true;
            }
            else
            {
                shieldSlot = (int)before.Ai.Ai2 - 1;
                bool shield = false;
                if (shieldSlot != -1)
                {
                    if (shieldSlot < 0 || shieldSlot >= RuntimeNpcStore.MaximumAddressableCapacity ||
                        !world.TryCaptureNpcSlot((byte)shieldSlot, out var slot)) return false;
                    shield = slot.IsActive && slot.State.Type == VanillaNpcIds.ForceBubble.Value;
                }
                float cooldown = before.Simulation.LocalAi.Ai0;
                if (!shield)
                {
                    if (cooldown == -1f) cooldown = 180f;
                    if (cooldown > 0f) cooldown--;
                }
                prepared = prepared with { Ai = prepared.Ai with { Ai2 = shield ? before.Ai.Ai2 : 0f },
                    Simulation = prepared.Simulation with { DontTakeDamage = shield,
                        LocalAi = prepared.Simulation.LocalAi with { Ai0 = cooldown } } };
            }
        }
        if (before.Type != 631) return true;
        if (!definition.TryResolveHitbox(before.Simulation, out var rockBody) ||
            !TryReadRangedTarget(prepared.Target, context, out var target)) return false;
        if (prepared.Target == byte.MaxValue || target.Dead || !target.Active)
        {
            var unrefreshed = prepared;
            if (!TryRefreshRangedTarget(in unrefreshed, in definition, context, unrefreshed.Ai.Ai2 > 0f,
                    out prepared, out force) || !TryReadRangedTarget(prepared.Target, context, out target)) return false;
        }
        float centerX = prepared.PositionX + rockBody.Width * .5f;
        float centerY = prepared.PositionY + rockBody.Height * .5f;
        float dx = target.PositionX + target.Width * .5f - centerX;
        float dy = target.PositionY + target.Height * .5f - centerY;
        bool close = !target.Dead && target.Active && MathF.Sqrt(dx * dx + dy * dy) < 320f;
        float sourceX = before.PositionX, sourceY = before.PositionY;
        bool Visible() => ((IVanillaNpcProjectileEnvironment)world).CanHit(target.PositionX, target.PositionY, target.Width, target.Height,
            sourceX, sourceY, rockBody.Width, rockBody.Height);
        float timer = prepared.Ai.Ai2, stuck = prepared.Ai.Ai3, vx = prepared.VelocityX;
        if (timer == 0f)
        {
            stuck = 65f;
            if (close && Visible()) { timer = 100f; stuck = 0f; vx = prepared.Simulation.DirectionX * .01f; force = true; }
        }
        else if (timer < 100f)
        {
            returnedFromAi = true;
            timer++;
            vx *= .9f;
            if ((double)Math.Abs(vx) < .001) vx = 0f;
            if (Math.Abs(prepared.VelocityY) > 1f) timer = 0f;
            bool overlaps = (int)target.PositionX < (int)prepared.PositionX + rockBody.Width &&
                (int)target.PositionX + target.Width > (int)prepared.PositionX &&
                (int)target.PositionY < (int)prepared.PositionY + rockBody.Height &&
                (int)target.PositionY + target.Height > (int)prepared.PositionY;
            if (timer == 84f && !overlaps && Visible())
            {
                if (shots.IsEmpty || !VanillaDefinitionCatalog.TryGet(new(909), out var projectile)) return false;
                float inverseLength = 1f / (float)Math.Sqrt(dx * dx + dy * dy);
                float x = dx * inverseLength * 8f, y = dy * inverseLength * 8f;
                if (float.IsNaN(x) || float.IsNaN(y)) { x = prepared.Simulation.DirectionX * 8f; y = 0f; }
                x += 1.6f * (float)random.NextDouble() - .8f;
                y += 1.6f * (float)random.NextDouble() - .8f;
                float spreadLength = (float)Math.Sqrt(x * x + y * y);
                if (x == 0f && y == 0f || float.IsNaN(x) || float.IsNaN(y)) { x = 0f; y = 0f; }
                else { float inverse = 1f / spreadLength; x *= inverse; y *= inverse; }
                x *= 8f; y *= 8f;
                shots[0] = new(new(909), centerX - projectile.Width * .5f, centerY - projectile.Height * .5f,
                    x, y, 20, 1f);
                count = 1;
            }
            if (timer >= 100f) { timer = 100f; stuck = 0f; vx = prepared.Simulation.DirectionX * .01f; force = true; }
        }
        else if (prepared.VelocityY == 0f && close &&
            ((int)target.PositionX < (int)prepared.PositionX + rockBody.Width &&
             (int)target.PositionX + target.Width > (int)prepared.PositionX &&
             (int)target.PositionY < (int)prepared.PositionY + rockBody.Height &&
             (int)target.PositionY + target.Height > (int)prepared.PositionY || Visible()))
        { timer = 68f; force = true; }
        prepared = prepared with { VelocityX = vx, Ai = prepared.Ai with { Ai2 = timer, Ai3 = stuck } };
        return true;
    }

    private static bool TryFinishEngineer(in NpcSnapshot before, in NpcStateUpdate common,
        IVanillaNpcPhaseWorld1458 world, RuntimeNpcStore.AiSpawnPreview transaction,
        IVanillaNpcRandom random, out NpcStateUpdate next)
    {
        next = common;
        if (before.Type != VanillaNpcIds.MartianEngineer.Value) return true;
        float timer = common.Ai.Ai2;
        bool force = common.RequiresImmediateSync;
        if (common.Simulation.Confused) timer = -60f;
        else
        {
            if (timer < 60f) timer++;
            if (timer > 0f)
            {
                if (!world.TryCountNpcType(VanillaNpcIds.MartianTurret, out int turrets) ||
                    !world.TryCountNpcType(VanillaNpcIds.MartianEngineer, out int engineers)) return false;
                if (turrets >= 4 * engineers) timer = 0f;
            }
            if (before.Simulation.JustHit) timer = -30f;
            if (timer == 30f)
            {
                if (!VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
                    !definition.TryResolveHitbox(before.Simulation, out var body)) return false;
                int centerX = (int)common.PositionX / 16;
                int centerY = (int)common.PositionY / 16;
                bool found = false;
                for (int attempt = 0; attempt < 100 && !found; attempt++)
                {
                    int x = random.NextInt32(centerX - 5, centerX + 5);
                    int firstY = random.NextInt32(centerY - 5, centerY + 5);
                    for (int y = firstY; y < centerY + 5; y++)
                    {
                        if (y >= centerY - 2 && y <= centerY + 2 && x >= centerX - 2 && x <= centerX + 2)
                            continue;
                        if (!world.TryGetTurretSite(x, y, out bool eligible)) return false;
                        if (!eligible) continue;
                        var intent = new NpcAiSpawnIntent(VanillaNpcIds.MartianTurret,
                            x * 16 - body.Width / 2, y * 16, 0f, 0f, byte.MaxValue);
                        if (!transaction.TryStageMartianChild(in intent, out var birth) ||
                            !VanillaNpcDefinitionCatalog.TryGet(birth.TypeIdentity, birth.NetIdentity, out var childDefinition) ||
                            !childDefinition.TryResolveHitbox(birth.Simulation, out var childBody) ||
                            !world.TryFinishMartianChild(transaction, in birth, birth.PositionX, y * 16 - childBody.Height,
                                birth.Ai, out _)) return false;
                        found = true;
                        force = true;
                        break;
                    }
                }
            }
            if (timer == 60f) timer = -120f;
        }
        next = common with { Ai = common.Ai with { Ai2 = timer }, RequiresImmediateSync = force };
        return true;
    }

    private static bool TryReadRangedTarget(ushort slot, VanillaNpcBehaviorContext context,
        out VanillaNpcRawPlayer1458 target)
    {
        target = default;
        if (slot > byte.MaxValue || !context.TryCaptureRawPlayer((byte)slot, out var captured) ||
            !context.IsRawPlayerCurrent(in captured) || !captured.Facts.IsValid) return false;
        target = captured.Facts;
        return true;
    }

    private static bool TryRefreshRangedTarget(in NpcSnapshot source, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, bool face, out NpcSnapshot next, out bool force)
    {
        next = source; force = false;
        if (!definition.TryResolveHitbox(source.Simulation, out var body)) return false;
        ushort slot = context.TrySelectRawClosestTarget(in source, in definition, out var closest) && closest.HasTarget
            ? closest.Target : source.Target < byte.MaxValue ? source.Target : (ushort)0;
        if (!TryReadRangedTarget(slot, context, out var target)) return false;
        int x = source.Simulation.DirectionX, y = source.Simulation.DirectionY;
        if (face && !target.Dead && !(target.NoAggro && x != 0) &&
            !(target.ItemAnimation == 0 && target.Aggro < 0 && source.Target < byte.MaxValue))
        {
            x = (int)target.PositionX + target.Width / 2 < source.PositionX + body.Width / 2 ? -1 : 1;
            y = (int)target.PositionY + target.Height <= source.PositionY + body.Height ? -1 : 1;
        }
        if (source.Simulation.Confused) x = -x;
        force = !source.Simulation.CollideX && !source.Simulation.CollideY &&
            (source.Target != slot || source.Simulation.DirectionX != x || source.Simulation.DirectionY != y);
        next = source with { Target = slot, Simulation = source.Simulation with { DirectionX = x, DirectionY = y } };
        return true;
    }
}
