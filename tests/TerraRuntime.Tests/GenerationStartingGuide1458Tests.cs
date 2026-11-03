using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationStartingGuide1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        // Original 1.4.5.8 NPC.NewNPC under the actual en-US localization and town
        // profile owners, then the ordinary Guide pass's home/homeless assignments.
        // Two independently reset repetitions preserve every canonical field and physical RNG slot.
        (int Seed, string Name, string Random, int Next)[] seeds =
        [
            (1, "f190d2ae997ec531a16e422d424eb756af1249ff4b88f5f29ab191b59fd6397a", "0725825db72b4bb39fff303c64cc34437c5449590f5ad64d333ab26b4e33a246", 1002897798),
            (42, "8b49a459569b664937d931dab01b9497d8bd5c7d7d414f6602a5c8aceb700165", "0f732e0975a2350d033cc9c7bd8539baf97cda9d9a20072541475419500a507f", 269548474),
            (1458, "a819b11c8fd86da6174dcc08f12310897fe45d052e62c55ae451fbe88855ab8b", "9ad56d18db03b97e4cb55d774c11587bc365bfe3edf12f9e968259ec01be8aa3", 1916656655),
            (8675309, "5c5db120cb11bee138ff3143edcbedaead684de7a0ba140e12287d436c5dc487", "69644a510c40fb8df9563422f3dcd4b7cfc9c7a27811ef45cf5ec41f18f4027e", 1229874138)
        ];
        foreach (var seed in seeds)
            foreach (int x in new[] { 10, 24, 35 })
                foreach (int y in new[] { 25, 100, 301 })
                    yield return [seed.Seed, x, y, seed.Name, seed.Random, seed.Next];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Ordinary_guide_uses_original_bottom_anchor_identity_and_shared_rng(
        int seed, int x, int y, string expectedName, string expectedRandom, int expectedNext)
    {
        var workspace = new Workspace(48, 500);
        Assert.True(workspace.TrySetSpawn(x, y));
        var random = new RandomAdapter(seed);
        StartingGuidePass1458.Instance.Execute(new Context(workspace, random));
        WorldTownNpc npc = Assert.Single(workspace.CaptureGeneratedNpcs().TownNpcs);
        Assert.Equal(22, npc.NetId);
        Assert.Equal(x * 16f - 9f, npc.X);
        Assert.Equal(y * 16f - 40f, npc.Y);
        Assert.Equal((x, y), (npc.HomeTileX, npc.HomeTileY));
        Assert.True(npc.Homeless);
        Assert.False(npc.HomelessDespawn);
        Assert.Equal(0, npc.TownNpcVariationIndex);
        Assert.Equal(expectedName, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(npc.GivenName))));
        Assert.Equal(expectedRandom, random.Digest());
        Assert.Equal(expectedNext, random.Next());
    }

    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "Guide", 1458, 48, 500);
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => TestContext.Current.CancellationToken;
        public void ReportProgress(double fraction, string? message = null) { }
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        internal string Digest()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            uint cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!;
            int[] slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
            byte[] bytes = new byte[4 + slots.Length * 4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, cursor);
            for (int i = 0; i < slots.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + i * 4), slots[i]);
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        public int Next() => random.Next(); public int Next(int n) => random.Next(n);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
