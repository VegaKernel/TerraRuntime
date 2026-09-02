using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    public void Tick()
    {
        _worldTileAuthority.AdvanceTo(Updates);

        _npcArchetypes.CommitPending();
        _npcShops.CommitPending();
        _npcActorCommands.CommitPending();
        TickServerPlayerPhysics();

        if (_vanillaNpcTargetingAiStepper is not null)
        {
            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);
            ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _npcTargetCandidates.AsSpan(0, candidateCount);
            _vanillaNpcTargetingAiStepper.SetCandidates(candidates);
            _vanillaNpcCheckActiveAiStepper?.SetCandidates(candidates);
            if (_worldClock is not null)
            {
                _vanillaNpcTargetingAiStepper.SetWorldConditions(
                    _worldClock.DayTime,
                    _worldClock.SlimeRainActive,
                    _worldClock.GetGoodWorld,
                    _expertMode,
                    _masterMode);
            }
        }

        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);
        _townNpcAuthority.TickShimmer();
        _townNpcAuthority.TickLifecycle(_worldClock);
        AppliedNpcDespawns += _npcs.DespawnExpired();
        if (_projectiles.TryTickState())
        {
            _townNpcAuthority.TickProjectileInteractions();
            _projectiles.ApplyReflections();
        }
        TickInstancedItemLeases();

        _worldClock?.Tick();
        Updates++;
    }

    private void TickServerPlayerPhysics()
    {
        if (_serverPlayerStates is null || _serverPlayerDryPhysics is null)
            return;

        int count = _serverPlayerStates.CopySnapshots(_serverPlayerSnapshots);
        for (int index = 0; index < count; index++)
        {
            PlayerStateSnapshot player = _serverPlayerSnapshots[index];
            ServerPlayerMovementIntent movementIntent =
                _serverPlayerCommands?.GetMovementIntent(player.Player) ?? ServerPlayerMovementIntent.Stop();
            ServerPlayerHorizontalIntent horizontalIntent;
            ServerPlayerJumpIntent jumpIntent;
            if (movementIntent.Kind != ServerPlayerMovementIntentKind.Stop)
            {
                RuntimeServerPlayerMovementIntentController.TryResolve(
                    in player,
                    in movementIntent,
                    this,
                    out horizontalIntent,
                    out jumpIntent);
            }
            else
            {
                horizontalIntent =
                    _serverPlayerCommands?.GetHorizontalIntent(player.Player) ?? ServerPlayerHorizontalIntent.Stop;
                jumpIntent =
                    _serverPlayerCommands?.GetJumpIntent(player.Player) ?? ServerPlayerJumpIntent.Released;
            }
            VanillaServerPlayerJumpState jumpState =
                _serverPlayerCommands?.GetJumpState(player.Player) ?? VanillaServerPlayerJumpState.Initial;
            int slot = player.Player.Slot.Value;
            VanillaLiquidContactState liquidContacts = _serverPlayerLiquidOwners[slot] == player.Player
                ? _serverPlayerLiquidContacts[slot]
                : default;
            if (!_serverPlayerDryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    jumpIntent,
                    in jumpState,
                    in liquidContacts,
                    out ServerPlayerDryPhysicsStepResult next,
                    out VanillaServerPlayerJumpState nextJumpState))
            {
                continue;
            }

            _serverPlayerCommands?.CommitJumpState(player.Player, in nextJumpState);
            VanillaLiquidContactState nextLiquidContacts = next.LiquidContacts;
            _serverPlayerLiquidOwners[slot] = player.Player;
            _serverPlayerLiquidContacts[slot] = nextLiquidContacts;

            if (next.PositionX == player.PositionX &&
                next.PositionY == player.PositionY &&
                next.VelocityX == player.VelocityX &&
                next.VelocityY == player.VelocityY)
            {
                continue;
            }

            if (_serverPlayerStates.TrySetMotion(
                player.Player,
                next.PositionX,
                next.PositionY,
                next.VelocityX,
                next.VelocityY,
                out PlayerStateSnapshot committed))
            {
                _serverPlayerEvents?.ServerPlayerMoved(in committed);
            }
        }
    }

    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)
    {
        int serverPlayerCount = _serverPlayerStates?.CopySnapshots(_serverPlayerSnapshots) ?? 0;
        int serverPlayerIndex = 0;
        int written = 0;

        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)
        {
            if (_players.TryGet(checked((byte)slot), out RuntimePlayerMember? player))
            {
                if (player.MountType != 0)
                    continue;

                destination[written++] = new VanillaNpcTargetCandidate(
                    Slot: checked((byte)slot),
                    CenterX: player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                    CenterY: player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                    Aggro: 0,
                    Active: true,
                    Dead: player.IsDead,
                    Ghost: false,
                    NoAggro: false);
                continue;
            }

            while (serverPlayerIndex < serverPlayerCount &&
                   _serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value < slot)
            {
                serverPlayerIndex++;
            }

            if (serverPlayerIndex >= serverPlayerCount ||
                _serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value != slot)
            {
                continue;
            }

            PlayerStateSnapshot serverPlayer = _serverPlayerSnapshots[serverPlayerIndex++];
            if (serverPlayer.MountType != 0)
                continue;

            destination[written++] = new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: serverPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                CenterY: serverPlayer.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: serverPlayer.IsDead,
                Ghost: false,
                NoAggro: false);
        }

        return written;
    }

    private bool IsTileActorFree(int tileX, int tileY)
    {
        if (_worldTiles is null)
            return false;
        if ((uint)tileX >= (uint)_worldTiles.Dimensions.WidthTiles || (uint)tileY >= (uint)_worldTiles.Dimensions.HeightTiles)
            return false;
        int tileLeft = tileX * 16;
        int tileTop = tileY * 16;
        int tileRight = tileLeft + 16;
        int tileBottom = tileTop + 16;
        foreach (RuntimePlayerMember player in _players.Members)
        {
            if (player.IsDead)
                continue;
            if (Intersects(
                    player.PositionX,
                    player.PositionY,
                    PlayerAuthority.VanillaBasePlayerWidth,
                    PlayerAuthority.VanillaBasePlayerHeight,
                    tileLeft,
                    tileTop,
                    tileRight,
                    tileBottom))
                return false;
        }
        if (_serverPlayerStates is not null)
        {
            int count = _serverPlayerStates.CopySnapshots(_serverPlayerSnapshots);
            for (int i = 0; i < count; i++)
            {
                PlayerStateSnapshot snapshot = _serverPlayerSnapshots[i];
                if (snapshot.IsDead)
                    continue;
                if (Intersects(
                        snapshot.PositionX,
                        snapshot.PositionY,
                        PlayerAuthority.VanillaBasePlayerWidth,
                        PlayerAuthority.VanillaBasePlayerHeight,
                        tileLeft,
                        tileTop,
                        tileRight,
                        tileBottom))
                    return false;
            }
        }
        var npcBuffer = new NpcSnapshot[_npcs.Capacity];
        int npcCount = _npcs.CopyActive(npcBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            if (!IsNpcFree(npcBuffer[i], tileLeft, tileTop, tileRight, tileBottom))
                return false;
        }
        return true;
    }

    private static bool IsNpcFree(in NpcSnapshot npc, int tileLeft, int tileTop, int tileRight, int tileBottom)
    {
        if (!npc.IsActive)
            return true;
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return !Intersects(npc.PositionX, npc.PositionY, 16f, 16f, tileLeft, tileTop, tileRight, tileBottom);
        }
        if (!definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            return true;
        return !Intersects(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height, tileLeft, tileTop, tileRight, tileBottom);
    }

    private static bool Intersects(float rx, float ry, float rw, float rh, int tx0, int ty0, int tx1, int ty1)
    {
        float rx1 = rx + rw;
        float ry1 = ry + rh;
        return rx < tx1 && rx1 > tx0 && ry < ty1 && ry1 > ty0;
    }

    internal bool IsTileActorFreeForTesting(int tileX, int tileY) => IsTileActorFree(tileX, tileY);
}
