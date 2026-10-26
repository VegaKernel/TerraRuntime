using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

internal static partial class ToolActorBoundaryChecks1458
{
    // Proposed continuation of the SAME existing grouped actor Fact. Original
    // binaries/four captures are numeric fixtures; no game assembly is loaded.
    private static void WorldBombSourceRows()
    {
        int scenarios = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        foreach (string corpus in new[] { "Bombs", "BombTail", "BombFuse" })
        {
            using var source = Read("AllToolWorld" + corpus + platform + "First1458");
            using var repeat = Read("AllToolWorld" + corpus + platform + "Repeat1458");
            Assert.True(JsonElement.DeepEquals(source.RootElement, repeat.RootElement));
            foreach (var row in source.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("npcProjectile").GetBoolean()) continue;
                // SourceNPC1 direct Damage positive is included; type75's local
                // player Hurt is a separate source-role fixture/participant.
                using var f = new ToolActorRuntimeFixture1458(platform == "Windows");
                f.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
                for (int x = 0; x < 100; x++) for (int y = 0; y < 100; y++) f.Tiles.Set(x, y, default);
                foreach (var cell in row.GetProperty("beforeCells").EnumerateArray())
                    f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), FallingCell(cell));
                var npcs = WorldBombNpcs(f.Composition.Npcs);
                NpcHandle target = default;
                if (row.TryGetProperty("targetActive", out var active) && active.GetBoolean())
                {
                    var state = new NpcStateUpdate(1, 1, 420, 420, 0, 0, byte.MaxValue, default,
                        NpcSimulationState.Initial with { Life = 5000, LifeMax = 5000, LifeRegenCounter = 0,
                            Immortal = false, MoneyValue = 0, ExtraMoneyValue = 0, DefenseOverride = 20 });
                    Assert.True(npcs.TrySpawn(0, in state, out var npc)); target = npc.Handle;
                }
                RestoreRandom(row.GetProperty("beforeRandom"), f.Random);
                var before = row.GetProperty("initial");
                var initial = new ProjectileStateUpdate(new(before.GetProperty("type").GetInt32()), byte.MaxValue,
                    WorldBombSingle(before, "position", "x"), WorldBombSingle(before, "position", "y"),
                    WorldBombSingle(before, "velocity", "x"), WorldBombSingle(before, "velocity", "y"),
                    new(WorldBombFloat(before.GetProperty("ai")[0]), WorldBombFloat(before.GetProperty("ai")[1]),
                        WorldBombFloat(before.GetProperty("ai")[2])), 0,
                    checked((short)before.GetProperty("damage").GetInt32()),
                    WorldBombFloat(before.GetProperty("knockBack")), checked((short)before.GetProperty("originalDamage").GetInt32()));
                var world = f.Composition.WorldTileAuthority;
                var overlay = new ToolWorldTileOverlay1458(f.Tiles);
                Assert.True(world.TryCreateToolMutationPlan(overlay, out var birth)); Assert.NotNull(birth);
                ProjectileSnapshot actor;
                using (birth!)
                {
                    Assert.True(birth.TryStageSourceProjectile(f.Shots, in initial, true, out actor));
                    Assert.True(birth.TrySeal()); Assert.True(birth.TryAdoptWholeUnpublished(out var publication));
                    Assert.NotNull(publication); Assert.True(publication!.TryPublishThrough(birth.CapturePublicationOffset()));
                }
                Assert.True(f.Shots.IsCombatTrusted(actor.Handle));
                Assert.False(f.Shots.TryGetCombatTrustedOwner(actor.Handle, out _));
                Assert.False(f.Shots.TryGetServerNpcSource(actor.Handle, out _));
                Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life)); Assert.True(life.NpcProjectile);
                if (corpus == "BombFuse") Assert.Equal(before.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
                life = life with { TimeLeft = before.GetProperty("timeLeft").GetInt32(),
                    LocalAi = new(WorldBombFloat(before.GetProperty("localAI")[0]), WorldBombFloat(before.GetProperty("localAI")[1]), WorldBombFloat(before.GetProperty("localAI")[2])) };
                Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
                Assert.True(setup!.TryStageMotion(in initial, in life));
                Assert.True(setup.TryAdoptUnpublished(out actor, out bool setupEnded)); Assert.False(setupEnded);
                f.Sink.Events.Clear();
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    Assert.True(f.Authority.TryTickState(f.Composition.Npcs.ProjectileNpcCombat));
                    var expected = phase.GetProperty("projectile");
                    bool alive = expected.GetProperty("active").GetBoolean();
                    Assert.Equal(alive, f.Shots.TryGet(actor.Handle, out var actual));
                    if (alive)
                    {
                        SameFallingBody(expected, in actual);
                        Assert.Equal(expected.GetProperty("knockBack").GetInt32(), BitConverter.SingleToInt32Bits(actual.KnockBack));
                        Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var actualLife));
                        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), actualLife.TimeLeft);
                        Assert.Equal(expected.GetProperty("wet").GetBoolean(), actualLife.Liquid.Wet);
                        Assert.Equal(expected.GetProperty("localAI").EnumerateArray().Select(value => value.GetInt32()),
                            new[] { BitConverter.SingleToInt32Bits(actualLife.LocalAi.Ai0), BitConverter.SingleToInt32Bits(actualLife.LocalAi.Ai1), BitConverter.SingleToInt32Bits(actualLife.LocalAi.Ai2) });
                    }
                    SameRandom(phase.GetProperty("random"), f.Random);
                    Assert.Equal(phase.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                    SameFallingCells(f, phase.GetProperty("cells")); SameWorldBombDrops(f, phase.GetProperty("drops"));
                    if (target.IsAssigned)
                    {
                        Assert.True(npcs.TryGet(target, out var npc));
                        var expectedNpc = phase.GetProperty("npc");
                        Assert.Equal(expectedNpc.GetProperty("life").GetInt32(), npc.Simulation.Life);
                        Assert.Equal(WorldBombWord(expectedNpc.GetProperty("velocity").GetProperty("x")), BitConverter.SingleToInt32Bits(npc.VelocityX));
                        Assert.Equal(WorldBombWord(expectedNpc.GetProperty("velocity").GetProperty("y")), BitConverter.SingleToInt32Bits(npc.VelocityY));
                    }
                }
                Assert.Single(f.Sink.Events, entry => entry.Kind == ProjectileStateCommitKind.Despawn);
                var destroy = row.GetProperty("frames").EnumerateArray().Select(value => DecodeFallingFrame(Convert.FromHexString(value.GetString()!))).Single(frame => frame.MessageId == 29);
                // Saved Kill entry origin, not the later common inactive tail.
                var payload = destroy.Payload.ToArray().AsSpan();
                var ended = f.Sink.Events.Single(entry => entry.Kind == ProjectileStateCommitKind.Despawn).State;
                Assert.Equal(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4)), BitConverter.SingleToInt32Bits(ended.PositionX));
                Assert.Equal(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8, 4)), BitConverter.SingleToInt32Bits(ended.PositionY));
                var completed = f.Random.Clone(); Assert.True(f.Authority.TryTickState(f.Composition.Npcs.ProjectileNpcCombat));
                Assert.True(f.Random.HasSameState(completed));
                scenarios++;
            }
        }
        Assert.Equal(32, scenarios);
        WorldBombPreparedTokenCurrentness();
    }

    private static int WorldBombWord(JsonElement value) => value.TryGetInt32(out int signed) ? signed : unchecked((int)value.GetUInt32());
    private static float WorldBombFloat(JsonElement value) => BitConverter.Int32BitsToSingle(WorldBombWord(value));
    private static float WorldBombSingle(JsonElement row, string vector, string lane) => WorldBombFloat(row.GetProperty(vector).GetProperty(lane));
    private static void SameWorldBombDrops(ToolActorRuntimeFixture1458 f, JsonElement expected)
    {
        var actual = new WorldItemSnapshot[401]; int count = f.Items.CopyActive(actual);
        Assert.Equal(expected.GetArrayLength(), count);
        for (int index = 0; index < count; index++)
        {
            var value = expected[index]; var item = actual[index];
            Assert.Equal(value.GetProperty("type").GetInt32(), item.ItemNetId);
            Assert.Equal(value.GetProperty("stack").GetInt32(), item.Stack);
            Assert.Equal(WorldBombWord(value.GetProperty("position").GetProperty("x")), BitConverter.SingleToInt32Bits(item.PositionX));
            Assert.Equal(WorldBombWord(value.GetProperty("position").GetProperty("y")), BitConverter.SingleToInt32Bits(item.PositionY));
            Assert.Equal(WorldBombWord(value.GetProperty("velocity").GetProperty("x")), BitConverter.SingleToInt32Bits(item.VelocityX));
            Assert.Equal(WorldBombWord(value.GetProperty("velocity").GetProperty("y")), BitConverter.SingleToInt32Bits(item.VelocityY));
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "npcs")]
    private static extern ref RuntimeNpcStore WorldBombNpcs(NpcAuthority authority);
}
