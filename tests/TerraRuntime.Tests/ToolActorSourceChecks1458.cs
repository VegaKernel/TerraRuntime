using Xunit;
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
    internal static void Run()
    {
        SourceActorRows();
        SourceLocalAxeCatchRows();
        ActorCutConsumers();
        RuntimeBoundaries();
        FallingSourceRows();
        WorldBombSourceRows();
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(ToolActorBoundaryChecks1458).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing original tool actor fixture: " + name);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void SourceActorRows()
    {
        int scenarios = 0, composedCatches = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var terrain = Read("AllToolActorTerrain" + platform + "First1458");
            using var repeat = Read("AllToolActorTerrain" + platform + "Repeat1458");
            Assert.True(JsonElement.DeepEquals(terrain.RootElement, repeat.RootElement));
            using var launches = Read("AllToolsExtension" + platform + "First1458");
            foreach (var row in terrain.RootElement.EnumerateArray())
            {
                int item = row.GetProperty("item").GetInt32();
                var launch = Launch(launches.RootElement, item);
                using var f = new ToolActorRuntimeFixture1458(platform == "Windows");
                var actor = f.Birth(launch);
                var member = f.Member;
                // Independent original rows configure a phase beforeimage, not a
                // wholeMain run. Birth above is real Apply/Tick/strict27; these
                // owned test setup writes deliberately align the original scene.
                ConfigureSourceBefore(f, member, ref actor, row);
                if (row.GetProperty("mode").GetString() == "inactive")
                    f.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
                var image = row.GetProperty("mode").GetString() == "inactive" ? null : f.InventoryImage();
                f.Sink.Events.Clear();
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    Assert.True(f.Shots.TryGet(actor.Handle, out var phaseBefore));
                    bool ownedCatch = step.GetProperty("state").GetProperty("active").GetBoolean() &&
                        QualifiesOwnedSourceCatch(f, in phaseBefore, launches.RootElement);
                    var catchRandom = ownedCatch ? f.Random.Clone() : null;
                    Assert.True(f.Authority.TryTickState());
                    var expected = step.GetProperty("state"); bool alive = expected.GetProperty("active").GetBoolean();
                    if (ownedCatch)
                    {
                        AssertOwnedSourceCatch(f, in phaseBefore, catchRandom!);
                        SameRandom(step.GetProperty("random"), f.Random);
                        if (image is not null) SameInventory(image, f.InventoryImage());
                        composedCatches++;
                        // Original dedicated trace remains immutable. Its later
                        // live steps do not describe the explicitly authorized
                        // local catch. SourceLocalAxeCatchRows replays the actual
                        // fullUpdate LOCAL oracle as a separate connected case.
                        break;
                    }
                    Assert.Equal(alive, f.Shots.TryGet(actor.Handle, out var actual));
                    if (alive)
                    {
                        SameBody(expected, in actual);
                        Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
                        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
                        SameLiquid(expected, life.Liquid);
                        Assert.True(f.Shots.TryGetSourceDirection(actor.Handle.Slot, out int direction));
                        Assert.Equal(expected.GetProperty("direction").GetInt32(), direction);
                        if (life.HeldTool is { } held) SameHeld(expected, in held);
                        else { Assert.NotNull(life.ThrownAxe); SameAxe(expected, life.ThrownAxe!.Value); }
                    }
                    else
                    {
                        var terminal = f.Sink.Events.Last();
                        Assert.True(terminal.Kind is ProjectileStateCommitKind.Remove or ProjectileStateCommitKind.Despawn);
                        SameBody(expected, in terminal.State);
                    }
                    SameRandom(step.GetProperty("random"), f.Random);
                    Assert.Equal(step.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                    if (actor.Type.Value != 1052)
                    {
                        Assert.Equal(2, member.ItemAnimation);
                        if (member.ItemPhase is { } phase)
                        { Assert.Equal(2, phase.Selected.ItemTime); Assert.Equal(3, phase.Selected.ItemTimeMax); }
                    }
                    if (image is not null) SameInventory(image, f.InventoryImage());
                }
                scenarios++;
            }
        }
        Assert.Equal(480, scenarios);
        Assert.Equal(2, composedCatches);
    }

    private static JsonElement Launch(JsonElement rows, int item) => rows.EnumerateArray().Single(row =>
        row.GetProperty("kind").GetString() == "launch" && row.GetProperty("item").GetInt32() == item &&
        row.GetProperty("actualPrefix").GetInt32() == 0 && row.GetProperty("meleeMultiplier").GetSingle() == 1f &&
        !row.GetProperty("left").GetBoolean());

    private static void ConfigureSourceBefore(ToolActorRuntimeFixture1458 f, RuntimePlayerMember member,
        ref ProjectileSnapshot actor, JsonElement row, JsonElement? capturedLiquid = null)
    {
        for (int x = 0; x < 100; x++) for (int y = 0; y < 100; y++) f.Tiles.Set(x, y, default);
        if (row.TryGetProperty("layout", out var sourceLayout))
        foreach (var cell in sourceLayout.EnumerateArray())
        {
            var tile = new WorldTile { Type = (ushort)cell.GetProperty("type").GetInt32(),
                Flags = cell.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : 0,
                LiquidAmount = (byte)cell.GetProperty("liquid").GetInt32(),
                LiquidKind = (WorldLiquidKind)cell.GetProperty("liquidKind").GetInt32() };
            Assert.True(tile.TrySetSourceShape(false, (byte)cell.GetProperty("slope").GetInt32()));
            f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), tile);
        }
        var owner = row.GetProperty("beforeOwner"); var body = row.GetProperty("before");
        member.PositionX = Single(owner, "position", "x"); member.PositionY = Single(owner, "position", "y");
        member.VelocityX = member.VelocityY = 0f; member.ItemAnimation = (short)owner.GetProperty("itemAnimation").GetInt32();
        member.ItemRotation = owner.GetProperty("itemRotation").GetSingle();
        Assert.NotNull(member.ItemPhase); var phase = member.ItemPhase!.Value;
        var clocks = phase.Selected with { Animation = owner.GetProperty("itemAnimation").GetInt32(),
            AnimationMax = owner.GetProperty("itemAnimationMax").GetInt32(),
            ItemTime = owner.GetProperty("itemTime").GetInt32(), ItemTimeMax = owner.GetProperty("itemTimeMax").GetInt32() };
        Assert.NotNull(phase.ToolUse);
        member.ItemPhase = phase with { Selected = clocks, ToolUse = phase.ToolUse! with
            { Clocks = phase.ToolUse!.Clocks with { Selected = clocks, ToolTime = 0, Channel = true } } };
        member.MarkRemotePhaseSnapshot();
        var update = new ProjectileStateUpdate(actor.Type, actor.Spawner,
            Single(body, "position", "x"), Single(body, "position", "y"),
            Single(body, "velocity", "x"), Single(body, "velocity", "y"),
            new(body.GetProperty("ai")[0].GetSingle(), body.GetProperty("ai")[1].GetSingle(), body.GetProperty("ai")[2].GetSingle()),
            0, (short)body.GetProperty("damage").GetInt32(), body.GetProperty("knockBack").GetSingle(), 0);
        Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
        var sourceLiquid = capturedLiquid ?? body;
        Assert.Equal(body.GetProperty("type").GetInt32(), sourceLiquid.GetProperty("type").GetInt32());
        life = life with { TimeLeft = body.GetProperty("timeLeft").GetInt32(), SourceWetCount = 0,
            Liquid = new(sourceLiquid.GetProperty("wet").GetBoolean(), sourceLiquid.GetProperty("lavaWet").GetBoolean(),
                sourceLiquid.GetProperty("honeyWet").GetBoolean(), sourceLiquid.GetProperty("shimmerWet").GetBoolean()),
            LocalAi = new(body.GetProperty("localAI")[0].GetSingle(), body.GetProperty("localAI")[1].GetSingle(), body.GetProperty("localAI")[2].GetSingle()) };
        if (actor.Type.Value == 1052)
            life = life with { ThrownAxe = new(update.PositionX, update.PositionY, update.VelocityX, update.VelocityY,
                body.GetProperty("rotation").GetSingle(), body.GetProperty("direction").GetInt32(), life.TimeLeft,
                body.GetProperty("soundDelay").GetInt32(), update.Ai.Ai0, update.Ai.Ai1,
                body.GetProperty("tileCollide").GetBoolean(), true, false) };
        else
            life = life with { HeldTool = new(update.PositionX, update.PositionY, update.VelocityX, update.VelocityY,
                body.GetProperty("scale").GetSingle(), body.GetProperty("rotation").GetSingle(),
                body.GetProperty("direction").GetInt32(), body.GetProperty("spriteDirection").GetInt32(), life.TimeLeft,
                body.GetProperty("soundDelay").GetInt32(), body.GetProperty("frame").GetInt32(), body.GetProperty("frameCounter").GetInt32(),
                life.LocalAi.Ai0, life.LocalAi.Ai1, true, false) };
        Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
        Assert.True(setup.TryStageMotion(in update, in life));
        Assert.True(setup.TryStageSourceDirection(body.GetProperty("direction").GetInt32()));
        Assert.True(setup.TryAdoptUnpublished(out actor, out bool expired)); Assert.False(expired);
        RestoreRandom(row.GetProperty("beforeRandom"), f.Random);
    }

    private static float Single(JsonElement row, string group, string field) => row.GetProperty(group).GetProperty(field).GetSingle();
    private static void SameBody(JsonElement expected, in ProjectileSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("position").GetProperty("bits")[0].GetInt32(), BitConverter.SingleToInt32Bits(actual.PositionX));
        Assert.Equal(expected.GetProperty("position").GetProperty("bits")[1].GetInt32(), BitConverter.SingleToInt32Bits(actual.PositionY));
        Assert.Equal(expected.GetProperty("velocity").GetProperty("bits")[0].GetInt32(), BitConverter.SingleToInt32Bits(actual.VelocityX));
        Assert.Equal(expected.GetProperty("velocity").GetProperty("bits")[1].GetInt32(), BitConverter.SingleToInt32Bits(actual.VelocityY));
        Assert.Equal(expected.GetProperty("ai").EnumerateArray().Select(value => BitConverter.SingleToInt32Bits(value.GetSingle())),
            new[] { BitConverter.SingleToInt32Bits(actual.Ai.Ai0), BitConverter.SingleToInt32Bits(actual.Ai.Ai1), BitConverter.SingleToInt32Bits(actual.Ai.Ai2) });
    }
    private static void SameLiquid(JsonElement expected, in ProjectileLiquidState actual)
    {
        Assert.Equal(expected.GetProperty("wet").GetBoolean(), actual.Wet);
        Assert.Equal(expected.GetProperty("lavaWet").GetBoolean(), actual.LavaWet);
        Assert.Equal(expected.GetProperty("honeyWet").GetBoolean(), actual.HoneyWet);
        Assert.Equal(expected.GetProperty("shimmerWet").GetBoolean(), actual.ShimmerWet);
    }
    private static void SameHeld(JsonElement expected, in HeldToolProjectileState1458 actual)
    {
        Assert.Equal(expected.GetProperty("bits").GetProperty("scale").GetInt32(), BitConverter.SingleToInt32Bits(actual.Scale));
        Assert.Equal(expected.GetProperty("bits").GetProperty("rotation").GetInt32(), BitConverter.SingleToInt32Bits(actual.Rotation));
        Assert.Equal(expected.GetProperty("frame").GetInt32(), actual.Frame); Assert.Equal(expected.GetProperty("frameCounter").GetInt32(), actual.FrameCounter);
        Assert.Equal(expected.GetProperty("soundDelay").GetInt32(), actual.SoundDelay); Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.SpriteDirection);
        Assert.Equal(expected.GetProperty("localAI")[0].GetSingle(), actual.LocalAi0); Assert.Equal(expected.GetProperty("localAI")[1].GetSingle(), actual.LocalAi1);
    }
    private static void SameAxe(JsonElement expected, in ThrownAxeState1458 actual)
    {
        Assert.Equal(expected.GetProperty("bits").GetProperty("rotation").GetInt32(), BitConverter.SingleToInt32Bits(actual.Rotation));
        Assert.Equal(expected.GetProperty("soundDelay").GetInt32(), actual.SoundDelay);
        Assert.Equal(expected.GetProperty("tileCollide").GetBoolean(), actual.TileCollide);
    }
    private static void RestoreRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    { Cursor(random) = source.GetProperty("cursor").GetUInt32(); source.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()).ToArray().CopyTo(Seeds(random), 0); }
    private static void SameRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    { Assert.Equal(source.GetProperty("cursor").GetUInt32(), Cursor(random)); Assert.Equal(source.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()), Seeds(random)); }
    private static void SameInventory(RuntimePlayerInventoryStore.ShopJournalCapture before, RuntimePlayerInventoryStore.ShopJournalCapture after)
    { Assert.Equal(219, before.Before.Length); Assert.Equal(before.Before, after.Before); Assert.Equal(before.Quantity, after.Quantity); Assert.Equal(before.Copper, after.Copper); Assert.Equal(before.Caps, after.Caps); }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Seeds(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}
