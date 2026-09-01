from pathlib import Path


def write(path: str, content: str) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content)


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text()
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one anchor, found {count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1))


def replace_between(path: str, start: str, end: str, replacement: str) -> None:
    p = Path(path)
    text = p.read_text()
    i = text.find(start)
    j = text.find(end, i)
    if i < 0 or j < 0:
        raise SystemExit(f"{path}: block anchors not found")
    p.write_text(text[:i] + replacement + text[j:])


# Source-pinned content ids used by bound-town rescue.
replace_once(
    'src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs',
    '''    public static readonly NpcTypeId Clothier = new(54);\n    public static readonly NpcTypeId GoblinTinkerer = new(107);''',
    '''    public static readonly NpcTypeId Clothier = new(54);\n    public static readonly NpcTypeId BoundGoblin = new(105);\n    public static readonly NpcTypeId BoundWizard = new(106);\n    public static readonly NpcTypeId GoblinTinkerer = new(107);''')
replace_once(
    'src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs',
    '''    public static readonly NpcTypeId Wizard = new(108);\n    public static readonly NpcTypeId Mechanic = new(124);''',
    '''    public static readonly NpcTypeId Wizard = new(108);\n    public static readonly NpcTypeId BoundMechanic = new(123);\n    public static readonly NpcTypeId Mechanic = new(124);''')
replace_once(
    'src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs',
    '''    public static readonly NpcTypeId Stylist = new(353);\n    public static readonly NpcTypeId Angler = new(369);''',
    '''    public static readonly NpcTypeId Stylist = new(353);\n    public static readonly NpcTypeId WebbedStylist = new(354);\n    public static readonly NpcTypeId Angler = new(369);\n    public static readonly NpcTypeId SleepingAngler = new(376);''')
replace_once(
    'src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs',
    '''    public static readonly NpcTypeId TaxCollector = new(441);\n    public static readonly NpcTypeId Tavernkeep = new(550);''',
    '''    public static readonly NpcTypeId TaxCollector = new(441);\n    public static readonly NpcTypeId DemonTaxCollector = new(534);\n    public static readonly NpcTypeId Tavernkeep = new(550);\n    public static readonly NpcTypeId BartenderUnconscious = new(579);''')
replace_once(
    'src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs',
    '''    public static readonly NpcTypeId Golfer = new(588);\n    public static readonly NpcTypeId Zoologist = new(633);''',
    '''    public static readonly NpcTypeId Golfer = new(588);\n    public static readonly NpcTypeId GolferRescue = new(589);\n    public static readonly NpcTypeId Zoologist = new(633);''')

write('src/TerraRuntime.Core/Npcs/VanillaTownNpcRescue1458.cs', r'''using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum VanillaTownNpcRescueTrigger1458 : byte
{
    Talk = 0,
    PurificationPowder = 1
}

public enum VanillaTownNpcRescueFact1458 : byte
{
    Goblin = 0,
    Wizard = 1,
    Mechanic = 2,
    Stylist = 3,
    Angler = 4,
    Bartender = 5,
    Golfer = 6,
    TaxCollector = 7
}

public readonly record struct VanillaTownNpcRescueRule1458(
    NpcTypeId BoundType,
    NpcTypeId ResidentType,
    VanillaTownNpcRescueTrigger1458 Trigger,
    VanillaTownNpcRescueFact1458 Fact,
    int BoundWidth,
    int BoundHeight,
    int BoundLifeMax)
{
    public bool IsValid =>
        BoundType.IsAssigned && ResidentType.IsAssigned && BoundWidth > 0 && BoundHeight > 0 && BoundLifeMax > 0;
}

/// <summary>
/// TerrariaServer 1.4.5.8 bound-town rescue/transform facts. Talk rules come directly from NPC.AI style 0 and
/// AI_000_TransformBoundNPC. Demon Tax Collector is catalogued separately because vanilla transforms it only when
/// Purification Powder projectile 10 intersects NPC 534.
/// </summary>
public static class VanillaTownNpcRescue1458
{
    private static readonly VanillaTownNpcRescueRule1458[] Rules =
    [
        new(VanillaNpcIds.BoundGoblin, VanillaNpcIds.GoblinTinkerer, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Goblin, 18, 34, 250),
        new(VanillaNpcIds.BoundWizard, VanillaNpcIds.Wizard, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Wizard, 18, 40, 250),
        new(VanillaNpcIds.BoundMechanic, VanillaNpcIds.Mechanic, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Mechanic, 16, 30, 250),
        new(VanillaNpcIds.WebbedStylist, VanillaNpcIds.Stylist, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Stylist, 16, 30, 250),
        new(VanillaNpcIds.SleepingAngler, VanillaNpcIds.Angler, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Angler, 30, 7, 250),
        new(VanillaNpcIds.BartenderUnconscious, VanillaNpcIds.Tavernkeep, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Bartender, 34, 8, 250),
        new(VanillaNpcIds.GolferRescue, VanillaNpcIds.Golfer, VanillaTownNpcRescueTrigger1458.Talk, VanillaTownNpcRescueFact1458.Golfer, 18, 34, 250),
        new(VanillaNpcIds.DemonTaxCollector, VanillaNpcIds.TaxCollector, VanillaTownNpcRescueTrigger1458.PurificationPowder, VanillaTownNpcRescueFact1458.TaxCollector, 18, 40, 400)
    ];

    public static ReadOnlySpan<VanillaTownNpcRescueRule1458> All => Rules;

    public static bool TryGet(NpcTypeId boundType, out VanillaTownNpcRescueRule1458 rule)
    {
        foreach (VanillaTownNpcRescueRule1458 candidate in Rules)
        {
            if (candidate.BoundType == boundType)
            {
                rule = candidate;
                return true;
            }
        }
        rule = default;
        return false;
    }

    public static bool TryGetTalkRule(NpcTypeId boundType, out VanillaTownNpcRescueRule1458 rule) =>
        TryGet(boundType, out rule) && rule.Trigger == VanillaTownNpcRescueTrigger1458.Talk;
}
''')

write('src/TerraRuntime.Core/Npcs/VanillaNpcCatchCatalog1458.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 NPCID.Sets.CountsAsCritter plus NPC.SetDefaults catchItem mappings.
/// CountsAsCritter and catchItem are intentionally separate: vanilla has critters with no catch item and catchable
/// entities such as Truffle Worm that are not in CountsAsCritter.
/// </summary>
public static class VanillaNpcCatchCatalog1458
{
    private static readonly HashSet<int> Critters =
    [
        46, 303, 337, 540, 443, 74, 297, 298, 442, 611, 689, 377, 446, 612, 613, 356, 444,
        595, 596, 597, 598, 599, 600, 601, 604, 605, 357, 448, 374, 484, 355, 358, 606, 359, 360,
        485, 486, 487, 148, 149, 55, 230, 592, 593, 299, 538, 539, 300, 447, 361, 445, 362, 363,
        364, 365, 367, 366, 583, 584, 585, 602, 603, 607, 608, 609, 610, 616, 617, 625, 626, 627,
        615, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649, 650, 651, 652, 653, 654, 655,
        661, 669, 671, 672, 673, 674, 675, 677, 687, 688
    ];

    public static bool CountsAsCritter(NpcTypeId type) => type.IsAssigned && Critters.Contains(type.Value);

    public static bool TryGetCatchItem(NpcTypeId type, out ItemTypeId itemType)
    {
        int item = type.Value switch
        {
            46 or 303 or 337 or 540 => 2019,
            55 or 230 => 261,
            74 => 2015,
            297 => 2016,
            298 => 2017,
            148 or 149 => 2205,
            299 => 2018,
            300 => 2003,
            355 => 1992,
            356 => 1994,
            357 => 2002,
            358 => 2004,
            359 => 2006,
            360 => 2007,
            361 => 2121,
            362 or 363 => 2122,
            364 or 365 => 2123,
            366 => 2156,
            367 => 2157,
            374 or 375 => 2673,
            377 => 2740,
            442 => 2889,
            443 => 2890,
            444 => 2891,
            445 => 2892,
            446 => 2893,
            447 => 2894,
            448 => 2895,
            >= 484 and <= 487 => 3191 + type.Value - 484,
            538 => 3563,
            539 => 3564,
            583 => 4068,
            584 => 4069,
            585 => 4070,
            592 or 593 => 4274,
            >= 595 and <= 601 => 4334 + type.Value - 595,
            602 or 603 => 4359,
            604 => 4361,
            605 => 4362,
            606 => 4363,
            607 => 4373,
            608 or 609 => 4374,
            610 => 4375,
            611 => 4395,
            612 => 4418,
            613 => 4419,
            614 => 1338,
            616 => 4464,
            617 => 4465,
            626 => 4480,
            627 => 4482,
            >= 639 and <= 645 => 4831 + type.Value - 639,
            >= 646 and <= 652 => 4838 + type.Value - 646,
            653 => 4845,
            654 => 4847,
            655 => 4849,
            661 => 4961,
            669 => 5132,
            671 => 5212,
            672 => 5300,
            673 => 5311,
            674 => 5312,
            675 => 5313,
            677 => 5350,
            687 => 2121,
            688 => 5511,
            _ => 0
        };

        if (item <= 0 || !VanillaItemIds.TryCreate(item, out itemType) || itemType.IsNone)
        {
            itemType = default;
            return false;
        }
        return true;
    }

    public static bool IsMysticFrog(NpcTypeId type) => type.Value == 687;
}

/// <summary>
/// Item.NewItem state used by NPC.CatchNPC. DefaultToCapturedCritter fixes all captured-critter item hitboxes at
/// 12x12; Item.NewItem is called with a zero-size source rectangle at player center, then ordinary gravity velocity.
/// </summary>
public static class VanillaNpcCatchWorldItem1458
{
    private const float CapturedItemHalfSize = 6f;
    private const float VelocityScale = 0.1f;
    public const int ReservationTicks = 100;

    public static WorldItemDropStateUpdate Create(
        float playerCenterX,
        float playerCenterY,
        ItemTypeId itemType,
        IWorldItemSpawnRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!itemType.IsAssigned)
            throw new ArgumentException("Catch item type must be assigned.", nameof(itemType));

        return new WorldItemDropStateUpdate(
            PositionX: playerCenterX - CapturedItemHalfSize,
            PositionY: playerCenterY - CapturedItemHalfSize,
            VelocityX: random.NextInt32(-30, 31) * VelocityScale,
            VelocityY: random.NextInt32(-40, -15) * VelocityScale,
            Stack: 1,
            Prefix: VanillaPrefixIds.NoneValue,
            Ownership: WorldItemOwnershipMode.ReserveForLocalPlayer,
            ItemNetId: checked((short)itemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: ReservationTicks);
    }
}
''')

# Runtime simulation carries the source CatchNPC statue branch fact without another side table.
replace_once(
    'src/TerraRuntime.Contracts/Runtime/NpcSnapshot.cs',
    '''    public bool ReflectsProjectiles { get; init; }\n\n    public static NpcSimulationState Initial =>''',
    '''    public bool ReflectsProjectiles { get; init; }\n\n    /// <summary>Vanilla NPC.SpawnedFromStatue; CatchNPC consumes this server-only lifecycle fact.</summary>\n    public bool SpawnedFromStatue { get; init; }\n\n    public static NpcSimulationState Initial =>''')
replace_once(
    'src/TerraRuntime.Contracts/Runtime/NpcSnapshot.cs',
    '''        DefenseOverride = null,\n        ReflectsProjectiles = false\n    };''',
    '''        DefenseOverride = null,\n        ReflectsProjectiles = false,\n        SpawnedFromStatue = false\n    };''')

# Packet 70 protocol boundary.
replace_once(
    'src/TerraRuntime.Protocol/TerrariaMessageId.cs',
    '''    ChestName = 69,\n    PlaceObject = 79,''',
    '''    ChestName = 69,\n    CatchNpc = 70,\n    PlaceObject = 79,''')

write('src/TerraRuntime.Protocol.Multiplicity/TerrariaNpcCatchCodec.cs', r'''using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaNpcCatchState(short NpcSlot);

public enum TerrariaNpcCatchDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2
}

/// <summary>Exact TerrariaServer 1.4.5.8 client packet 70 payload: one little-endian Int16 NPC slot.</summary>
public static class TerrariaNpcCatchCodec
{
    public const int PayloadLength = 2;
    public const int MaximumNpcSlots = 200;

    public static TerrariaNpcCatchDecodeResult TryDecode(in TerrariaFrame frame, out TerrariaNpcCatchState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.CatchNpc)
            return TerrariaNpcCatchDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaNpcCatchDecodeResult.InvalidPayloadLength;

        Span<byte> payload = stackalloc byte[PayloadLength];
        if (frame.Payload.IsSingleSegment)
            frame.Payload.FirstSpan.CopyTo(payload);
        else
        {
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(payload[offset..]);
                offset += segment.Length;
            }
        }
        state = new TerrariaNpcCatchState(BinaryPrimitives.ReadInt16LittleEndian(payload));
        return TerrariaNpcCatchDecodeResult.Decoded;
    }

    public static bool IsValidNpcSlot(short npcSlot) => (uint)npcSlot < MaximumNpcSlots;
}
''')

write('src/TerraRuntime/RuntimeNpcCatchCommands.cs', r'''using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal sealed record ClientNpcCatchRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcCatchState State) : RuntimeCommand;
''')

write('src/TerraRuntime/RuntimeNpcCatchNetworkIngress.cs', r'''using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal interface INpcCatchNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaNpcCatchState state);
}

internal sealed class RuntimeNpcCatchNetworkIngress : INpcCatchNetworkIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeNpcCatchNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, in TerrariaNpcCatchState state) =>
        connection.IsAssigned && ingress.TryPost(connection.Source, new ClientNpcCatchRuntimeCommand(connection, state));
}
''')

write('src/TerraRuntime/NpcCatchFrameSink.cs', r'''using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum NpcCatchFrameStopReason : byte
{
    None = 0,
    InvalidJoinState = 1,
    MalformedPacket = 2,
    InvalidNpcSlot = 3,
    GameIngressBackpressure = 4
}

/// <summary>Connection-owned packet-70 ingress; authoritative catch state is applied only by the game-loop owner.</summary>
public sealed class NpcCatchFrameSink : ITerrariaFrameSink, ITerrariaFrameRejectionSource
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly INpcCatchNetworkIngress ingress;

    internal NpcCatchFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        INpcCatchNetworkIngress ingress)
    {
        if (source.IsSystem)
            throw new ArgumentException("NPC catch ingress requires a connection command source.", nameof(source));
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public NpcCatchFrameStopReason StopReason { get; private set; }

    public TerrariaFrameRejectionCategory RejectionCategory => StopReason switch
    {
        NpcCatchFrameStopReason.InvalidJoinState => TerrariaFrameRejectionCategory.InvalidState,
        NpcCatchFrameStopReason.MalformedPacket => TerrariaFrameRejectionCategory.MalformedProtocol,
        NpcCatchFrameStopReason.InvalidNpcSlot => TerrariaFrameRejectionCategory.InvalidState,
        NpcCatchFrameStopReason.GameIngressBackpressure => TerrariaFrameRejectionCategory.Backpressure,
        _ => inner is ITerrariaFrameRejectionSource rejection ? rejection.RejectionCategory : TerrariaFrameRejectionCategory.None
    };

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != NpcCatchFrameStopReason.None)
            return TerrariaFrameSinkResult.Stop;
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.CatchNpc)
            return inner.OnFrame(in frame);
        if (bootstrap.JoinState != PlayerJoinState.Playing || bootstrap.AssignedPlayerHandle is not PlayerHandle player)
            return Stop(NpcCatchFrameStopReason.InvalidJoinState);
        if (TerrariaNpcCatchCodec.TryDecode(in frame, out TerrariaNpcCatchState state) != TerrariaNpcCatchDecodeResult.Decoded)
            return Stop(NpcCatchFrameStopReason.MalformedPacket);
        if (!TerrariaNpcCatchCodec.IsValidNpcSlot(state.NpcSlot))
            return Stop(NpcCatchFrameStopReason.InvalidNpcSlot);

        var connection = new ConnectionHandle(source, player);
        return ingress.TryPost(connection, in state)
            ? TerrariaFrameSinkResult.Continue
            : Stop(NpcCatchFrameStopReason.GameIngressBackpressure);
    }

    private TerrariaFrameSinkResult Stop(NpcCatchFrameStopReason reason)
    {
        StopReason = reason;
        return TerrariaFrameSinkResult.Stop;
    }
}
''')

# Persistent saved-NPC mutation bits.
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''namespace TerraRuntime.World;\n\n/// <summary>''',
    '''namespace TerraRuntime.World;\n\n[Flags]\npublic enum RuntimeTownRescueFacts1458 : ushort\n{\n    None = 0,\n    Goblin = 1 << 0,\n    Wizard = 1 << 1,\n    Mechanic = 1 << 2,\n    Stylist = 1 << 3,\n    Angler = 1 << 4,\n    Bartender = 1 << 5,\n    Golfer = 1 << 6,\n    TaxCollector = 1 << 7,\n    All = Goblin | Wizard | Mechanic | Stylist | Angler | Bartender | Golfer | TaxCollector\n}\n\n/// <summary>''')
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''    public bool UnlockTruffleSpawn { get; init; }\n\n    public bool HasAny => CompletedMask != 0 || UnlockSlimeBlueSpawn || UnlockTruffleSpawn;''',
    '''    public bool UnlockTruffleSpawn { get; init; }\n\n    public RuntimeTownRescueFacts1458 RescuedTownNpcs { get; init; }\n\n    public bool HasAny => CompletedMask != 0 || UnlockSlimeBlueSpawn || UnlockTruffleSpawn || RescuedTownNpcs != RuntimeTownRescueFacts1458.None;''')
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''    private bool baselineTruffleSpawnUnlocked;\n    private bool unlockTruffleSpawn;''',
    '''    private bool baselineTruffleSpawnUnlocked;\n    private bool unlockTruffleSpawn;\n    private RuntimeTownRescueFacts1458 baselineRescuedTownNpcs;\n    private RuntimeTownRescueFacts1458 rescuedTownNpcs;''')
replace_once(
    'src/TerraRuntime.World/RuntimeWorldProgressionMutations.cs',
    '''    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() =>\n        new(completedMask)\n        {\n            UnlockSlimeBlueSpawn = unlockSlimeBlueSpawn,\n            UnlockTruffleSpawn = unlockTruffleSpawn\n        };''',
    '''    public void SetTownRescueBaseline(RuntimeTownRescueFacts1458 facts)\n    {\n        if ((facts & ~RuntimeTownRescueFacts1458.All) != 0)\n            throw new ArgumentOutOfRangeException(nameof(facts));\n        baselineRescuedTownNpcs |= facts;\n    }\n\n    public bool MarkTownNpcRescued(RuntimeTownRescueFacts1458 fact)\n    {\n        ushort raw = (ushort)fact;\n        if (raw == 0 || (raw & (raw - 1)) != 0 || (fact & ~RuntimeTownRescueFacts1458.All) != 0)\n            throw new ArgumentOutOfRangeException(nameof(fact));\n        if (((baselineRescuedTownNpcs | rescuedTownNpcs) & fact) != 0)\n            return false;\n        rescuedTownNpcs |= fact;\n        return true;\n    }\n\n    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() =>\n        new(completedMask)\n        {\n            UnlockSlimeBlueSpawn = unlockSlimeBlueSpawn,\n            UnlockTruffleSpawn = unlockTruffleSpawn,\n            RescuedTownNpcs = rescuedTownNpcs\n        };''')

# Replace the town-state locator in the lossless header patcher with one that also captures all saved-NPC flags.
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''        int slimeBlueUnlockOffset = -1;\n        int truffleUnlockOffset = -1;\n        bool persistedSlimeBlueUnlock = false;\n        bool persistedTruffleUnlock = false;\n        if ((mutations.UnlockSlimeBlueSpawn || mutations.UnlockTruffleSpawn) &&\n            !TryLocateTownSpawnUnlocks(\n                ref reader,\n                out slimeBlueUnlockOffset,\n                out persistedSlimeBlueUnlock,\n                out truffleUnlockOffset,\n                out persistedTruffleUnlock))\n        {\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;\n        }''',
    '''        TownStateOffsets1458 townState = default;\n        bool needsTownState = mutations.UnlockSlimeBlueSpawn ||\n            mutations.UnlockTruffleSpawn ||\n            mutations.RescuedTownNpcs != RuntimeTownRescueFacts1458.None;\n        if (needsTownState && !TryLocateTownState(ref reader, out townState))\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;''')
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''        if (mutations.UnlockSlimeBlueSpawn && !persistedSlimeBlueUnlock)\n            patchedHeader[slimeBlueUnlockOffset] = 1;\n        if (mutations.UnlockTruffleSpawn && !persistedTruffleUnlock)\n            patchedHeader[truffleUnlockOffset] = 1;\n\n        return WorldFileProgressionHeaderPatchResult.Patched;''',
    '''        if (mutations.UnlockSlimeBlueSpawn && !townState.PersistedSlimeBlue)\n            patchedHeader[townState.SlimeBlueOffset] = 1;\n        if (mutations.UnlockTruffleSpawn && !townState.PersistedTruffle)\n            patchedHeader[townState.TruffleOffset] = 1;\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Goblin, townState.SavedGoblinOffset, townState.PersistedSavedGoblin);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Wizard, townState.SavedWizardOffset, townState.PersistedSavedWizard);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Mechanic, townState.SavedMechanicOffset, townState.PersistedSavedMechanic);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Angler, townState.SavedAnglerOffset, townState.PersistedSavedAngler);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Stylist, townState.SavedStylistOffset, townState.PersistedSavedStylist);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.TaxCollector, townState.SavedTaxCollectorOffset, townState.PersistedSavedTaxCollector);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Golfer, townState.SavedGolferOffset, townState.PersistedSavedGolfer);\n        PatchTownRescueFact(patchedHeader, mutations.RescuedTownNpcs, RuntimeTownRescueFacts1458.Bartender, townState.SavedBartenderOffset, townState.PersistedSavedBartender);\n\n        return WorldFileProgressionHeaderPatchResult.Patched;''')

replace_between(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''    private static bool TryLocateTownSpawnUnlocks(''',
    '''    private ref struct HeaderPrefixReader''',
    r'''    private readonly record struct TownStateOffsets1458(
        int SavedGoblinOffset,
        bool PersistedSavedGoblin,
        int SavedWizardOffset,
        bool PersistedSavedWizard,
        int SavedMechanicOffset,
        bool PersistedSavedMechanic,
        int SavedAnglerOffset,
        bool PersistedSavedAngler,
        int SavedStylistOffset,
        bool PersistedSavedStylist,
        int SavedTaxCollectorOffset,
        bool PersistedSavedTaxCollector,
        int SavedGolferOffset,
        bool PersistedSavedGolfer,
        int SavedBartenderOffset,
        bool PersistedSavedBartender,
        int SlimeBlueOffset,
        bool PersistedSlimeBlue,
        int TruffleOffset,
        bool PersistedTruffle);

    private static void PatchTownRescueFact(
        byte[] header,
        RuntimeTownRescueFacts1458 mutations,
        RuntimeTownRescueFacts1458 fact,
        int offset,
        bool persisted)
    {
        if ((mutations & fact) != 0 && !persisted)
            header[offset] = 1;
    }

    private static bool TryLocateTownState(ref HeaderPrefixReader reader, out TownStateOffsets1458 state)
    {
        state = default;

        int savedGoblinOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedGoblin)) return false;
        int savedWizardOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedWizard)) return false;
        int savedMechanicOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedMechanic)) return false;

        if (!reader.TrySkipBools(6) ||
            !reader.TryReadByte(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TrySkipBools(2) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadDouble(out _) ||
            !reader.TryReadDouble(out _) ||
            !reader.TryReadByte(out _) ||
            !reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadSingle(out _) ||
            !reader.TrySkip(sizeof(int) * 3) ||
            !reader.TrySkip(8) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt16(out _) ||
            !reader.TryReadSingle(out _) ||
            !reader.TryReadInt32(out int anglerCount) || anglerCount < 0 || anglerCount > 255)
        {
            return false;
        }
        for (int i = 0; i < anglerCount; i++)
        {
            if (!reader.TryReadString(out _)) return false;
        }

        int savedAnglerOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedAngler) || !reader.TryReadInt32(out _)) return false;
        int savedStylistOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedStylist)) return false;
        int savedTaxCollectorOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedTaxCollector)) return false;
        int savedGolferOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedGolfer) || !reader.TryReadInt32(out _) || !reader.TryReadInt32(out _)) return false;

        if (!reader.TryReadInt16(out short killCount) || killCount < 0 ||
            !reader.TrySkip(checked(killCount * sizeof(int))) ||
            !reader.TryReadInt16(out short claimableCount) || claimableCount < 0 ||
            !reader.TrySkip(checked(claimableCount * sizeof(ushort))) ||
            !reader.TryReadBool(out _) ||
            !reader.TrySkipBools(18) ||
            !reader.TrySkipBools(2) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out int partyCount) || partyCount < 0 || partyCount > 255 ||
            !reader.TrySkip(checked(partyCount * sizeof(int))) ||
            !reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadSingle(out _) ||
            !reader.TryReadSingle(out _))
        {
            return false;
        }

        int savedBartenderOffset = reader.Offset;
        if (!reader.TryReadBool(out bool savedBartender) ||
            !reader.TrySkipBools(3) ||
            !reader.TrySkip(5) ||
            !reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TrySkipBools(3) ||
            !reader.TryReadInt32(out int treeTopCount) || treeTopCount < 0 || treeTopCount > 64 ||
            !reader.TrySkip(checked(treeTopCount * sizeof(int))) ||
            !reader.TrySkipBools(2) ||
            !reader.TrySkip(sizeof(int) * 4) ||
            !reader.TrySkipBools(6))
        {
            return false;
        }

        int slimeBlueOffset = reader.Offset;
        if (!reader.TryReadBool(out bool slimeBlue) || !reader.TrySkipBools(4)) return false;
        int truffleOffset = reader.Offset;
        if (!reader.TryReadBool(out bool truffle)) return false;

        state = new TownStateOffsets1458(
            savedGoblinOffset, savedGoblin,
            savedWizardOffset, savedWizard,
            savedMechanicOffset, savedMechanic,
            savedAnglerOffset, savedAngler,
            savedStylistOffset, savedStylist,
            savedTaxCollectorOffset, savedTaxCollector,
            savedGolferOffset, savedGolfer,
            savedBartenderOffset, savedBartender,
            slimeBlueOffset, slimeBlue,
            truffleOffset, truffle);
        return true;
    }

''')

# Rescued residents become persistent homeless town NPCs in the same runtime slot, matching Transform + UpdateHomeTileState.
replace_once(
    'src/TerraRuntime/RuntimeTownNpcStateStore.cs',
    '''    public bool TryUpdatePosition(short slot, in NpcSnapshot snapshot)\n    {''',
    r'''    public bool CanAdoptRescuedResident(short slot, NpcTypeId type) =>
        slot >= 0 &&
        !townNpcsBySlot.ContainsKey(slot) &&
        !ContainsNpcType(type) &&
        VanillaTownNpcFacts1458.IsHousingEligible(type);

    public bool TryAdoptRescuedResident(short slot, NpcTypeId type, in NpcSnapshot snapshot)
    {
        if (!CanAdoptRescuedResident(slot, type) ||
            snapshot.Handle.Slot != slot ||
            snapshot.Type != type.Value ||
            !VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int homeTileX = (int)(snapshot.PositionX + definition.Width / 2f) / 16;
        int homeTileY = (int)(snapshot.PositionY + definition.Height) / 16;
        if (!IsInWorld(homeTileX, homeTileY))
            return false;

        townNpcsBySlot.Add(slot, new WorldTownNpc(
            type.Value,
            string.Empty,
            snapshot.PositionX,
            snapshot.PositionY,
            Homeless: true,
            homeTileX,
            homeTileY,
            TownNpcVariationIndex: null,
            HomelessDespawn: false));
        roomsByNpcType.Remove(type.Value);
        return true;
    }

    public bool TryUpdatePosition(short slot, in NpcSnapshot snapshot)
    {''')

write('src/TerraRuntime/RuntimeTownNpcRescueService1458.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>Authoritative source-shaped bound-town transform transaction for TerrariaServer 1.4.5.8.</summary>
internal sealed class RuntimeTownNpcRescueService1458
{
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly RuntimeWorldProgressionMutations progression;

    public RuntimeTownNpcRescueService1458(
        RuntimeNpcStore npcs,
        RuntimeTownNpcStateStore townNpcs,
        RuntimeWorldProgressionMutations progression)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }

    public bool TryRescueTalk(short npcSlot, out NpcSnapshot transformed)
    {
        transformed = default;
        if ((uint)npcSlot >= Terraria.Protocol.Multiplicity.TerrariaNpcTalkCodec.MaximumNpcSlots ||
            !npcs.TryGetActive(checked((byte)npcSlot), out NpcSnapshot source) ||
            !NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType) ||
            !VanillaTownNpcRescue1458.TryGetTalkRule(sourceType, out VanillaTownNpcRescueRule1458 rule))
        {
            return false;
        }
        return TryTransform(in source, in rule, out transformed);
    }

    private bool TryTransform(
        in NpcSnapshot source,
        in VanillaTownNpcRescueRule1458 rule,
        out NpcSnapshot transformed)
    {
        transformed = default;
        short slot = source.Handle.Slot;
        if (!townNpcs.CanAdoptRescuedResident(slot, rule.ResidentType) ||
            !VanillaTownNpcFacts1458.TryGetDefinition(rule.ResidentType, out VanillaNpcDefinition target))
        {
            return false;
        }

        int oldLifeMax = source.Simulation.LifeMax > 0 ? source.Simulation.LifeMax : rule.BoundLifeMax;
        int oldLife = source.Simulation.Life > 0 ? source.Simulation.Life : oldLifeMax;
        int life = Math.Max(1, oldLife * target.LifeMax / oldLifeMax);
        float positionY = source.PositionY + rule.BoundHeight - target.Height;
        NpcSimulationState simulation = NpcSimulationState.Initial with
        {
            Life = life,
            LifeMax = target.LifeMax,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            DirectionX = source.Simulation.DirectionX,
            DirectionY = source.Simulation.DirectionY,
            SpriteDirection = source.Simulation.SpriteDirection
        };
        var update = new NpcStateUpdate(
            Type: rule.ResidentType.Value,
            NetId: checked((short)rule.ResidentType.Value),
            PositionX: source.PositionX,
            PositionY: positionY,
            VelocityX: source.VelocityX,
            VelocityY: source.VelocityY,
            Target: source.Target,
            Ai: default,
            Simulation: simulation);
        if (!npcs.TryUpdate(source.Handle, in update, out transformed))
            return false;
        if (!townNpcs.TryAdoptRescuedResident(slot, rule.ResidentType, in transformed))
            throw new InvalidOperationException("Preflighted town rescue could not be committed to the persistent roster.");

        progression.MarkTownNpcRescued(ToRuntimeFact(rule.Fact));
        return true;
    }

    private static RuntimeTownRescueFacts1458 ToRuntimeFact(VanillaTownNpcRescueFact1458 fact) => fact switch
    {
        VanillaTownNpcRescueFact1458.Goblin => RuntimeTownRescueFacts1458.Goblin,
        VanillaTownNpcRescueFact1458.Wizard => RuntimeTownRescueFacts1458.Wizard,
        VanillaTownNpcRescueFact1458.Mechanic => RuntimeTownRescueFacts1458.Mechanic,
        VanillaTownNpcRescueFact1458.Stylist => RuntimeTownRescueFacts1458.Stylist,
        VanillaTownNpcRescueFact1458.Angler => RuntimeTownRescueFacts1458.Angler,
        VanillaTownNpcRescueFact1458.Bartender => RuntimeTownRescueFacts1458.Bartender,
        VanillaTownNpcRescueFact1458.Golfer => RuntimeTownRescueFacts1458.Golfer,
        VanillaTownNpcRescueFact1458.TaxCollector => RuntimeTownRescueFacts1458.TaxCollector,
        _ => throw new ArgumentOutOfRangeException(nameof(fact))
    };
}
''')

# Game-loop integration: initialize saved baselines, transform talk-rescued residents, apply packet-70 CatchNPC transaction.
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    private readonly RuntimeTownNpcStateStore? _townNpcs;\n    private readonly RuntimeTownCommerceResolver1458? _townCommerce;''',
    '''    private readonly RuntimeTownNpcStateStore? _townNpcs;\n    private readonly RuntimeWorldProgressionMutations? _worldProgression;\n    private readonly RuntimeTownNpcRescueService1458? _townRescue;\n    private readonly RuntimeTownCommerceResolver1458? _townCommerce;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        _npcReplication = npcReplication;\n        _townNpcs = townNpcs;''',
    '''        _npcReplication = npcReplication;\n        _townNpcs = townNpcs;\n        _worldProgression = worldTiles is null ? null : RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);\n        _townRescue = townNpcs is not null && _worldProgression is not null\n            ? new RuntimeTownNpcRescueService1458(_npcs, townNpcs, _worldProgression)\n            : null;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''            if (townSpawnWorldFacts is VanillaTownSpawnWorldFacts1458 facts)\n            {\n                var houseIndex = new RuntimeTownHouseCandidateIndex1458(worldTiles, _housingValidator);\n                RuntimeWorldProgressionMutations progression = RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);\n                progression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);''',
    '''            if (townSpawnWorldFacts is VanillaTownSpawnWorldFacts1458 facts)\n            {\n                var houseIndex = new RuntimeTownHouseCandidateIndex1458(worldTiles, _housingValidator);\n                RuntimeWorldProgressionMutations progression = _worldProgression ?? RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);\n                progression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);\n                RuntimeTownRescueFacts1458 rescuedBaseline = RuntimeTownRescueFacts1458.None;\n                if (facts.SavedGoblin) rescuedBaseline |= RuntimeTownRescueFacts1458.Goblin;\n                if (facts.SavedWizard) rescuedBaseline |= RuntimeTownRescueFacts1458.Wizard;\n                if (facts.SavedMechanic) rescuedBaseline |= RuntimeTownRescueFacts1458.Mechanic;\n                if (facts.SavedStylist) rescuedBaseline |= RuntimeTownRescueFacts1458.Stylist;\n                if (facts.SavedAngler) rescuedBaseline |= RuntimeTownRescueFacts1458.Angler;\n                if (facts.SavedBartender) rescuedBaseline |= RuntimeTownRescueFacts1458.Bartender;\n                if (facts.SavedGolfer) rescuedBaseline |= RuntimeTownRescueFacts1458.Golfer;\n                if (facts.SavedTaxCollector) rescuedBaseline |= RuntimeTownRescueFacts1458.TaxCollector;\n                progression.SetTownRescueBaseline(rescuedBaseline);''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''            case ClientNpcTalkRuntimeCommand talk:\n                ApplyClientNpcTalk(talk);\n                break;''',
    '''            case ClientNpcTalkRuntimeCommand talk:\n                ApplyClientNpcTalk(talk);\n                break;\n            case ClientNpcCatchRuntimeCommand npcCatch:\n                ApplyClientNpcCatch(npcCatch);\n                break;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''        byte playerSlot = command.Connection.Player.Slot.Value;\n        _playerTalkNpcSlots[playerSlot] = command.State.NpcSlot;''',
    '''        byte playerSlot = command.Connection.Player.Slot.Value;\n        if (command.State.NpcSlot != TerrariaNpcTalkCodec.NoNpc)\n            _townRescue?.TryRescueTalk(command.State.NpcSlot, out _);\n        _playerTalkNpcSlots[playerSlot] = command.State.NpcSlot;''')
replace_once(
    'src/TerraRuntime/ServerRuntimeState.cs',
    '''    internal bool TryGetPlayerTalkNpc(PlayerHandle player, out short npcSlot)\n    {''',
    r'''    private void ApplyClientNpcCatch(ClientNpcCatchRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !TerrariaNpcCatchCodec.IsValidNpcSlot(command.State.NpcSlot) ||
            !_players.TryGetValue(command.Connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            !_npcs.TryGetActive(checked((byte)command.State.NpcSlot), out NpcSnapshot npc) ||
            !NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcCatchCatalog1458.TryGetCatchItem(npcType, out ItemTypeId catchItem))
        {
            return;
        }

        // Terraria 1.4.5.8 Mystic Frog (687) teleports instead of becoming an item. That special transform is
        // deliberately left to its own N4 special-NPC slice; packet 70 must not incorrectly despawn it here.
        if (VanillaNpcCatchCatalog1458.IsMysticFrog(npcType))
            return;

        if (npc.Simulation.SpawnedFromStatue)
        {
            _npcs.TryDespawn(npc.Handle);
            return;
        }

        float playerCenterX = player.PositionX + VanillaBasePlayerWidth / 2f;
        float playerCenterY = player.PositionY + VanillaBasePlayerHeight / 2f;
        WorldItemDropStateUpdate drop = VanillaNpcCatchWorldItem1458.Create(
            playerCenterX,
            playerCenterY,
            catchItem,
            _worldItemSpawnRandom);
        if (!_worldItems.TryReserveDrop(in drop, out WorldItemDropReservation reservation))
            return;
        if (!_npcs.TryDespawn(npc.Handle))
        {
            _worldItems.TryReleaseDropReservation(in reservation);
            return;
        }
        if (!_worldItems.TryCommitReservedDrop(in reservation, out WorldItemSnapshot item))
            throw new InvalidOperationException("Reserved NPC catch item failed after authoritative NPC despawn.");

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: command.Connection.Player.Slot.Value,
            TimeToKeepReservation: VanillaNpcCatchWorldItem1458.ReservationTicks,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0,
            PositionX: item.PositionX,
            PositionY: item.PositionY);
        if (!_worldItems.TryApplyOwner(item.Handle.Slot, in owner, out _))
            throw new InvalidOperationException("Caught NPC item could not be reserved for the authenticated player.");
    }

    internal bool TryGetPlayerTalkNpc(PlayerHandle player, out short npcSlot)
    {''')

# Host packet-chain wiring.
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''        var npcTalkIngress = new RuntimeNpcTalkNetworkIngress(commandIngress);\n        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);''',
    '''        var npcTalkIngress = new RuntimeNpcTalkNetworkIngress(commandIngress);\n        var npcCatchIngress = new RuntimeNpcCatchNetworkIngress(commandIngress);\n        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);''')
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''                    townNpcHomeIngress,\n                    npcTalkIngress,\n                    disconnectIngress,''',
    '''                    townNpcHomeIngress,\n                    npcTalkIngress,\n                    npcCatchIngress,\n                    disconnectIngress,''')
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''        ITownNpcHomeNetworkIngress townNpcHomeIngress,\n        INpcTalkNetworkIngress npcTalkIngress,\n        RuntimePlayerDisconnectIngress disconnectIngress,''',
    '''        ITownNpcHomeNetworkIngress townNpcHomeIngress,\n        INpcTalkNetworkIngress npcTalkIngress,\n        INpcCatchNetworkIngress npcCatchIngress,\n        RuntimePlayerDisconnectIngress disconnectIngress,''')
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''            var npcTalkSink = new NpcTalkFrameSink(\n                source,\n                bootstrapSink,\n                townNpcHomeSink,\n                npcTalkIngress);\n\n            try''',
    '''            var npcTalkSink = new NpcTalkFrameSink(\n                source,\n                bootstrapSink,\n                townNpcHomeSink,\n                npcTalkIngress);\n            var npcCatchSink = new NpcCatchFrameSink(\n                source,\n                bootstrapSink,\n                npcTalkSink,\n                npcCatchIngress);\n\n            try''')
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''                        npcTalkSink,\n                        outbound,''',
    '''                        npcCatchSink,\n                        outbound,''')
replace_once(
    'src/TerraRuntime/TerrariaServerHost.cs',
    '''$"bootstrap={bootstrapSink.StopReason}, vitals={vitalsSink.StopReason}, items={itemSink.StopReason}, projectiles={projectileSink.StopReason}, chests={chestSink.StopReason}, signs={signSink.StopReason}, housing={townNpcHomeSink.StopReason}, talk={npcTalkSink.StopReason}, tiles={projectileSink.TileStopReason}, state={bootstrapSink.JoinState}; "''',
    '''$"bootstrap={bootstrapSink.StopReason}, vitals={vitalsSink.StopReason}, items={itemSink.StopReason}, projectiles={projectileSink.StopReason}, chests={chestSink.StopReason}, signs={signSink.StopReason}, housing={townNpcHomeSink.StopReason}, talk={npcTalkSink.StopReason}, catchNpc={npcCatchSink.StopReason}, tiles={projectileSink.TileStopReason}, state={bootstrapSink.JoinState}; "''')

# Tests: source tables, exact packet 70, rescue transaction, catch transaction.
write('tests/TerraRuntime.Tests/VanillaTownNpcRescue1458Tests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTownNpcRescue1458Tests
{
    [Fact]
    public void Talk_rescue_catalog_matches_pinned_1458_transform_pairs()
    {
        int[] expectedBound = [589, 105, 106, 123, 354, 376, 579];
        int[] expectedTown = [588, 107, 108, 124, 353, 369, 550];
        VanillaTownNpcRescueRule1458[] talk = VanillaTownNpcRescue1458.All
            .ToArray()
            .Where(static r => r.Trigger == VanillaTownNpcRescueTrigger1458.Talk)
            .ToArray();
        Assert.Equal(expectedBound.Order(), talk.Select(static r => r.BoundType.Value).Order());
        Assert.Equal(expectedTown.Order(), talk.Select(static r => r.ResidentType.Value).Order());
        Assert.All(talk, static rule => Assert.True(rule.IsValid));
    }

    [Fact]
    public void Runtime_rescue_preserves_bottom_repositions_and_journals_saved_fact()
    {
        var npcs = new RuntimeNpcStore();
        var town = new RuntimeTownNpcStateStore(
            new WorldNpcPersistence([], [], []),
            [],
            new WorldDimensions(200, 200));
        var progression = new RuntimeWorldProgressionMutations();
        var service = new RuntimeTownNpcRescueService1458(npcs, town, progression);
        NpcSimulationState sim = NpcSimulationState.Initial with { Life = 125, LifeMax = 250 };
        Assert.True(npcs.TrySpawn(3, new NpcStateUpdate(105, 105, 100f, 200f, 0f, 0f, 255, default, sim), out NpcSnapshot before));

        Assert.True(service.TryRescueTalk(3, out NpcSnapshot after));
        Assert.Equal(VanillaNpcIds.GoblinTinkerer.Value, after.Type);
        Assert.Equal(before.PositionY + 34f - 40f, after.PositionY);
        Assert.Equal(125, after.Simulation.Life);
        Assert.True(town.ContainsNpcType(VanillaNpcIds.GoblinTinkerer));
        Assert.Equal(RuntimeTownRescueFacts1458.Goblin, progression.CaptureSnapshot().RescuedTownNpcs);
    }
}
''')

write('tests/TerraRuntime.Tests/VanillaNpcCatchCatalog1458Tests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcCatchCatalog1458Tests
{
    [Theory]
    [InlineData(46, 2019, true)]
    [InlineData(375, 2673, false)]
    [InlineData(615, 0, true)]
    [InlineData(625, 0, true)]
    [InlineData(687, 2121, true)]
    [InlineData(688, 5511, true)]
    public void Critter_and_catch_item_are_distinct_source_facts(int npc, int catchItem, bool critter)
    {
        var type = new NpcTypeId(npc);
        Assert.Equal(critter, VanillaNpcCatchCatalog1458.CountsAsCritter(type));
        Assert.Equal(catchItem > 0, VanillaNpcCatchCatalog1458.TryGetCatchItem(type, out ItemTypeId item));
        if (catchItem > 0)
            Assert.Equal(catchItem, item.Value);
    }
}
''')

write('tests/TerraRuntime.Tests/TerrariaNpcCatchCodecTests.cs', r'''using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcCatchCodecTests
{
    [Fact]
    public void Packet_70_decodes_exact_int16_slot()
    {
        byte[] bytes = [5, 0, (byte)TerrariaMessageId.CatchNpc, 123, 0];
        var buffer = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(TerrariaNpcCatchDecodeResult.Decoded, TerrariaNpcCatchCodec.TryDecode(in frame, out TerrariaNpcCatchState state));
        Assert.Equal((short)123, state.NpcSlot);
    }

    [Fact]
    public void Packet_70_rejects_wrong_payload_length_and_slot_bounds()
    {
        var frame = new TerrariaFrame((byte)TerrariaMessageId.CatchNpc, new ReadOnlySequence<byte>(new byte[] { 1 }));
        Assert.Equal(TerrariaNpcCatchDecodeResult.InvalidPayloadLength, TerrariaNpcCatchCodec.TryDecode(in frame, out _));
        Assert.True(TerrariaNpcCatchCodec.IsValidNpcSlot(199));
        Assert.False(TerrariaNpcCatchCodec.IsValidNpcSlot(200));
        Assert.False(TerrariaNpcCatchCodec.IsValidNpcSlot(-1));
    }
}
''')

# Documentation keeps the remaining Tax Collector projectile trigger and Mystic Frog special behavior explicit.
for path, text in [
    ('docs/en/town-npc-housing-shops.md', '\n### Rescue and critter lifecycle\n\nTerrariaServer 1.4.5.8 bound talk-rescue is now authoritative for Golfer Rescue, Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler and unconscious Tavernkeep. The transform keeps the NPC slot/generation, repositions from the old bottom edge like `NPC.Transform`, converts the slot into a persistent homeless resident and journals the corresponding `saved*` world flag for the lossless `.wld` header patch.\n\nPacket 70 (`CatchNPC`) is decoded as the exact signed `Int16` NPC slot and committed on the game-loop owner. The runtime pins the complete `NPCID.Sets.CountsAsCritter` set separately from all verified `catchItem` mappings, reserves world-item capacity before despawning the NPC, creates the 12x12 captured-critter item at the authoritative player center with vanilla spawn velocity and reserves it for that authenticated player. Statue-spawned critters follow the no-item despawn branch. Mystic Frog remains fail-closed here because vanilla teleports it instead of catching it; Demon Tax Collector remains the separate Purification Powder projectile-10 transform path.\n'),
    ('docs/ru/town-npc-housing-shops.md', '\n### Rescue и жизненный цикл critter\n\nTalk-rescue из TerrariaServer 1.4.5.8 теперь authoritative для Golfer Rescue, Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler и лежащего без сознания Tavernkeep. Transform сохраняет NPC slot/generation, переносит позицию от старой нижней границы как `NPC.Transform`, превращает слот в persistent homeless resident и журналирует соответствующий `saved*` флаг для lossless-патча `.wld`.\n\nPacket 70 (`CatchNPC`) декодируется как точный signed `Int16` NPC slot и применяется только game-loop owner. Runtime отдельно закрепляет полный `NPCID.Sets.CountsAsCritter` и все проверенные `catchItem` mapping: до despawn резервируется world-item slot, затем у authoritative player center создаётся 12x12 captured-critter item с vanilla spawn velocity и резервируется за authenticated player. Statue-spawned critter удаляется без предмета. Mystic Frog здесь fail-closed, потому что vanilla телепортирует его вместо обычного catch; Demon Tax Collector остаётся отдельным transform-путём от Purification Powder projectile 10.\n')
]:
    p = Path(path)
    p.write_text(p.read_text().rstrip() + '\n' + text)

print('N4 rescue/catchability block applied')
