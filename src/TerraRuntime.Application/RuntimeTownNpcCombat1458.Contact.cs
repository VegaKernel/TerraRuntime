using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcCombat1458
{
    private const int Gnome = 624;
    private const int StatueMimic = 690;
    private const int MoonLordFreeEye = 400;
    private const int HostileContactDuration = 30;
    private const float ContactKnockBack = 6f;

    internal readonly record struct ContactStrike(int VariedDamage, float KnockBack, int HitDirection);

    internal bool TryPlanContact(in NpcSnapshot source, in NpcStateUpdate accelerated,
        ReadOnlySpan<NpcSnapshot> peers, bool canTransactRandom, out NpcStateUpdate next, out ContactStrike? strike)
    {
        next = accelerated;
        strike = null;
        if (next.Simulation.DontTakeDamage || next.Simulation.Immortal == true ||
            next.Simulation.HostileContactImmunity > 0) return true;
        if (next.Simulation.Immortal is null || !TryRectangle(next.PositionX, next.PositionY,
                source.TypeIdentity, source.NetIdentity, next.Simulation, out var victim)) return false;
        foreach (NpcSnapshot peer in peers)
        {
            if (!peer.IsActive || peer.Handle == source.Handle || peer.Handle.Slot >= 200 ||
                peer.Type is Gnome or StatueMimic or MoonLordFreeEye ||
                VanillaTownNpcDangerCatalog1458.IsTurningCritter(peer.Type) || peer.Simulation.Friendly == true) continue;
            if (peer.Simulation.Friendly is null ||
                !VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var attacker) ||
                !TryRectangle(peer.PositionX, peer.PositionY, peer.TypeIdentity, peer.NetIdentity, peer.Simulation, out var body))
                return false;
            int damage = peer.Simulation.DamageOverride ?? attacker.Damage;
            if (damage <= 0) continue;
            float attackerCenterX = peer.PositionX + body.Width * .5f;
            if (!TryContactGeometry(in peer, in victim, ref body)) return false;
            if (!Intersects(in victim, in body)) continue;
            // Bees reflect the resident's source damage into the attacker in a second actor transaction.
            // That sibling damage/death transaction is not part of this ordinary one-resident family.
            if (peer.TypeIdentity == VanillaNpcIds.Bee || peer.TypeIdentity == VanillaNpcIds.SmallBee) return false;
            if (!canTransactRandom || !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition)) return false;
            int defense = next.Simulation.DefenseOverride ?? definition.Defense;
            var maximum = new NpcDamageRequest(source.Handle, DamageSource.FromNpcContact(peer.Handle),
                (int)Math.Round(damage * 1.15f), KnockBack: ContactKnockBack, HitDirection: 1);
            // Town death requires source chat/tombstone/loot ownership; do not admit a potentially lethal strike.
            if (!VanillaNpcDamageResolver.TryResolve(defense, in maximum, out _, out int maximumDamage) ||
                maximumDamage >= next.Simulation.Life) return false;
            int varied = (int)Math.Round(damage * (1f + (random.Next(31) - 15) * .01f));
            if (varied > short.MaxValue) return false;
            int direction = attackerCenterX > next.PositionX + victim.Width * .5f ? -1 : 1;
            var request = maximum with { BaseDamage = varied, HitDirection = direction };
            if (!VanillaNpcDamageResolver.TryResolve(defense, in request, out _, out int resolved)) return false;
            var knockback = VanillaNpcKnockbackResolver.Resolve(next.VelocityX, next.VelocityY,
                next.Simulation.NoGravity, next.Simulation.LifeMax,
                next.Simulation.KnockBackResist ?? definition.KnockBackResist,
                ContactKnockBack, direction, resolved, false, expertMode);
            next = next with { VelocityX = knockback.VelocityX, VelocityY = knockback.VelocityY,
                Ai = next.Ai with { Ai0 = 1f, Ai1 = 300 + random.Next(300), Ai2 = 0f },
                Simulation = next.Simulation with { Life = next.Simulation.Life - resolved,
                    JustHit = true, DirectionX = direction, HostileContactImmunity = HostileContactDuration,
                    LocalAi = next.Simulation.LocalAi with { Ai3 = 0f } } };
            strike = new ContactStrike(varied, ContactKnockBack, direction);
            return true; // Source slot order: only the first admitted hostile strikes this resident.
        }
        return true;
    }

    internal void PublishContact(in NpcSnapshot committed, in ContactStrike strike)
    {
        if (contactReplication is null) return;
        var wire = new TerrariaNpcDamageState(committed.Handle.Slot,
            RuntimeNpcPacketProjection.ToProtocolGeneration(committed.Handle.Generation),
            checked((short)strike.VariedDamage), strike.KnockBack,
            checked((byte)(strike.HitDirection + 1)), 0);
        // StrikeNPC sends28 before its inner mutation. Our accepted transaction publishes the same
        // strike first, followed by its sole final23, after unpublished state and RNG have been adopted.
        contactReplication.TryPublishDamage(default, in wire);
    }

    internal static NpcStateUpdate PlanFriendlyRegeneration(in NpcStateUpdate state)
    {
        if (state.Simulation.Life >= state.Simulation.LifeMax) return state;
        const int RegenerationThreshold = 180;
        const int GuideExtraRegeneration = 5;
        const int CyborgExtraRegeneration = 9;
        int increment = 1 + (state.Type == VanillaNpcIds.Guide.Value ? GuideExtraRegeneration :
            state.Type == VanillaNpcIds.Cyborg.Value ? CyborgExtraRegeneration : 0);
        int counter = state.Simulation.FriendlyRegenerationCounter + increment;
        bool heal = counter > RegenerationThreshold;
        return state with { Simulation = state.Simulation with { FriendlyRegenerationCounter = heal ? 0 : counter,
            Life = state.Simulation.Life + (heal ? 1 : 0) } };
    }

    internal bool AdmitsContactBeforeWorldEffects(in NpcSnapshot input, ReadOnlySpan<NpcSnapshot> peers)
    {
        NpcSnapshot resident = input;
        var vitals = PlanFriendlyRegeneration(new NpcStateUpdate(resident.Type, resident.NetId,
            resident.PositionX, resident.PositionY, resident.VelocityX, resident.VelocityY, resident.Target,
            resident.Ai, resident.Simulation));
        resident = resident with { Simulation = vitals.Simulation };
        if (resident.Simulation.Immortal == true || resident.Simulation.HostileContactImmunity > 1) return true;
        if (!TryRectangle(resident.PositionX, resident.PositionY, resident.TypeIdentity, resident.NetIdentity,
                resident.Simulation, out var body) ||
            !VanillaNpcDefinitionCatalog.TryGet(resident.TypeIdentity, resident.NetIdentity, out var definition)) return false;
        // Home relocation is already staged. Walking chair alignment can move within its bottom tile;
        // cover two tile widths before any door mutation so a later unowned contact cannot leave a door effect.
        const int FurnitureContactMarginPixels = 32;
        var envelope = body with { X = body.X - FurnitureContactMarginPixels, Y = body.Y - FurnitureContactMarginPixels,
            Width = body.Width + FurnitureContactMarginPixels * 2, Height = body.Height + FurnitureContactMarginPixels * 2 };
        foreach (NpcSnapshot peer in peers)
        {
            if (!peer.IsActive || peer.Handle == resident.Handle || peer.Handle.Slot >= 200 ||
                peer.Type is Gnome or StatueMimic or MoonLordFreeEye ||
                VanillaTownNpcDangerCatalog1458.IsTurningCritter(peer.Type) || peer.Simulation.Friendly == true) continue;
            if (peer.Simulation.Friendly is null ||
                !VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var attacker) ||
                !TryRectangle(peer.PositionX, peer.PositionY, peer.TypeIdentity, peer.NetIdentity, peer.Simulation, out var contact)) return false;
            int damage = peer.Simulation.DamageOverride ?? attacker.Damage;
            if (damage <= 0) continue;
            if (!TryContactGeometry(in peer, in envelope, ref contact)) return false;
            if (!Intersects(in envelope, in contact)) continue;
            if (peer.TypeIdentity == VanillaNpcIds.Bee || peer.TypeIdentity == VanillaNpcIds.SmallBee) return false;
            var maximum = new NpcDamageRequest(resident.Handle, DamageSource.FromNpcContact(peer.Handle),
                (int)Math.Round(damage * 1.15f), KnockBack: ContactKnockBack, HitDirection: 1);
            if (maximum.BaseDamage > short.MaxValue ||
                !VanillaNpcDamageResolver.TryResolve(resident.Simulation.DefenseOverride ?? definition.Defense,
                    in maximum, out _, out int resolved) || resolved >= resident.Simulation.Life) return false;
        }
        return true;
    }

    private readonly record struct ContactRectangle(int X, int Y, int Width, int Height);
    private static bool TryRectangle(float x, float y, NpcTypeId type, NpcNetId net,
        in NpcSimulationState simulation, out ContactRectangle rectangle)
    {
        rectangle = default;
        if (!float.IsFinite(x) || !float.IsFinite(y) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, net, out var definition) ||
            !definition.TryResolveHitbox(simulation, out var size)) return false;
        rectangle = new((int)x, (int)y, size.Width, size.Height);
        return true;
    }
    private static bool Intersects(in ContactRectangle a, in ContactRectangle b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private static bool TryContactGeometry(in NpcSnapshot peer, in ContactRectangle victim, ref ContactRectangle body)
    {
        // These source contact bodies depend on animation frames not yet retained by the runtime.
        // Admit the ordinary rectangle family; their complete frame-dependent body is a separate boundary.
        const int OgreTierTwo = 576, OgreTierThree = 577, Deerclops = 668;
        const int ArmedZombieFirst = 430, ArmedZombieLast = 436, ArmedTorchZombie = 591;
        const int Crawdad = 494, CrawdadAlternate = 495;
        const int SolarSroller = 417, Psycho = 466;
        const int GoblinTierOne = 552, GoblinTierTwo = 553, GoblinTierThree = 554;
        if (peer.Type is OgreTierTwo or OgreTierThree or Deerclops)
        {
            // GetMeleeCollisionData uses unretained frame rows. Its largest Ogre side weapon is 120px;
            // Deerclops reaches 100px above and 80px below its body. Far actors cannot contact this victim.
            const int FrameContactSideReachPixels = 120;
            const int FrameContactTopReachPixels = 100;
            const int FrameContactBottomReachPixels = 80;
            var possible = body with { X = body.X - FrameContactSideReachPixels,
                Y = body.Y - FrameContactTopReachPixels, Width = body.Width + FrameContactSideReachPixels * 2,
                Height = body.Height + FrameContactTopReachPixels + FrameContactBottomReachPixels };
            return !Intersects(in victim, in possible);
        }
        int extension = peer.Type is >= ArmedZombieFirst and <= ArmedZombieLast or ArmedTorchZombie && peer.Ai.Ai2 > 5f ? 34 :
            peer.Type is Crawdad or CrawdadAlternate && peer.Ai.Ai2 > 5f ? 18 : 0;
        if (extension != 0)
        {
            body = body with { X = body.X - (peer.Simulation.SpriteDirection < 0 ? extension : 0), Width = body.Width + extension };
            return true;
        }
        int width = 0, height = 0, bottomOffset = 0;
        if (peer.TypeIdentity == VanillaNpcIds.Butcher) { width = 30; height = 14; bottomOffset = 20; }
        else if (peer.Type == Psycho) { width = 30; height = 8; bottomOffset = 32; }
        else if (peer.Type is GoblinTierOne or GoblinTierTwo or GoblinTierThree && peer.Ai.Ai0 is > 0f and < 24f)
        { width = 34; height = 14; bottomOffset = 20; }
        else if (peer.Type == SolarSroller && peer.Ai.Ai0 == 6f && peer.Ai.Ai3 is > 0f and < 4f)
        {
            var expanded = new ContactRectangle((int)(peer.PositionX + body.Width * .5f - 50f),
                (int)(peer.PositionY + body.Height * .5f - 50f), 100, 100);
            if (Intersects(in victim, in expanded)) body = expanded;
            return true;
        }
        if (width != 0)
        {
            var weapon = new ContactRectangle((int)(peer.PositionX + body.Width * .5f) -
                (peer.Simulation.DirectionX < 0 ? width : 0), body.Y + body.Height - bottomOffset, width, height);
            if (Intersects(in victim, in weapon)) body = weapon;
        }
        return true;
    }
}
