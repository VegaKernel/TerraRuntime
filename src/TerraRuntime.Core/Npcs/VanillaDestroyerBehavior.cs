using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Server-authoritative TerrariaServer 1.4.5.8 AI_037 Destroyer slice. Head movement uses the
/// source worm steering law and the runtime worm collision query; body/tail slots are linked by ai[1],
/// share root identity in ai[3], and terminate when their predecessor disappears. Chain allocation and
/// body Death Laser side effects occur after a successful source commit.
/// </summary>
internal sealed class VanillaDestroyerNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom _random;
    private IVanillaWormEnvironment? _environment;

    public VanillaDestroyerNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        _random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetEnvironment(IVanillaWormEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public static void SpawnChain(in NpcSnapshot before, in NpcSnapshot committed, bool goodWorld,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != VanillaNpcIds.Destroyer || committed.Type != before.Type ||
            before.Ai.Ai0 != 0f || committed.Ai.Ai0 != 0f ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var hitbox)) return;

        // AI_037 creates every body and the tail in the head call, before later slots run AI.
        // Its NewNPC X uses integer width / 2 and the pre-motion head position.
        int bottomX = (int)(before.PositionX + hitbox.Width / 2);
        int bottomY = (int)(before.PositionY + hitbox.Height);
        int bodies = goodWorld ? 100 : 80;
        NpcHandle previous = committed.Handle;
        for (int index = 0; index <= bodies; index++)
        {
            var intent = new NpcAiSpawnIntent(index == bodies ? VanillaNpcIds.DestroyerTail : VanillaNpcIds.DestroyerBody,
                bottomX, bottomY, 0f, 0f, byte.MaxValue)
            {
                StartSlot = before.Handle.Slot,
                InitialAi = new NpcAiState(0f, previous.Slot, 0f, before.Handle.Slot)
            };
            if (!mutations.TrySpawn(in intent, out var child))
            {
                // Original subsequent failed allocations only rewrite npc[200], an inactive scratch sentinel.
                // Retain the visible predecessor link without allocating or exposing that sentinel as an NPC.
                mutations.TryLinkFollower(previous, VanillaNpcSpawnRules.PhysicalSlotCount);
                break;
            }
            if (!mutations.TryLinkFollower(previous, child.Handle.Slot)) break;
            previous = child.Handle;
        }
    }

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.Destroyer ||
            (npc.TypeIdentity != VanillaNpcIds.Destroyer && npc.TypeIdentity != VanillaNpcIds.DestroyerBody && npc.TypeIdentity != VanillaNpcIds.DestroyerTail) ||
            _environment is null || !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
        { next = default; return false; }

        NpcAiState ai = npc.Ai;
        NpcSimulationState sim = npc.Simulation;
        NpcAiState local = sim.LocalAi;
        float x = npc.PositionX, y = npc.PositionY, vx = npc.VelocityX, vy = npc.VelocityY;
        ushort targetSlot = npc.Target;

        if (npc.TypeIdentity != VanillaNpcIds.Destroyer)
        {
            if (!TryGetTarget(targetSlot, context, out _))
                TryRefresh(in npc, in definition, context, ref targetSlot, out _);

            if (!TryResolveParent(in npc, context, out NpcSnapshot parent))
            {
                sim = sim with { Life = 0, TimeLeft = 0 };
                next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
                return true;
            }

            if (!VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, parent.NetIdentity, out VanillaNpcDefinition parentDef) ||
                !parentDef.TryResolveHitbox(parent.Simulation, out VanillaNpcHitboxSize parentHitbox))
            { next = default; return false; }

            // realLife ownership: every linked segment mirrors the head's authoritative life.
            if (float.IsFinite(ai.Ai3) && ai.Ai3 >= 0f && ai.Ai3 < byte.MaxValue && ai.Ai3 == MathF.Truncate(ai.Ai3) &&
                context.TryFindNpcPeer((byte)ai.Ai3, out NpcSnapshot root) && root.TypeIdentity == VanillaNpcIds.Destroyer)
                sim = sim with { Life = root.Simulation.Life };

            float cx = x + hitbox.Width * .5f, cy = y + hitbox.Height * .5f;
            float parentCenterX = parent.PositionX + parentHitbox.Width * .5f;
            float parentCenterY = parent.PositionY + parentHitbox.Height * .5f;
            float px = parentCenterX, py = parentCenterY;
            int mechdusaSegmentIndex = TryGetMechQueen(context, out _) ?
                GetMechdusaSegmentIndex(in npc, context) : 0;
            float gap = 44f * npc.Simulation.Scale;
            if (mechdusaSegmentIndex > 0)
            {
                // AI_037_Destroyer counts up to nine ancestor links from this segment to the
                // Mechdusa head. It first aims below the predecessor and then compresses the
                // radial separation, making the Destroyer curl around Prime rather than using
                // the ordinary fixed 44-pixel worm spacing.
                float baseGap = (int)(44f * npc.Simulation.Scale);
                float verticalOffset = Math.Clamp(baseGap - baseGap * ((mechdusaSegmentIndex - 1) * .1f), 0f, baseGap);
                py += verticalOffset;
                gap = baseGap / 10f * mechdusaSegmentIndex;
            }

            float dx = px - cx, dy = py - cy;
            float distance = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
            float ratio = (distance - gap) / distance;
            x += dx * ratio; y += dy * ratio; vx = 0f; vy = 0f;

            // AI_037 recomputes its final attachment facing from the unshifted parent vector
            // measured before it translates this segment. Mechdusa's temporary vertical curl
            // offset participates in spacing only, not in the retained rotation.
            float rotation = MathF.Atan2(parentCenterY - cy, parentCenterX - cx) + MathF.PI * .5f;

            if (npc.TypeIdentity == VanillaNpcIds.DestroyerBody)
            {
                float laserCounter = local.Ai0 + _random.NextInt32(0, 4);
                if (laserCounter >= _random.NextInt32(1400, 26000))
                {
                    laserCounter = 0f;
                    // AI_037 refreshes closest target immediately before its line-of-fire check.
                    TryRefresh(in npc, in definition, context, ref targetSlot, out _);
                }
                local = local with { Ai0 = laserCounter };
            }
            sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, Rotation = rotation, JustHit = false };
            next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
            return true;
        }

        if (ai.Ai0 == 0f)
            ai = ai with { Ai3 = npc.Handle.Slot };

        if (!TryGetTarget(targetSlot, context, out VanillaNpcTargetCandidate target))
            TryRefresh(in npc, in definition, context, ref targetSlot, out target);
        bool hasTarget = TryGetTarget(targetSlot, context, out target);
        bool digging = _environment.IsDigging(x, y, hitbox.Width, hitbox.Height);
        local = local with { Ai1 = digging ? 0f : 1f };

        if (context.DayTime || !hasTarget)
        {
            digging = false;
            vy += 1f;
            if (y > (float)context.WorldSurfacePixels) vy += 1f;
            vy = MathF.Min(vy, 32f);
            int tl = sim.TimeLeft;
            if (tl < 0 || tl > 10) tl = 10;
            sim = sim with { TimeLeft = tl, NoGravity = true, NoTileCollide = true, LocalAi = local };
            next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
            return true;
        }

        // In the non-digging head branch, AI_037 invokes TargetClosest before every movement update.
        if (!digging)
            hasTarget = TryRefresh(in npc, in definition, context, ref targetSlot, out target);

        float maxSpeed = 16f;
        float turn = context.GoodWorld ? .12f : .1f;
        float align = context.GoodWorld ? .18f : .15f;
        float cxh = MathF.Truncate((x + hitbox.Width * .5f) / 16f) * 16f;
        float cyh = MathF.Truncate((y + hitbox.Height * .5f) / 16f) * 16f;
        float tx = MathF.Truncate(target.CenterX / 16f) * 16f;
        float ty = MathF.Truncate(target.CenterY / 16f) * 16f;
        float dxh = tx - cxh, dyh = ty - cyh;
        float dist = MathF.Max(.001f, MathF.Sqrt(dxh * dxh + dyh * dyh));

        if (!digging)
        {
            vy += .15f;
            vy = MathF.Min(vy, maxSpeed);
            if (MathF.Abs(vx) + MathF.Abs(vy) < maxSpeed * .4f)
                vx += vx < 0f ? -turn * 1.1f : turn * 1.1f;
            else if (vy >= maxSpeed)
                Approach(ref vx, dxh, turn);
            else if (vy > 4f)
                vx += vx < 0f ? turn * .9f : -turn * .9f;
        }
        else
        {
            float inv = maxSpeed / dist;
            float desiredX = dxh * inv, desiredY = dyh * inv;
            bool sameX = (vx > 0f && desiredX > 0f) || (vx < 0f && desiredX < 0f);
            bool sameY = (vy > 0f && desiredY > 0f) || (vy < 0f && desiredY < 0f);
            if (sameX && sameY)
            { Approach(ref vx, desiredX, align); Approach(ref vy, desiredY, align); }
            else if (sameX || sameY)
            {
                Approach(ref vx, desiredX, turn); Approach(ref vy, desiredY, turn);
                if (MathF.Abs(desiredY) < maxSpeed*.2f && MathF.Sign(vx) != MathF.Sign(desiredX)) vy += vy > 0f ? turn*2f : -turn*2f;
                if (MathF.Abs(desiredX) < maxSpeed*.2f && MathF.Sign(vy) != MathF.Sign(desiredY)) vx += vx > 0f ? turn*2f : -turn*2f;
            }
            else if (MathF.Abs(dxh) > MathF.Abs(dyh))
            { Approach(ref vx, desiredX, turn*1.1f); if (MathF.Abs(vx)+MathF.Abs(vy)<maxSpeed*.5f) vy += vy>0f?turn:-turn; }
            else
            { Approach(ref vy, desiredY, turn*1.1f); if (MathF.Abs(vx)+MathF.Abs(vy)<maxSpeed*.5f) vx += vx>0f?turn:-turn; }
        }

        if (TryGetMechQueen(context, out NpcSnapshot prime) &&
            VanillaNpcDefinitionCatalog.TryGet(prime.TypeIdentity, prime.NetIdentity, out VanillaNpcDefinition primeDefinition) &&
            primeDefinition.TryResolveHitbox(prime.Simulation, out VanillaNpcHitboxSize primeHitbox))
        {
            // TerrariaServer 1.4.5.8 NPC.AI_037_Destroyer overrides the finished head motion
            // for Mechdusa. GetMechQueenCenter is Prime.Center shifted up by 14 pixels.
            float queenCenterX = prime.PositionX + primeHitbox.Width * .5f;
            float queenCenterY = prime.PositionY + primeHitbox.Height * .5f - 14f;
            float orbit = prime.VelocityX * .025f;
            float anchorX = queenCenterX - 100f * MathF.Sin(orbit);
            float anchorY = queenCenterY + 100f * MathF.Cos(orbit);
            x = anchorX - hitbox.Width * .5f + prime.VelocityX;
            y = anchorY - hitbox.Height * .5f + prime.VelocityY;
            vx = 0f;
            vy = 0f;
            sim = sim with { Rotation = orbit * .75f + MathF.PI };
        }

        sim = sim with { NoGravity = true, NoTileCollide = true, LocalAi = local, JustHit = false };
        next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, targetSlot, ai, sim);
        return true;
    }

    private static bool TryGetMechQueen(VanillaNpcBehaviorContext context, out NpcSnapshot prime)
    {
        Span<NpcSnapshot> primes = stackalloc NpcSnapshot[VanillaNpcSpawnRules.PhysicalSlotCount];
        int count = context.CopyNpcPeers(VanillaNpcIds.SkeletronPrime, primes);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot candidate = primes[index];
            if (candidate.Ai.Ai3 == candidate.Handle.Slot)
            {
                prime = candidate;
                return true;
            }
        }

        prime = default;
        return false;
    }

    private static int GetMechdusaSegmentIndex(in NpcSnapshot npc, VanillaNpcBehaviorContext context)
    {
        float rawParentSlot = npc.Ai.Ai1;
        int index = 0;
        while (float.IsFinite(rawParentSlot) && rawParentSlot > 0f &&
               rawParentSlot < VanillaNpcSpawnRules.PhysicalSlotCount)
        {
            if (!context.TryFindNpcPeer((byte)rawParentSlot, out NpcSnapshot parent) ||
                (parent.TypeIdentity != VanillaNpcIds.Destroyer &&
                 parent.TypeIdentity != VanillaNpcIds.DestroyerBody &&
                 parent.TypeIdentity != VanillaNpcIds.DestroyerTail))
            {
                return 0;
            }

            index++;
            if (parent.TypeIdentity == VanillaNpcIds.Destroyer)
                return index;
            if (index >= 10)
                return 0;
            rawParentSlot = parent.Ai.Ai1;
        }

        return 0;
    }

    private static bool TryResolveParent(in NpcSnapshot npc, VanillaNpcBehaviorContext context, out NpcSnapshot parent)
    {
        if (float.IsFinite(npc.Ai.Ai1) && npc.Ai.Ai1 >= 0f && npc.Ai.Ai1 < byte.MaxValue && npc.Ai.Ai1 == MathF.Truncate(npc.Ai.Ai1) &&
            context.TryFindNpcPeer((byte)npc.Ai.Ai1, out parent) && parent.Simulation.Life > 0 &&
            (parent.TypeIdentity == VanillaNpcIds.Destroyer || parent.TypeIdentity == VanillaNpcIds.DestroyerBody)) return true;
        parent = default; return false;
    }
    private static bool TryGetTarget(ushort slot, VanillaNpcBehaviorContext c, out VanillaNpcTargetCandidate t)
    { if(slot<byte.MaxValue&&c.TryFindCandidate((byte)slot,out t)&&t.Active&&!t.Dead&&!t.Ghost)return true;t=default;return false; }
    private static bool TryRefresh(in NpcSnapshot n,in VanillaNpcDefinition d,VanillaNpcBehaviorContext c,ref ushort slot,out VanillaNpcTargetCandidate t)
    { if(c.TrySelectClosestTarget(in n,in d,out VanillaBlueSlimeTargetRefresh r)&&r.HasTarget&&r.Target<byte.MaxValue&&c.TryFindCandidate((byte)r.Target,out t)&&t.Active&&!t.Dead&&!t.Ghost){slot=r.Target;return true;}t=default;return false; }
    private static void Approach(ref float value,float desired,float amount){if(value<desired)value=MathF.Min(value+amount,desired);else if(value>desired)value=MathF.Max(value-amount,desired);}
}
