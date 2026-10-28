using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OwnedBowPlayerUpdateSource1458Tests
{
    [Fact]
    public void Real_owned_bow_repeated_payment_preserves_original_full_update_random_and_clocks()
    {
        // Fresh pinned original Player.Update(0), dedicated/headless local role,
        // no gear/buffs, explicit fall baseline and empty ordinary scene. These
        // full physical state hashes are observations, not helper-produced draws.
        // Original EXE SHA256: 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
        foreach (var (seed, hashes) in new[]
        {
            (14, new[] { "d72b05b7fa71c3f49f27ee855e5692e4659a837f8d6808efd4a90cad9d32137b", "0b1df034e5f8528a9651a31171bc09e91fb077bfe616b3ae6ddb14e7bc7b591d", "caec4cc9cb71e6f4bcf9914d979fbd06841661a377c17e35525b8a5e02e49b88", "d76403d833824be1dc392d28a081ce64116b284b04febd1f364a28717d321cc8" }),
            (1458, new[] { "e19439cfa799a62e6dabc6152e579f93ed96286dfecdd16e0ac5bcc2d76c3e47", "dbb60bce78d42f92c2d69c84660a8ce2553126c15ee31ea8af23ea752a8fc6c2", "1dad04627de79de7e91993c2b0693fc6a7e73037e28979bb68be96d18296b232", "979b88c6fe943845582c56bdff7c1a8e7b06e51132c75afd34d0045c00cb33db" })
        })
        {
            using var fixture = new LocalWeaponClockRuntimeChecks1458.Fixture(false, selectInitially: false);
            fixture.Equip(0, 39, 1); fixture.Equip(54, 40, 4); fixture.Fund(54, 4);
            fixture.PlayerRandom.SetSeed(seed);
            int hash = 0;
            for (int step = 0; step < 65; step++)
            {
                bool held = step is >= 1 and < 3 or >= 32 and < 34;
                fixture.Control(0, held); fixture.State.Tick();
                Assert.NotNull(fixture.Member.ItemPhase);
                Assert.True(fixture.ToolClock.LocalClocksKnown);
                int paid = step >= 32 ? 2 : step >= 1 ? 1 : 0;
                Assert.Equal(4 - paid, fixture.Item(54).Stack);
                Assert.Equal(4 - paid, fixture.Funded(54));
                Assert.Equal(paid, fixture.ToolClock.WeaponShootHistory?.Uses.Length ?? 0);
                int sinceUse = step >= 32 ? step - 32 : step - 1;
                Assert.Equal(step == 0 ? 0 : Math.Max(0, 29 - sinceUse), fixture.ToolClock.Clocks.Selected.Animation);
                Assert.Equal(step == 0 ? 0 : Math.Max(0, 30 - sinceUse), fixture.ToolClock.Clocks.Selected.ItemTime);
                if (step is 0 or 1 or 32 or 64) Assert.Equal(hashes[hash++], Hash(fixture.PlayerRandom));
            }
        }
    }

    [Fact]
    public void Unrepresented_local_break_contacts_retire_before_payment_or_random_adoption()
    {
        foreach (ushort type in new ushort[] { 162, 481, 482, 483 })
        {
            using var fixture = new LocalWeaponClockRuntimeChecks1458.Fixture(false, selectInitially: false);
            fixture.Equip(0, 39, 1); fixture.Equip(54, 40, 4); fixture.Fund(54, 4);
            fixture.Tiles.Set(47, 54, new WorldTile { Type = type, Flags = WorldTileFlags.Active });
            var before = fixture.PlayerRandom.Clone();
            fixture.State.Apply(new PlayerMovementRuntimeCommand(fixture.Connection,
                new PlayerMovementCommitRequest(fixture.Player.Slot, 32, 16, 0, 64, 0, 736f, 822f,
                    false, 0f, 0f, false, 0, false, 0, 0, 0, 0, false, 0, 0))
                { ItemUseCaller = RuntimeItemUseCaller1458.LocalOwnerProjection });
            fixture.State.Tick();
            Assert.True(fixture.Member.ItemPhase is null, $"type={type}, caller={fixture.Member.ItemUseCaller}, vx={fixture.Member.VelocityX}, tile={fixture.Tiles.Get(47,54).Type}");
            Assert.Equal(4, fixture.Item(54).Stack);
            Assert.Equal(4, fixture.Funded(54));
            Assert.True(fixture.PlayerRandom.HasSameState(before));
        }
    }

    private static string Hash(VanillaUnifiedRandom1458 random)
    {
        Span<byte> bytes = stackalloc byte[228];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Cursor(random));
        for (int i = 0; i < 56; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes[(4 + i * 4)..], Slots(random)[i]);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Slots(VanillaUnifiedRandom1458 random);
}
