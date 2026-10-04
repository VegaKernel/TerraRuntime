using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    public RuntimeTownNpcCombatTickSummary1458 Tick(in RuntimeTownNpcScheduleConditions1458 conditions,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players,
        RuntimeNpcStinkyStatus1458 status, RuntimeTownNpcCombat1458 combat,
        ReadOnlySpan<RuntimeTownPlayerConversation1458> conversations = default,
        ReadOnlySpan<RuntimeTownPlayerSeat1458> seatedPlayers = default,
        ReadOnlySpan<RuntimeTownPlayerDanger1458> playerDanger = default)
    {
        combat.BeginWorldTick();
        if (doors.Count > 0)
            for (int index = 0; index <= byte.MaxValue; index++)
                if (doors.TryGetValue((byte)index, out RememberedDoor retained) &&
                    (!npcs.TryGetActive((byte)index, out NpcSnapshot live) || live.Handle != retained.Handle))
                    doors.Remove((byte)index);
        int visited = 0, started = 0, advanced = 0, shots = 0, hits = 0, rejected = 0;
        Span<RuntimeTownNpcHomeCommit> homes = stackalloc RuntimeTownNpcHomeCommit[RuntimeTownNpcStateStore.MaximumTownNpcs];
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        Span<RuntimeTownNpcMeleeIntent1458> melee = stackalloc RuntimeTownNpcMeleeIntent1458[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = townNpcs.CopyHomeBaselines(homes);
        for (int index = 0; index < count; index++)
        {
            RuntimeTownNpcHomeCommit home = homes[index];
            if ((uint)home.NpcSlot > byte.MaxValue || !npcs.TryGetActive((byte)home.NpcSlot, out var before) ||
                before.Type != home.NpcType.Value) continue;
            visited++;
            int peerCount = npcs.CopyActive(peers);
            if (!AdmitsOuterContact(in before, peers[..peerCount]) ||
                !status.TryGetStinky(before.Handle, out bool selfStinky)) { rejected++; continue; }
            int originX = BottomTileX(in before, home.NpcType), originY = BottomTileY(in before, home.NpcType, 1f);
            if (Interior(originX, originY) && Cell(originX, originY).TileType == VanillaTileIds.PoopBlock)
                status.TryApplyRepeated(before.Handle);
            if (selfStinky)
            {
                RuntimeNpcStinkyVisualOffer1458 offer = combat.PlanStinkyVisualOffer();
                status.ObserveVisualOffer(before.Handle, in offer);
            }
            if (!TryPlanUnifiedResident(in before, in home, in conditions, players, conversations,
                    seatedPlayers, playerDanger, peers[..peerCount], status, combat, melee,
                    out NpcStateUpdate aiState, out bool force, out NpcAiProjectileIntent? projectile,
                    out int meleeCount)) { rejected++; continue; }
            double surface = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);
            if (!VanillaNpcWorldMotionAiStepper.TryFinishPhysics(tiles, surface, in before, in aiState, out var moved))
            { rejected++; continue; }
            moved = FinishResidentPresentation(in moved);
            if (!npcs.TryGet(before.Handle, out var current) || current.Revision != before.Revision ||
                !npcs.TryUpdate(before.Handle, in moved, out var committed, forceSync: force)) { rejected++; continue; }
            townNpcs.TryUpdatePosition(home.NpcSlot, in committed);
            states[home.NpcSlot] = committed.Ai.Ai0 == 5f || conditions.ReturnHomeRequested && committed.Ai.Ai0 == 0f &&
                Math.Abs(BottomTileX(in committed, home.NpcType) - home.HomeTileX) <= 1
                ? RuntimeTownNpcScheduleState1458.RestingAtHome :
                conditions.ReturnHomeRequested && committed.Ai.Ai0 is 0f or 1f
                    ? RuntimeTownNpcScheduleState1458.ReturningHome : RuntimeTownNpcScheduleState1458.DayWander;
            bool wasAttack = before.Ai.Ai0 is 10f or 12f or 15f;
            if (wasAttack) advanced++;
            else if (committed.Ai.Ai0 is 10f or 12f or 15f) started++;
            combat.ApplyCommittedEffects(in committed, projectile, melee[..meleeCount]);
            if (projectile.HasValue) shots++;
            hits += meleeCount;
        }
        return new(visited, started, advanced, shots, hits, rejected, 0);
    }

    internal static NpcStateUpdate FinishResidentPresentation(in NpcStateUpdate moved) =>
        moved.VelocityY == 0f && moved.Simulation.DirectionX is -1 or 1
            ? moved with { Simulation = moved.Simulation with { SpriteDirection = moved.Simulation.DirectionX } }
            : moved;

    internal bool TryPlanUnifiedResident(in NpcSnapshot before, in RuntimeTownNpcHomeCommit home,
        in RuntimeTownNpcScheduleConditions1458 conditions,
        ReadOnlySpan<RuntimeTownPlayerBounds1458> players,
        ReadOnlySpan<RuntimeTownPlayerConversation1458> conversations,
        ReadOnlySpan<RuntimeTownPlayerSeat1458> seatedPlayers,
        ReadOnlySpan<RuntimeTownPlayerDanger1458> playerDanger, ReadOnlySpan<NpcSnapshot> peers,
        RuntimeNpcStinkyStatus1458 status, RuntimeTownNpcCombat1458 combat,
        Span<RuntimeTownNpcMeleeIntent1458> melee, out NpcStateUpdate update, out bool force,
        out NpcAiProjectileIntent? projectile, out int meleeCount)
    {
        update = default;
        force = false;
        projectile = null;
        meleeCount = 0;
        if (!VanillaTownNpcFacts1458.TryGetHousingCategory(home.NpcType, out int category) || category != VanillaTownNpcFacts1458.OrdinaryHousingCategory ||
            before.Simulation.Wet || before.Ai.Ai0 is 13f or 14f or 24f ||
            VanillaWorldCollision.TryGetWetContact(tiles, before.PositionX, before.PositionY,
                GetWidth(home.NpcType), GetHeight(home.NpcType), out _)) return false;
        if (before.Handle.Slot >= TerrariaNpcTalkCodec.MaximumNpcSlots ||
            !TryPlanHomePrelude(in before, in home, in conditions, players, out var homeInput,
                out int floorX, out int floorY, out int originTileX, out int originTileY, out bool homeForce) ||
            !status.TryGetStinky(before.Handle, out bool selfStinky)) return false;
        force |= homeForce;
        bool attackExempt = homeInput.Ai.Ai0 is 10f or 12f or 14f or 15f or 24f;
        RuntimeTownPlayerConversation1458? talker = null;
        if (!attackExempt)
            foreach (RuntimeTownPlayerConversation1458 player in conversations)
                if (player.Slot < byte.MaxValue && player.TalkNpcSlot == before.Handle.Slot &&
                    (!talker.HasValue || player.Slot > talker.Value.Slot)) talker = player;
        bool activeTalk = talker.HasValue;
        bool meleeProfile = VanillaTownNpcMeleeAttackCatalog1458.TryGet(home.NpcType, out _);
        if (!RuntimeTownNpcDangerScanner1458.TryScan(tiles, in homeInput, peers, playerDanger, status,
                activeTalk, meleeProfile, out RuntimeTownNpcDanger1458 scanned)) return false;
        NpcSnapshot input = homeInput;
        if (activeTalk)
        {
            input = input with { Ai = input.Ai with { Ai0 = 0f, Ai1 = 300f },
                Simulation = input.Simulation with { LocalAi = input.Simulation.LocalAi with { Ai3 = 100f } } };
            force |= before.Ai.Ai0 != 0f;
            if (!scanned.WithinRange && !scanned.Present)
            {
                RuntimeTownPlayerBounds1458 bounds = talker!.Value.Bounds;
                int facing = bounds.X + (int)bounds.Width / 2 < input.PositionX + GetWidth(home.NpcType) / 2 ? -1 : 1;
                input = input with { Simulation = input.Simulation with { DirectionX = facing } };
            }
        }
        if (!TryPlanDangerResponse(in input, in home, in scanned, activeTalk, out var staged,
                out RuntimeTownNpcDanger1458 danger, out bool preludeForce)) return false;
        force |= preludeForce;
        bool activeAttack = staged.Ai.Ai0 is 10f or 12f or 15f;
        bool bodyForce;
        if (activeAttack)
        {
            if (!combat.TryPlanActiveAttack(in staged, in danger, peers, melee, out update,
                    out projectile, out meleeCount, out bodyForce)) return false;
        }
        else if (staged.Ai.Ai0 is 0f or 1f)
        {
            // Shelter geometry is staged separately; danger and authenticated talk suppress quiet return-home.
            bool shelter = conditions.ReturnHomeRequested && !activeTalk && !danger.WithinRange;
            bool atHome = IsInGoodRestingSpot(conditions.DayTime, staged.Ai.Ai0, originTileX, originTileY,
                floorX, floorY, home.NpcType, staged.Simulation.Wet);
            if (!TryPlanOrdinaryMotion(in staged, in home, shelter && atHome, shelter, seatedPlayers, in danger,
                    out update, out bodyForce, selfStinky, floorX, floorY, originTileX, originTileY)) return false;
        }
        else if (staged.Ai.Ai0 == 8f)
        {
            if (!TryPlanBlockedFlee(in staged, in danger, out update, out bodyForce)) return false;
        }
        else if (!TryPlanResidentPose(in staged, home.NpcType, conversations, out update, out bodyForce)) return false;
        force |= bodyForce;
        if (!activeTalk || danger.WithinRange)
        {
            if (!TryPlanRealIdleOffers(in staged, in update, in danger, conversations, playerDanger, peers,
                    conditions.PartyIsUp,
                    out update, out bool offerForce)) return false;
            force |= offerForce;
            if (!combat.TryPlanAttackInitialization(in before, in update, in danger, activeTalk,
                    out update, out bool attackForce)) return false;
            force |= attackForce;
        }
        return true;
    }

    internal bool TryPlanResidentPose(in NpcSnapshot source, NpcTypeId type,
        ReadOnlySpan<RuntimeTownPlayerConversation1458> players, out NpcStateUpdate update, out bool force)
    {
        update = default;
        force = false;
        NpcAiState ai = source.Ai;
        NpcAiState local = source.Simulation.LocalAi;
        int direction = source.Simulation.DirectionX;
        if (ai.Ai0 is 2f or 11f)
        {
            local = local with { Ai3 = local.Ai3 - 1f };
            if (random.Next(60) == 0 && local.Ai3 == 0f)
            {
                local = local with { Ai3 = 60f };
                direction *= -1;
                force = true;
            }
            ai = ai with { Ai1 = ai.Ai1 - 1f };
            if (ai.Ai1 <= 0f)
            {
                local = local with { Ai3 = 40f };
                ai = ai with { Ai0 = 0f, Ai1 = 60 + random.Next(60) };
                force = true;
            }
        }
        else if (ai.Ai0 is 5f or 9f)
        {
            ai = ai with { Ai1 = ai.Ai1 - 1f };
            if (ai.Ai0 == 5f)
            {
                int x = BottomTileX(in source, type), y = BottomTileY(in source, type, -2f);
                if (!Interior(x, y) || !VanillaTileIds.IsNpcChair(Cell(x, y).TileType)) ai = ai with { Ai1 = 0f };
            }
            if (ai.Ai1 <= 0f)
            {
                ai = ai with { Ai0 = 0f, Ai1 = 60 + random.Next(60), Ai2 = 0f };
                local = local with { Ai3 = 30 + random.Next(60) };
                force = true;
            }
        }
        else if (ai.Ai0 is 6f or 7f or 18f or 19f)
        {
            if (ai.Ai0 == 18f && (local.Ai3 < 1f || local.Ai3 > 2f)) local = local with { Ai3 = 2f };
            float remaining = ai.Ai1 - 1f;
            RuntimeTownPlayerConversation1458? peer = null;
            foreach (RuntimeTownPlayerConversation1458 player in players)
                if (player.Slot == (int)ai.Ai2) peer = player;
            float centerX = source.PositionX + GetWidth(type) * .5f, centerY = source.PositionY + GetHeight(type) * .5f;
            if (!peer.HasValue || !peer.Value.CanBeTalkedTo) remaining = 0f;
            else
            {
                RuntimeTownPlayerBounds1458 bounds = peer.Value.Bounds;
                float dx = bounds.X + bounds.Width * .5f - centerX, dy = bounds.Y + bounds.Height * .5f - centerY;
                if (MathF.Sqrt(dx * dx + dy * dy) > 200f ||
                    !VanillaWorldLineOfSight.CanHitLine(tiles, centerX, source.PositionY,
                        bounds.X + bounds.Width * .5f, bounds.Y)) remaining = 0f;
            }
            ai = ai with { Ai1 = remaining };
            if (remaining <= 0f)
            {
                ai = ai with { Ai0 = 0f, Ai1 = 60 + random.Next(60), Ai2 = 0f };
                local = local with { Ai3 = 30 + random.Next(60) };
                force = true;
            }
            else
            {
                int facing = centerX < peer!.Value.Bounds.X + peer.Value.Bounds.Width * .5f ? 1 : -1;
                force = direction != facing;
                direction = facing;
            }
        }
        else return false;
        update = new(source.Type, source.NetId, source.PositionX, source.PositionY,
            source.VelocityX * .8f, source.VelocityY, source.Target, ai,
            source.Simulation with { DirectionX = direction, DirectionY = -1, LocalAi = local });
        return true;
    }
}
