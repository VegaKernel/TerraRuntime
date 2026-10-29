using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

internal sealed partial class VanillaNpcPhaseAccepted1458
{
    private static bool TryStepChild(in NpcSnapshot source, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, IVanillaNpcPhaseWorld1458 world,
        RuntimeNpcStore.AiSpawnPreview preview, IVanillaNpcRandom random,
        Span<NpcAiProjectileIntent> shots, out int count, out bool inactive, out NpcStateUpdate next)
    {
        count = 0; inactive = false; next = default;
        if (source.NetIdentity.Value != source.Type || !definition.TryResolveHitbox(source.Simulation, out var body))
            return false;
        var working = source;
        bool targetSync = false;
        if (source.TypeIdentity == VanillaNpcIds.Maggot)
        {
            if (source.VelocityY == 0f && source.Ai.Ai0 == 1f && source.Simulation.DirectionX == 0 &&
                !RefreshChildTarget(ref working, in definition, context, face: true, out targetSync)) return false;
            if (!VanillaMaggotAi1458.TryStep(in working, random, out var moved, out bool force)) return false;
            next = State(in moved) with { RequiresImmediateSync = force || targetSync };
            return true;
        }
        VanillaMartianChildShot1458? shot;
        if (source.TypeIdentity == VanillaNpcIds.MartianTurret)
        {
            if (!RefreshChildTarget(ref working, in definition, context, face: false, out targetSync) ||
                !ReadChildTarget(working.Target, context, out var target) ||
                !VanillaMartianChildAi1458.TryStepTurret(in working, in body, in target, random,
                    out var moved, out bool force, out shot))
                return false;
            next = State(in moved) with { RequiresImmediateSync = force || targetSync };
            return StageShot(shot, shots, out count);
        }
        if (!VanillaMartianChildAi1458.IsSupported(source.TypeIdentity, source.NetIdentity)) return false;
        NpcStateUpdate parent;
        bool active;
        if (source.TypeIdentity == VanillaNpcIds.ScutlixRider && source.Simulation.LocalAi.Ai0 == 0f)
        {
            var intent = new NpcAiSpawnIntent(VanillaNpcIds.Scutlix,
                (int)(source.PositionX + body.Width * .5f), (int)(source.PositionY + body.Height * .5f),
                0f, 0f, byte.MaxValue) { StartSlot = source.Handle.Slot };
            if (!preview.TryStageScutlixMount(in intent, out var birth) ||
                !world.TryFinishScutlixMount(preview, in birth, out var mount)) return false;
            working = working with { Ai = working.Ai with { Ai0 = mount.Handle.Slot },
                Simulation = working.Simulation with { LocalAi = working.Simulation.LocalAi with { Ai0 = 1f } } };
            parent = State(in mount); active = true; targetSync = true;
        }
        else
        {
            // Main.npc indexing truncates the source float; impossible physical slots are an unsupported import.
            int slot = (int)source.Ai.Ai0;
            if (slot < 0 || slot >= RuntimeNpcStore.MaximumAddressableCapacity ||
                !world.TryCaptureNpcSlot((byte)slot, out var captured)) return false;
            parent = captured.State; active = captured.IsActive;
        }
        NpcTypeId expected = source.TypeIdentity == VanillaNpcIds.ForceBubble ? VanillaNpcIds.MartianOfficer : VanillaNpcIds.Scutlix;
        if (!active || parent.Type != expected.Value)
        {
            if (source.TypeIdentity == VanillaNpcIds.ForceBubble)
            {
                // Source AI072 uses HitEffect's dedicated no-op, not CheckDead/NPCLoot.
                next = State(in working) with { Simulation = working.Simulation with { Life = 0 } };
                inactive = true;
                return true;
            }
            if (!world.TryStageTransform(preview, in working, VanillaNpcIds.RayGunner, default, out var transformed)) return false;
            next = State(in transformed);
            return true;
        }
        if (!VanillaNpcDefinitionCatalog.TryGet(new(parent.Type), new(parent.NetId), out var parentDefinition) ||
            !parentDefinition.TryResolveHitbox(parent.Simulation, out var parentBody)) return false;
        var attachment = new VanillaMartianChildParent1458(parent.PositionX, parent.PositionY,
            parent.VelocityX, parent.VelocityY, parentBody.Width, parentBody.Height, parent.Target,
            parent.Simulation.TimeLeft, parent.Simulation.DirectionX, parent.Simulation.SpriteDirection);
        if (source.TypeIdentity == VanillaNpcIds.ForceBubble)
        {
            var moved = VanillaMartianChildAi1458.FollowBubble(in working, in body, in attachment);
            next = State(in moved);
            return true;
        }
        if (!ReadChildTarget(parent.Target, context, out var riderTarget)) return false;
        float x = parent.PositionX + parentBody.Width * .5f - body.Width / 2;
        float y = parent.PositionY + parentBody.Height * .5f - body.Height / 2 - 14f;
        bool canHit = ((IVanillaNpcProjectileEnvironment)world).CanHit(x, y, body.Width, body.Height,
            riderTarget.PositionX, riderTarget.PositionY, riderTarget.Width, riderTarget.Height);
        if (!VanillaMartianChildAi1458.TryStepRider(in working, in body, in attachment,
                in riderTarget, canHit, random, out var rider, out bool riderForce, out shot)) return false;
        next = State(in rider) with { RequiresImmediateSync = riderForce || targetSync };
        return StageShot(shot, shots, out count);
    }

    private static bool RefreshChildTarget(ref NpcSnapshot source, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, bool face, out bool force)
    {
        force = false;
        ushort slot = context.TrySelectClosestTarget(in source, in definition, out var selected)
            ? selected.Target : source.Target < byte.MaxValue ? source.Target : (ushort)0;
        if (!ReadChildTarget(slot, context, out var raw) || !definition.TryResolveHitbox(source.Simulation, out var body)) return false;
        int x = source.Simulation.DirectionX, y = source.Simulation.DirectionY;
        if (face && !raw.Dead && !(raw.NoAggro && x != 0) &&
            !(raw.ItemAnimation == 0 && raw.Aggro < 0 && source.Target < byte.MaxValue))
        {
            x = (int)raw.PositionX + raw.Width / 2 < source.PositionX + body.Width / 2 ? -1 : 1;
            y = (int)raw.PositionY + raw.Height / 2 < source.PositionY + body.Height / 2 ? -1 : 1;
        }
        if (source.Simulation.Confused) x = -x;
        force = !source.Simulation.CollideX && !source.Simulation.CollideY &&
            (slot != source.Target || x != source.Simulation.DirectionX || y != source.Simulation.DirectionY);
        source = source with { Target = slot, Simulation = source.Simulation with { DirectionX = x, DirectionY = y } };
        return true;
    }

    private static bool ReadChildTarget(ushort slot, VanillaNpcBehaviorContext context, out VanillaNpcRawPlayer1458 target)
    {
        target = default;
        if (slot > byte.MaxValue || !context.TryCaptureRawPlayer((byte)slot, out var captured) ||
            !context.IsRawPlayerCurrent(in captured) || !captured.Facts.IsValid || captured.Facts.Slot != slot) return false;
        target = captured.Facts;
        return true;
    }

    private static bool StageShot(VanillaMartianChildShot1458? shot, Span<NpcAiProjectileIntent> shots, out int count)
    {
        count = 0;
        if (shot is not { } value) return true;
        if (shots.IsEmpty || !VanillaDefinitionCatalog.TryGet(value.Type, out var definition)) return false;
        shots[0] = new(value.Type, value.CenterX - definition.Width * .5f, value.CenterY - definition.Height * .5f,
            value.VelocityX, value.VelocityY, value.Damage, 0f);
        count = 1;
        return true;
    }
}
