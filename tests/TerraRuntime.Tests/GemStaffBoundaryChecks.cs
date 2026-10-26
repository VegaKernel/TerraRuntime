using Xunit;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;
public static class GemStaffBoundaryChecks
{
    private static JsonDocument Read(string name)
    {
        using var stream = typeof(GemStaffBoundaryChecks).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing original source resource: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    public static void Run()
    {
        SourceBirths();
        SourceEpochs();
        StaffRefillCustody();
        PendingTwins();
        SecurityAndCallbacks();
        GenerationAndProvider();
        RefillOrders();
        ActiveNoItems();
    }

    public static void FreshCrowdControl()
    {
        using var source = Read("GemStaffBehaviorLinux1458");
        var rows = source.RootElement.EnumerateArray().Where(row =>
            row.GetProperty("local").GetBoolean() && row.GetProperty("mode").GetString() == "ordinary" &&
            row.GetProperty("robe").GetInt32() == 0)
            .GroupBy(row => row.GetProperty("id").GetInt32()).Select(group => group.First()).ToArray();
        Assert.Equal(7, rows.Length);
        int cases = 0;
        foreach (var row in rows)
        foreach (int buff in new[] { 20, 23, 47, 149, 156 })
        foreach (bool paidBeforeContact in new[] { false, true })
        {
            using var fixture = new GemStaffFixture();
            var connection = fixture.Connections[2];
            var member = fixture.Member(2);
            fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            fixture.Tick();
            fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                new(connection.Player.Slot, 0, 1, 0, (short)row.GetProperty("id").GetInt32(), 0)));
            fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            fixture.Apply(new PlayerManaRuntimeCommand(connection, new(connection.Player.Slot, 200, 200)));
            fixture.Move(2, false);
            fixture.Tick();
            fixture.Move(2, true);
            if (paidBeforeContact) fixture.Tick();
            var contact = fixture.Buffs(2).Clone();
            Assert.True(contact.TryApplyNpcContactBuff(new(buff), 60));
            Profiles(fixture.Players).RestoreBuffState(connection, contact);
            Assert.True(fixture.Players.TryGetInventoryItem(connection, 0, out var selected));
            var phase = member.ItemPhase;
            var mana = member.Mana;
            var random = UpdateRandom(fixture.Players)!.Clone();

            bool allowed = fixture.Players.TryPrepareGemStaffPayment(connection, selected,
                out _, out _, out _, out _);

            Assert.Equal(paidBeforeContact || buff == 20, allowed);
            Assert.Equal(mana, member.Mana);
            Assert.Equal(phase, member.ItemPhase);
            Assert.True(UpdateRandom(fixture.Players)!.HasSameState(random));
            Assert.Equal(60, fixture.Buffs(2).GetDuration(new(buff)));
            cases++;
        }
        Assert.Equal(70, cases);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectileRandom")]
    private static extern ref VanillaUnifiedRandom1458 ProjectileRandom(ProjectileAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Seeds(VanillaUnifiedRandom1458 owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 owner);
    private static void Restore(VanillaUnifiedRandom1458 target, JsonElement state)
    {
        state.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo(Seeds(target), 0);
        Cursor(target) = state.GetProperty("cursor").GetUInt32();
    }

    private static PlayerSelectedConsumableState1458 SourceClocks(JsonElement s) => new(s.GetProperty("itemTime").GetInt32(),
        s.GetProperty("itemTimeMax").GetInt32(),
        s.GetProperty("itemAnimation").GetInt32(),
        s.GetProperty("itemAnimationMax").GetInt32(),
        s.GetProperty("releaseUseItem").GetBoolean(),
        s.GetProperty("potionDelay").GetInt32(),
        s.GetProperty("revolverCritChanceBonus").GetInt32());
    public static void SourceEpochs()
    {
        int cases = 0, midpoints = 0, repeats = 0;
        foreach (string platform in new[]
        {
            "Linux",
            "Windows"
        }

        )
        {
            using var source = Read("GemStaffBehavior" + platform + "1458");
            foreach (var row in source.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("local").GetBoolean() || row.GetProperty("mode").GetString() != "ordinary" || row.GetProperty("robe").GetInt32() != 0)
                    continue;
                using var f = new GemStaffFixture();
                var c = f.Connections[2];
                var m = f.Member(2);
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                f.Tick();
                short staff = (short)row.GetProperty("id").GetInt32();
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, staff, 0)));
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, 200, 200)));
                f.Move(2, false);
                f.Tick();
                f.Move(2, true);
                m.ItemPhase = m.ItemPhase!.Value with
                {
                    Selected = SourceClocks(row.GetProperty("before")),
                    ToolUse=m.ItemPhase.Value.ToolUse! with {Clocks=m.ItemPhase.Value.ToolUse!.Clocks with
                        {Selected=SourceClocks(row.GetProperty("before")),ToolTime=row.GetProperty("before").GetProperty("toolTime").GetInt32()}}
                };
                m.ItemAnimation = 0;
                var authority = Composition(f.Runtime).Projectiles;
                var beforeRandom = row.GetProperty("beforeRandom");
                int bornCount = 0;
                ulong attempt = 0;
                int expectedComposedCrit=SourceClocks(row.GetProperty("before")).RevolverCritBonus;
                int expectedRawCrit=expectedComposedCrit;
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    Restore(UpdateRandom(f.Players)!, beforeRandom);
                    // The numeric row begins at ItemCheck; the runtime also
                    // executes its preceding UpdateManaRegen. Compose that
                    // separately source-qualified prefix instead of pretending
                    // both entry boundaries have the same common draw.
                    var prefix=new VanillaUnifiedRandom1458(0);Restore(prefix,beforeRandom);
                    var beforeOwned=m.ItemPhase!.Value;
                    Assert.True(VanillaSelectedConsumable1458.TryStepHeat(beforeOwned.ManaHeat,prefix.NextDouble,prefix.Next,out _));
                    var manaFacts=new PlayerManaRegenerationFacts1458(m.VelocityX,m.VelocityY,false,false,false,0,0,0);
                    Assert.True(VanillaRemoteManaRegeneration1458.TryStep(beforeOwned.Mana with {Mana=m.Mana,Maximum=200},in manaFacts,out var afterRegen,out _));
                    if(beforeOwned.ToolUse is {LocalClocksKnown:true} local && local.Clocks.Selected.Animation==0 && local.Clocks.ReuseDelay==0 &&
                        m.Mana<afterRegen.Maximum && afterRegen.Mana>=afterRegen.Maximum)
                        for(int particle=0;particle<5;particle++)prefix.Next(20,26);
                    if(prefix.Next(0,3)==0)expectedComposedCrit-=2;
                    var rawCommon=new VanillaUnifiedRandom1458(0);Restore(rawCommon,beforeRandom);
                    if(rawCommon.Next(0,3)==0)expectedRawCrit-=2;
                    Assert.Equal(expectedRawCrit,SourceClocks(phase.GetProperty("state")).RevolverCritBonus);
                    f.Tick();
                    Assert.True(UpdateRandom(f.Players)!.HasSameState(prefix));
                    Assert.NotNull(m.ItemPhase);
                    Assert.NotNull(m.ItemPhase!.Value.MagicPayment);
                    var payment = m.ItemPhase.Value.MagicPayment!.Value;
                    Assert.Equal(SourceClocks(phase.GetProperty("state")) with {RevolverCritBonus=expectedComposedCrit}, payment.GemClocks);
                    Assert.False(m.ItemPhase.Value.Channel);
                    if (attempt > 0 && payment.Attempt > attempt)
                        repeats++;
                    attempt = payment.Attempt;
                    var checkpoint = new VanillaUnifiedRandom1458(0);
                    Restore(checkpoint, beforeRandom);
                    checkpoint.Next(0, 3);
                    ProjectileRandom(authority).CopyStateFrom(checkpoint);
                    short paidMana = m.Mana;
                    int newCount = phase.GetProperty("born").GetArrayLength();
                    for (int index = bornCount; index < newCount; index++)
                    {
                        if (payment.MidpointEligible && !payment.MidpointConsumed)
                            midpoints++;
                        var born = phase.GetProperty("born")[index];
                        var pos = born.GetProperty("position");
                        var vel = born.GetProperty("velocity");
                        var packet = new TerrariaProjectileUpdateState(new(c.Player.Slot.Value,
                            (ushort)(600 + index),
                            1),
                            (short)born.GetProperty("type").GetInt32(),
                            pos.GetProperty("x").GetSingle() + m.PositionX - 400f,
                            pos.GetProperty("y").GetSingle() + m.PositionY - 400f,
                            vel.GetProperty("x").GetSingle(),
                            vel.GetProperty("y").GetSingle(),
                            BitConverter.Int32BitsToSingle(born.GetProperty("aiBits")[0].GetInt32()),
                            born.GetProperty("ai")[1].GetSingle(),
                            0,
                            0,
                            (short)born.GetProperty("damage").GetInt32(),
                            born.GetProperty("knockBack").GetSingle(),
                            0);
                        f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
                    }

                    Assert.Equal(newCount, authority.PromotedClientProjectileSpawns);
                    Assert.Equal(paidMana, m.Mana);
                    bornCount = newCount;
                    beforeRandom = phase.GetProperty("random");
                    Assert.Equal(beforeRandom.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), Seeds(ProjectileRandom(authority)));
                    Assert.Equal(phase.GetProperty("next").GetInt32(), ProjectileRandom(authority).Clone().Next());
                    cases++;
                }
            }
        }

        Assert.Equal(630, cases);
        Assert.True(midpoints > 0);
        Assert.True(repeats > 0);
        Console.WriteLine($"SOURCE_EPOCHS phases={cases} midpointReports={midpoints} repeatPayments={repeats}");
    }

    public static void ActiveNoItems()
    {
        int cases = 0, midpoints = 0, repeats = 0;
        foreach (string platform in new[]
        {
            "Linux",
            "Windows"
        }

        )
        {
            using var source = Read("GemStaffActiveNoItems" + platform + "1458");
            foreach (var row in source.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("local").GetBoolean() || row.GetProperty("mode").GetString() is not ("active-clear" or "active-buff23"))
                    continue;
                using var f = new GemStaffFixture();
                var c = f.Connections[2];
                var m = f.Member(2);
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                f.Tick();
                short staff = (short)row.GetProperty("id").GetInt32();
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, staff, 0)));
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, (short)row.GetProperty("robe").GetInt32(), 0)));
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, 200, 200)));
                f.Move(2, false);
                f.Tick();
                f.Move(2, true);
                m.ItemPhase = m.ItemPhase!.Value with
                {
                    Selected = SourceClocks(row.GetProperty("before"))
                };
                m.ItemAnimation = 0;
                var authority = Composition(f.Runtime).Projectiles;
                var beforeRandom = row.GetProperty("beforeRandom");
                int bornCount = 0;
                ulong attempt = 0;
                int phaseIndex = 0;
                var beforeImage = CaptureImage(f, c);
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    if (phaseIndex == 1 && row.GetProperty("mode").GetString() == "active-buff23")
                        f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new BuffTypeId[] { new(23) })));
                    Restore(UpdateRandom(f.Players)!, beforeRandom);
                    f.Tick();
                    Assert.NotNull(m.ItemPhase);
                    Assert.NotNull(m.ItemPhase!.Value.MagicPayment);
                    var payment = m.ItemPhase.Value.MagicPayment!.Value;
                    Assert.Equal(SourceClocks(phase.GetProperty("state")), payment.GemClocks);
                    Assert.False(m.ItemPhase.Value.Channel);
                    if (attempt > 0 && payment.Attempt > attempt)
                        repeats++;
                    attempt = payment.Attempt;
                    var checkpoint = new VanillaUnifiedRandom1458(0);
                    Restore(checkpoint, beforeRandom);
                    checkpoint.Next(0, 3);
                    ProjectileRandom(authority).CopyStateFrom(checkpoint);
                    short paidMana = m.Mana;
                    int newCount = phase.GetProperty("born").GetArrayLength();
                    for (int index = bornCount; index < newCount; index++)
                    {
                        if (payment.MidpointEligible && !payment.MidpointConsumed)
                            midpoints++;
                        var born = phase.GetProperty("born")[index];
                        var pos = born.GetProperty("position");
                        var vel = born.GetProperty("velocity");
                        var packet = new TerrariaProjectileUpdateState(new(c.Player.Slot.Value,
                            (ushort)(600 + index),
                            1),
                            (short)born.GetProperty("type").GetInt32(),
                            pos.GetProperty("x").GetSingle() + m.PositionX - 400f,
                            pos.GetProperty("y").GetSingle() + m.PositionY - 400f,
                            vel.GetProperty("x").GetSingle(),
                            vel.GetProperty("y").GetSingle(),
                            BitConverter.Int32BitsToSingle(born.GetProperty("aiBits")[0].GetInt32()),
                            born.GetProperty("ai")[1].GetSingle(),
                            0,
                            0,
                            (short)born.GetProperty("damage").GetInt32(),
                            born.GetProperty("knockBack").GetSingle(),
                            0);
                        f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
                    }

                    Assert.Equal(newCount, authority.PromotedClientProjectileSpawns);
                    Assert.Equal(paidMana, m.Mana);
                    bornCount = newCount;
                    beforeRandom = phase.GetProperty("random");
                    Assert.Equal(beforeRandom.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), Seeds(ProjectileRandom(authority)));
                    Assert.Equal(phase.GetProperty("next").GetInt32(), ProjectileRandom(authority).Clone().Next());
                    SameImage(beforeImage, CaptureImage(f, c));
                    phaseIndex++;
                    cases++;
                }
            }
        }

        Assert.Equal(180, cases);
        Assert.Equal(6, midpoints);
        Assert.Equal(2, repeats);
        Console.WriteLine($"ACTIVE_NOITEMS phases={cases} midpointReports={midpoints} repeatPayments={repeats}");
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "projectiles")]
    private static extern ref RuntimeProjectileStore ProjectileStore(ProjectileAuthority authority);
    private static RuntimePlayerInventoryStore.ShopJournalCapture CaptureImage(GemStaffFixture f, ConnectionHandle c)
    {
        Assert.True(Inventory(f.Players).TryCaptureShopJournal(c, out var captured));
        Assert.NotNull(captured);
        return captured!;
    }

    private static void SameImage(RuntimePlayerInventoryStore.ShopJournalCapture before, RuntimePlayerInventoryStore.ShopJournalCapture after)
    {
        Assert.Equal(219, before.Before.Length);
        Assert.Equal(before.Before, after.Before);
        Assert.Equal(before.Quantity, after.Quantity);
        Assert.Equal(before.Copper, after.Copper);
        Assert.Equal(before.Caps, after.Caps);
    }

    private static TerrariaProjectileUpdateState SourcePacket(GemStaffFixture f, JsonElement born, ushort identity)
    {
        var c = f.Connections[2];
        var m = f.Member(2);
        var pos = born.GetProperty("position");
        var vel = born.GetProperty("velocity");
        return new(new(c.Player.Slot.Value,
            identity,
            1),
            (short)born.GetProperty("type").GetInt32(),
            pos.GetProperty("x").GetSingle() + m.PositionX - 400f,
            pos.GetProperty("y").GetSingle() + m.PositionY - 400f,
            vel.GetProperty("x").GetSingle(),
            vel.GetProperty("y").GetSingle(),
            BitConverter.Int32BitsToSingle(born.GetProperty("aiBits")[0].GetInt32()),
            born.GetProperty("ai")[1].GetSingle(),
            0,
            0,
            (short)born.GetProperty("damage").GetInt32(),
            born.GetProperty("knockBack").GetSingle(),
            0);
    }

    private static GemStaffFixture ReadyStaff(short staff, short robe = 0, bool flower = false)
    {
        var f = new GemStaffFixture();
        var c = f.Connections[2];
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, staff, 0)));
        if (robe != 0)
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, robe, 0)));
        if (flower)
        {
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 3, 0, 110, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 2, 3, 0, 189, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 62, 1, 0, 555, 0)));
            var q = Inventory(f.Players).CaptureFundedQuantities(c);
            q[1] = q[2] = 1;
            Inventory(f.Players).AdoptFundedQuantities(c, q);
        }

        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, (short)(flower ? 0 : 200), 200)));
        f.Move(2, false);
        f.Tick();
        f.Move(2, true);
        return f;
    }

    public static void StaffRefillCustody()
    {
        using var source = Read("GemStaffBehaviorLinux1458");
        int cases = 0;
        foreach (short staff in new short[]
        {
            739,
            740,
            741,
            742,
            743,
            744,
            3377
        }

        )
            foreach (bool phaseFirst in new[]
            {
                false,
                true
            }

            )
                foreach (short funded in new short[]
                {
                    0,
                    1
                }

                )
                {
                    var row = source.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() &&
                        r.GetProperty("mode").GetString() == "ordinary" &&
                        r.GetProperty("id").GetInt32() == staff &&
                        r.GetProperty("robe").GetInt32() == 0);
                    using var f = ReadyStaff(staff, flower: true);
                    var c = f.Connections[2];
                    var m = f.Member(2);
                    var inv = Inventory(f.Players);
                    var q = inv.CaptureFundedQuantities(c);
                    q[1] = funded;
                    inv.AdoptFundedQuantities(c, q);
                    var image = CaptureImage(f, c);
                    if (phaseFirst)
                        f.Tick();
                    var authority = Composition(f.Runtime).Projectiles;
                    var rng = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
                    rng.Next(0, 3);
                    ProjectileRandom(authority).CopyStateFrom(rng);
                    int count = 0;
                    foreach (var born in row.GetProperty("phases")[0].GetProperty("born").EnumerateArray())
                    {
                        var packet = SourcePacket(f, born, (ushort)(750 + count++));
                        f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
                    }

                    Assert.Equal(count, authority.PromotedClientProjectileSpawns);
                    Assert.False(m.ItemPhase!.Value.Channel);
                    Assert.Equal(funded == 0, m.ItemPhase.Value.SlowMagicUse);
                    var after = CaptureImage(f, c);
                    var expectedImage = image.Before.ToArray();
                    var expectedQ = image.Quantity.ToArray();
                    var expectedCaps = image.Caps.ToArray();
                    if (funded == 1)
                    {
                        expectedImage[1] = expectedImage[1] with
                        {
                            Stack = 2
                        };
                        expectedQ[1] = 0;
                        expectedCaps[6] = 2;
                    }

                    Assert.Equal(expectedImage, after.Before);
                    Assert.Equal(expectedQ, after.Quantity);
                    Assert.Equal(image.Copper, after.Copper);
                    Assert.Equal(expectedCaps, after.Caps);
                    Assert.Equal(funded == 1 ? 300 : 0, f.Buffs(2).GetDuration(new(94)));
                    if (funded == 0)
                        Assert.Equal((short)0, m.Mana);
                    else
                        Assert.True(m.Mana > 0);
                    short paid = m.Mana;
                    if (!phaseFirst)
                        f.Tick();
                    Assert.Equal(paid, m.Mana);
                    SameImage(after, CaptureImage(f, c));
                    cases++;
                }

        Assert.Equal(28, cases);
        Console.WriteLine("STAFF_REFILL_CUSTODY " + cases);
    }

    public static void PendingTwins()
    {
        using var source = Read("GemStaffBehaviorLinux1458");
        var row = source.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() &&
            r.GetProperty("mode").GetString() == "ordinary" &&
            r.GetProperty("id").GetInt32() == 741 &&
            r.GetProperty("robe").GetInt32() == 0);
        var born = row.GetProperty("phases")[0].GetProperty("born");
        foreach (bool phaseFirst in new[]
        {
            false,
            true
        }

        )
            foreach (bool flower in new[]
            {
                false,
                true
            }

            )
            {
                using var f = ReadyStaff(741, flower: flower);
                var c = f.Connections[2];
                var authority = Composition(f.Runtime).Projectiles;
                if (phaseFirst)
                    f.Tick();
                var first = SourcePacket(f, born[0], 730);
                var second = SourcePacket(f, born[1], 731);
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, first));
                Assert.Equal(0, authority.PromotedClientProjectileSpawns);
                f.Tick();
                short paid = f.Member(2).Mana;
                var clocks = f.Member(2).ItemPhase!.Value.MagicPayment!.Value.GemClocks;
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, second));
                Assert.Equal(2, authority.PromotedClientProjectileSpawns);
                Assert.Equal(paid, f.Member(2).Mana);
                Assert.Equal(clocks, f.Member(2).ItemPhase!.Value.MagicPayment!.Value.GemClocks);
                Assert.True(f.Member(2).ItemPhase!.Value.MagicPayment!.Value.BirthConsumed);
                Console.WriteLine($"PENDING_TWINS phaseFirst={phaseFirst} flower={flower} mana={paid}");
                if (flower)
                {
                    Assert.Equal((short)0, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
                    Assert.True(Inventory(f.Players).TryGet(c, 1, out var potion));
                    Assert.Equal((short)2, potion.Stack);
                }
            }

        using (var f = ReadyStaff(741, flower: true))
        {
            var c = f.Connections[2];
            var authority = Composition(f.Runtime).Projectiles;
            var first = SourcePacket(f, born[0], 735);
            var second = SourcePacket(f, born[1], 736);
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, first));
            f.Tick();
            Assert.Equal((short)0, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
            var changed = Inventory(f.Players).CaptureFundedQuantities(c);
            changed[1] = 1;
            Inventory(f.Players).AdoptFundedQuantities(c, changed);
            var image = CaptureImage(f, c);
            short mana = f.Member(2).Mana;
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, second));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mana, f.Member(2).Mana);
            SameImage(image, CaptureImage(f, c));
        }

        using (var f = ReadyStaff(741, flower: true))
        {
            var c = f.Connections[2];
            var authority = Composition(f.Runtime).Projectiles;
            var first = SourcePacket(f, born[0], 740);
            var second = SourcePacket(f, born[1], 741);
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, first));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            var image = CaptureImage(f, c);
            short mana = f.Member(2).Mana;
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, 1283, 0)));
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, second));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mana, f.Member(2).Mana);
            SameImage(image, CaptureImage(f, c));
            Assert.Equal(0, f.Buffs(2).GetDuration(new(94)));
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "tickProvider")]
    private static extern ref Func<long> ProjectileTickProvider(ProjectileAuthority owner);
    public static void GenerationAndProvider()
    {
        using var source = Read("GemStaffBehaviorLinux1458");
        var row = source.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() &&
            r.GetProperty("mode").GetString() == "ordinary" &&
            r.GetProperty("id").GetInt32() == 741 &&
            r.GetProperty("robe").GetInt32() == 0);
        var born = row.GetProperty("phases")[0].GetProperty("born");
        using (var f = ReadyStaff(741))
        {
            var old = f.Connections[2];
            var authority = Composition(f.Runtime).Projectiles;
            var first = SourcePacket(f, born[0], 770);
            var second = SourcePacket(f, born[1], 771);
            f.Apply(new ClientProjectileUpdateRuntimeCommand(old, first));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            f.ReplaceSecond();
            var current = f.Connections[2];
            Assert.NotEqual(old.Player, current.Player);
            var image = CaptureImage(f, current);
            short mana = f.Member(2).Mana;
            f.Apply(new ClientProjectileUpdateRuntimeCommand(old, second));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mana, f.Member(2).Mana);
            SameImage(image, CaptureImage(f, current));
            f.Tick();
            f.Move(2, true);
            first = SourcePacket(f, born[0], 772);
            second = SourcePacket(f, born[1], 773);
            f.Apply(new ClientProjectileUpdateRuntimeCommand(current, first));
            f.Apply(new ClientProjectileUpdateRuntimeCommand(current, second));
            Assert.Equal(2, authority.PromotedClientProjectileSpawns);
        }

        using (var f = ReadyStaff(739, flower: true))
        {
            var c = f.Connections[2];
            var m = f.Member(2);
            var authority = Composition(f.Runtime).Projectiles;
            var simple = source.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() &&
                r.GetProperty("mode").GetString() == "ordinary" &&
                r.GetProperty("id").GetInt32() == 739 &&
                r.GetProperty("robe").GetInt32() == 0);
            var packet = SourcePacket(f, simple.GetProperty("phases")[0].GetProperty("born")[0], 780);
            short mana = m.Mana;
            bool changed = false;
            var original = ProjectileTickProvider(authority);
            ProjectileTickProvider(authority) = () =>
            {
                if (!changed)
                {
                    changed = true;
                    var q = Inventory(f.Players).CaptureFundedQuantities(c);
                    q[1] = 0;
                    Inventory(f.Players).AdoptFundedQuantities(c, q);
                }

                return original();
            };
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
            Assert.True(changed);
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mana, m.Mana);
            Assert.True(Inventory(f.Players).TryGet(c, 1, out var stock));
            Assert.Equal((short)3, stock.Stack);
            Assert.Equal((short)0, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
            Assert.Equal(0, f.Buffs(2).GetDuration(new(94)));
        }

        Console.WriteLine("GENERATION_PROVIDER_GREEN");
    }

    public static void SecurityAndCallbacks()
    {
        using var source = Read("GemStaffBehaviorLinux1458");
        JsonElement Row(int robe) => source.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() &&
            r.GetProperty("mode").GetString() == "ordinary" &&
            r.GetProperty("id").GetInt32() == 739 &&
            r.GetProperty("robe").GetInt32() == robe);
        using (var f = ReadyStaff(739))
        {
            var c = f.Connections[2];
            var m = f.Member(2);
            var authority = Composition(f.Runtime).Projectiles;
            var packet = SourcePacket(f, Row(0).GetProperty("phases")[0].GetProperty("born")[0], 700);
            var image = CaptureImage(f, c);
            short mana = m.Mana;
            var rng = ProjectileRandom(authority).Clone();
            var bad = new[]
            {
                packet with
                {
                    Ai0 = 0
                },
                packet with
                {
                    Ai2 = BitConverter.Int32BitsToSingle(1)
                },
                packet with
                {
                    Ai1 = 1
                },
                packet with
                {
                    Damage = (short)(packet.Damage + 1)
                },
                packet with
                {
                    OriginalDamage = 1
                },
                packet with
                {
                    PositionX = packet.PositionX + 1000
                }
            };
            foreach (var report in bad)
            {
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, report));
                Assert.Equal(0, authority.PromotedClientProjectileSpawns);
                Assert.Equal(mana, m.Mana);
                Assert.True(ProjectileRandom(authority).HasSameState(rng));
                SameImage(image, CaptureImage(f, c));
            }

            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
            Assert.Equal(1, authority.PromotedClientProjectileSpawns);
            mana = m.Mana;
            var paidImage = CaptureImage(f, c);
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet with { Key = new(c.Player.Slot.Value, 701, 1) }));
            Assert.Equal(1, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mana, m.Mana);
            SameImage(paidImage, CaptureImage(f, c));
            Assert.True(f.Replication.WireIdentities.TryResolve(packet.Key, out var handle));
            Assert.True(ProjectileStore(authority).TryGet(handle, out var owned));
            Assert.True(ProjectileStore(authority).TryGetLifecycle(handle, out var lifecycle));
            foreach (var update in new[]
            {
                packet with { PositionX = packet.PositionX + 12f, VelocityX = -packet.VelocityX },
                packet with { Ai0 = 0f }, packet with { Ai2 = 1f },
                packet with { Damage = (short)(packet.Damage + 1) }
            })
            {
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, update));
                Assert.True(ProjectileStore(authority).TryGet(handle, out var afterUpdate));
                Assert.Equal(owned, afterUpdate);
                Assert.True(ProjectileStore(authority).TryGetLifecycle(handle, out var afterLifecycle));
                Assert.Equal(lifecycle, afterLifecycle);
                Assert.Equal(mana, m.Mana);
                SameImage(paidImage, CaptureImage(f, c));
            }
        }

        foreach (bool transfer in new[]
        {
            false,
            true
        }

        )
        {
            using var f = ReadyStaff(739);
            var c = f.Connections[2];
            f.Tick();
            var m = f.Member(2);
            var authority = Composition(f.Runtime).Projectiles;
            var packet = SourcePacket(f, Row(0).GetProperty("phases")[0].GetProperty("born")[0], 710);
            var image = CaptureImage(f, c);
            short mana = m.Mana;
            if (transfer)
            {
                var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
                f.Apply(new PlayerTransferDetachRuntimeCommand(c, detach));
                Assert.NotNull(detach.Task.GetAwaiter().GetResult());
                var attach = new TaskCompletionSource<bool>();
                f.Apply(new PlayerTransferAttachRuntimeCommand(c, detach.Task.GetAwaiter().GetResult()!, 100, 103, true, false, attach));
                Assert.True(attach.Task.GetAwaiter().GetResult());
                Assert.False(f.Member(2).ItemPhase!.Value.MagicPayment!.Value.EligibleBirth);
                Assert.True(f.Member(2).ItemPhase!.Value.MagicPayment!.Value.MidpointConsumed);
                SameImage(image, CaptureImage(f, c));
            }
            else
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, 1283, 0)));
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal(mana, f.Member(2).Mana);
        }

        // Completion observers see both twin generations, one exact funded debit,
        // mana and sickness together; reentry and an exception cannot repay them.
        using (var f = ReadyStaff(739, 1284, flower: true))
        {
            var c = f.Connections[2];
            var m = f.Member(2);
            var authority = Composition(f.Runtime).Projectiles;
            var born = Row(1284).GetProperty("phases")[0].GetProperty("born");
            var first = SourcePacket(f, born[0], 720);
            var second = SourcePacket(f, born[1], 721);
            var peerQueue = new TerrariaConnectionOutboundQueue(new(16, 16384, 4096));
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(99555), new(new(1), new(1)));
            Assert.True(f.Replication.TryRegister(peer.Source, peerQueue));
            f.Replication.PlayerSpawned(peer, new(peer.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
            int callbacks = 0;
            f.Events.OnEquipment = (who, report) =>
            {
                if (who != c || report.SlotId != 1)
                    return;
                callbacks++;
                Assert.Equal(2, authority.PromotedClientProjectileSpawns);
                Assert.Equal((short)0, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
                Assert.True(Inventory(f.Players).TryGet(c, 1, out var remaining));
                Assert.Equal((short)2, remaining.Stack);
                Assert.Equal(300, f.Buffs(2).GetDuration(new(94)));
                Assert.True(m.Mana > 0);
                short paid = m.Mana;
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, first));
                Assert.Equal(paid, m.Mana);
                throw new IOException("configured observer failure");
            };
            f.Apply(new ClientProjectileUpdateRuntimeCommand(c, first));
            Assert.Equal(0, authority.PromotedClientProjectileSpawns);
            Assert.Equal((short)1, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
            Assert.Throws<IOException>(() => f.Apply(new ClientProjectileUpdateRuntimeCommand(c, second)));
            Assert.Equal(1, callbacks);
            Assert.Equal(2, authority.PromotedClientProjectileSpawns);
            Assert.Equal(2, peerQueue.QueuedFrames);
            Assert.Equal((short)0, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
            Assert.Equal((short)1, Inventory(f.Players).CaptureFundedQuantities(c)[2]);
        }

        using (var f = ReadyStaff(739, flower: true))
        {
            var c = f.Connections[2];
            var m = f.Member(2);
            short before = m.Mana;
            bool changed = false;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (!changed)
                {
                    changed = true;
                    m.OwnsManaMaximum = !m.OwnsManaMaximum;
                }

                return new(false, false);
            });
            f.Tick();
            Assert.True(changed);
            Assert.Equal(before, m.Mana);
            Assert.Null(m.ItemPhase);
            Assert.Equal((short)1, Inventory(f.Players).CaptureFundedQuantities(c)[1]);
            Assert.True(Inventory(f.Players).TryGet(c, 1, out var potion));
            Assert.Equal((short)3, potion.Stack);
            Assert.Equal(0, f.Buffs(2).GetDuration(new(94)));
        }

        Console.WriteLine("SECURITY_CALLBACKS_GREEN");
    }

    public static void SourceBirths()
    {
        int cases = 0;
        foreach (string platform in new[]
        {
            "Linux",
            "Windows"
        }

        )
        {
            using var source = Read("GemStaffBehavior" + platform + "1458");
            foreach (var row in source.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("local").GetBoolean() || row.GetProperty("mode").GetString() != "ordinary")
                    continue;
                foreach (bool phaseFirst in new[]
                {
                    false,
                    true
                }

                )
                {
                    using var f = new GemStaffFixture();
                    var c = f.Connections[2];
                    var m = f.Member(2);
                    f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    f.Tick();
                    short staff = (short)row.GetProperty("id").GetInt32();
                    short robe = (short)row.GetProperty("robe").GetInt32();
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, staff, 0)));
                    if (robe != 0)
                        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 60, 1, 0, robe, 0)));
                    f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, 200, 200)));
                    f.Move(2, false);
                    f.Tick();
                    f.Move(2, true);
                    if (phaseFirst)
                        f.Tick();
                    Assert.NotNull(m.ItemPhase);
                    var authority = Composition(f.Runtime).Projectiles;
                    var boundaryRandom = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
                    // Source ItemCheck's crit draw precedes the observed first Shoot.
                    // The launch cursor is explicitly configured at that paid boundary;
                    // server update/player streams are not claimed identical to Main.rand.
                    boundaryRandom.Next(0, 3);
                    ProjectileRandom(authority).CopyStateFrom(boundaryRandom);
                    var first = row.GetProperty("phases")[0];
                    short before = m.Mana;
                    int promoted = 0;
                    var image = CaptureImage(f, c);
                    foreach (var born in first.GetProperty("born").EnumerateArray())
                    {
                        var pos = born.GetProperty("position");
                        var velocity = born.GetProperty("velocity");
                        var packet = new TerrariaProjectileUpdateState(new(c.Player.Slot.Value,
                            (ushort)(920 + promoted),
                            1),
                            (short)born.GetProperty("type").GetInt32(),
                            pos.GetProperty("x").GetSingle() + m.PositionX - 400f,
                            pos.GetProperty("y").GetSingle() + m.PositionY - 400f,
                            velocity.GetProperty("x").GetSingle(),
                            velocity.GetProperty("y").GetSingle(),
                            BitConverter.Int32BitsToSingle(born.GetProperty("aiBits")[0].GetInt32()),
                            born.GetProperty("ai")[1].GetSingle(),
                            0,
                            0,
                            (short)born.GetProperty("damage").GetInt32(),
                            born.GetProperty("knockBack").GetSingle(),
                            0);
                        f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
                        promoted++;
                    }

                    Assert.Equal(promoted, authority.PromotedClientProjectileSpawns);
                    SameImage(image, CaptureImage(f, c));
                    var active = new ProjectileSnapshot[2];
                    Assert.Equal(promoted, ProjectileStore(authority).CopyActive(active));
                    for (int index = 0; index < promoted; index++)
                    {
                        var expected = first.GetProperty("born")[index];
                        var actual = active[index];
                        Assert.True(ProjectileStore(authority).TryGetLifecycle(actual.Handle, out var lifecycle));
                        Assert.True(ProjectileStore(authority).IsCombatTrusted(actual.Handle));
                        Assert.Equal(expected.GetProperty("type").GetInt32(), (int)actual.Type.Value);
                        Assert.Equal(expected.GetProperty("damage").GetInt32(), (int)actual.Damage);
                        Assert.Equal(expected.GetProperty("aiBits")[0].GetInt32(), BitConverter.SingleToInt32Bits(actual.Ai.Ai0));
                        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
                        Assert.Equal(expected.GetProperty("penetrate").GetInt32(), lifecycle.PenetrateOverride);
                        Assert.NotNull(lifecycle.GemStaff);
                        Assert.Equal((GemStaffFeatures1458)row.GetProperty("featureBits").GetInt32(), lifecycle.GemStaffFeatures);
                    }

                    Assert.NotNull(m.ItemPhase!.Value.MagicPayment);
                    var payment = m.ItemPhase.Value.MagicPayment!.Value;
                    Assert.True(payment.GemStaff);
                    Assert.True(payment.BirthConsumed);
                    Assert.False(m.ItemPhase.Value.Channel);
                    Assert.Equal(row.GetProperty("featureBits").GetInt32(), (int)payment.GemFeatures);
                    if (phaseFirst)
                        Assert.Equal(before, m.Mana);
                    else
                        Assert.True(m.Mana < before);
                    Assert.Equal(first.GetProperty("random").GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), Seeds(ProjectileRandom(authority)));
                    Assert.Equal(first.GetProperty("next").GetInt32(), ProjectileRandom(authority).Clone().Next());
                    cases++;
                }
            }
        }

        Assert.Equal(224, cases);
        Console.WriteLine("SOURCE_BIRTHS " + cases);
    }

    public static void RefillOrders()
    {
        foreach (bool phaseFirst in new[]
        {
            false,
            true
        }

        )
            foreach (short funded in new short[]
            {
                0,
                1
            }

            )
            {
                using var f = new GemStaffFixture();
                var c = f.Connections[2];
                var m = f.Member(2);
                var inv = Inventory(f.Players);
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                f.Tick();
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, 113, 0)));
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 3, 0, 110, 0)));
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 2, 3, 0, 189, 0)));
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 62, 1, 0, 555, 0)));
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                short[] q = new short[59];
                q[0] = 1;
                q[1] = funded;
                q[2] = 1;
                inv.AdoptFundedQuantities(c, q);
                f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, 0, 200)));
                f.Move(2, false);
                f.Tick();
                f.Move(2, true);
                Assert.True(m.ItemPhase!.Value.DerivedCombat!.Value.ManaFlower);
                if (phaseFirst)
                    f.Tick();
                var packet = new TerrariaProjectileUpdateState(new(c.Player.Slot.Value, 901, 1), 16, m.PositionX + 10, m.PositionY + 21, 6f, 0, 0, 0, 0, 0, 35, 7.5f, 0);
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
                Assert.Equal(1, Composition(f.Runtime).Projectiles.PromotedClientProjectileSpawns);
                Assert.True(inv.TryGet(c, 1, out var first));
                Assert.Equal((short)(funded == 1 ? 2 : 3), first.Stack);
                Assert.True(inv.TryGet(c, 2, out var later));
                Assert.Equal((short)3, later.Stack);
                Assert.Equal((short)0, inv.CaptureFundedQuantities(c)[1]);
                Assert.Equal((short)1, inv.CaptureFundedQuantities(c)[2]);
                Assert.Equal(funded == 0, m.ItemPhase!.Value.SlowMagicUse);
                Assert.Equal(funded == 0 ? 0 : 300, f.Buffs(2).GetDuration(new(94)));
                short paid = m.Mana;
                if (funded == 0)
                    Assert.Equal((short)0, paid);
                else
                    Assert.InRange(paid, (short)1, (short)49);
                if (!phaseFirst)
                    f.Tick();
                Assert.Equal(paid, m.Mana);
                Assert.True(inv.TryGet(c, 1, out first));
                Assert.Equal((short)(funded == 1 ? 2 : 3), first.Stack);
                Assert.Equal((short)0, inv.CaptureFundedQuantities(c)[1]);
                // Duplicate known wire key cannot create a fresh use or another refill.
                f.Apply(new ClientProjectileUpdateRuntimeCommand(c, packet));
                Assert.Equal(1, Composition(f.Runtime).Projectiles.PromotedClientProjectileSpawns);
                Assert.Equal(paid, m.Mana);
                Assert.True(inv.TryGet(c, 1, out first));
                Assert.Equal((short)(funded == 1 ? 2 : 3), first.Stack);
                Console.WriteLine($"REFILL_ORDER phaseFirst={phaseFirst} funded={funded} physical={first.Stack} q={inv.CaptureFundedQuantities(c)[1]} mana={paid} slow={m.ItemPhase.Value.SlowMagicUse}");
            }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class GemStaffFixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        private readonly ServerRuntimeState state;
        internal ServerRuntimeState Runtime => state;

        private readonly WorldTileStore tiles = new(new WorldDimensions(400, 300));
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly GemStaffEventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal GemStaffFixture()
        {
            for (int x = 0; x < 400; x++)
                tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore(1000, Replication);
            state = new(playerEvents: Events,
                worldTiles: tiles,
                naturalSpawnRandom: new SystemVanillaNpcRandom(new VanillaUnifiedRandom1458(0)),
                projectiles: projectiles,
                projectileReplication: Replication,
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458)with { WorldSurface = 80 },
                playerUpdateRandomSeed: new(0));
            Players = Composition(state).Players;
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false) { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(UpdateRandom(Players)!);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int index = 0; index < 3; index++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }

            // The dedicated local placeholder is source-excluded, not a network actor.
            Membership(Players).Commit(new RuntimePlayerMember { Slot = new(255), Revision = 1, Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255),
                new(1))), ItemPhase = null, PhysicsPhase = null });
            var session = sessions[2];
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(99002), session.Handle);
            Connections.Add(2, c);
            Apply(new PlayerHealthRuntimeCommand(c, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(c, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(c, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(c, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Move(2, false);
        }

        internal RuntimePlayerMember Member(byte slot)
        {
            Assert.True(Players.TryGet(slot, out var member));
            return member;
        }

        internal void Apply(RuntimeCommand command) => state.Apply(command);
        internal void Tick() => state.Tick();
        internal void ReplaceSecond()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[2]));
            sessions[2].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!);
            sessions.Add(session);
            Assert.Equal(new PlayerSlotId(2), session.Slot);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(99302), session.Handle);
            Connections[2] = c;
            Apply(new PlayerHealthRuntimeCommand(c, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(c, new(session.Slot, 200, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(c, new(session.Slot, 0, 1, 0, 741, 0)));
            Apply(new PlayerSpawnRuntimeCommand(c, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            moved.Remove(2);
            Move(2, false);
            Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        }

        internal void Move(byte slot, bool control)
        {
            var m = Member(slot);
            bool first = moved.Add(slot);
            Apply(new PlayerMovementRuntimeCommand(Connections[slot],
                new(new(slot),
                control ? (byte)32 : (byte)0,
                (byte)(m.MovementFlags | 16),
                0,
                64,
                0,
                first ? 1600 + slot * 40 : m.PositionX,
                first ? 1606 : m.PositionY,
                false,
                0,
                0,
                false,
                0,
                false,
                0,
                0,
                0,
                0,
                false,
                0,
                0)));
        }

        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose()
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private sealed class GemStaffEventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r)
        {
            Count++;
            Seen.Add(5);
            OnEquipment?.Invoke(c, r);
        }

        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r)
        {
            Count++;
            Seen.Add(16);
        }

        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r)
        {
            Count++;
            Seen.Add(42);
        }

        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r)
        {
            Count++;
            Seen.Add(50);
        }

        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
