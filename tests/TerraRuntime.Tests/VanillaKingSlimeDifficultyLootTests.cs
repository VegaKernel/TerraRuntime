using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaKingSlimeDifficultyLootTests
{
    [Fact]
    public void Committed_player_hit_marks_source_slot_interaction()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot king = SpawnKing(store);
        var interactions = new RuntimeNpcPlayerInteractionLedger(store);
        var executor = new RuntimeNpcDamageExecutor(store, interactions: interactions);
        PlayerHandle player = Player(slot: 3, generation: 1);
        var request = new NpcDamageRequest(
            king.Handle,
            DamageSource.FromPlayerItem(player),
            BaseDamage: 1);

        Assert.True(executor.TryApply(in request, out _));
        Assert.True(interactions.HasInteraction(king.Handle, player.Slot));
        Assert.False(interactions.HasInteraction(king.Handle, new PlayerSlotId(4)));
    }

    [Fact]
    public void Environment_hit_does_not_create_player_interaction()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot king = SpawnKing(store);
        var interactions = new RuntimeNpcPlayerInteractionLedger(store);
        var executor = new RuntimeNpcDamageExecutor(store, interactions: interactions);
        var request = new NpcDamageRequest(
            king.Handle,
            DamageSource.Environment,
            BaseDamage: 1);

        Assert.True(executor.TryApply(in request, out _));
        Assert.False(interactions.HasInteraction(king.Handle, new PlayerSlotId(0)));
    }

    [Fact]
    public void Ledger_copies_slots_in_source_order_and_never_truncates()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot king = SpawnKing(store);
        var interactions = new RuntimeNpcPlayerInteractionLedger(store);
        Assert.True(interactions.TryMark(king.Handle, Player(9, 1)));
        Assert.True(interactions.TryMark(king.Handle, Player(2, 1)));
        Assert.True(interactions.TryMark(king.Handle, Player(6, 1)));

        Span<PlayerSlotId> tooSmall = stackalloc PlayerSlotId[2];
        Assert.False(interactions.TryCopyInteractingSlots(king.Handle, tooSmall, out int rejectedCount));
        Assert.Equal(0, rejectedCount);

        Span<PlayerSlotId> slots = stackalloc PlayerSlotId[3];
        Assert.True(interactions.TryCopyInteractingSlots(king.Handle, slots, out int count));
        Assert.Equal(3, count);
        Assert.Equal(new byte[] { 2, 6, 9 }, slots[..count].ToArray().Select(x => x.Value).ToArray());
    }

    [Fact]
    public void Master_execution_preserves_source_rng_and_inline_materialization_order()
    {
        var context = new VanillaKingSlimeDifficultyLootContext(IsExpertMode: true, IsMasterMode: true);
        var origin = new NpcLootWorldItemOrigin(100f, 200f);
        VanillaKingSlimeLootPlayer[] players =
        [
            new(new PlayerSlotId(1), 300f, 400f),
            new(new PlayerSlotId(4), 500f, 600f)
        ];
        var rolls = new ScriptedRollSource(
        [
            0, 1, 100, // bag chance, stack, materialization
            0, 1, 100, // relic chance, stack, materialization
            1,         // shared pet stack
            0, 100,    // player 1 succeeds, materializes immediately
            3           // player 4 fails
        ]);
        var sink = new RecordingSink();

        Assert.True(VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context,
            in origin,
            players,
            rolls,
            sink,
            out KingSlimeDifficultyLootExecutionResult result));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(2, result.InstancedRecipientCount);
        Assert.Equal(2, result.WorldItemCount);
        Assert.Equal(1, result.MasterPetDropCount);
        Assert.Equal(
            new[]
            {
                "rng:0:1", "rng:1:2", "rng:100:101",
                "rng:0:1", "rng:1:2", "rng:100:101",
                "rng:1:2", "rng:0:4", "rng:100:101", "rng:0:4"
            },
            rolls.Calls);
        Assert.Equal(
            new[]
            {
                $"instanced:{VanillaKingSlimeItemIds.KingSlimeBossBag.Value}:2:54000",
                $"world:{VanillaKingSlimeItemIds.KingSlimeMasterTrophy.Value}:100:200",
                $"world:{VanillaKingSlimeItemIds.KingSlimePetItem.Value}:300:400"
            },
            sink.Events);
    }

    [Fact]
    public void Expert_execution_only_delivers_instanced_bag()
    {
        var context = new VanillaKingSlimeDifficultyLootContext(IsExpertMode: true, IsMasterMode: false);
        var origin = new NpcLootWorldItemOrigin(10f, 20f);
        VanillaKingSlimeLootPlayer[] players =
        [
            new(new PlayerSlotId(2), 30f, 40f)
        ];
        var rolls = new ScriptedRollSource([0, 1, 100]);
        var sink = new RecordingSink();

        Assert.True(VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context, in origin, players, rolls, sink, out KingSlimeDifficultyLootExecutionResult result));

        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(1, result.InstancedRecipientCount);
        Assert.Equal(0, result.WorldItemCount);
        Assert.Equal(new[] { $"instanced:{VanillaKingSlimeItemIds.KingSlimeBossBag.Value}:1:54000" }, sink.Events);
    }

    [Fact]
    public void Missing_master_pet_delivery_support_fails_before_first_rng_call()
    {
        var context = new VanillaKingSlimeDifficultyLootContext(IsExpertMode: true, IsMasterMode: true);
        var origin = new NpcLootWorldItemOrigin(10f, 20f);
        var rolls = new ScriptedRollSource([]);
        var sink = new RecordingSink(supportPet: false);

        Assert.False(VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context,
            in origin,
            ReadOnlySpan<VanillaKingSlimeLootPlayer>.Empty,
            rolls,
            sink,
            out _));
        Assert.Empty(rolls.Calls);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void Finalizer_filters_interactions_by_current_active_slot_and_despawns_exact_generation()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot king = SpawnKing(store);
        var interactions = new RuntimeNpcPlayerInteractionLedger(store);
        Assert.True(interactions.TryMark(king.Handle, Player(2, 1)));
        Assert.True(interactions.TryMark(king.Handle, Player(6, 1)));
        Kill(store, king.Handle);

        var players = new FakePlayerLookup(
            Snapshot(Player(6, 7), x: 700f, y: 800f));
        var finalizer = new RuntimeKingSlimeDifficultyLootFinalizer(store, interactions, players);
        var context = new VanillaKingSlimeDifficultyLootContext(IsExpertMode: true, IsMasterMode: false);
        var rolls = new ScriptedRollSource([0, 1, 100]);
        var sink = new RecordingSink();

        Assert.True(finalizer.TryFinalize(
            king.Handle, in context, rolls, sink, out RuntimeKingSlimeDifficultyLootResult result));

        Assert.True(result.IsValid);
        Assert.Equal(1, result.Loot.InstancedRecipientCount);
        Assert.Equal(new byte[] { 6 }, sink.LastRecipients);
        Assert.False(store.TryGet(king.Handle, out _));
        Assert.False(interactions.HasInteraction(king.Handle, new PlayerSlotId(6)));
    }

    [Fact]
    public void Normal_mode_is_rejected_without_consuming_rng_or_finalizing_dead_king()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot king = SpawnKing(store);
        var interactions = new RuntimeNpcPlayerInteractionLedger(store);
        Kill(store, king.Handle);
        var finalizer = new RuntimeKingSlimeDifficultyLootFinalizer(store, interactions, new FakePlayerLookup());
        var context = new VanillaKingSlimeDifficultyLootContext(IsExpertMode: false, IsMasterMode: false);
        var rolls = new ScriptedRollSource([]);
        var sink = new RecordingSink();

        Assert.False(finalizer.TryFinalize(king.Handle, in context, rolls, sink, out _));
        Assert.Empty(rolls.Calls);
        Assert.True(store.TryGet(king.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);
    }

    private static NpcSnapshot SpawnKing(RuntimeNpcStore store)
    {
        var update = new NpcStateUpdate(
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot npc));
        return npc;
    }

    private static void Kill(RuntimeNpcStore store, NpcHandle target)
    {
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(target, DamageSource.Environment, int.MaxValue);
        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.True(result.Lethal);
    }

    private static PlayerHandle Player(byte slot, ulong generation) =>
        new(new PlayerSlotId(slot), new PlayerSessionGeneration(generation));

    private static PlayerStateSnapshot Snapshot(PlayerHandle player, float x, float y) =>
        new(
            player,
            new PlayerStateRevision(1),
            Team: 0,
            ControlFlags: 0,
            MovementFlags: 0,
            MiscFlags1: 0,
            MiscFlags2: 0,
            SelectedItem: 0,
            PositionX: x,
            PositionY: y,
            VelocityX: 0f,
            VelocityY: 0f,
            MountType: 0,
            PotionOfReturnOriginalPositionX: 0f,
            PotionOfReturnOriginalPositionY: 0f,
            PotionOfReturnHomePositionX: 0f,
            PotionOfReturnHomePositionY: 0f,
            CameraTargetX: 0f,
            CameraTargetY: 0f);

    private sealed class FakePlayerLookup(params PlayerStateSnapshot[] players) : IRuntimePlayerSlotSnapshotLookup
    {
        private readonly Dictionary<byte, PlayerStateSnapshot> _players = players.ToDictionary(x => x.Player.Slot.Value);

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) =>
            _players.TryGetValue(slot.Value, out snapshot);
    }

    private sealed class RecordingSink(
        bool supportBag = true,
        bool supportRelic = true,
        bool supportPet = true) : IKingSlimeDifficultyLootDeliverySink
    {
        public List<string> Events { get; } = [];
        public byte[] LastRecipients { get; private set; } = [];

        public bool CanDeliverInstanced(ItemTypeId itemType) =>
            supportBag && itemType == VanillaKingSlimeItemIds.KingSlimeBossBag;

        public bool CanDeliverWorldItem(ItemTypeId itemType) =>
            (supportRelic && itemType == VanillaKingSlimeItemIds.KingSlimeMasterTrophy) ||
            (supportPet && itemType == VanillaKingSlimeItemIds.KingSlimePetItem);

        public bool TryDeliverInstanced(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            ReadOnlySpan<VanillaKingSlimeLootPlayer> recipients,
            int slotLeaseTicks,
            INpcLootRollSource random)
        {
            random.NextInt32(100, 101);
            LastRecipients = recipients.ToArray().Select(x => x.Slot.Value).ToArray();
            Events.Add($"instanced:{drop.ItemType.Value}:{recipients.Length}:{slotLeaseTicks}");
            return CanDeliverInstanced(drop.ItemType);
        }

        public bool TryDeliverWorldItem(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            INpcLootRollSource random)
        {
            random.NextInt32(100, 101);
            Events.Add($"world:{drop.ItemType.Value}:{origin.CenterX:0}:{origin.CenterY:0}");
            return CanDeliverWorldItem(drop.ItemType);
        }
    }

    private sealed class ScriptedRollSource(IEnumerable<int> randomResults) : INpcLootRollSource
    {
        private readonly Queue<int> _random = new(randomResults);
        public List<string> Calls { get; } = [];

        public int RollLuck(int chanceDenominator) =>
            throw new InvalidOperationException("King Slime difficulty rules in this slice use raw RNG, not RollLuck.");

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Calls.Add($"rng:{inclusiveMin}:{exclusiveMax}");
            int value = _random.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
