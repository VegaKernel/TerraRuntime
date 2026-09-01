using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcArrivalIdentity1458Tests
{
    [Fact]
    public void Legacy_profile_consumes_two_worldgen_name_rolls_and_keeps_second_name()
    {
        var main = new SequenceRandom();
        var world = new SequenceRandom(0, 1);
        var resolver = new VanillaTownNpcIdentityResolver1458(mainRandom: main, worldGenRandom: world);

        VanillaTownNpcSpawnIdentity1458 identity = resolver.Resolve(VanillaNpcIds.Merchant, shimmeredTownNpc: false);

        Assert.Equal("Barney", identity.GivenName);
        Assert.Equal(0, identity.VariationIndex);
        Assert.Empty(main.Maxima);
        Assert.Equal([23, 23], world.Maxima);
    }

    [Fact]
    public void Variant_pet_consumes_default_name_then_main_variation_then_variant_name()
    {
        var main = new SequenceRandom(4);
        var world = new SequenceRandom(0, 1);
        var resolver = new VanillaTownNpcIdentityResolver1458(mainRandom: main, worldGenRandom: world);

        VanillaTownNpcSpawnIdentity1458 identity = resolver.Resolve(VanillaNpcIds.TownCat, shimmeredTownNpc: false);

        Assert.Equal("Blaze", identity.GivenName);
        Assert.Equal(4, identity.VariationIndex);
        Assert.Equal([6], main.Maxima);
        Assert.Equal([12, 20], world.Maxima);
    }

    [Fact]
    public void Shimmer_override_happens_after_pet_name_roll_without_rerolling_name()
    {
        var main = new SequenceRandom(4);
        var world = new SequenceRandom(0, 1);
        var resolver = new VanillaTownNpcIdentityResolver1458(mainRandom: main, worldGenRandom: world);

        VanillaTownNpcSpawnIdentity1458 identity = resolver.Resolve(VanillaNpcIds.TownCat, shimmeredTownNpc: true);

        Assert.Equal("Blaze", identity.GivenName);
        Assert.Equal(1, identity.VariationIndex);
        Assert.Equal([6], main.Maxima);
        Assert.Equal([12, 20], world.Maxima);
    }

    [Fact]
    public void Santa_profile_is_named_empty_without_consuming_name_rng()
    {
        var main = new SequenceRandom();
        var world = new SequenceRandom();
        var resolver = new VanillaTownNpcIdentityResolver1458(mainRandom: main, worldGenRandom: world);

        VanillaTownNpcSpawnIdentity1458 identity = resolver.Resolve(VanillaNpcIds.SantaClaus, shimmeredTownNpc: false);

        Assert.Equal(string.Empty, identity.GivenName);
        Assert.Equal(0, identity.VariationIndex);
        Assert.Empty(main.Maxima);
        Assert.Empty(world.Maxima);
    }

    [Fact]
    public void Arrival_packet_keeps_localization_tree_and_npc_travel_color()
    {
        Assert.True(TerrariaTownNpcArrivalCodec1458.TryEncode(VanillaNpcIds.Merchant.Value, "Barney", out byte[] bytes));
        Assert.True(TerrariaPacket.TryDeserializePayload(
            bytes[2],
            bytes.AsMemory(TerrariaPacket.PacketHeaderLength),
            out TerrariaPacket packet));
        LoadNetModule load = Assert.IsType<LoadNetModule>(packet);
        NetTextModule module = Assert.IsType<NetTextModule>(load.LoadedModule);

        Assert.Equal(NetTextModulePayloadKind.ServerChatMessage, module.PayloadKind);
        Assert.Equal(byte.MaxValue, module.AuthorId);
        Assert.Equal(50, module.MessageColor.R);
        Assert.Equal(125, module.MessageColor.G);
        Assert.Equal(255, module.MessageColor.B);

        NetworkText arrival = Assert.IsType<NetworkText>(module.ServerText);
        Assert.Equal((byte)NetworkText.Mode.LocalizationKey, arrival.TextMode);
        Assert.Equal("Announcement.HasArrived", arrival.Text);
        NetworkText fullName = Assert.Single(arrival.SubstitutionList);
        Assert.Equal((byte)NetworkText.Mode.LocalizationKey, fullName.TextMode);
        Assert.Equal("Game.NPCTitle", fullName.Text);
        Assert.Equal(2, fullName.SubstitutionList.Length);
        Assert.Equal((byte)NetworkText.Mode.Literal, fullName.SubstitutionList[0].TextMode);
        Assert.Equal("Barney", fullName.SubstitutionList[0].Text);
        Assert.Equal((byte)NetworkText.Mode.LocalizationKey, fullName.SubstitutionList[1].TextMode);
        Assert.Equal("NPCName.Merchant", fullName.SubstitutionList[1].Text);
    }

    [Fact]
    public void Nameless_town_npc_arrival_uses_type_name_without_title_wrapper()
    {
        Assert.True(TerrariaTownNpcArrivalCodec1458.TryEncode(VanillaNpcIds.SantaClaus.Value, string.Empty, out byte[] bytes));
        Assert.True(TerrariaPacket.TryDeserializePayload(
            bytes[2],
            bytes.AsMemory(TerrariaPacket.PacketHeaderLength),
            out TerrariaPacket packet));
        NetTextModule module = Assert.IsType<NetTextModule>(Assert.IsType<LoadNetModule>(packet).LoadedModule);
        NetworkText arrival = Assert.IsType<NetworkText>(module.ServerText);
        NetworkText typeName = Assert.Single(arrival.SubstitutionList);
        Assert.Equal("NPCName.SantaClaus", typeName.Text);
        Assert.Empty(typeName.SubstitutionList);
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaTownNpcRandom1458
    {
        private readonly Queue<int> values = new(values);
        public List<int> Maxima { get; } = [];

        public int Next(int exclusiveMax)
        {
            Maxima.Add(exclusiveMax);
            Assert.NotEmpty(values);
            int value = values.Dequeue();
            Assert.InRange(value, 0, exclusiveMax - 1);
            return value;
        }
    }
}
