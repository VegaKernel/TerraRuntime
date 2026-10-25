using System.IO.Compression;
using Xunit;

using System.Text.Json;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RetainedNpcShopVendorStatus1458Tests
{
    // Official 1.4.5.8 NPC119 flag/expiry and dedicated VFX capture; original manifest SHA256
    // 8c7c8a5f2804e859799ae17446bb646a09a1ebef3e0c16de61be0ec3dd16a823.
    // Six vendors retain admitted status lifecycle; 368/453 have creation defaults only.
    // Private full56/cursor inspection is test-only managed evidence, not Native coverage.
    [Fact]
    public void Generation_owned_vendor_status_preserves_source_expiry_and_dedicated_visual_random_order()
    {
        int checks = 0;
        void Check(bool value) { checks++; Assert.True(value, "source check=" + checks); }
        NpcSnapshot Spawn(RuntimeNpcStore store, int type) { var state = new NpcStateUpdate(type, (short)type, 100, 100, 0, 0, 255, default, NpcSimulationState.Initial with { Life = 250, LifeMax = 250 }); Check(store.TrySpawn(0, in state, out var npc)); return npc; }
        using var resource = typeof(RetainedNpcShopVendorStatus1458Tests).Assembly.GetManifestResourceStream("NpcShopVendorStatus1458");
        Assert.NotNull(resource);
        using var expanded = new GZipStream(resource, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(expanded);
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            var store = new RuntimeNpcStore(); var npc = Spawn(store, row.GetProperty("type").GetInt32()); var status = new RuntimeNpcBuffStatus1458(store);
            if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) && !definition.SupportsRuntimeLifecycle) { Check(npc.Type is 368 or 453); Check(!status.TryApply(npc.Handle, new BuffTypeId(119), row.GetProperty("duration").GetInt32())); Check(!status.TryGetShoppingLoveStruck(npc.Handle, out _)); continue; }
            Check(status.TryApply(npc.Handle, new BuffTypeId(119), row.GetProperty("duration").GetInt32()));
            Check(status.TryGetShoppingLoveStruck(npc.Handle, out bool immediate)); Check(!immediate);
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                Check(status.TryGetShoppingLoveStruck(npc.Handle, out bool retainedBeforePrepass));
                status.BeginWorldTick(); Check(status.TryGetShoppingLoveStruck(npc.Handle, out bool retainedAfterPrepass)); Check(retainedBeforePrepass == retainedAfterPrepass);
                Check(status.TryPlan(in npc, out var plan)); Check(status.Commit(in plan, in npc));
                Check(status.TryGetShoppingLoveStruck(npc.Handle, out bool flag)); Check(flag == step.GetProperty("cleared").GetProperty("loveStruck").GetBoolean());
                var wire = new TerrariaNpcBuffEntryState[20]; Check(status.TryCopyWireBuffs(npc.Handle, wire, out int count));
                var types = step.GetProperty("cleared").GetProperty("types").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                var times = step.GetProperty("cleared").GetProperty("times").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                Check(count == types.Zip(times).Count(x => x.First > 0 && x.Second > 0));
                for (int i = 0; i < 20; i++) { Check(plan.Slots[i].Type == types[i]); Check(plan.Slots[i].Duration == times[i]); }
            }
            Check(store.TryDespawn(npc.Handle)); var replacement = Spawn(store, row.GetProperty("type").GetInt32());
            Check(!status.TryGetShoppingLoveStruck(npc.Handle, out _)); Check(status.TryGetShoppingLoveStruck(replacement.Handle, out bool replacementFlag)); Check(!replacementFlag);
        }
        foreach (var row in doc.RootElement.GetProperty("visualRows").EnumerateArray())
        {
            var store = new RuntimeNpcStore(); var npc = Spawn(store, row.GetProperty("type").GetInt32()); var status = new RuntimeNpcBuffStatus1458(store);
            if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) && !definition.SupportsRuntimeLifecycle) { Check(npc.Type is 368 or 453); Check(!status.TryGetShoppingLoveStruck(npc.Handle, out _)); continue; }
            if (row.GetProperty("admitted").GetBoolean()) Check(status.TryApply(npc.Handle, new BuffTypeId(119), 1));
            Check(status.TryPlan(in npc, out var plan));
            var source = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
            _ = RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan, new NpcRuntimeTownCombatRandom1458(source), false);
            var random = source.SourceRandom; const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var state = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
            var expected = row.GetProperty("after").GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Check(state.SequenceEqual(expected));
            Check(Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)) == row.GetProperty("after").GetProperty("cursor").GetInt32());
            Check(random.Clone().Next() == row.GetProperty("afterNext").GetInt32());
        }
        Assert.Equal(8800, checks);
    }
}
