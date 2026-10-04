using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private RuntimeTownSocialWorld1458? socialWorld;
    private PlayerAuthority? socialPlayerOwner;
    private RuntimeWorldClock? socialClockOwner;
    private RuntimeWorldProgressionMutations? socialProgressionOwner;
    private readonly SocialPlayer[] socialPlayers = new SocialPlayer[byte.MaxValue];
    private int socialPlayerCount;
    private readonly record struct SocialPlayer(ConnectionHandle Connection, ulong Revision,
        RuntimeTownPlayerBounds1458 Bounds, bool Dead);

    internal void SetSocialContext(RuntimeTownSocialWorld1458? world, PlayerAuthority? players,
        RuntimeWorldClock? clock = null, RuntimeWorldProgressionMutations? progression = null)
    {
        socialWorld = world;
        socialPlayerOwner = players;
        socialClockOwner = clock;
        socialProgressionOwner = progression;
        socialPlayerCount = 0;
        if (players is null) return;
        foreach (RuntimePlayerMember player in players.Members)
        {
            if (player.Slot.Value == byte.MaxValue) continue;
            (float width, float height) = player.HasMount
                ? TerraRuntime.Gameplay.Players.VanillaPlayerMountHitbox1458.Resolve(player.MountType)
                : (PlayerAuthority.VanillaBasePlayerWidth, PlayerAuthority.VanillaBasePlayerHeight);
            socialPlayers[socialPlayerCount++] = new(player.Connection, player.Revision,
                new(player.PositionX, player.PositionY, width, height), player.IsDead);
        }
    }

    // Recheck the complete candidate/closest-player population before any RNG adoption or packet91.
    private bool SocialContextIsCurrent(ReadOnlySpan<NpcSnapshot> expected)
    {
        if (socialWorld is { } world &&
            (socialClockOwner is { } clock && (clock.DayTime != world.DayTime ||
                clock.BloodMoonActive != world.BloodMoon || clock.Time != world.Time) ||
             socialProgressionOwner is { } progression && progression.CaptureSnapshot() != world.Progression)) return false;
        int actualCount = 0;
        for (int slot = 0; slot < RuntimeNpcStore.MaximumAddressableCapacity; slot++)
            if (npcs.TryGetActive((byte)slot, out NpcSnapshot current))
            {
                actualCount++;
                bool matched = false;
                foreach (NpcSnapshot before in expected)
                    if (current.Handle == before.Handle && current.Revision == before.Revision) { matched = true; break; }
                if (!matched) return false;
            }
        if (actualCount != expected.Length) return false;
        if (socialPlayerOwner is null) return socialPlayerCount == 0;
        int playerCount = 0;
        foreach (RuntimePlayerMember player in socialPlayerOwner.Members)
        {
            if (player.Slot.Value == byte.MaxValue) continue;
            playerCount++;
            bool matched = false;
            for (int i = 0; i < socialPlayerCount; i++)
                if (socialPlayers[i].Connection == player.Connection && socialPlayers[i].Revision == player.Revision)
                { matched = true; break; }
            if (!matched) return false;
        }
        return playerCount == socialPlayerCount;
    }

    internal bool TryContextualEmote(in NpcStateUpdate actor, in NpcStateUpdate other,
        ReadOnlySpan<NpcSnapshot> peers, out byte emote)
    {
        emote = 0;
        if (random is not NpcRuntimeTownScheduleRandom1458 { Current: SystemVanillaNpcRandom }) return false;
        Span<byte> buffer = stackalloc byte[256];
        var choices = new SocialCandidates(buffer);
        bool boss = false;
        foreach (NpcSnapshot peer in peers)
        {
            if (!VanillaTownNpcSocialEmoteCatalog1458.TryGet(peer.TypeIdentity, out bool sourceBoss, out _)) return false;
            boss |= sourceBoss;
        }
        if (boss) choices.Add([16, 1, 2, 91, 93, 84, 84]);
        else
        {
            if (socialWorld is not { } world || world.Metadata is null) return false;
            if (random.Next(3) == 0)
            {
                // Source counts identities, then appends faces in identity order, not physical-slot order.
                Span<bool> present = stackalloc bool[VanillaTownNpcSocialEmoteCatalog1458.PositiveIdentityCount];
                present.Clear();
                foreach (NpcSnapshot peer in peers) present[peer.Type] = true;
                for (int type = 1; type < present.Length; type++)
                    if (type != actor.Type && present[type] &&
                        VanillaTownNpcSocialEmoteCatalog1458.TryGet(new(type), out _, out byte face) && face > 0)
                        choices.Add(face);
            }
            if (random.Next(3) == 0)
            {
                choices.Add([0, 1, 2, 3, 15, 16, 17, 87, 91, 136, 134, 135, 137, 138, 139]);
                if (world.BloodMoon && !world.DayTime)
                { ReadOnlySpan<byte> angry = [16, 1, 138]; byte selected = angry[random.Next(3)]; choices.Add([selected, selected, selected]); }
            }
            if (random.Next(3) == 0 && !TrySocialBiome(in actor, in world, ref choices)) return false;
            if (random.Next(2) == 0 && !TrySocialCritters(in actor, in world, ref choices)) return false;
            // Source Items needs derived statLifeMax2; packet16 owns only statLifeMax. No guessed substitute.
            if (random.Next(2) == 0) return false;
            if (random.Next(5) == 0) SocialBosses(in world, ref choices);
            // NPC fire/poison, live events and cloudBGActive/cloudAlpha have no complete owner yet.
            if (random.Next(2) == 0) return false;
            if (random.Next(2) == 0) return false;
            if (random.Next(2) == 0) return false;
            SocialExceptions(in actor, in other, in world, ref choices);
        }
        if (choices.Count > 0) emote = choices[random.Next(choices.Count)];
        return true;
    }

    private bool TrySocialBiome(in NpcStateUpdate actor, in RuntimeTownSocialWorld1458 world,
        ref SocialCandidates choices)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(new(actor.Type), new(actor.NetId), out var definition) ||
            !definition.TryResolveHitbox(actor.Simulation, out var size)) return false;
        float cx = actor.PositionX + size.Width * .5f, cy = actor.PositionY + size.Height * .5f;
        SocialPlayer? closest = null;
        float distance = -1f;
        foreach (SocialPlayer player in socialPlayers.AsSpan(0, socialPlayerCount))
        {
            // FindClosest fallback is the first active physical slot, including dead players.
            if (closest is null || distance < 0f && player.Connection.Player.Slot.Value < closest.Value.Connection.Player.Slot.Value)
                closest = player;
            if (player.Dead) continue;
            float candidate = MathF.Abs(player.Bounds.X + (int)player.Bounds.Width / 2 - cx) +
                MathF.Abs(player.Bounds.Y + (int)player.Bounds.Height / 2 - cy);
            if (distance < 0f || candidate < distance || candidate == distance &&
                player.Connection.Player.Slot.Value < closest.Value.Connection.Player.Slot.Value)
            { distance = candidate; closest = player; }
        }
        if (closest is not { } selected) return false; // Source inactive slot0 body has no owner.
        double tileY = selected.Bounds.Y / 16f;
        if (tileY < world.Metadata.WorldSurface * .45d) choices.Add(22);
        else if (tileY > world.Metadata.RockLayer + tiles.Dimensions.HeightTiles / 2 - 100d) choices.Add(31);
        else if (tileY > world.Metadata.RockLayer) choices.Add(30);
        else return false; // Remote Zone* comes from packet36, not commerce's tile scan.
        return true;
    }

    private bool TrySocialCritters(in NpcStateUpdate actor, in RuntimeTownSocialWorld1458 world,
        ref SocialCandidates choices)
    {
        if (random is not NpcRuntimeTownScheduleRandom1458 adapter ||
            !VanillaNpcDefinitionCatalog.TryGet(new(actor.Type), new(actor.NetId), out var definition) ||
            !definition.TryResolveHitbox(actor.Simulation, out var size)) return false;
        bool above = actor.PositionY + size.Height * .5f < world.Metadata.RockLayer * 16d;
        if ((float)adapter.Current.NextDouble() <= (above ? 1f : .2f))
        {
            if (world.DayTime) choices.Add([13, 12, 68, 62, 63, 69, 70]);
            if (!world.DayTime || world.Time < 5400d || world.Time > 48600d) choices.Add(61);
            if (world.Done(VanillaWorldProgressionId.GoblinArmy, world.Metadata.DownedGoblins)) choices.Add(64);
            if (world.Done(VanillaWorldProgressionId.FrostLegion, world.Metadata.DownedFrost)) choices.Add(66);
            if (world.Done(VanillaWorldProgressionId.PirateInvasion, world.Metadata.DownedPirates)) choices.Add(65);
            if (world.Done(VanillaWorldProgressionId.MartianMadness, world.Metadata.DownedMartians)) choices.Add(71);
            if (world.Metadata.Crimson) choices.Add(67);
        }
        if ((float)adapter.Current.NextDouble() <= (above ? .2f : 1f)) choices.Add([72, 69]);
        return true;
    }

    private void SocialBosses(in RuntimeTownSocialWorld1458 world, ref SocialCandidates choices)
    {
        var m = world.Metadata;
        int stage = world.Done(VanillaWorldProgressionId.EyeOfCthulhu, m.DownedBoss1) || !world.DayTime ? 1 : 0;
        if (world.Done(VanillaWorldProgressionId.EvilBoss, m.DownedBoss2)) stage = 2;
        if (world.Done(VanillaWorldProgressionId.QueenBee, m.DownedQueenBee) || world.Done(VanillaWorldProgressionId.Skeletron, m.DownedBoss3)) stage = 3;
        if (world.HardMode) stage = 4;
        if (world.Done(VanillaWorldProgressionId.AnyMechanicalBoss, m.DownedMechBossAny)) stage = 5;
        if (world.Done(VanillaWorldProgressionId.Plantera, m.DownedPlantBoss)) stage = 6;
        if (world.Done(VanillaWorldProgressionId.Golem, m.DownedGolemBoss)) stage = 7;
        if (world.Done(VanillaWorldProgressionId.LunaticCultist, m.DownedAncientCultist)) stage = 8;
        int bound = world.Done(VanillaWorldProgressionId.MoonLord, m.DownedMoonlord) ? 1 : 10;
        if (stage is >= 1 and <= 2 || stage >= 1 && random.Next(bound) == 0) choices.Add([39, m.Crimson ? (byte)41 : (byte)40, 51]);
        if (stage is >= 2 and <= 3 || stage >= 2 && random.Next(bound) == 0) choices.Add([43, 42]);
        if (stage is >= 4 and <= 5 || stage >= 4 && random.Next(bound) == 0) choices.Add([44, 47, 45, 46]);
        if (stage is >= 5 and <= 6 || stage >= 5 && random.Next(bound) == 0)
        {
            if (!world.Done(VanillaWorldProgressionId.Destroyer, m.DownedMechBoss1)) choices.Add(47);
            if (!world.Done(VanillaWorldProgressionId.Twins, m.DownedMechBoss2)) choices.Add(45);
            if (!world.Done(VanillaWorldProgressionId.SkeletronPrime, m.DownedMechBoss3)) choices.Add(46);
            choices.Add(48);
        }
        if (stage == 6 || stage >= 6 && random.Next(bound) == 0) choices.Add([48, 49, 50]);
        if (stage == 7 || stage >= 7 && random.Next(bound) == 0) choices.Add([49, 50, 52]);
        if (stage == 8 || stage >= 8 && random.Next(bound) == 0) choices.Add([52, 53]);
        if (world.ExpertMode && world.Done(VanillaWorldProgressionId.PirateInvasion, m.DownedPirates)) choices.Add(59);
        ReadOnlySpan<(VanillaWorldProgressionId Id, bool Saved, byte Emote)> extras = [
            (VanillaWorldProgressionId.MartianMadness,m.DownedMartians,60),
            (VanillaWorldProgressionId.IceQueen,m.DownedChristmasIceQueen,57),
            (VanillaWorldProgressionId.SantaNk1,m.DownedChristmasSantank,58),
            (VanillaWorldProgressionId.Everscream,m.DownedChristmasTree,56),
            (VanillaWorldProgressionId.Pumpking,m.DownedHalloweenKing,55),
            (VanillaWorldProgressionId.MourningWood,m.DownedHalloweenTree,54),
            (VanillaWorldProgressionId.EmpressOfLight,m.DownedEmpressOfLight,143),
            (VanillaWorldProgressionId.QueenSlime,m.DownedQueenSlime,144),
            (VanillaWorldProgressionId.Deerclops,m.DownedDeerclops,150)];
        foreach (var extra in extras) if (world.Done(extra.Id, extra.Saved)) choices.Add(extra.Emote);
    }

    private ref struct SocialCandidates(Span<byte> buffer)
    {
        private readonly Span<byte> values = buffer;
        internal int Count { get; private set; }
        internal readonly byte this[int index] => values[index];
        internal void Add(byte value) => values[Count++] = value;
        internal void Add(scoped ReadOnlySpan<byte> items) { items.CopyTo(values[Count..]); Count += items.Length; }
        internal readonly bool Contains(byte value) => values[..Count].Contains(value);
        internal void Remove(byte value)
        { int index = values[..Count].IndexOf(value); if (index >= 0) { values[(index + 1)..Count].CopyTo(values[index..]); Count--; } }
    }
}
