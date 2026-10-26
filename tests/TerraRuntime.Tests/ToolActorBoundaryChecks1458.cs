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
    private static void RuntimeBoundaries()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var launches = Read("AllToolsExtension" + platform + "First1458");
            var definitions = launches.RootElement.EnumerateArray().Where(row => row.GetProperty("kind").GetString() == "launch" &&
                row.GetProperty("actualPrefix").GetInt32() == 0 && row.GetProperty("meleeMultiplier").GetSingle() == 1f &&
                !row.GetProperty("left").GetBoolean()).ToArray();
            Assert.Equal(24, definitions.Length);
            foreach (var launch in definitions)
            {
                using var f = new ToolActorRuntimeFixture1458(platform == "Windows");
                var actor = f.Birth(launch); var image = f.InventoryImage();
                var before = f.Random.Clone();
                f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
                    new(f.BirthPacket.Key, actor.PositionX, actor.PositionY)));
                Assert.True(f.Shots.TryGet(actor.Handle, out var retained)); Assert.Equal(actor, retained);
                Assert.True(f.Random.HasSameState(before));
                Assert.True(f.Authority.TryTickState());
                if (actor.Type.Value == 1052) Assert.True(f.Shots.TryGet(actor.Handle, out _));
                else
                {
                    Assert.False(f.Shots.TryGet(actor.Handle, out _)); Assert.Equal(2, f.Member.ItemAnimation);
                    Assert.Equal(1, f.Sink.Events.Count(entry => entry.Kind == ProjectileStateCommitKind.Despawn));
                    var ended = f.Random.Clone(); Assert.True(f.Authority.TryTickState()); Assert.True(f.Random.HasSameState(ended));
                }
                SameInventory(image, f.InventoryImage());
            }
            foreach (int item in new[] { 383, 2798, 5519 })
                foreach (string mode in new[] { "throw", "reentry", "same-generation", "replacement", "terminal-throw", "disconnect", "owner-reuse", "no-items" })
                    CallbackBoundary(Launch(launches.RootElement, item), platform == "Windows", mode);
            ControlledInputs(launches.RootElement, platform);
        }
    }

    private static void CallbackBoundary(JsonElement launch, bool windows, string mode)
    {
        using var f = new ToolActorRuntimeFixture1458(windows); var actor = f.Birth(launch); var member = f.Member;
        if (mode == "no-items")
        {
            f.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, new[] { new BuffTypeId(23) })));
            f.State.Tick();
            // Original held source no-items-after-start: AI20 continues;
            // drill445 cancels in its local source branch. Nonchannel1052 returns normally.
            Assert.Equal(actor.Type.Value != 445, f.Shots.TryGet(actor.Handle, out _));
            return;
        }
        if (mode is "disconnect" or "owner-reuse")
        {
            RuntimePlayerMember? newcomer = null; PlayerStateSnapshot nextBefore = default;
            if (mode == "owner-reuse") { newcomer = f.ReplaceOwner(); nextBefore = newcomer.CaptureSnapshot(); }
            else f.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
            Assert.True(f.Authority.TryTickState()); Assert.False(f.Shots.TryGet(actor.Handle, out _));
            if (newcomer is not null)
            { Assert.Equal(nextBefore, newcomer.CaptureSnapshot()); Assert.NotEqual(member.Connection.Player, newcomer.Connection.Player); }
            return;
        }
        if (mode == "terminal-throw")
        {
            if (actor.Type.Value == 1052)
            {
                Assert.True(f.Shots.TryGetLifecycle(actor.Handle, out var life));
                Assert.NotNull(life.ThrownAxe);
                var returning = new ProjectileStateUpdate(actor.Type, actor.Spawner,
                    member.PositionX, member.PositionY, actor.VelocityX, actor.VelocityY,
                    new(1, 0, 0), actor.BannerIdToRespondTo, actor.Damage, actor.KnockBack, actor.OriginalDamage);
                life = life with { ThrownAxe = life.ThrownAxe!.Value with { Ai0 = 1, Ai1 = 0, X = member.PositionX, Y = member.PositionY, TileCollide = false } };
                Assert.True(f.Shots.TryPrepareSimulation(in actor, out var setup)); Assert.NotNull(setup);
                Assert.True(setup.TryStageMotion(in returning, in life)); Assert.True(setup.TryAdoptUnpublished(out actor, out _));
            }
            else f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
                new(f.BirthPacket.Key, actor.PositionX, actor.PositionY)));
        }
        int callbacks = 0; bool reentered = true; ProjectileSnapshot replacement = default, revised = default;
        var beforeInventory = f.InventoryImage();
        f.Sink.BeforeReplication = (kind, state) =>
        {
            if (state.Handle != actor.Handle || kind == ProjectileStateCommitKind.Spawn || callbacks++ != 0) return;
            if (actor.Type.Value != 1052)
            { Assert.Equal(2, member.ItemAnimation); Assert.Equal(2, member.ItemPhase!.Value.Selected.ItemTime); }
            SameInventory(beforeInventory, f.InventoryImage());
            if (mode == "reentry") reentered = f.Authority.TryTickState();
            else if (mode == "same-generation")
            {
                var update = new ProjectileStateUpdate(state.Type, state.Spawner, state.PositionX + 1, state.PositionY,
                    state.VelocityX, state.VelocityY, state.Ai, state.BannerIdToRespondTo, state.Damage, state.KnockBack, state.OriginalDamage);
                Assert.True(f.Shots.TryUpdate(state.Handle, in update, out revised));
            }
            else if (mode == "replacement")
            {
                Assert.True(f.Shots.TryRemove(state.Handle, out _));
                var update = new ProjectileStateUpdate(new(1), 255, 1200, 1200, 1, 0, default, 0, 0, 0, 0);
                Assert.True(f.Shots.TrySpawnVanilla(in update, out replacement));
            }
            else if (mode is "throw" or "terminal-throw") throw new InvalidOperationException("tool observer");
        };
        if (mode is "throw" or "terminal-throw")
            Assert.Throws<InvalidOperationException>(() => f.Authority.TryTickState());
        else Assert.True(f.Authority.TryTickState());
        Assert.True(callbacks > 0);
        if (mode == "reentry") Assert.False(reentered);
        if (mode == "same-generation") { Assert.True(f.Shots.TryGet(actor.Handle, out var current)); Assert.Equal(revised, current); }
        if (mode == "replacement")
        { Assert.False(f.Shots.TryGet(actor.Handle, out _)); Assert.True(f.Shots.TryGet(replacement.Handle, out var current)); Assert.Equal(replacement, current); }
        if (mode == "terminal-throw") Assert.False(f.Shots.TryGet(actor.Handle, out _));
        // First exception does not leave the actor tick owner latched.
        f.Sink.BeforeReplication = null; Assert.True(f.Authority.TryTickState());
    }

    private static void ControlledInputs(JsonElement launches, string platform)
    {
        using var terrain = Read("AllToolActorTerrain" + platform + "First1458");
        using var local = Read("AllToolsHeld" + platform + "First1458");
        foreach (int item in new[] { 383, 2798 })
            foreach (bool valid in new[] { true, false })
            {
                using var f = new ToolActorRuntimeFixture1458(platform == "Windows"); var actor = f.Birth(Launch(launches, item));
                var before = terrain.RootElement.EnumerateArray().Single(row => row.GetProperty("item").GetInt32() == item && row.GetProperty("mode").GetString() == "air");
                ConfigureSourceBefore(f, f.Member, ref actor, before);
                var source = local.RootElement.EnumerateArray().Single(row => row.GetProperty("item").GetInt32() == item &&
                    row.GetProperty("local").GetBoolean() && row.GetProperty("mode").GetString() == "hold" &&
                    row.GetProperty("kind").GetString() == "full-projectile-update");
                var after = source.GetProperty("steps")[0].GetProperty("state");
                float vx = valid ? Single(after, "velocity", "x") : 100000f;
                float vy = valid ? Single(after, "velocity", "y") : 100000f;
                var packet = new TerrariaProjectileUpdateState(f.BirthPacket.Key, actor.Type.Value,
                    valid ? Single(after, "position", "x") : 100000f,
                    valid ? Single(after, "position", "y") : 100000f, vx, vy,
                    0, 0, 0, 0, actor.Damage, actor.KnockBack, actor.OriginalDamage);
                var budget = f.InventoryImage(); var randomBefore = f.Random.Clone();
                f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet));
                Assert.True(f.Shots.TryGet(actor.Handle, out var retained)); Assert.Equal(actor, retained);
                Assert.True(f.Random.HasSameState(randomBefore));
                Assert.True(f.Authority.TryTickState()); Assert.True(f.Shots.TryGet(actor.Handle, out var actual));
                if (valid)
                {
                    Assert.InRange(MathF.Abs(actual.VelocityX - vx), 0f, .002f);
                    Assert.InRange(MathF.Abs(actual.VelocityY - vy), 0f, .002f);
                }
                else
                {
                    var ordinary = before.GetProperty("steps")[0]; SameBody(ordinary.GetProperty("state"), in actual);
                    SameRandom(ordinary.GetProperty("random"), f.Random);
                }
                SameInventory(budget, f.InventoryImage());
            }
    }
}
