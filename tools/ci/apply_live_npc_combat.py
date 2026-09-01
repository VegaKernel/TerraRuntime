from pathlib import Path

ROOT = Path('.')


def replace_once(path: str, old: str, new: str) -> None:
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one match, got {count}\n--- needle ---\n{old[:800]}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


def write_file(path: str, content: str) -> None:
    p = ROOT / path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding='utf-8')


write_file('src/TerraRuntime.Protocol/TerrariaNpcDamageState.cs', r'''namespace TerraRuntime.Protocol;

/// <summary>
/// Protocol-neutral projection of TerrariaServer 1.4.5.8 packet 28 / StrikeNPC.
/// Damage remains signed at the transport boundary because the vanilla server clamps negative wire damage to zero
/// only after the NPC generation check. HitDirectionWire is the raw source byte and maps to semantic -1..254.
/// </summary>
public readonly record struct TerrariaNpcDamageState(
    byte NpcSlot,
    byte Generation,
    short Damage,
    float KnockBack,
    byte HitDirectionWire,
    byte CriticalRaw)
{
    public int HitDirection => HitDirectionWire - 1;
    public bool Critical => CriticalRaw == 1;

    public bool IsStructurallyValid =>
        Generation != 0 &&
        float.IsFinite(KnockBack);
}

public enum TerrariaNpcDamageDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    InvalidState = 3
}

public enum TerrariaNpcDamageEncodeResult : byte
{
    Encoded = 0,
    InvalidState = 1,
    FrameTooLarge = 2,
    Failed = 3
}
''')

write_file('src/TerraRuntime.Protocol.Multiplicity/TerrariaNpcDamageCodec.cs', r'''using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Source-pinned packet-28 codec for TerrariaServer 1.4.5.8 / protocol 326.
/// Payload: npc byte, generation byte, damage int16, knockback single, hitDirection+1 byte, crit byte.
/// </summary>
public static class TerrariaNpcDamageCodec
{
    public const int PayloadLength = 10;
    public const int VanillaNpcSlots = 200;

    public static TerrariaNpcDamageDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaNpcDamageState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.NpcDamage)
            return TerrariaNpcDamageDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaNpcDamageDecodeResult.InvalidPayloadLength;

        Span<byte> scratch = stackalloc byte[PayloadLength];
        ReadOnlySpan<byte> payload;
        if (frame.Payload.IsSingleSegment)
        {
            payload = frame.Payload.FirstSpan;
        }
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(scratch[offset..]);
                offset += segment.Length;
            }
            payload = scratch;
        }

        float knockBack = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]));
        state = new TerrariaNpcDamageState(
            payload[0],
            payload[1],
            BinaryPrimitives.ReadInt16LittleEndian(payload[2..4]),
            knockBack,
            payload[8],
            payload[9]);
        return state.IsStructurallyValid
            ? TerrariaNpcDamageDecodeResult.Decoded
            : TerrariaNpcDamageDecodeResult.InvalidState;
    }

    public static TerrariaNpcDamageEncodeResult TryEncode(
        in TerrariaNpcDamageState state,
        out byte[] frame)
    {
        frame = [];
        if (!state.IsStructurallyValid ||
            state.NpcSlot >= VanillaNpcSlots ||
            state.Damage < 0)
        {
            return TerrariaNpcDamageEncodeResult.InvalidState;
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = state.NpcSlot;
        payload[1] = state.Generation;
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..4], state.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(
            payload[4..8],
            BitConverter.SingleToInt32Bits(state.KnockBack));
        payload[8] = state.HitDirectionWire;
        payload[9] = state.CriticalRaw;

        var writer = new ArrayBufferWriter<byte>(PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.NpcDamage,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
            return TerrariaNpcDamageEncodeResult.FrameTooLarge;
        if (result != TerrariaFrameWriteResult.Written)
            return TerrariaNpcDamageEncodeResult.Failed;

        frame = writer.WrittenSpan.ToArray();
        return TerrariaNpcDamageEncodeResult.Encoded;
    }

    public static TerrariaNpcDamageEncodeResult TryEncodeAck(out byte[] frame)
    {
        frame = [];
        var writer = new ArrayBufferWriter<byte>(TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.NpcDamageAck,
            ReadOnlySpan<byte>.Empty);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
            return TerrariaNpcDamageEncodeResult.FrameTooLarge;
        if (result != TerrariaFrameWriteResult.Written)
            return TerrariaNpcDamageEncodeResult.Failed;
        frame = writer.WrittenSpan.ToArray();
        return TerrariaNpcDamageEncodeResult.Encoded;
    }
}
''')

write_file('src/TerraRuntime/RuntimeNpcDamageCommands.cs', r'''using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime;

/// <summary>Connection-owned packet-28 command. Exact generation resolution and every mutation occur on the game loop.</summary>
internal sealed record ClientNpcDamageRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcDamageState State) : RuntimeCommand;
''')

write_file('src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeNpcNetworkDamageResult : byte
{
    Rejected = 0,
    Relayed = 1,
    Committed = 2,
    Killed = 3
}

/// <summary>
/// Authoritative packet-28 bridge. It resolves wrapped wire generations against the live slot, preserves the
/// TerrariaServer ordering PlayerInteraction -> StrikeNPC -> imported loot -> King Slime death effects -> despawn ->
/// packet 28 -> packet 23, and never lets a socket thread touch runtime entity state.
/// </summary>
internal sealed class RuntimeNpcNetworkCombatPipeline
{
    private const int MaxOrdinaryDrops = 16;
    private const float VanillaPlayerWidth = 20f;
    private const float VanillaPlayerHeight = 42f;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeNpcPlayerInteractionLedger interactions;
    private readonly RuntimeNpcDamageExecutor damage;
    private readonly RuntimeNpcReplicationRegistry? npcReplication;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly RuntimeKingSlimeDifficultyLootDeliverySink? difficultyLoot;
    private readonly VanillaNpcLootWorldItemMaterializer materializer = VanillaNpcLootWorldItemMaterializer.Instance;
    private readonly SystemNpcCombatRandom random = new();
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly WorldTileStore? worldTiles;
    private readonly RuntimeWorldClock? worldClock;
    private readonly PlayerSlotId[] interactionSlots =
        new PlayerSlotId[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly VanillaKingSlimeLootPlayer[] activeLootPlayers =
        new VanillaKingSlimeLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];

    public RuntimeNpcNetworkCombatPipeline(
        RuntimeNpcStore npcs,
        RuntimeWorldItemStore worldItems,
        IRuntimePlayerSlotSnapshotLookup players,
        RuntimeNpcReplicationRegistry? npcReplication,
        RuntimeWorldItemInstancedLeaseStore instancedLeases,
        RuntimeWorldItemReplicationRegistry? worldItemReplication,
        WorldTileStore? worldTiles,
        RuntimeWorldClock? worldClock,
        bool expertMode,
        bool masterMode)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.npcReplication = npcReplication;
        this.worldTiles = worldTiles;
        this.worldClock = worldClock;
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        interactions = new RuntimeNpcPlayerInteractionLedger(npcs);
        damage = new RuntimeNpcDamageExecutor(npcs, expertMode, interactions);
        if (worldItemReplication is not null)
        {
            difficultyLoot = new RuntimeKingSlimeDifficultyLootDeliverySink(
                worldItems,
                instancedLeases ?? throw new ArgumentNullException(nameof(instancedLeases)),
                worldItemReplication);
        }
    }

    public RuntimeNpcNetworkDamageResult TryApply(
        ConnectionHandle connection,
        in TerrariaNpcDamageState wireState)
    {
        if (!connection.IsAssigned || !wireState.IsStructurallyValid)
            return RuntimeNpcNetworkDamageResult.Rejected;

        // TerrariaServer emits packet 162 before resolving packet-28 NPC generation.
        npcReplication?.TryAcknowledgeDamage(connection.Source);

        if (wireState.NpcSlot >= TerrariaNpcDamageCodec.VanillaNpcSlots ||
            !npcs.TryGetActive(wireState.NpcSlot, out NpcSnapshot current) ||
            RuntimeNpcPacketProjection.ToProtocolGeneration(current.Handle.Generation) != wireState.Generation)
        {
            return RuntimeNpcNetworkDamageResult.Rejected;
        }

        short normalizedDamage = Math.Max(wireState.Damage, (short)0);
        float normalizedKnockBack = Math.Max(wireState.KnockBack, 0f);
        int normalizedHitDirection = Math.Clamp(wireState.HitDirection, -1, 1);
        var normalizedWire = new TerrariaNpcDamageState(
            wireState.NpcSlot,
            wireState.Generation,
            normalizedDamage,
            normalizedKnockBack,
            checked((byte)(normalizedHitDirection + 1)),
            wireState.Critical ? (byte)1 : (byte)0);

        // PlayerInteraction occurs before StrikeNPC in MessageBuffer case 28. Keep credit even when the strike itself
        // is rejected by invulnerability or another authoritative combat guard.
        interactions.TryMark(current.Handle, connection.Player);

        var request = new NpcDamageRequest(
            current.Handle,
            DamageSource.FromPlayerItem(connection.Player),
            normalizedDamage,
            Critical: normalizedWire.Critical,
            KnockBack: normalizedWire.KnockBack,
            HitDirection: normalizedHitDirection);

        bool suppressing = npcReplication?.TryBeginClientDamage(current.Handle) == true;
        try
        {
            if (!damage.TryApply(in request, out NpcDamageResult result))
            {
                if (suppressing)
                    npcReplication!.CompleteClientDamage(current.Handle);
                npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                return RuntimeNpcNetworkDamageResult.Relayed;
            }

            if (!result.Lethal)
            {
                if (suppressing)
                    npcReplication!.CompleteClientDamage(current.Handle);
                npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                return RuntimeNpcNetworkDamageResult.Committed;
            }

            if (!npcs.TryGet(current.Handle, out NpcSnapshot dead))
                throw new InvalidOperationException("A lethal packet-28 commit disappeared before death finalization.");

            if (!TryExecuteImportedLoot(in dead))
                throw new InvalidOperationException("Imported NPC loot could not be finalized after a lethal packet-28 commit.");

            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
                ApplyKingSlimeDeathEffects(in dead);

            if (!npcs.TryDespawn(dead.Handle))
                throw new InvalidOperationException("A lethal packet-28 NPC could not be despawned after death effects.");
            interactions.Forget(dead.Handle);

            if (suppressing)
                npcReplication!.CompleteClientDamage(dead.Handle);
            npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
            npcReplication?.TryPublishDeath(in dead);
            return RuntimeNpcNetworkDamageResult.Killed;
        }
        catch
        {
            if (suppressing)
                npcReplication!.AbortClientDamage(current.Handle);
            throw;
        }
    }

    private bool TryExecuteImportedLoot(in NpcSnapshot npc)
    {
        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)
            return TryExecuteKingSlimeDifficultyLoot(in npc);

        bool kingSlimeNormal = npc.TypeIdentity == VanillaNpcIds.KingSlime;
        VanillaNpcLootTable genericTable = default;
        if (!kingSlimeNormal && !VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npc.TypeIdentity, out genericTable))
            return true;

        int maximumDropCount = kingSlimeNormal
            ? VanillaKingSlimeNormalLootCatalog.MaximumDropCount
            : genericTable.MaximumDropCount;
        if (maximumDropCount > MaxOrdinaryDrops ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        Span<WorldItemDropReservation> capacity = stackalloc WorldItemDropReservation[MaxOrdinaryDrops];
        Span<WorldItemDropReservation> staged = stackalloc WorldItemDropReservation[MaxOrdinaryDrops];
        int reserved = 0;
        for (; reserved < maximumDropCount; reserved++)
        {
            if (worldItems.TryReserveDropSlot(out capacity[reserved]))
                continue;
            ReleaseReservations(capacity[..reserved]);
            return false;
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        int stagedCount = 0;
        var context = new VanillaNpcLootContext(expertMode, DropExtraGel: false);

        if (kingSlimeNormal)
        {
            ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
                        in rules[index], random, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacity);
                    ReleaseReservations(staged[..stagedCount]);
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, capacity, staged, ref stagedCount))
                    return false;
            }
        }
        else
        {
            ReadOnlySpan<VanillaNpcLootRule> rules = genericTable.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaNpcLootEvaluator.TryEvaluateRule(
                        in rules[index], in context, random, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacity);
                    ReleaseReservations(staged[..stagedCount]);
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, capacity, staged, ref stagedCount))
                    return false;
            }
        }

        ReleaseReservations(capacity[stagedCount..maximumDropCount]);
        for (int index = 0; index < stagedCount; index++)
        {
            if (!worldItems.TryCommitReservedDrop(in staged[index], out _))
                throw new InvalidOperationException("A staged NPC-loot reservation failed after source-ordered evaluation.");
        }
        return true;
    }

    private bool StageDrop(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        Span<WorldItemDropReservation> capacity,
        Span<WorldItemDropReservation> staged,
        ref int stagedCount)
    {
        if (!materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized))
        {
            ReleaseReservations(capacity);
            ReleaseReservations(staged[..stagedCount]);
            return false;
        }

        int capacityIndex = stagedCount;
        if (!worldItems.TryReleaseDropReservation(in capacity[capacityIndex]))
            throw new InvalidOperationException("Failed to release an exact NPC-loot capacity reservation.");
        capacity[capacityIndex] = default;
        if (!worldItems.TryReserveDrop(in materialized, out staged[stagedCount]))
            throw new InvalidOperationException("Preflighted NPC loot lost reserved world-item capacity.");
        stagedCount++;
        return true;
    }

    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)
    {
        if (difficultyLoot is null ||
            !interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeLootPlayers[activeCount++] = new VanillaKingSlimeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaKingSlimeDifficultyLootContext(expertMode, masterMode);
        return VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context,
            in origin,
            activeLootPlayers.AsSpan(0, activeCount),
            random,
            difficultyLoot,
            out _);
    }

    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)
    {
        RuntimeWorldProgressionMutations? progression = worldTiles is null
            ? null
            : RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);
        progression?.SetSlimeBlueSpawnBaseline(worldClock?.SlimeBlueSpawnUnlocked == true);

        worldClock?.TryStopSlimeRain(random);
        if (worldClock is not null && progression?.MarkSlimeBlueSpawnUnlocked() == true)
        {
            worldClock.MarkSlimeBlueSpawnUnlocked();
            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                RuntimeNpcSpawnIntentApplier.TryApply(npcs, in intent, out NpcSnapshot nerdy))
            {
                float velocityX = random.NextFloatDirection() * 3f;
                var update = new NpcStateUpdate(
                    nerdy.Type,
                    nerdy.NetId,
                    nerdy.PositionX,
                    nerdy.PositionY,
                    velocityX,
                    -10f,
                    nerdy.Target,
                    nerdy.Ai,
                    nerdy.Simulation);
                if (!npcs.TryUpdate(nerdy.Handle, in update, out _))
                    throw new InvalidOperationException("Nerdy Slime death spawn could not receive launch velocity.");
            }
        }
        progression?.MarkCompleted(VanillaWorldProgressionId.KingSlime);
    }

    private static bool TryCreateNerdySlimeSpawnIntent(in NpcSnapshot source, out NpcAiSpawnIntent intent)
    {
        intent = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        intent = new NpcAiSpawnIntent(
            VanillaNpcIds.TownSlimeBlue,
            BottomX: (int)centerX - 10,
            BottomY: (int)centerY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: checked((ushort)VanillaNpcDefinitionCatalog.DefaultTarget));
        return true;
    }

    private void ReleaseReservations(Span<WorldItemDropReservation> reservations)
    {
        for (int index = 0; index < reservations.Length; index++)
        {
            if (reservations[index].IsAssigned)
                worldItems.TryReleaseDropReservation(in reservations[index]);
        }
    }

    private sealed class SystemNpcCombatRandom : INpcLootRollSource, IKingSlimeDeathRandom
    {
        private readonly Random random = new();

        public int RollLuck(int chanceDenominator)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chanceDenominator, 1);
            return random.Next(chanceDenominator);
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);

        public float NextFloatDirection() => random.NextSingle() * 2f - 1f;
    }
}
''')

# Protocol IDs.
replace_once(
    'src/TerraRuntime.Protocol/TerrariaMessageId.cs',
    '''    WorldItemOwner = 22,\n    ChatMessage = 25,\n    ProjectileNew = 27,\n    ProjectileDestroy = 29,''',
    '''    WorldItemOwner = 22,\n    NpcUpdate = 23,\n    ChatMessage = 25,\n    ProjectileNew = 27,\n    NpcDamage = 28,\n    ProjectileDestroy = 29,''')
replace_once(
    'src/TerraRuntime.Protocol/TerrariaMessageId.cs',
    '''    FinishedConnectingToServer = 129,\n    SyncChestSize = 155\n}''',
    '''    FinishedConnectingToServer = 129,\n    InstancedItemSlotRelease = 151,\n    SyncChestSize = 155,\n    NpcDamageAck = 162\n}''')

# Packet-28 zero damage is normalized by the vanilla server and still resolves to at least one damage after defense.
replace_once(
    'src/TerraRuntime.Contracts/Gameplay/CombatDamageContracts.cs',
    '''        Source.IsValid &&\n        BaseDamage > 0 &&\n        ArmorPenetration >= 0 &&''',
    '''        Source.IsValid &&\n        BaseDamage >= 0 &&\n        ArmorPenetration >= 0 &&''')

# Add packet-28 to the already production-composed projectile/tile/object gameplay ingress.
replace_once(
    'src/TerraRuntime/RuntimeProjectileNetworkIngress.cs',
    '''internal interface IProjectileNetworkIngress\n{\n    bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state);\n\n    bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state);\n}\n''',
    '''internal interface IProjectileNetworkIngress\n{\n    bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state);\n\n    bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state);\n}\n\ninternal interface INpcDamageNetworkIngress\n{\n    bool TryPostNpcDamage(ConnectionHandle connection, in TerrariaNpcDamageState state);\n}\n''')
replace_once(
    'src/TerraRuntime/RuntimeProjectileNetworkIngress.cs',
    '''    RuntimeTileNetworkIngress,\n    IProjectileNetworkIngress,\n    IObjectPlacementNetworkIngress''',
    '''    RuntimeTileNetworkIngress,\n    IProjectileNetworkIngress,\n    INpcDamageNetworkIngress,\n    IObjectPlacementNetworkIngress''')
replace_once(
    'src/TerraRuntime/RuntimeProjectileNetworkIngress.cs',
    '''    public bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state)\n    {\n        if (!connection.IsAssigned || !state.IsValid)\n            return false;\n\n        return Ingress.TryPost(\n            connection.Source,\n            new ClientProjectileDestroyRuntimeCommand(connection, state));\n    }\n\n    public bool TryPost(ConnectionHandle connection, in TerrariaPlaceObjectState state)''',
    '''    public bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state)\n    {\n        if (!connection.IsAssigned || !state.IsValid)\n            return false;\n\n        return Ingress.TryPost(\n            connection.Source,\n            new ClientProjectileDestroyRuntimeCommand(connection, state));\n    }\n\n    public bool TryPostNpcDamage(ConnectionHandle connection, in TerrariaNpcDamageState state)\n    {\n        if (!connection.IsAssigned || !state.IsStructurallyValid)\n            return false;\n\n        return Ingress.TryPost(\n            connection.Source,\n            new ClientNpcDamageRuntimeCommand(connection, state));\n    }\n\n    public bool TryPost(ConnectionHandle connection, in TerrariaPlaceObjectState state)''')

# Connection-owned frame decode and bounded admission.
replace_once(
    'src/TerraRuntime/ProjectileLifecycleFrameSink.cs',
    '''    MalformedDestroy = 3,\n    GameIngressBackpressure = 4\n}''',
    '''    MalformedDestroy = 3,\n    GameIngressBackpressure = 4,\n    MalformedNpcDamage = 5\n}''')
replace_once(
    'src/TerraRuntime/ProjectileLifecycleFrameSink.cs',
    '''    private readonly IProjectileNetworkIngress ingress;\n    private readonly TileManipulationFrameSink? tileManipulation;''',
    '''    private readonly IProjectileNetworkIngress ingress;\n    private readonly INpcDamageNetworkIngress? npcDamageIngress;\n    private readonly TileManipulationFrameSink? tileManipulation;''')
replace_once(
    'src/TerraRuntime/ProjectileLifecycleFrameSink.cs',
    '''        this.inner = inner;\n        this.ingress = ingress;\n        tileManipulation = ingress is ITileNetworkIngress tileIngress''',
    '''        this.inner = inner;\n        this.ingress = ingress;\n        npcDamageIngress = ingress as INpcDamageNetworkIngress;\n        tileManipulation = ingress is ITileNetworkIngress tileIngress''')
replace_once(
    'src/TerraRuntime/ProjectileLifecycleFrameSink.cs',
    '''                ProjectileLifecycleFrameStopReason.MalformedUpdate or ProjectileLifecycleFrameStopReason.MalformedDestroy => TerrariaFrameRejectionCategory.MalformedProtocol,''',
    '''                ProjectileLifecycleFrameStopReason.MalformedUpdate or\n                ProjectileLifecycleFrameStopReason.MalformedDestroy or\n                ProjectileLifecycleFrameStopReason.MalformedNpcDamage => TerrariaFrameRejectionCategory.MalformedProtocol,''')
replace_once(
    'src/TerraRuntime/ProjectileLifecycleFrameSink.cs',
    '''            TerrariaMessageId.ProjectileNew => HandleUpdate(in frame),\n            TerrariaMessageId.ProjectileDestroy => HandleDestroy(in frame),''',
    '''            TerrariaMessageId.ProjectileNew => HandleUpdate(in frame),\n            TerrariaMessageId.NpcDamage when npcDamageIngress is not null => HandleNpcDamage(in frame),\n            TerrariaMessageId.ProjectileDestroy => HandleDestroy(in frame),''')
replace_once(
    'src/TerraRuntime/ProjectileLifecycleFrameSink.cs',
    '''    private TerrariaFrameSinkResult HandleDestroy(in TerrariaFrame frame)\n    {''',
    '''    private TerrariaFrameSinkResult HandleNpcDamage(in TerrariaFrame frame)\n    {\n        if (!TryGetPlayingConnection(out ConnectionHandle connection))\n            return Stop(ProjectileLifecycleFrameStopReason.InvalidJoinState);\n\n        TerrariaNpcDamageDecodeResult decode = TerrariaNpcDamageCodec.TryDecode(\n            in frame,\n            out TerrariaNpcDamageState state);\n        if (decode != TerrariaNpcDamageDecodeResult.Decoded)\n            return Stop(ProjectileLifecycleFrameStopReason.MalformedNpcDamage);\n\n        return npcDamageIngress!.TryPostNpcDamage(connection, in state)\n            ? TerrariaFrameSinkResult.Continue\n            : Stop(ProjectileLifecycleFrameStopReason.GameIngressBackpressure);\n    }\n\n    private TerrariaFrameSinkResult HandleDestroy(in TerrariaFrame frame)\n    {''')

# Suppress the store's synchronous packet-23 update/despawn only for the exact NPC being processed by packet 28.
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '''    private long rejectedFrames;\n    private long unsupportedCommits;''',
    '''    private long rejectedFrames;\n    private long unsupportedCommits;\n    private NpcHandle suppressedClientDamageNpc;''')
replace_once(
    'src/TerraRuntime/RuntimeNpcReplicationRegistry.cs',
    '''    public bool TryUnregister(GameCommandSourceId source) => endpoints.TryRemove(source, out _);\n\n    public void ConfigureTownHomeBaselines''',
    '''    public bool TryUnregister(GameCommandSourceId source) => endpoints.TryRemove(source, out _);\n\n    public bool TryBeginClientDamage(NpcHandle npc)\n    {\n        if (!npc.IsAssigned || suppressedClientDamageNpc.IsAssigned)\n            return false;\n        suppressedClientDamageNpc = npc;\n        return true;\n    }\n\n    public void CompleteClientDamage(NpcHandle npc)\n    {\n        if (suppressedClientDamageNpc != npc)\n            throw new InvalidOperationException("Packet-28 replication scope does not match the completing NPC generation.");\n        suppressedClientDamageNpc = default;\n    }\n\n    public void AbortClientDamage(NpcHandle npc)\n    {\n        if (suppressedClientDamageNpc == npc)\n            suppressedClientDamageNpc = default;\n    }\n\n    public bool TryAcknowledgeDamage(GameCommandSourceId source)\n    {\n        if (!endpoints.TryGetValue(source, out Endpoint? endpoint) || !endpoint.IsPlaying ||\n            TerrariaNpcDamageCodec.TryEncodeAck(out byte[] encoded) != TerrariaNpcDamageEncodeResult.Encoded)\n        {\n            return false;\n        }\n\n        if (endpoint.Outbound.TryEnqueue(new OutboundFrame(encoded)) == OutboundEnqueueResult.Enqueued)\n        {\n            Interlocked.Increment(ref relayedFrames);\n            return true;\n        }\n\n        Interlocked.Increment(ref rejectedFrames);\n        return false;\n    }\n\n    public bool TryPublishDamage(GameCommandSourceId excludedSource, in TerrariaNpcDamageState state)\n    {\n        if (TerrariaNpcDamageCodec.TryEncode(in state, out byte[] encoded) != TerrariaNpcDamageEncodeResult.Encoded)\n        {\n            Interlocked.Increment(ref unsupportedCommits);\n            return false;\n        }\n\n        BroadcastExcept(excludedSource, encoded);\n        return true;\n    }\n\n    public bool TryPublishDeath(in NpcSnapshot snapshot)\n    {\n        if (!RuntimeNpcPacketProjection.TryCreate(in snapshot, RuntimeNpcSyncKind.Despawn, out TerrariaNpcUpdateState state) ||\n            !TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded))\n        {\n            Interlocked.Increment(ref unsupportedCommits);\n            return false;\n        }\n\n        Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);\n        Broadcast(encoded);\n        return true;\n    }\n\n    public void ConfigureTownHomeBaselines''')
old_method = '''    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)\n    {\n        RuntimeNpcSyncKind syncKind = kind switch\n        {\n            NpcStateCommitKind.Spawn => RuntimeNpcSyncKind.Spawn,\n            NpcStateCommitKind.Update => RuntimeNpcSyncKind.Update,\n            NpcStateCommitKind.Despawn => RuntimeNpcSyncKind.Despawn,\n            _ => throw new ArgumentOutOfRangeException(nameof(kind))\n        };\n\n        if (!RuntimeNpcPacketProjection.TryCreate(in snapshot, syncKind, out var state) ||\n            !TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded))\n        {\n            Interlocked.Increment(ref unsupportedCommits);\n            return;\n        }\n\n        if (kind == NpcStateCommitKind.Despawn)\n        {\n            Broadcast(encoded);\n            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);\n            return;\n        }\n\n        // Baselines deliberately use spawn semantics even after an ordinary update. The explicit\n        // SpawnNeedsSyncing flag protects a joining client from byte-generation wrap aliasing.\n        if (RuntimeNpcPacketProjection.TryCreate(\n                in snapshot,\n                RuntimeNpcSyncKind.Spawn,\n                out var baselineState) &&\n            TerrariaNpcUpdateEncoder.TryEncode(in baselineState, out byte[] baseline))\n        {\n            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], baseline);\n        }\n\n        Broadcast(encoded);\n    }\n'''
new_method = '''    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)\n    {\n        RuntimeNpcSyncKind syncKind = kind switch\n        {\n            NpcStateCommitKind.Spawn => RuntimeNpcSyncKind.Spawn,\n            NpcStateCommitKind.Update => RuntimeNpcSyncKind.Update,\n            NpcStateCommitKind.Despawn => RuntimeNpcSyncKind.Despawn,\n            _ => throw new ArgumentOutOfRangeException(nameof(kind))\n        };\n\n        if (!RuntimeNpcPacketProjection.TryCreate(in snapshot, syncKind, out var state) ||\n            !TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded))\n        {\n            Interlocked.Increment(ref unsupportedCommits);\n            return;\n        }\n\n        bool suppressBroadcast = suppressedClientDamageNpc.IsAssigned && snapshot.Handle == suppressedClientDamageNpc;\n        if (kind == NpcStateCommitKind.Despawn)\n        {\n            if (!suppressBroadcast)\n                Broadcast(encoded);\n            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], null);\n            return;\n        }\n\n        // Baselines deliberately use spawn semantics even after an ordinary update. The explicit\n        // SpawnNeedsSyncing flag protects a joining client from byte-generation wrap aliasing.\n        if (RuntimeNpcPacketProjection.TryCreate(\n                in snapshot,\n                RuntimeNpcSyncKind.Spawn,\n                out var baselineState) &&\n            TerrariaNpcUpdateEncoder.TryEncode(in baselineState, out byte[] baseline))\n        {\n            Volatile.Write(ref baselineFrames[snapshot.Handle.Slot], baseline);\n        }\n\n        if (!suppressBroadcast)\n            Broadcast(encoded);\n    }\n'''
replace_once('src/TerraRuntime/RuntimeNpcReplicationRegistry.cs', old_method, new_method)

# Authoritative runtime ownership and item-lease cadence.
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private readonly RuntimeProjectileReplicationRegistry? _projectileReplication;\n    private readonly RuntimeNpcReplicationRegistry? _npcReplication;\n    private readonly RuntimeTownNpcStateStore? _townNpcs;''',
    '''    private readonly RuntimeProjectileReplicationRegistry? _projectileReplication;\n    private readonly RuntimeNpcReplicationRegistry? _npcReplication;\n    private readonly RuntimeWorldItemReplicationRegistry? _worldItemReplication;\n    private readonly RuntimeWorldItemInstancedLeaseStore _instancedItemLeases;\n    private readonly RuntimeNpcNetworkCombatPipeline _npcCombat;\n    private readonly short[] _expiredInstancedItemSlots = new short[RuntimeWorldItemStore.VanillaCapacity];\n    private readonly RuntimeTownNpcStateStore? _townNpcs;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        RuntimeProjectileReplicationRegistry? projectileReplication = null,\n        RuntimeNpcReplicationRegistry? npcReplication = null,\n        RuntimeTownNpcStateStore? townNpcs = null,''',
    '''        RuntimeProjectileReplicationRegistry? projectileReplication = null,\n        RuntimeNpcReplicationRegistry? npcReplication = null,\n        RuntimeWorldItemReplicationRegistry? worldItemReplication = null,\n        RuntimeTownNpcStateStore? townNpcs = null,''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities = null,\n        bool expertMode = false)\n    {''',
    '''        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities = null,\n        bool expertMode = false,\n        bool masterMode = false)\n    {''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _worldClock = worldClock;\n        _expertMode = expertMode;''',
    '''        _worldClock = worldClock;\n        _expertMode = expertMode;\n        if (masterMode && !expertMode)\n            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _worldItems = worldItems ?? new RuntimeWorldItemStore();\n\n        if (npcAiStepper is null)''',
    '''        _worldItems = worldItems ?? new RuntimeWorldItemStore();\n        _worldItemReplication = worldItemReplication;\n        _instancedItemLeases = new RuntimeWorldItemInstancedLeaseStore(_worldItems);\n        _npcCombat = new RuntimeNpcNetworkCombatPipeline(\n            _npcs,\n            _worldItems,\n            this,\n            _npcReplication,\n            _instancedItemLeases,\n            _worldItemReplication,\n            _worldTiles,\n            _worldClock,\n            expertMode,\n            masterMode);\n\n        if (npcAiStepper is null)''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    public long RejectedClientProjectileDestroys { get; private set; }\n\n    public long RelayedUnknownProjectileDestroys { get; private set; }''',
    '''    public long RejectedClientProjectileDestroys { get; private set; }\n\n    public long AppliedClientNpcDamage { get; private set; }\n\n    public long RejectedClientNpcDamage { get; private set; }\n\n    public long RelayedUnknownProjectileDestroys { get; private set; }''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''            case ClientProjectileDestroyRuntimeCommand destroy:\n                ApplyClientProjectileDestroy(destroy);\n                break;\n            case ClientTileManipulationRuntimeCommand tile:''',
    '''            case ClientProjectileDestroyRuntimeCommand destroy:\n                ApplyClientProjectileDestroy(destroy);\n                break;\n            case ClientNpcDamageRuntimeCommand npcDamage:\n                ApplyClientNpcDamage(npcDamage);\n                break;\n            case ClientTileManipulationRuntimeCommand tile:''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        if (_projectileStepper is not null)\n            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);\n\n        _worldClock?.Tick();''',
    '''        if (_projectileStepper is not null)\n            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);\n        TickInstancedItemLeases();\n\n        _worldClock?.Tick();''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private void ApplyClientProjectileUpdate(ClientProjectileUpdateRuntimeCommand command)\n    {''',
    '''    private void ApplyClientNpcDamage(ClientNpcDamageRuntimeCommand command)\n    {\n        if (!IsCurrentPlayerConnection(command.Connection))\n        {\n            RejectedClientNpcDamage++;\n            return;\n        }\n\n        RuntimeNpcNetworkDamageResult result = _npcCombat.TryApply(command.Connection, in command.State);\n        if (result == RuntimeNpcNetworkDamageResult.Rejected)\n            RejectedClientNpcDamage++;\n        else\n            AppliedClientNpcDamage++;\n    }\n\n    private void TickInstancedItemLeases()\n    {\n        int expired = _instancedItemLeases.Tick(_expiredInstancedItemSlots);\n        if (_worldItemReplication is null)\n            return;\n        for (int index = 0; index < expired; index++)\n            _worldItemReplication.TryBroadcastInstancedSlotRelease(_expiredInstancedItemSlots[index]);\n    }\n\n    private void ApplyClientProjectileUpdate(ClientProjectileUpdateRuntimeCommand command)\n    {''')

# Production passes difficulty transport explicitly; all other constructor call sites keep optional defaults.
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''            projectileReplication: projectileReplication,\n            npcReplication: npcReplication,\n            townNpcs: townNpcStore,''',
    '''            projectileReplication: projectileReplication,\n            npcReplication: npcReplication,\n            worldItemReplication: worldItemReplication,\n            townNpcs: townNpcStore,''')
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''            expertMode: world.RuntimeMetadata.GameMode is\n                (byte)WorldGenerationGameMode.Expert or\n                (byte)WorldGenerationGameMode.Master);''',
    '''            expertMode: world.RuntimeMetadata.GameMode is\n                (byte)WorldGenerationGameMode.Expert or\n                (byte)WorldGenerationGameMode.Master,\n            masterMode: world.RuntimeMetadata.GameMode == (byte)WorldGenerationGameMode.Master);''')

# Regression coverage for the wire shape and vanilla zero-damage clamp/minimum-hit behavior.
write_file('tests/TerraRuntime.Tests/TerrariaNpcDamageCodecTests.cs', r'''using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcDamageCodecTests
{
    [Fact]
    public void Packet_28_round_trips_source_wire_shape()
    {
        var state = new TerrariaNpcDamageState(
            NpcSlot: 17,
            Generation: 9,
            Damage: 123,
            KnockBack: 4.5f,
            HitDirectionWire: 0,
            CriticalRaw: 1);

        Assert.Equal(TerrariaNpcDamageEncodeResult.Encoded, TerrariaNpcDamageCodec.TryEncode(in state, out byte[] encoded));
        var sequence = new ReadOnlySequence<byte>(encoded);
        var decoder = new TerrariaFrameDecoder();
        Assert.Equal(TerrariaFrameReadResult.Frame, decoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcDamageDecodeResult.Decoded, TerrariaNpcDamageCodec.TryDecode(in frame, out TerrariaNpcDamageState decoded));
        Assert.Equal(state, decoded);
        Assert.Equal(-1, decoded.HitDirection);
        Assert.True(decoded.Critical);
    }

    [Fact]
    public void Ack_is_empty_packet_162_frame()
    {
        Assert.Equal(TerrariaNpcDamageEncodeResult.Encoded, TerrariaNpcDamageCodec.TryEncodeAck(out byte[] encoded));
        var sequence = new ReadOnlySequence<byte>(encoded);
        var decoder = new TerrariaFrameDecoder();
        Assert.Equal(TerrariaFrameReadResult.Frame, decoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal((byte)TerrariaMessageId.NpcDamageAck, frame.MessageId);
        Assert.Equal(0, frame.Payload.Length);
    }
}
''')

replace_once(
    'tests/TerraRuntime.Tests/RuntimeNpcDamageExecutorTests.cs',
    '''    [Fact]\n    public void Defense_never_reduces_a_valid_hit_below_one_damage()\n    {''',
    '''    [Fact]\n    public void Zero_source_damage_still_resolves_to_vanilla_minimum_one()\n    {\n        var store = new RuntimeNpcStore(capacity: 4);\n        NpcSnapshot target = SpawnZombie(store);\n        var executor = new RuntimeNpcDamageExecutor(store);\n        var request = new NpcDamageRequest(target.Handle, DamageSource.Server, BaseDamage: 0);\n\n        Assert.True(executor.TryApply(in request, out NpcDamageResult result));\n        Assert.Equal(1, result.ResolvedDamage);\n        Assert.Equal(44, result.LifeAfter);\n    }\n\n    [Fact]\n    public void Defense_never_reduces_a_valid_hit_below_one_damage()\n    {''')

# Document the live boundary without claiming unimplemented generic death events.
for doc in ('docs/en/combat-damage.md', 'docs/ru/combat-damage.md'):
    p = ROOT / doc
    text = p.read_text(encoding='utf-8')
    marker = '\n## Live packet 28 integration\n' if '/en/' in doc else '\n## Live integration packet 28\n'
    if marker not in text:
        if '/en/' in doc:
            text += r'''

## Live packet 28 integration

Production now decodes TerrariaServer 1.4.5.8 packet 28 in the existing bounded gameplay ingress. The authoritative owner sends packet 162 acknowledgement before generation resolution, compares the wrapped 1..255 wire generation against the current runtime handle, records player interaction before the strike, clamps negative wire damage to zero, and applies the existing defense/critical/knockback resolver.

For lethal imported deaths, implemented NPC-specific loot is materialized before death effects. King Slime normal/Expert/Master paths use the existing source-backed loot evaluators and instanced-item transport; Slime Rain termination, blue-town-slime unlock/Nerdy spawn and downed-King-Slime progression follow loot, then the NPC generation is despawned. Packet 28 is relayed after those server-side effects and packet 23 follows for death, while the synchronous store packet-23 commit is suppressed only for that exact NPC generation.

This does not claim full Terraria `NPCLoot`: money, hearts, banners, bestiary, generic global drop rules, segmented `realLife` death sync and every boss-specific death event remain separate compatibility work.
'''
        else:
            text += r'''

## Live integration packet 28

Production теперь декодирует packet 28 TerrariaServer 1.4.5.8 в существующем bounded gameplay ingress. Authoritative owner отправляет acknowledgement packet 162 до generation resolution, сравнивает wrapped wire generation 1..255 с текущим runtime handle, отмечает player interaction до strike, clamp'ит отрицательный wire damage в zero и применяет существующий resolver defense/critical/knockback.

Для lethal deaths с уже импортированным loot NPC-specific drops materialize до death effects. King Slime normal/Expert/Master использует существующие source-backed loot evaluators и instanced-item transport; после loot выполняются остановка Slime Rain, unlock blue town slime/Nerdy spawn и progression downed King Slime, затем generation NPC despawn'ится. Packet 28 relay идёт после этих server-side effects, а packet 23 следует за ним как death sync; synchronous packet-23 commit store подавляется только для exact generation этого NPC.

Это не заявка на полный Terraria `NPCLoot`: money, hearts, banners, bestiary, generic global drop rules, segmented `realLife` death sync и все boss-specific death events остаются отдельной compatibility работой.
'''
        p.write_text(text, encoding='utf-8')

print('live NPC combat patch applied')
