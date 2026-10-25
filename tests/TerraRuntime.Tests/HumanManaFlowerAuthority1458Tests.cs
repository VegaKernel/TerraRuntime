using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
using TerraRuntime.Protocol;
using TerraRuntime.Network;

using Xunit;
namespace TerraRuntime.Tests;

public sealed class HumanManaFlowerAuthority1458Tests
{
    [Fact]
    public void Original_whole_phases_restore_first_potion_before_reduced_payment()
    {
        int count = 0;
        foreach (bool windows in new[] { false, true })
            foreach (int additive in new[] { 0, 1, 2 })
            {
                using var corpus = ReadSource("HumanManaFlowerRemote1458", "66cc10062897d6c359e9b5f45c3cd6e25e1a8c1f07dd5d8878f5c39f9f1d6091");
                var rows = corpus.RootElement.GetProperty(windows ? "windows" : "linux").GetProperty(new[] { "original33", "additional9", "early3" }[additive]);
                foreach (var row in rows.EnumerateArray())
                {
                    using var f = new Harness.Fixture(row, windows);
                    var spec = row.GetProperty("spec");
                    foreach (var initial in row.GetProperty("initialInventory").EnumerateArray())
                        f.Equipment(initial.GetProperty("index").GetInt16(), initial.GetProperty("type").GetInt16(), initial.GetProperty("stack").GetInt16(), initial.GetProperty("prefix").GetByte());
                    f.FundSourceManaPotions();
                    if (spec.TryGetProperty("manaPotionDelay", out var delay))
                        f.Member.ItemPhase = f.Member.ItemPhase!.Value with { ManaPotionDelay = delay.GetInt32() };
                    foreach (var step in row.GetProperty("steps").EnumerateArray())
                    {
                        if (additive == 2) f.PrepareStep(step);
                        else f.Move(step.GetProperty("tick").GetInt32() > 0, 0, spec.GetProperty("lastSuccess").GetBoolean());
                        f.State.Tick();
                        Assert.NotNull(f.Member.ItemPhase);
                        var actual = f.Member.ItemPhase!.Value;
                        var expected = step.GetProperty("player");
                        Assert.Equal(expected.GetProperty("statMana").GetInt32(), (int)f.Member.Mana);
                        Assert.Equal(expected.GetProperty("statManaMax2").GetInt32(), actual.Mana.Maximum);
                        Assert.Equal(expected.GetProperty("manaRegenCount").GetInt32(), actual.Mana.Count);
                        Assert.Equal(expected.GetProperty("manaRegenDelay").GetSingle(), actual.Mana.Delay);
                        Assert.Equal(expected.GetProperty("manaHeat").GetSingle(), actual.ManaHeat);
                        Assert.Equal(expected.GetProperty("manaPotionDelay").GetInt32(), actual.ManaPotionDelay);
                        Assert.Equal(expected.GetProperty("itemAnimation").GetInt32(), actual.Selected.Animation);
                        Assert.Equal(expected.GetProperty("itemAnimationMax").GetInt32(), actual.Selected.AnimationMax);
                        Assert.Equal(expected.GetProperty("itemTime").GetInt32(), actual.Selected.ItemTime);
                        Assert.Equal(expected.GetProperty("itemTimeMax").GetInt32(), actual.Selected.ItemTimeMax);
                        Assert.Equal(expected.GetProperty("releaseUseItem").GetBoolean(), actual.Selected.ReleaseUseItem);
                        Assert.Equal(expected.GetProperty("channel").GetBoolean(), actual.Channel);
                        Assert.Equal(expected.GetProperty("slowMagicUse").GetBoolean(), actual.SlowMagicUse);
                        Assert.Equal(expected.GetProperty("pendingItemReuse").GetBoolean(), actual.PendingItemReuse);
                        Assert.Equal(expected.GetProperty("manaFlower").GetBoolean(), actual.DerivedCombat!.Value.ManaFlower);
                        Assert.Equal(expected.GetProperty("manaCost").GetSingle(), actual.DerivedCombat!.Value.ManaCostMultiplier);
                        var combat = actual.DerivedCombat.Value;
                        Assert.Equal(expected.GetProperty("statDefense").GetInt32(), combat.Defense);
                        Assert.Equal(expected.GetProperty("endurance").GetSingle(), combat.Endurance);
                        Assert.Equal(expected.GetProperty("meleeDamage").GetSingle(), combat.MeleeDamage);
                        Assert.Equal(expected.GetProperty("rangedDamage").GetSingle(), combat.RangedDamage);
                        Assert.Equal(expected.GetProperty("magicDamage").GetSingle(), combat.MagicDamage);
                        Assert.Equal(expected.GetProperty("rangedMultDamage").GetSingle(), combat.RangedMultDamage);
                        Assert.Equal(expected.GetProperty("arrowDamage").GetSingle(), combat.ArrowDamage);
                        Assert.Equal(expected.GetProperty("arrowDamageAdditiveStack").GetSingle(), combat.ArrowDamageAdditiveStack);
                        Assert.Equal(expected.GetProperty("bulletDamage").GetSingle(), combat.BulletDamage);
                        Assert.Equal(expected.GetProperty("meleeCrit").GetInt32(), combat.MeleeCrit);
                        Assert.Equal(expected.GetProperty("rangedCrit").GetInt32(), combat.RangedCrit);
                        Assert.Equal(expected.GetProperty("magicCrit").GetInt32(), combat.MagicCrit);
                        Assert.Equal(expected.GetProperty("meleeSpeed").GetSingle(), combat.MeleeAttackSpeed);
                        Assert.Equal(expected.GetProperty("armorPenetration").GetInt32(), combat.ArmorPenetration);
                        Assert.Equal(expected.GetProperty("meleeArmorPenetration").GetInt32(), combat.MeleeArmorPenetration);
                        Assert.Equal(expected.GetProperty("noKnockback").GetBoolean(), combat.NoKnockback);
                        Assert.Equal(expected.GetProperty("magicQuiver").GetBoolean(), combat.MagicQuiver);
                        Assert.Equal(expected.GetProperty("archery").GetBoolean(), combat.Archery);
                        Assert.Equal(expected.GetProperty("minionDamage").GetSingle(), combat.MinionDamage);
                        Assert.Equal(expected.GetProperty("position").GetProperty("X").GetSingle(), f.Member.PositionX);
                        Assert.Equal(expected.GetProperty("position").GetProperty("Y").GetSingle(), f.Member.PositionY);
                        Assert.Equal(expected.GetProperty("velocity").GetProperty("X").GetSingle(), f.Member.VelocityX);
                        Assert.Equal(expected.GetProperty("velocity").GetProperty("Y").GetSingle(), f.Member.VelocityY);
                        Assert.Equal(expected.GetProperty("statLife").GetInt32(), f.Member.NpcHealth!.Value.Life);
                        Assert.Equal(expected.GetProperty("itemRotation").GetSingle(), f.Member.ItemRotation);
                        Assert.Equal(expected.GetProperty("toolTime").GetInt32(), actual.ToolTime);
                        Assert.Equal(expected.GetProperty("attackCD").GetInt32(), actual.AttackCD);

                        var buffs = f.Profiles.CaptureBuffState(f.Connection)!; buffs.CaptureSlots(out var types, out var durations);
                        Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
                        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()), durations);
                        foreach (var item in step.GetProperty("inventory").EnumerateArray())
                        {
                            Assert.True(f.Players.TryGetInventoryItem(f.Connection, item.GetProperty("index").GetInt16(), out var retained));
                            Assert.Equal(item.GetProperty("type").GetInt32(), retained.ItemType.Value);
                            Assert.Equal(item.GetProperty("stack").GetInt32(), (int)retained.Stack);
                            Assert.Equal(item.GetProperty("prefix").GetInt32(), retained.Prefix.Value);
                        }
                        var rng = step.GetProperty("after");
                        Assert.Equal(rng.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Random)!);
                        Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Random)!);
                        count++;
                    }
                }
            }
        Assert.Equal(180, count);
    }
    [Fact]
    public void Packet_and_phase_orders_publish_trusted_birth_and_preserve_sync_acknowledgements()
    {
        using var phases = ReadSource("HumanManaFlowerRemote1458", "66cc10062897d6c359e9b5f45c3cd6e25e1a8c1f07dd5d8878f5c39f9f1d6091");
        using var local = ReadSource("HumanManaFlowerLocal1458", "553adb2978e8c1ea6df79bd792bf493b6b7625d228b45726dbc82111f06477ab");
        int births = 0;
        foreach (bool windows in new[] { false, true })
            foreach (int weapon in new[] { 113, 218, 495 })
                foreach (int manaCase in new[] { 0, 1, 2, 3 })
                    foreach (short potionType in new short[] { 110, 189, 500, 2209 })
                        foreach (bool phaseFirst in new[] { false, true })
                        {
                            string name = $"{weapon}-flower1-{new[] { "above", "equal", "below", "zero" }[manaCase]}";
                            var phase = phases.RootElement.GetProperty("linux").GetProperty("original33").EnumerateArray().Single(r => r.GetProperty("spec").GetProperty("name").GetString() == name);
                            var original = local.RootElement.GetProperty(windows ? "windows" : "linux").EnumerateArray().Single(r => r.GetProperty("weapon").GetInt32() == weapon && r.GetProperty("potion").GetInt32() == potionType && r.GetProperty("flower").GetBoolean() && r.GetProperty("manaProfile").GetInt32() == manaCase);
                            using var f = new Harness.Fixture(phase, windows);
                            f.Equipment(2, potionType, 3);
                            f.FundSourceManaPotions();
                            f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, original.GetProperty("initialMana").GetInt16(), 200)));
                            f.Move(false, 0, true); f.State.Tick(); f.Move(true, 0, true);
                            if (phaseFirst) f.State.Tick();
                            var shot = original.GetProperty("shots")[0]; var position = shot.GetProperty("position"); var velocity = shot.GetProperty("velocity");
                            var packet = new TerrariaProjectileUpdateState(new(0, 901, 1), shot.GetProperty("type").GetInt32(),
                                position.GetProperty("X").GetSingle() + 1200, position.GetProperty("Y").GetSingle() + 1206,
                                velocity.GetProperty("X").GetSingle(), velocity.GetProperty("Y").GetSingle(), 0, 0, 0, 0,
                                shot.GetProperty("damage").GetInt16(), shot.GetProperty("knockBack").GetSingle(), 0);
                            f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet));
                            Assert.Equal(1, f.Projectiles.ActiveCount);
                            var live = new ProjectileSnapshot[f.Projectiles.Capacity]; Assert.Equal(1, f.Projectiles.CopyActive(live));
                            Assert.True(f.Projectiles.IsCombatTrusted(live[0].Handle)); Assert.Equal(packet.Damage, live[0].Damage);
                            Assert.Equal(packet.ProjectileType, live[0].Type.Value);
                            Assert.Equal(packet.PositionX, live[0].PositionX); Assert.Equal(packet.PositionY, live[0].PositionY);
                            Assert.Equal(packet.VelocityX, live[0].VelocityX); Assert.Equal(packet.VelocityY, live[0].VelocityY);
                            Assert.Equal(packet.KnockBack, live[0].KnockBack);
                            Assert.Equal(new ProjectileAiState(shot.GetProperty("ai")[0].GetSingle(), shot.GetProperty("ai")[1].GetSingle(), shot.GetProperty("ai")[2].GetSingle()), live[0].Ai);

                            int paid = original.GetProperty("afterPayment").GetProperty("statMana").GetInt32();
                            Assert.Equal(paid, (int)f.Member.Mana);
                            int remaining = original.GetProperty("afterInventory")[2].GetProperty("stack").GetInt32();
                            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var potion)); Assert.Equal(remaining, (int)potion.Stack);
                            int duration = f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94));
                            if (original.GetProperty("initialMana").GetInt32() < original.GetProperty("cost").GetInt32()) Assert.Equal(300, duration);
                            var before = f.Member.ItemPhase!.Value.Mana;
                            // Source post-Shoot5/42/50 acknowledge the producer already adopted.
                            f.Equipment(2, potionType, (short)remaining, 0, 1);
                            f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, (short)paid, 200)));
                            var types = f.Profiles.CaptureBuffState(f.Connection)!.CaptureTypes();
                            f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, types)));
                            Assert.Equal(before, f.Member.ItemPhase!.Value.Mana);
                            Assert.Equal(duration, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94)));
                            f.State.Apply(new PlayerItemAnimationRuntimeCommand(f.Connection, 0, (short)(weapon == 113 ? 22 : weapon == 218 ? 30 : 25)));
                            f.State.Tick(); Assert.Equal(paid, (int)f.Member.Mana);
                            f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet with { Key = new(0, 902, 1) }));
                            Assert.Equal(1, f.Projectiles.ActiveCount); Assert.Equal(paid, (int)f.Member.Mana);
                            if (duration > 0)
                            {
                                // An arbitrary50 cannot erase inventory quantity custody.
                                f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, Array.Empty<BuffTypeId>())));
                                var member = f.Member.CaptureSnapshot(); var random = f.Random.Clone();
                                f.Equipment(2, potionType, 3);
                                Assert.Equal(member, f.Member.CaptureSnapshot()); Assert.True(f.Random.HasSameState(random));
                                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out potion)); Assert.Equal(remaining, (int)potion.Stack);
                            }
                            births++;
                        }
        Assert.Equal(192, births);
    }


    [Fact]
    public async Task Owned_potion_quantity_and_indexed_buff_acknowledgement_survive_attempt_retirement_and_transfer()
    {
        using var phases = ReadSource("HumanManaFlowerRemote1458", "66cc10062897d6c359e9b5f45c3cd6e25e1a8c1f07dd5d8878f5c39f9f1d6091");
        var phase = phases.RootElement.GetProperty("linux").GetProperty("original33").EnumerateArray().Single(r => r.GetProperty("spec").GetProperty("name").GetString() == "113-flower1-zero");
        foreach (short potion in new short[] { 110, 189, 500, 2209 })
            foreach (int lifecycle in new[] { 0, 1, 2 })
            {
                using var f = new Harness.Fixture(phase, false);
                f.Equipment(2, potion, 3); f.FundSourceManaPotions(); f.Move(false, 0, true); f.State.Tick(); f.Move(true, 0, true); f.State.Tick();
                Assert.Equal(300, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94)));
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var after)); Assert.Equal(2, (int)after.Stack);
                if (lifecycle == 0)
                {
                    f.Move(false, 0, true);
                    for (int i = 0; i < 31; i++) f.State.Tick();
                    f.Move(true, 0, true); f.State.Tick();
                    Assert.Null(f.Member.ItemPhase!.Value.MagicPayment!.Value.Refill);
                }
                else if (lifecycle == 1)
                {
                    // Genuine unsupported selected identity retires whole-phase custody.
                    f.Equipment(0, 5, 1); f.State.Tick(); Assert.Null(f.Member.ItemPhase);
                }
                else
                {
                    var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
                    f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach));
                    Assert.NotNull(await detach.Task);
                    var attach = new TaskCompletionSource<bool>();
                    f.Connection = new(GameCommandSourceId.FromConnection(99811), new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)));
                    f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, (await detach.Task)!, 100, 103, true, false, attach));
                    Assert.True(await attach.Task);
                    Assert.Null(f.Member.ItemPhase!.Value.MagicPayment!.Value.Refill);
                }
                var buffs = f.Profiles.CaptureBuffState(f.Connection)!; int duration = buffs.GetDuration(new(94)); Assert.True(duration > 60);
                var compact = buffs.CaptureTypes();
                f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, compact)));
                Assert.Equal(duration, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94)));
                Assert.True(f.Member.OwnsManaMaximum);
                var current = f.Member.CaptureSnapshot();
                f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, f.Member.Mana, f.Member.MaxMana)));
                Assert.Equal(current, f.Member.CaptureSnapshot());
                f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 200, 200)));
                Assert.Equal(current, f.Member.CaptureSnapshot());
                f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, f.Member.Mana, 220)));
                Assert.Equal(current, f.Member.CaptureSnapshot());
                var before = f.Member.CaptureSnapshot(); var rng = f.Random.Clone();
                f.Equipment(2, potion, 3); Assert.Equal(before, f.Member.CaptureSnapshot()); Assert.True(f.Random.HasSameState(rng));
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out after)); Assert.Equal(2, (int)after.Stack);
                // A changed compact report uses the existing60-duration policy. A later exact
                // acknowledgement preserves that60; it never reconstructs the owned300.
                f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, new BuffTypeId[] { new(94), new(93) })));
                Assert.Equal(60, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94)));
                f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, new BuffTypeId[] { new(94), new(93) })));
                Assert.Equal(60, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94)));
            }

        foreach (bool mutateBuffDuration in new[] { false, true })
        {
            using var f = new Harness.Fixture(phase, false);
            f.Equipment(2, 110, 3); f.FundSourceManaPotions(); f.Move(false, 0, true); f.State.Tick(); f.Move(true, 0, true);
            var bt = new BuffTypeId[44]; var times = new int[44]; bt[0] = new(93); times[0] = 60;
            f.Profiles.RestoreBuffState(f.Connection, PlayerBuffState.FromSlots(bt, times));
            var replication = new RuntimeProjectileReplicationRegistry();
            var store = new RuntimeProjectileStore(commitSink: replication); bool once = true;
            var authority = new ProjectileAuthority(store, f.Players, new RuntimeNpcStore(), new RuntimePlayerSnapshotLookup(f.Players, null), null, replication, () =>
            {
                if (once)
                {
                    once = false;
                    if (mutateBuffDuration) { times[0] = 61; f.Profiles.RestoreBuffState(f.Connection, PlayerBuffState.FromSlots(bt, times)); }
                    else f.Equipment(2, 110, 2);
                }
                return 0;
            });
            var before = f.Member.ItemPhase; int mana = f.Member.Mana; var random = f.Random.Clone();
            var packet = new TerrariaProjectileUpdateState(new(0, 993, 1), 16, 1594, 1611, 6, 0, 0, 0, 0, 0, 35, 7.5f, 0);
            Assert.True(authority.TryApply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet)));
            Assert.False(once); Assert.Equal(0, store.ActiveCount); Assert.Equal(mana, (int)f.Member.Mana); Assert.Equal(before, f.Member.ItemPhase);
            Assert.Equal(0, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94))); Assert.True(f.Random.HasSameState(random));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var item)); Assert.Equal(mutateBuffDuration ? 3 : 2, (int)item.Stack);
            Assert.Equal(mutateBuffDuration ? 61 : 60, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(93)));
        }
        using (var failed = new Harness.Fixture(phase, false))
        {
            failed.Equipment(2, 110, 3); failed.FundSourceManaPotions(); failed.Move(false, 0, false); failed.State.Tick(); failed.Move(true, 0, false); failed.State.Tick();
            Assert.Equal(38, (int)failed.Member.Mana); Assert.Equal(0, failed.Member.ItemPhase!.Value.Selected.Animation);
            Assert.False(failed.Member.ItemPhase.Value.MagicPayment!.Value.EligibleBirth);
            failed.State.Tick(); Assert.Equal(38, (int)failed.Member.Mana);
            failed.Move(false, 0, false); failed.State.Tick(); failed.Move(true, 0, false); failed.State.Tick(); Assert.Equal(26, (int)failed.Member.Mana);
            Assert.Equal((ulong)2, failed.Member.ItemPhase!.Value.MagicPayment!.Value.Attempt);
            Assert.True(failed.Players.TryGetInventoryItem(failed.Connection, 2, out var potion)); Assert.Equal(2, (int)potion.Stack);
        }


        foreach (bool phaseFirst in new[] { false, true })
        {
            var events = new Events(); using var f = new Harness.Fixture(phase, false, events);
            f.Equipment(2, 110, 3); f.FundSourceManaPotions(); f.Move(false, 0, true); f.State.Tick(); f.Move(true, 0, true);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(99812), new(new(1), new(1)));
            var queue = new TerrariaConnectionOutboundQueue(new(64, 65536, 4096));
            Assert.True(f.Replication.TryRegister(peer.Source, queue));
            f.Replication.PlayerSpawned(peer, new(peer.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
            events.Seen.Clear(); events.OnEquipment = () =>
            {
                Assert.Equal(38, (int)f.Member.Mana);
                Assert.Equal(300, f.Profiles.CaptureBuffState(f.Connection)!.GetDuration(new(94)));
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var potion)); Assert.Equal(2, (int)potion.Stack);
                Assert.True(f.Member.ItemPhase!.Value.MagicPayment!.Value.Refill!.Value.IsValid);
                Assert.Equal(phaseFirst ? 0 : 1, f.Projectiles.ActiveCount);
                if (!phaseFirst) { var live = new ProjectileSnapshot[1000]; Assert.Equal(1, f.Projectiles.CopyActive(live)); Assert.True(f.Projectiles.IsCombatTrusted(live[0].Handle)); }
                var before = f.Member.CaptureSnapshot(); f.Equipment(2, 110, 3); Assert.Equal(before, f.Member.CaptureSnapshot());
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var capped)); Assert.Equal(2, (int)capped.Stack);
                throw new InvalidOperationException("observer after adopted QuickMana");
            };
            var failure = Assert.Throws<InvalidOperationException>(() =>
            {
                if (phaseFirst) f.State.Tick();
                else f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, new TerrariaProjectileUpdateState(new(0, 994, 1), 16, 1594, 1611, 6, 0, 0, 0, 0, 0, 35, 7.5f, 0)));
            });
            Assert.Equal("observer after adopted QuickMana", failure.Message);
            Assert.Equal(new[] { 5, 42, 50 }, events.Seen);
            Assert.Equal(phaseFirst ? 0 : 1, queue.QueuedFrames);
            events.OnEquipment = null;
            Assert.Equal(38, (int)f.Member.Mana);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var item)); Assert.Equal(2, (int)item.Stack);
        }


        var reentryEvents = new Events();
        using (var reentered = new Harness.Fixture(phase, false, reentryEvents))
        {
            // The synchronous observer may replace the accepted potion image and report
            // another birth. All custody/cadence owners must already be adopted.
            var events = reentryEvents;
            reentered.Equipment(2, 110, 3); reentered.FundSourceManaPotions(); reentered.Move(false, 0, true); reentered.State.Tick(); reentered.Move(true, 0, true);
            var packet = new TerrariaProjectileUpdateState(new(0, 995, 1), 16, 1594, 1611, 6, 0, 0, 0, 0, 0, 35, 7.5f, 0);
            events.Seen.Clear(); events.OnEquipment = () =>
            {
                events.OnEquipment = null;
                Assert.Equal(38, (int)reentered.Member.Mana); Assert.Equal(1, reentered.Projectiles.ActiveCount);
                reentered.Equipment(2, 110, 1);
                reentered.State.Apply(new ClientProjectileUpdateRuntimeCommand(reentered.Connection, packet with { Key = new(0, 996, 1) }));
                Assert.Equal(1, reentered.Projectiles.ActiveCount); Assert.Equal(38, (int)reentered.Member.Mana);
            };
            reentered.State.Apply(new ClientProjectileUpdateRuntimeCommand(reentered.Connection, packet));
            Assert.Equal(new[] { 5, 5, 42, 50 }, events.Seen);
            Assert.True(reentered.Players.TryGetInventoryItem(reentered.Connection, 2, out var item)); Assert.Equal(1, (int)item.Stack);
            Assert.Equal(300, reentered.Profiles.CaptureBuffState(reentered.Connection)!.GetDuration(new(94)));
        }


        var newerManaEvents = new Events();
        using (var newerMana = new Harness.Fixture(phase, false, newerManaEvents))
        {
            newerMana.Equipment(2, 110, 3); newerMana.FundSourceManaPotions(); newerMana.Move(false, 0, true); newerMana.State.Tick(); newerMana.Move(true, 0, true);
            newerManaEvents.Seen.Clear(); newerManaEvents.OnEquipment = () =>
            {
                newerManaEvents.OnEquipment = null;
                // Adversarial trusted-owner tuple replacement, not an accepted client42
                // or a claimed original regeneration amount. The receipt remains identical.
                newerMana.Member.Mana = 39;
                newerMana.Member.ItemPhase = newerMana.Member.ItemPhase!.Value with
                { Mana = newerMana.Member.ItemPhase.Value.Mana with { Mana = 39 } };
            };
            newerMana.State.Apply(new ClientProjectileUpdateRuntimeCommand(newerMana.Connection,
                new TerrariaProjectileUpdateState(new(0, 997, 1), 16, 1594, 1611, 6, 0, 0, 0, 0, 0, 35, 7.5f, 0)));
            Assert.Equal(39, (int)newerMana.Member.Mana);
            Assert.Equal(38, newerMana.Member.ItemPhase!.Value.MagicPayment!.Value.PaidMana);
            Assert.Equal(new[] { 5, 50 }, newerManaEvents.Seen);
        }
    }


    private static JsonDocument ReadSource(string logicalName, string expectedSha256)
    {
        using var compressed = typeof(HumanManaFlowerAuthority1458Tests).Assembly.GetManifestResourceStream(logicalName);
        Assert.NotNull(compressed);
        using var gzip = new GZipStream(compressed!, CompressionMode.Decompress);
        using var data = new MemoryStream(); gzip.CopyTo(data);
        Assert.Equal(expectedSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data.ToArray())).ToLowerInvariant());
        data.Position = 0; return JsonDocument.Parse(data);
    }

    internal sealed class Events : IRuntimePlayerEventSink
    {
        internal Action? OnEquipment;
        internal readonly List<int> Seen = new();
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Seen.Add(5); OnEquipment?.Invoke(); }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) { Seen.Add(42); }
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) { Seen.Add(50); }
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) { }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) { }
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) { }
        public void PlayerDisconnected(ConnectionHandle c) { }
    }

    internal sealed class Harness
    {
        private static int I(JsonElement e, string n) => e.GetProperty(n).GetInt32();
        private static float S(JsonElement e, string n) => e.GetProperty(n).GetSingle();
        internal sealed class Fixture : IDisposable
        {
            private readonly PlayerJoinSession session;
            private readonly string branch;
            private readonly JsonElement spec;
            private readonly PlayerSlotPool pool = new(3);
            private PlayerSlotPool.PlayerSlotLease? gap;
            private PlayerJoinSession? secondSession;
            internal ConnectionHandle SecondConnection;
            internal RuntimePlayerMember SecondMember { get { Assert.True(Players.TryGet(SecondConnection, out var member)); return member; } }

            internal readonly ServerRuntimeState State;
            internal readonly PlayerAuthority Players;
            internal ConnectionHandle Connection;
            internal readonly VanillaUnifiedRandom1458 Random;
            internal readonly RuntimeProjectileReplicationRegistry Replication = new();
            internal readonly RuntimeProjectileStore Projectiles;
            internal RuntimePlayerMember Member
            {
                get { Assert.True(Players.TryGet(Connection, out var member)); return member; }
            }
            internal RuntimePlayerTransferProfileStore Profiles =>
                (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                    .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;

            internal Fixture(JsonElement row, bool windows, IRuntimePlayerEventSink? playerEvents = null)
            {
                Projectiles = new(commitSink: Replication);
                var tiles = new WorldTileStore(new WorldDimensions(400, 300));
                for (int x = 0; x < 400; x++)
                    tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                State = new(worldTiles: tiles, projectiles: Projectiles, projectileReplication: Replication, playerUpdateRandomSeed: new(0), playerEvents: playerEvents);
                Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState)
                    .GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
                Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority)
                    .GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
                spec = row.GetProperty("spec");
                branch = spec.GetProperty("branch").GetString()!;
                Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
                { WindowsItemPrefixArithmetic = windows });
                Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
                Assert.True(pool.TryAcquireConnection(out var lease));
                session = new(lease!);
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                Connection = new(GameCommandSourceId.FromConnection(99809), session.Handle);
                State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
                State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, (short)I(spec, "currentMana"), (short)I(spec, "baseMana"))));
                State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(false, 0, false, 1600, 1606);
                int mode = I(spec, "mode");
                bool good = spec.GetProperty("good").GetBoolean();
                Players.SetCombatEquipmentWorldModes(mode > 0 || good, mode >= 2 || mode == 1 && good);
                byte heart = spec.GetProperty("heart").GetBoolean() ? (byte)4 : (byte)0;
                State.Apply(new PlayerAppearanceRuntimeCommand(Connection,
                    new(Connection.Player.Slot, 0, 0, 0f, 0, "Defensive", 0, 0, 0,
                        default, default, default, default, default, default, default, heart, 0, 0)));
                State.Apply(new PlayerManaRuntimeCommand(Connection, new(Connection.Player.Slot, (short)I(spec, "currentMana"), (short)I(spec, "baseMana"))));
                Equipment(0, (short)I(spec, "weapon"), (short)1,
                    (byte)I(spec, "prefix"));
                Equipment(1, (short)I(spec, "weapon"), 1);
                Equipment(54, 0, 0);
                foreach (var entry in spec.GetProperty("armor").EnumerateArray())
                {
                    int loadout = I(entry, "loadout");
                    short slot = checked((short)((loadout < 0 ? VanillaPlayerItemSlotCatalog.ArmorStart :
                        VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride) + I(entry, "slot")));
                    Equipment(slot, (short)I(entry, "type"), 1, (byte)I(entry, "prefix"),
                        entry.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
                }
                var initial = row.GetProperty("initial");
                var types = initial.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray();
                var times = initial.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Profiles.RestoreBuffState(Connection, PlayerBuffState.FromSlots(types, times));
                Member.ItemPhase = Member.ItemPhase!.Value with { ManaHeat = S(initial, "manaHeat"), Mana = Member.ItemPhase.Value.Mana with { Count = I(spec, "count"), Delay = S(spec, "delay") } };
                if (spec.TryGetProperty("mixed", out var mixed) && mixed.GetBoolean())
                {
                    Assert.True(pool.TryAcquireConnection(out gap)); Assert.True(pool.TryAcquireConnection(out var secondLease));
                    secondSession = new(secondLease!); secondSession.ObserveWorldRequest(); secondSession.ObserveSectionRequest();
                    SecondConnection = new(GameCommandSourceId.FromConnection(99810), secondSession.Handle);
                    State.Apply(new PlayerHealthRuntimeCommand(SecondConnection, new(secondSession.Slot, 400, 400)));
                    State.Apply(new PlayerManaRuntimeCommand(SecondConnection, new(secondSession.Slot, 10, 200)));
                    State.Apply(new PlayerSpawnRuntimeCommand(SecondConnection, secondSession, new(secondSession.Slot, 100, 103, 0, 0, 0, 0, 0)));
                    MoveSecond(false, false); State.Apply(new PlayerEquipmentRuntimeCommand(SecondConnection, new(SecondConnection.Player.Slot, 0, 1, 0, 98, 0)));
                    State.Apply(new PlayerEquipmentRuntimeCommand(SecondConnection, new(SecondConnection.Player.Slot, 54, 50, 0, 97, 0)));
                }
            }

            // Trusted source-fixture setup only; raw reports and observer reentry never call this.
            internal void FundSourceManaPotions()
            {
                var store=(RuntimePlayerInventoryStore)typeof(PlayerAuthority)
                    .GetField("inventory",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Players)!;
                var quantity=store.CaptureFundedQuantities(Connection);
                for(short slot=0;slot<58;slot++)
                    if(store.TryGet(Connection,slot,out var item)&&item.ItemType.Value is 110 or 189 or 500 or 2209)
                        quantity[slot]=item.Stack;
                store.AdoptFundedQuantities(Connection,quantity);
            }

            internal void Equipment(short slot, short type, short stack, byte prefix = 0, byte flags = 0) =>
                State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
                    new(Connection.Player.Slot, slot, stack, prefix, type, flags)));

            internal void Move(bool use, byte selected, bool success, float? x = null, float? y = null) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
                new(Connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, success ? (byte)64 : (byte)0, selected,
                    x ?? Member.PositionX, y ?? Member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

            private void MoveSecond(bool use, bool success) => State.Apply(new PlayerMovementRuntimeCommand(SecondConnection, new(SecondConnection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, success ? (byte)64 : (byte)0, 0, 1700, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            internal void PrepareStep(JsonElement step)
            {
                int tick = I(step, "tick");
                if (tick == 1 && branch != "living")
                {
                    if (branch == "dead")
                    {
                        Member.IsDead = true;
                        Member.NpcHealth = Member.NpcHealth!.Value with { Life = 0 };
                        Member.ItemPhase = Member.ItemPhase!.Value with { DeadTime = 17, RespawnTimer = 500 };
                    }
                    if (branch == "ghost") Member.MovementFlags |= 64;
                    if (branch == "outside") { Member.PositionX = 0; Member.PositionY = 0; }
                }
                if (spec.GetProperty("name").GetString() == "reported-add-remove" && (tick == 8 || tick == 16))
                {
                    var bt = new BuffTypeId[44]; var duration = new int[44];
                    if (tick == 8) { bt[0] = new(16); bt[1] = new(117); duration[0] = duration[1] = 60; }
                    Profiles.RestoreBuffState(Connection, PlayerBuffState.FromSlots(bt, duration));
                }
                if (secondSession is not null) MoveSecond(tick > 0 && tick != 5, step.GetProperty("beforeSecond").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
                bool use = branch == "living" && tick > 0 && tick != 5;
                Move(use, (byte)(tick >= 3 && branch == "living" ? 1 : 0),
                    step.GetProperty("beforePlayer").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
                if (branch == "ghost" && tick > 0) Member.MovementFlags |= 64;
            }

            public void Dispose() { session.Dispose(); secondSession?.Dispose(); gap?.Dispose(); }
        }
    }

}
