using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeActorInteractionBoundaryTests
{
    [Fact]
    public void Current_generations_in_vanilla_simple_reach_are_accepted_with_captured_revisions()
    {
        Fixture fixture = new(playerX: 80f, playerY: 100f);
        ActorInteractionRequest request = fixture.Request();

        ActorInteractionValidationResult result = fixture.Boundary.TryValidate(in request, out ActorInteractionAcceptance accepted);

        Assert.Equal(ActorInteractionValidationResult.Accepted, result);
        Assert.Equal(request, accepted.Request);
        Assert.Equal(fixture.Player.Revision, accepted.PlayerRevision);
        Assert.Equal(fixture.Npc.Revision, accepted.TargetRevision);
    }

    [Fact]
    public void Stale_npc_generation_cannot_interact_with_reused_slot()
    {
        Fixture fixture = new(playerX: 80f, playerY: 100f);
        ActorInteractionRequest stale = fixture.Request();
        Assert.True(fixture.Npcs.TryDespawn(fixture.Npc.Handle));
        NpcStateUpdate replacement = Fixture.CreateNpc(positionX: 100f, positionY: 100f);
        Assert.True(fixture.Npcs.TrySpawn(fixture.Npc.Handle.Slot, in replacement, out _));

        Assert.Equal(
            ActorInteractionValidationResult.InvalidTarget,
            fixture.Boundary.TryValidate(in stale, out _));
    }

    [Fact]
    public void Distant_player_is_rejected_before_policy_dispatch()
    {
        Fixture fixture = new(playerX: 1000f, playerY: 1000f);
        ActorInteractionRequest request = fixture.Request(ActorInteractionKind.NpcShopOpen);

        Assert.Equal(
            ActorInteractionValidationResult.OutOfRange,
            fixture.Boundary.TryValidate(in request, out _));
    }

    [Fact]
    public void Dead_player_is_unavailable()
    {
        Fixture fixture = new(playerX: 80f, playerY: 100f, playerDead: true);
        ActorInteractionRequest request = fixture.Request();

        Assert.Equal(
            ActorInteractionValidationResult.PlayerUnavailable,
            fixture.Boundary.TryValidate(in request, out _));
    }

    private sealed class Fixture
    {
        public Fixture(float playerX, float playerY, bool playerDead = false)
        {
            PlayerHandle playerHandle = new(new PlayerSlotId(0), new PlayerSessionGeneration(1));
            Player = CreatePlayer(playerHandle, playerX, playerY, playerDead);
            Players = new StubPlayerLookup(Player);
            Npcs = new RuntimeNpcStore(capacity: 4);
            NpcStateUpdate npc = CreateNpc(positionX: 100f, positionY: 100f);
            Assert.True(Npcs.TrySpawn(1, in npc, out NpcSnapshot created));
            Npc = created;
            Boundary = new RuntimeActorInteractionBoundary(Npcs, Players);
        }

        public PlayerStateSnapshot Player { get; }
        public StubPlayerLookup Players { get; }
        public RuntimeNpcStore Npcs { get; }
        public NpcSnapshot Npc { get; }
        public RuntimeActorInteractionBoundary Boundary { get; }

        public ActorInteractionRequest Request(
            ActorInteractionKind kind = ActorInteractionKind.NpcConversation) =>
            new(Player.Player, Npc.Handle, kind);

        public static NpcStateUpdate CreateNpc(float positionX, float positionY) =>
            new(
                Type: VanillaNpcIds.Zombie.Value,
                NetId: checked((short)VanillaNpcIds.Zombie.Value),
                PositionX: positionX,
                PositionY: positionY,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: default,
                Simulation: NpcSimulationState.Initial);

        private static PlayerStateSnapshot CreatePlayer(
            PlayerHandle player,
            float positionX,
            float positionY,
            bool dead) =>
            new(
                player,
                new PlayerStateRevision(1),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                positionX,
                positionY,
                VelocityX: 0f,
                VelocityY: 0f,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f)
            {
                HasHealth = true,
                Life = dead ? (short)0 : (short)100,
                MaxLife = 100,
                IsDead = dead
            };
    }

    private sealed class StubPlayerLookup(PlayerStateSnapshot player) : IRuntimePlayerSnapshotLookup
    {
        public bool TryGetPlayer(PlayerHandle handle, out PlayerStateSnapshot snapshot)
        {
            if (handle == player.Player)
            {
                snapshot = player;
                return true;
            }

            snapshot = default;
            return false;
        }
    }
}
