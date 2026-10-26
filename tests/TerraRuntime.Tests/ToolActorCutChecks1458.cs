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
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.Tools1458.ActorRuntime;

internal static partial class ToolActorBoundaryChecks1458
{
    private static void ActorCutConsumers()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = Read("AllToolActorCuts" + platform + "First1458");
            using var repeat = Read("AllToolActorCuts" + platform + "Repeat1458");
            Assert.True(JsonElement.DeepEquals(source.RootElement, repeat.RootElement));
            using var terrain = Read("AllToolActorTerrain" + platform + "First1458");
            using var launches = Read("AllToolsExtension" + platform + "First1458");
            var rows = source.RootElement.EnumerateArray().Where(row => row.GetProperty("role").GetInt32() == 2 &&
                row.GetProperty("target").GetString() == "plants").ToArray();
            Assert.Equal(24, rows.Length);
            foreach (var row in rows)
            {
                int item = row.GetProperty("item").GetInt32();
                using var f = new ToolActorRuntimeFixture1458(platform == "Windows"); var actor = f.Birth(Launch(launches.RootElement, item));
                var air = terrain.RootElement.EnumerateArray().Single(value => value.GetProperty("item").GetInt32() == item && value.GetProperty("mode").GetString() == "air");
                ConfigureSourceBefore(f, f.Member, ref actor, air);
                var body = row.GetProperty("beforeState");
                AlignCutBody(f, ref actor, body, air.GetProperty("steps")[0].GetProperty("state"));
                foreach (var cell in row.GetProperty("before").EnumerateArray())
                {
                    var tile = new WorldTile { Type = (ushort)cell.GetProperty("type").GetInt32(),
                        Wall = (ushort)cell.GetProperty("wall").GetInt32(), FrameX = (short)cell.GetProperty("frameX").GetInt32(),
                        FrameY = (short)cell.GetProperty("frameY").GetInt32(),
                        Flags = (cell.GetProperty("header").GetInt32() & 32) != 0 ? WorldTileFlags.Active : 0 };
                    f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), tile);
                }
                var before = f.InventoryImage(); f.ReadTileFrames(); int observed = 0;
                f.Sink.BeforeReplication = (kind, state) =>
                {
                    if (state.Handle != actor.Handle || kind != ProjectileStateCommitKind.Update) return;
                    observed++; SameCutCells(f, row); SameInventory(before, f.InventoryImage());
                };
                Assert.True(f.Authority.TryTickState()); Assert.Equal(1, observed); SameCutCells(f, row);
                // Grass/flower scene has no drops and a fixed framing draw count.
                // Compose its independently captured count with the independent
                // fullUpdate AI prefix; do not reuse the implementation's RNG offer.
                var expected = new VanillaUnifiedRandom1458(0);
                RestoreRandom(air.GetProperty("steps")[0].GetProperty("random"), expected);
                int draws = row.GetProperty("afterRandom").GetProperty("cursor").GetInt32();
                for (int draw = 0; draw < draws; draw++) expected.Next();
                Assert.True(f.Random.HasSameState(expected));
                var frames = f.ReadTileFrames().Where(frame => frame.MessageId == 17).ToArray();
                var golden = row.GetProperty("frames").EnumerateArray().Select(value => Convert.FromHexString(value.GetString()!))
                    .Where(bytes => bytes[2] == 17).ToArray();
                Assert.Equal(golden.Length, frames.Length);
                var phaseBeforeEcho = f.Member.ItemPhase; long acceptedEchoes = f.Composition.WorldTileAuthority.AcceptedProjectileExplosionEchoes;
                for (int index = 0; index < frames.Length; index++)
                {
                    Assert.Equal(golden[index], frames[index].Packet.ToArray());
                    Assert.Equal(TerrariaTileManipulationDecodeResult.Decoded,
                        TerrariaTileManipulationCodec.TryDecode(frames[index], out var state));
                    f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, state));
                }
                Assert.Equal(acceptedEchoes + frames.Length, f.Composition.WorldTileAuthority.AcceptedProjectileExplosionEchoes);
                Assert.Equal(phaseBeforeEcho, f.Member.ItemPhase); SameInventory(before, f.InventoryImage());
            }
            NatureAndLaserPositives(launches.RootElement, platform == "Windows");
        }
    }

    private static void AlignCutBody(ToolActorRuntimeFixture1458 f, ref ProjectileSnapshot actor,
        JsonElement body, JsonElement airAfter)
    {
        float x = Single(body, "position", "x"), y = Single(body, "position", "y");
        if (actor.Type.Value == 1052)
        {
            Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life)); Assert.NotNull(life.ThrownAxe);
            var update = new ProjectileStateUpdate(actor.Type, actor.Spawner,
                x - actor.VelocityX, y - actor.VelocityY, actor.VelocityX, actor.VelocityY,
                actor.Ai, actor.BannerIdToRespondTo, actor.Damage, actor.KnockBack, actor.OriginalDamage);
            life = life with { ThrownAxe = life.ThrownAxe!.Value with { X = update.PositionX, Y = update.PositionY } };
            Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
            Assert.True(setup.TryStageMotion(in update, in life)); Assert.True(setup.TryAdoptUnpublished(out actor, out _));
        }
        else
        {
            var m = f.Member; int width = body.GetProperty("width").GetInt32(), height = body.GetProperty("height").GetInt32();
            float halfX = actor.Type.Value == 445 ? width / 2f : width / 2;
            float halfY = actor.Type.Value == 445 ? height / 2f : height / 2;
            float vx = Single(airAfter, "velocity", "x"), vy = Single(airAfter, "velocity", "y");
            m.PositionX = x - 10 + halfX - vx; m.PositionY = y - 21 + halfY - vy;
            for (int n = 0; n < 4 && (m.PositionX + 10 - halfX) + vx < x; n++) m.PositionX = MathF.BitIncrement(m.PositionX);
            m.MarkRemotePhaseSnapshot();
        }
    }

    private static void SameCutCells(ToolActorRuntimeFixture1458 f, JsonElement row)
    {
        var after = row.GetProperty("after").EnumerateArray().ToDictionary(cell =>
            (cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32()));
        foreach (var cell in row.GetProperty("before").EnumerateArray())
        {
            int x = cell.GetProperty("x").GetInt32(), y = cell.GetProperty("y").GetInt32(); var tile = f.Tiles.Get(x, y);
            bool exists = after.TryGetValue((x, y), out var expected);
            Assert.Equal(exists && (expected.GetProperty("header").GetInt32() & 32) != 0, tile.IsActive);
            Assert.Equal(exists ? expected.GetProperty("type").GetInt32() : 0, tile.Type);
        }
    }

    private static void NatureAndLaserPositives(JsonElement launches, bool windows)
    {
        foreach (int item in new[] { 383, 2798 })
            foreach (bool nature in new[] { false, true })
            {
                using var f = new ToolActorRuntimeFixture1458(windows); var actor = f.Birth(Launch(launches, item));
                if (nature) f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 1, 1, 0, 5309, 0)));
                f.Member.MarkRemotePhaseSnapshot();
                Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
                // Configured positive inside both source physical footprints.
                // The original445 direct scene placed its flowers OUTSIDE10x10;
                // that preserved negative is checked above, never relabeled.
                int x = (int)(f.Member.PositionX + 10) / 16, y = (int)(f.Member.PositionY + 21) / 16;
                f.Tiles.Set(x, y, new WorldTile { Type = 3, Flags = WorldTileFlags.Active });
                f.Tiles.Set(x, y + 1, new WorldTile { Type = 2, Flags = WorldTileFlags.Active });
                var stopped = new ProjectileStateUpdate(actor.Type, actor.Spawner, actor.PositionX, actor.PositionY,
                    0, 0, actor.Ai, actor.BannerIdToRespondTo, actor.Damage, actor.KnockBack, actor.OriginalDamage);
                if (life.HeldTool is { } held) life = life with { HeldTool = held with { VelocityX = 0, VelocityY = 0 } };
                Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
                Assert.True(setup.TryStageMotion(in stopped, in life)); Assert.True(setup.TryAdoptUnpublished(out actor, out _));
                Assert.True(f.Authority.TryTickState()); Assert.Equal(nature, f.Tiles.Get(x, y).IsActive);
                if (nature)
                {
                    // Source IgnoreDontHurtNature explicitly keeps weed32 eligible;
                    // preservation is not a blanket refusal of projectile cuts.
                    f.Tiles.Set(x, y, new WorldTile { Type = 32, Flags = WorldTileFlags.Active });
                    Assert.True(f.Authority.TryTickState()); Assert.False(f.Tiles.Get(x, y).IsActive);
                }
            }
    }
}
