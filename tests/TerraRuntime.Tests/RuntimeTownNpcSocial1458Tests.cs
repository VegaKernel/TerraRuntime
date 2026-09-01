using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcSocial1458Tests
{
    [Fact]
    public void Packet91_roundtrips_source_positive_npc_bubble_shape()
    {
        var state = new TerrariaEmoteBubbleState(42, TerrariaEmoteBubbleState.NpcAnchor, 17, 45, 38);
        Assert.Equal(TerrariaEmoteBubbleEncodeResult.Encoded, TerrariaEmoteBubbleCodec.TryEncode(in state, out byte[] encoded));
        Assert.Equal(13, encoded.Length);
        Assert.Equal((byte)TerrariaMessageId.EmoteBubble, encoded[2]);

        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(TerrariaEmoteBubbleDecodeResult.Decoded, TerrariaEmoteBubbleCodec.TryDecode(in frame, out TerrariaEmoteBubbleState decoded));
        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Ordinary_conversation_commits_source_state_three_and_peer_state_four()
    {
        SocialFixture f = SocialFixture.Create([VanillaNpcIds.Merchant, VanillaNpcIds.Nurse]);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));

        Assert.True(f.Social.TryStartConversationForTesting(source.Handle, rpsGame: false));
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.True(f.Npcs.TryGetActive(1, out NpcSnapshot peer));
        Assert.Equal(3f, source.Ai.Ai0);
        Assert.Equal(4f, peer.Ai.Ai0);
        Assert.Equal(1f, source.Ai.Ai2);
        Assert.Equal(0f, peer.Ai.Ai2);
        Assert.Equal(420f, source.Ai.Ai1);
        Assert.Equal(source.Ai.Ai1, peer.Ai.Ai1);
        Assert.Equal(1, source.Simulation.DirectionX);
        Assert.Equal(-1, peer.Simulation.DirectionX);
    }

    [Fact]
    public void Rps_pair_emits_two_packet91_bubbles_on_source_tick_forty()
    {
        var emotes = new RecordingEmotes();
        SocialFixture f = SocialFixture.Create(
            [VanillaNpcIds.Merchant, VanillaNpcIds.Nurse],
            emotes: emotes,
            random: new ZeroRandom());
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartConversationForTesting(source.Handle, rpsGame: true));

        for (int i = 0; i < 39; i++)
            f.Social.Tick();
        Assert.Empty(emotes.States);

        RuntimeTownNpcSocialTickSummary1458 tick = f.Social.Tick();
        Assert.Equal(2, tick.BubblesPublished);
        Assert.Equal(2, emotes.States.Count);
        Assert.All(emotes.States, x => Assert.Equal(TerrariaEmoteBubbleState.NpcAnchor, x.AnchorType));
        Assert.All(emotes.States, x => Assert.Equal((ushort)45, x.Lifetime));
        Assert.Contains(emotes.States, x => x.AnchorIndex == 0);
        Assert.Contains(emotes.States, x => x.AnchorIndex == 1);
        Assert.All(emotes.States, x => Assert.InRange(x.Emote, (byte)36, (byte)38));
    }

    [Fact]
    public void Player_facing_state_resets_when_generation_safe_player_disappears()
    {
        var players = new MutablePlayers(CreatePlayer(0, 220f, 160f));
        SocialFixture f = SocialFixture.Create([VanillaNpcIds.Merchant], players: players);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartPlayerStateForTesting(source.Handle, 7f, 220));
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(7f, source.Ai.Ai0);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.Equal(1, source.Simulation.DirectionX);

        players.Clear();
        f.Social.Tick();
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(0f, source.Ai.Ai0);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.InRange(source.Ai.Ai1, 60f, 119f);
    }

    [Theory]
    [InlineData(637, 20, 500)]
    [InlineData(638, 20, 200)]
    [InlineData(656, 20, 200)]
    [InlineData(670, 20, 180)]
    public void Pet_idle_entry_matches_source_state_selection_and_type_specific_duration(
        int typeValue,
        int expectedState,
        int expectedDuration)
    {
        SocialFixture f = SocialFixture.Create([new NpcTypeId(typeValue)]);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartPetIdleForTesting(source.Handle));
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(expectedState, source.Ai.Ai0);
        Assert.Equal(expectedDuration, source.Ai.Ai1);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.Equal(0f, source.Simulation.LocalAi.Ai3);
    }

    [Fact]
    public void Rps_state_times_out_back_to_wander_and_clears_peer_reference()
    {
        SocialFixture f = SocialFixture.Create([VanillaNpcIds.Merchant, VanillaNpcIds.Nurse]);
        Assert.True(f.Npcs.TryGetActive(0, out NpcSnapshot source));
        Assert.True(f.Social.TryStartConversationForTesting(source.Handle, rpsGame: true));
        for (int i = 0; i < 420; i++)
            f.Social.Tick();
        Assert.True(f.Npcs.TryGetActive(0, out source));
        Assert.Equal(0f, source.Ai.Ai0);
        Assert.Equal(0f, source.Ai.Ai2);
        Assert.InRange(source.Ai.Ai1, 60f, 119f);
    }

    private static PlayerStateSnapshot CreatePlayer(byte slot, float x, float y) => new(
        new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)),
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
        CameraTargetY: 0f)
    {
        HasHealth = true,
        Life = 100,
        MaxLife = 100,
        IsDead = false
    };

    private sealed class SocialFixture
    {
        private SocialFixture(RuntimeNpcStore npcs, RuntimeTownNpcSocial1458 social)
        {
            Npcs = npcs;
            Social = social;
        }
        public RuntimeNpcStore Npcs { get; }
        public RuntimeTownNpcSocial1458 Social { get; }

        public static SocialFixture Create(
            NpcTypeId[] types,
            MutablePlayers? players = null,
            RecordingEmotes? emotes = null,
            IRuntimeTownNpcSocialRandom1458? random = null)
        {
            var tiles = new WorldTileStore(new WorldDimensions(120, 80));
            var residents = new WorldTownNpc[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                residents[i] = new WorldTownNpc(
                    types[i].Value,
                    $"Town{i}",
                    160f + i * 60f,
                    160f,
                    true,
                    10 + i * 4,
                    14,
                    null,
                    false);
            }
            var persistence = new WorldNpcPersistence([], residents, []);
            var town = new RuntimeTownNpcStateStore(persistence, [], tiles.Dimensions);
            var npcs = new RuntimeNpcStore();
            Assert.True(town.TryReserveRuntimeSlots(npcs));
            var social = new RuntimeTownNpcSocial1458(
                town,
                npcs,
                tiles,
                players ?? new MutablePlayers(),
                emotes,
                schedule: null,
                random ?? new ZeroRandom());
            return new SocialFixture(npcs, social);
        }
    }

    private sealed class MutablePlayers(params PlayerStateSnapshot[] initial) : IRuntimePlayerSlotSnapshotLookup
    {
        private readonly Dictionary<byte, PlayerStateSnapshot> players = initial.ToDictionary(x => x.Player.Slot.Value);
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) => players.TryGetValue(slot.Value, out snapshot);
        public void Clear() => players.Clear();
    }

    private sealed class RecordingEmotes : IRuntimeTownNpcEmoteSink1458
    {
        public List<TerrariaEmoteBubbleState> States { get; } = [];
        public bool TryPublishEmoteBubble(in TerrariaEmoteBubbleState state)
        {
            States.Add(state);
            return true;
        }
    }

    private sealed class ZeroRandom : IRuntimeTownNpcSocialRandom1458
    {
        public int Next(int exclusiveMax) => 0;
    }
}
