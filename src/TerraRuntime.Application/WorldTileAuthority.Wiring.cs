using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private WorldWiringCircuit1458? wiring;
    private void InitializeWiring(bool plantBossDowned)
    {
        if (tiles is null) return;
        var devices = new WorldWiringDeviceExecutor1458(tiles, playerDoorOpenings, PublishWiringSquare,
            () => worldItemSpawnRandom.NextInt32(0, 2) == 0 ? -1 : 1,
            () => plantBossDowned || progression.IsCompleted(VanillaWorldProgressionId.Plantera),
            canKillTile: (x, y) => VanillaToolWorldRules1458.TryEvaluate(tiles, x, y,
                hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode), out var result) && result.CanKill,
            publishDoorToggle: transition => replication?.TryPublishDoorToggle(new TerrariaDoorToggleState(
                (byte)transition.Action, (short)transition.X, (short)transition.Y, (sbyte)transition.Direction)));
        wiring = new WorldWiringCircuit1458(tiles, devices, PublishWiringSquare);
    }

    private void PublishWiringSquare(int x, int y, byte width, byte height)
    {
        if (tiles is not null)
            replication?.TryPublishTileSquareToAll(tiles, x, y, width, height,
                VanillaTileChangeType1458.None, respectSectionRange: true);
    }

    private void ApplyHitSwitch(ClientHitSwitchRuntimeCommand1458 command)
    {
        ClientManipulationRequests++;
        // Source case59 has no reach check. Keep repeated legal presses meaningful; authenticate
        // the full connection generation on execution, never infer world state from client frames.
        if (wiring is null || !players.TryGet(command.Connection, out _) ||
            !editBudget.TryConsume(command.Connection.Player.Slot) ||
            !wiring.HitSwitch(command.State.TileX, command.State.TileY))
        {
            RejectedClientManipulations++;
            return;
        }
        AppliedClientManipulations++;
        replication?.TryPublishHitSwitch(command.Connection.Source, command.State);
    }

    public void TickMechanisms() => wiring?.Tick();

    public void ApplyPlayerPressurePlate(PlayerStateSnapshot before, PlayerStateSnapshot after)
    {
        if (tiles is null || wiring is null || before.Player != after.Player || after.IsDead ||
            (after.MovementFlags & 64) != 0) return;
        (float width, float height) = after.HasMount
            ? VanillaPlayerMountHitbox1458.Resolve(after.MountType) : (20, 42);
        if (!WorldPressurePlateContacts1458.TryFindContact(tiles, after.PositionX, after.PositionY,
            (int)width, (int)height, before.PositionX, before.PositionY, WorldPressurePlateActor1458.Player, out var contact)) return;
        // Style7 must destroy the plate after its impulse. That path needs the full source kill/drop
        // transaction; leave it unadmitted until that owner is connected rather than retain a spent plate.
        if (contact.DestroyAfterPulse) return;
        if (wiring.HitSwitch(contact.X, contact.Y))
            replication?.TryPublishHitSwitch(default, new((short)contact.X, (short)contact.Y));
    }

    public void ApplyNpcPressurePlate(NpcSnapshot before, NpcSnapshot after)
    {
        if (tiles is null || wiring is null || before.Handle != after.Handle ||
            after.Simulation.NoTileCollide || after.Simulation.LifeMax <= 1) return;
        int width, height;
        if (after.Simulation.HitboxOverride is { } physical)
        { width = physical.Width; height = physical.Height; }
        else if (VanillaNpcDefinitionCatalog.TryGet(after.TypeIdentity, after.NetIdentity, out var definition) &&
            definition.TryResolveHitbox(after.Simulation, out var hitbox))
        { width = hitbox.Width; height = hitbox.Height; }
        else return;
        if (!WorldPressurePlateContacts1458.TryFindContact(tiles, after.PositionX, after.PositionY, width, height,
            before.PositionX, before.PositionY, WorldPressurePlateActor1458.Npc, out var contact) ||
            contact.DestroyAfterPulse || !wiring.HitSwitch(contact.X, contact.Y)) return;
        replication?.TryPublishHitSwitch(default, new((short)contact.X, (short)contact.Y));
        // NPC.UpdateNPC puts represented pressure-sensitive critters into their flee state after a pulse.
        if (after.Type is 46 or 148 or 149 or 303 or 361 or 362 or 364 or 366 or 367 or
            >= 442 and <= 448 or 602 or 608 or 614 or 687 && npcs.TryGet(after.Handle, out var current) && current.Revision == after.Revision)
        {
            var update = new NpcStateUpdate(current.Type, current.NetId, current.PositionX, current.PositionY,
                current.VelocityX, current.VelocityY, current.Target,
                current.Ai with { Ai0 = 1, Ai1 = 400, Ai2 = 0 }, current.Simulation);
            npcs.TryUpdate(current.Handle, in update, out _);
        }
    }
}
