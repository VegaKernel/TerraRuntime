using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

// Detached canonical storage plans; source caller/publication and runtime custody scopes stay separate.
public sealed class OwnedPickupStorageSource1458Tests
{
    [Fact]
    public void Ordinary_and_ammo_storage_matches_original_numeric_rows_and_metadata()
    {
        int checks=0, storageRows=0, pickupRows=0, excludedAmmo=0, admittedIdentities=0;
        void Check(bool value,string detail) { checks++; Assert.True(value, detail); }
        WorldItemPickupSlot1458 Slot(JsonElement x) => new(x.GetProperty("type").GetInt32(),x.GetProperty("stack").GetInt32(),x.GetProperty("prefix").GetInt32(),x.GetProperty("favorite").GetBoolean());
        void Replay(JsonElement root,bool pickup)
        {
            foreach(var row in root.GetProperty("rows").EnumerateArray())
            {
                var incoming=Slot(row.GetProperty("beforeIncoming"));
                var inventory=row.GetProperty("before").EnumerateArray().Select(Slot).ToList();
                if(inventory.Count==58)inventory.Add(default);
                Check(VanillaWorldItemPickupCatalog1458.TryGet(incoming.Type,out var facts),"metadata");
                bool accepted=VanillaWorldItemOwner1458.TryPlanStorage(incoming,inventory.ToArray(),false,new(false,false,false),out var plan);
                if(facts.FitsAmmoSlot) excludedAmmo++;
                Check(accepted&&plan is not null,$"admission {incoming.Type}");
                var expected=row.GetProperty("after").EnumerateArray().Select(Slot).ToArray();
                foreach(var change in plan!.Mutations)
                {
                    Check(inventory[change.Slot]==change.Before,"captured slot");
                    inventory[change.Slot]=change.After;
                }
                for(int i=0;i<expected.Length;i++)Check(expected[i]==inventory[i],$"after {incoming.Type}/{row.GetProperty("profile")}/slot{i}");
                var residual=Slot(row.GetProperty("residual"));
                Check(plan.Residual==residual,"returned residual");
                Check(plan.Taken==incoming.Stack-residual.Stack,"conservation");
                Check(row.GetProperty("beforeRng").GetRawText()==row.GetProperty("afterRng").GetRawText(),"source RNG unchanged");
                Check(row.GetProperty("beforeNext").GetInt32()==row.GetProperty("afterNext").GetInt32(),"source next unchanged");
                if(!pickup)
                {
                    var steps=row.GetProperty("steps").EnumerateArray().ToArray();
                    Check(plan.Mutations.Length==steps.Length,"ordered steps count");
                    for(int i=0;i<steps.Length;i++)Check(plan.Mutations[i].Slot==steps[i].GetProperty("slot").GetInt32(),"source step order");
                }
                if(pickup)pickupRows++;else storageRows++;
            }
        }
        using var fixture = Read("OwnedOrdinaryStorageSource1458");
        var root = fixture.RootElement;
        Replay(root.GetProperty("getItem"), false);
        Replay(root.GetProperty("pickup").GetProperty("linux"), true);
        Replay(root.GetProperty("supplement"), false);
        Replay(root.GetProperty("pickup").GetProperty("windowsClr4X86"), true);
        foreach(var row in root.GetProperty("metadata").GetProperty("rows").EnumerateArray())
            {
                int id=row.GetProperty("requested").GetInt32();if(id==0)continue;
                var expected=row.GetProperty("defaults");
                Check(VanillaWorldItemPickupCatalog1458.TryGet(id,out var facts),"all metadata admitted");
                Check(facts.HasUseStyle==(expected.GetProperty("useStyle").GetInt32()!=0),"all useStyle selectors");
                Check(facts.FitsAmmoSlot==expected.GetProperty("fitsAmmo").GetBoolean(),"all FitsAmmoSlot selectors");
            }
        // Source GetItem algorithm uses type+prefix and preserves destination favorite.
        var slots=new WorldItemPickupSlot1458[59];Array.Fill(slots,new(3,9999,0,false));slots[10]=new(2,9990,1,true);slots[11]=new(2,9980,0,false);
        Check(VanillaWorldItemOwner1458.TryPlanStorage(new(2,20,1,false),slots,false,null,out var merge),"prefix merge");
        Check(merge!.Mutations.Length==1&&merge.Mutations[0].Slot==10&&merge.Mutations[0].After.Favorite&&merge.Residual.Stack==11,"prefix/favorite preserved");
        Check(!VanillaWorldItemOwner1458.TryPlanStorage(new(2,20,1,false),slots,true,null,out _),"void fallback excluded");
        foreach(var invalid in new[]{new WorldItemPickupSlot1458(-1,1,0,false),new(2,10000,0,false),new(2,1,VanillaItemPrefixCatalog.Count,false),new(226,1,0,false),new(58,1,0,false)})
         Check(!VanillaWorldItemOwner1458.TryPlanStorage(invalid,slots,false,null,out _), $"malformed/special boundary {invalid}");
        // Broad admission is queried against canonical original SetDefaults identity, not a potion whitelist.

        {
            var empty = new WorldItemPickupSlot1458[59];
            foreach(var row in root.GetProperty("metadata").GetProperty("rows").EnumerateArray())
            {
                int requested=row.GetProperty("requested").GetInt32();if(requested==0)continue;
                int retained=row.GetProperty("defaults").GetProperty("type").GetInt32();
                bool accepted=VanillaWorldItemOwner1458.TryPlanStorage(new(requested,1,0,false),empty,false,new(false,false,false),out var plan);
                if(accepted){admittedIdentities++;Check(retained==requested,"source retained identity");Check(plan!.Taken==1,"positive broad placement");}
            }
        }
        Console.WriteLine($"checks={checks} getItemRows={storageRows} pickup={pickupRows} admittedAmmoRows={excludedAmmo} admittedIdentities={admittedIdentities}");

        Assert.Equal(264, storageRows);
        Assert.Equal(60, pickupRows);
        Assert.True(admittedIdentities >= 6144);
        // Coin71 moved from the historical ordinary-only exclusion into source-backed coin coverage.
        Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(new(71,1,0,false),new WorldItemPickupSlot1458[59],false,null,out _));
    }

    [Fact]
    public void Coin_storage_matches_original_value_order_conversion_and_final_journal()
    {
        int checks = 0, rows = 0;
        void Check(bool value, string detail)
        {
            checks++;
            Assert.True(value, detail);
        }
        WorldItemPickupSlot1458 Slot(JsonElement item) => new(item.GetProperty("type").GetInt32(),
            item.GetProperty("stack").GetInt32(), item.GetProperty("prefix").GetInt32(), item.GetProperty("favorite").GetBoolean());
        long Value(WorldItemPickupSlot1458 item) => item.Type switch
        {
            71 => item.Stack,
            72 => item.Stack * 100L,
            73 => item.Stack * 10_000L,
            74 => item.Stack * 1_000_000L,
            _ => 0
        };
        using var fixture = Read("OwnedCoinPickupSource1458");
        foreach (string path in new[] { "linux", "windows", "linuxTupleSupplement", "windowsTupleSupplement" })
        {
            var root = fixture.RootElement.GetProperty(path);
            Check(root.GetProperty("errors").GetArrayLength() == 0, "original errors");
            foreach (var row in root.GetProperty("rows").EnumerateArray())
            {
                string identity = $"{row.GetProperty("id")}/{row.GetProperty("profile")}";
                var incoming = Slot(row.GetProperty("beforeIncoming"));
                var before = row.GetProperty("before").EnumerateArray().Select(Slot).ToArray();
                var expected = row.GetProperty("after").EnumerateArray().Select(Slot).ToArray();
                Check(VanillaWorldItemOwner1458.TryPlanStorage(incoming, before, false, null, out var plan) && plan is not null,
                    $"source admission {identity}");
                var actual = before.ToArray();
                var slots = new HashSet<short>();
                Check(plan!.Mutations.Length <= 54, "bounded final journal");
                foreach (var change in plan.Mutations)
                {
                    Check(change.Slot is >= 0 and < 54 && slots.Add(change.Slot), "unique ordinary/coin slot");
                    Check(change.Before == before[change.Slot], "independent original before tuple");
                    Check(change.Before != change.After, "no fake mutation");
                    actual[change.Slot] = change.After;
                }
                for (int slot = 0; slot < 59; slot++)
                    Check(actual[slot] == expected[slot], $"source final tuple {identity} slot{slot}");
                var residual = Slot(row.GetProperty("residual"));
                Check(plan.Residual == residual, $"source residual {identity}");
                Check(plan.Taken == incoming.Stack - residual.Stack, "source ground-quantity take");
                Check(before.Sum(Value) + Value(incoming) == actual.Sum(Value) + Value(residual), "copper-unit value conservation");
                Check(row.GetProperty("beforeRng").GetRawText() == row.GetProperty("afterRng").GetRawText(), "original full56 unchanged");
                Check(row.GetProperty("beforeNext").GetInt32() == row.GetProperty("afterNext").GetInt32(), "original cloned Next unchanged");
                foreach (var observation in row.GetProperty("pickupObservations").EnumerateArray())
                    Check(observation.GetProperty("inventory").EnumerateArray().Select(Slot).SequenceEqual(actual),
                        "source ground publication follows complete inventory writes");
                rows++;
            }
        }
        var full = Enumerable.Repeat(new WorldItemPickupSlot1458(3, 9999, 0, false), 59).ToArray();
        foreach (var bad in new WorldItemPickupSlot1458[]
        {
            default, new(-1, 1, 0, false), new(71, 0, 0, false), new(71, 101, 0, false),
            new(74, 10000, 0, false), new(71, 1, -1, false), new(71, 1, 256, false)
        }) Check(!VanillaWorldItemOwner1458.TryPlanStorage(bad, full, false, null, out _), "malformed incoming refusal");
        var copper = new WorldItemPickupSlot1458(71, 20, 0, false);
        Check(!VanillaWorldItemOwner1458.TryPlanStorage(copper, full, true, null, out _), "void residual boundary");
        Check(VanillaWorldItemOwner1458.TryPlanStorage(copper, full, false, null, out var noSpace) &&
            noSpace!.Taken == 0 && noSpace.Mutations.Length == 0 && noSpace.Residual == copper, "no-capacity plan");
        var rich = Enumerable.Repeat(new WorldItemPickupSlot1458(74, 9999, 0, false), 59).ToArray();
        var platinum = new WorldItemPickupSlot1458(74, 9999, 0, false);
        Check(rich.Sum(Value) > int.MaxValue, "long-domain witness");
        Check(VanillaWorldItemOwner1458.TryPlanStorage(platinum, rich, false, null, out var richPlan) &&
            richPlan!.Taken == 0 && richPlan.Residual == platinum, "canonical long-domain bounded full inventory");
        Console.WriteLine(JsonSerializer.Serialize(new { checks, rows, failures = 0, runnerErrors = 0,
            scope = "Pure source storage comparison; final unique-slot journal, not intermediate callback/wire/wholeMain parity" }));

        Assert.Equal(168, rows);
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(OwnedPickupStorageSource1458Tests).Assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        using var gzip = new System.IO.Compression.GZipStream(stream!, System.IO.Compression.CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
