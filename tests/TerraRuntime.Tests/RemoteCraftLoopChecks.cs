using TerraRuntime.Core.Worlds;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.WorldChestCrafting;

internal static class RemoteCraftLoopChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { Interlocked.Increment(ref checks); if (!value) throw new InvalidOperationException(message); }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var source = WorldChestCraftSource.Read("RemoteCraftEarly" + platform + "1458");
            var row = source.RootElement.EnumerateArray().Single(x => x.GetProperty("mode").GetString() == "remote-open-approve");
            WorldChestItem[] Stock(int index) => row.GetProperty("before").GetProperty("worldChests")[index].EnumerateArray().Select(x => new WorldChestItem(x.GetProperty("stack").GetInt32(), x.GetProperty("type").GetInt32(), x.GetProperty("prefix").GetByte())).ToArray();
            using var wrapper = JsonDocument.Parse("{\"name\":\"owner-open\",\"before\":" + row.GetProperty("before").GetProperty("worldChests")[0].GetRawText() + "}");
            using var fixture = new Fixture(wrapper.RootElement, new[] { new WorldChest(0, 20, 20, "", Stock(0)), new WorldChest(1, 24, 20, "", Stock(1)) });
            Check(fixture.ActorInventory.TryApplyAtomic(fixture.Connection, new[] { new RuntimePlayerInventoryMutation((byte)0, new(new ItemTypeId(9), 3, default, 0)) }), "trusted actor"); Fixture.Funding(fixture.ActorInventory)[0] = 3;
            fixture.Member.HasHealth = true; fixture.Member.Life = 100; fixture.Member.MaxLife = 100;
            var costReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resultReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int other = 0, maxSteps = 0, publications = 0;
            fixture.Events.OnEquipment = () =>
            {
                publications++;
                var actual = fixture.CapturePlayer();
                Check(actual.Items[0].IsEmpty && actual.Quantity[0] == 0 && actual.Items[58].ItemType.Value == 36 && actual.Quantity[58] == 1, "real owner observer sees joint result");
                if (publications == 2) resultReady.TrySetResult(true);
            };
            using var loop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(fixture.State, (state, command) =>
            {
                if (command is WorkerResultCommand) other++;
                if (!fixture.Processor.TryApply(command)) state.Apply(command);
            }, state =>
            {
                state.Tick(); maxSteps = Math.Max(maxSteps, fixture.Processor.LastCraftingWorkSteps + fixture.ShopSteps);
                if (!costReady.Task.IsCompleted && fixture.Processor.AppliedCraftingRequests == 1 && fixture.Member.CursorMove?.Shop?.RemoteCostWorkPending == false)
                {
                    var retained = fixture.CapturePlayer(); Check(retained.Items[0].Stack == 3 && retained.Quantity[0] == 3 && retained.Items[58].IsEmpty, "cost approval before client result does not alter actor custody");
                    costReady.TrySetResult(true);
                }
            }, new GameLoopOptions { CommandCapacity = 16, MaxCommandIngressPerTick = 4, MaxCommandsPerTick = 2, MaxCommandsPerSourcePerTick = 1, MaxPendingCommandsPerSource = 8 },
            (state, sourceId, command) => state.CanApplyCommand(sourceId, command));
            fixture.Commands.Forward = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(loop);
            void Post(IEnumerable<JsonElement> frames) { foreach (var encoded in frames) { var frame = Fixture.Decode(Convert.FromHexString(encoded.GetString()!)); Check(fixture.Bootstrap.OnFrame(in frame) == TerrariaFrameSinkResult.Continue, "actual raw ingress admitted"); } }
            Post(row.GetProperty("craftFrames").EnumerateArray().Concat(row.GetProperty("preResponseSyncFrames").EnumerateArray()));
            Check(loop.TryPost(GameCommandSourceId.FromConnection(2), new WorkerResultCommand(17)), "other source admitted");
            loop.Start();
            Check(costReady.Task.Wait(TimeSpan.FromSeconds(5)), "bounded cost completion on owner thread");
            Post(row.GetProperty("syncFrames").EnumerateArray());
            Check(resultReady.Task.Wait(TimeSpan.FromSeconds(5)), "bounded source result completion on owner thread");
            Check(loop.Stop(TimeSpan.FromSeconds(3)), "loop stopped");
            Check(other == 1 && publications == 2 && maxSteps <= 64, "other source/FIFO/shared64/publication once");
            Check(fixture.Processor.AppliedCraftingRequests == 1 && fixture.Processor.RejectedCraftingRequests == 0, "one cost operation only");
            Console.WriteLine("PASS " + platform + " bounded-authoritative-loop");
        }
        Console.WriteLine("REMOTE_LOOP cases=2 checks=" + checks);

        return checks;
    }
}
