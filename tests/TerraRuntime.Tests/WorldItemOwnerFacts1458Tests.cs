using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class WorldItemOwnerFacts1458Tests
{
    [Theory] [MemberData(nameof(Defaults))]
    public void Every_item_pickup_rule_matches_independent_original_defaults(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;
        Assert.True(VanillaWorldItemPickupCatalog1458.TryGet(row.GetProperty("type").GetInt32(),out var facts));
        Assert.Equal(row.GetProperty("uniqueStack").GetBoolean(),facts.UniqueStack);
        Assert.Equal(row.GetProperty("notAmmo").GetBoolean(),facts.NotAmmo);
        Assert.Equal(row.GetProperty("emptyAmmo").GetBoolean(),facts.EmptyAmmo);
        Assert.Equal(row.GetProperty("pickup").GetBoolean(),facts.Pickup);
        Assert.Equal(row.GetProperty("ignoresEncumbering").GetBoolean(),facts.IgnoresEncumbering);
        Assert.Equal(row.GetProperty("notInventory").GetBoolean(),facts.NotInventory);
        Assert.Equal(row.GetProperty("nebula").GetBoolean(),facts.Nebula);
        Assert.Equal(row.GetProperty("onlyNeedOne").GetBoolean(),facts.OnlyNeedOne);
        Assert.Equal(row.GetProperty("quest").GetBoolean(),facts.Quest);
        Assert.Equal(row.GetProperty("ammo").GetInt32()>0,facts.Ammo);
    }

    [Theory] [MemberData(nameof(Owners))]
    public void Actual_original_owner_inventory_body_magnets_and_hopper_matrix_matches(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;int type=row.GetProperty("type").GetInt32();
        var players=new List<WorldItemOwnerPlayer1458>();
        foreach(var p in row.GetProperty("spaces").EnumerateArray())
        {
            var inventory=p.GetProperty("inventory").EnumerateArray().Select(static i=>new WorldItemPickupSlot1458(
                i.GetProperty("type").GetInt32(),i.GetProperty("stack").GetInt32(),i.GetProperty("prefix").GetInt32(),i.GetProperty("favorite").GetBoolean())).ToArray();
            Assert.True(VanillaWorldItemOwner1458.TryCanPull(type,0,inventory,default,false,p.GetProperty("prevent").GetBoolean(),out bool canPull));
            Assert.Equal(p.GetProperty("canPull").GetBoolean(),canPull);
            if(!p.GetProperty("active").GetBoolean())continue;
            players.Add(new((byte)p.GetProperty("slot").GetInt32(),p.GetProperty("x").GetSingle(),p.GetProperty("y").GetSingle(),
                p.GetProperty("width").GetInt32(),p.GetProperty("height").GetInt32(),p.GetProperty("dead").GetBoolean(),canPull,
                p.GetProperty("mana").GetBoolean(),p.GetProperty("life").GetBoolean(),p.GetProperty("grabRange").GetInt32()));
        }
        byte owner=row.GetProperty("shimmer").GetSingle()>0?byte.MaxValue:VanillaWorldItemOwner1458.Select(type,1000,1000,
            row.GetProperty("grabDelay").GetInt32(),(byte)row.GetProperty("grabPlayer").GetInt32(),players.ToArray());
        if(owner!=byte.MaxValue && row.GetProperty("hopper").GetBoolean() &&
            VanillaWorldItemPickupCatalog1458.TryGet(type,out var facts) && !facts.NotInventory &&
            !VanillaWorldItemOwner1458.InGrabRange(players.Single(p=>p.Slot==owner),1000,1000))owner=byte.MaxValue;
        Assert.Equal(row.GetProperty("owner").GetInt32(),owner);
        Assert.Equal(906992634,row.GetProperty("next").GetInt32());
    }

    public static IEnumerable<object[]> Defaults()=>Rows("world-item-pickup-defaults-official.json.gz");
    public static IEnumerable<object[]> Owners()=>Rows("world-item-owner-official.json.gz");
    private static IEnumerable<object[]> Rows(string name)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TerraRuntime.Tests.Fixtures."+name)!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var doc=JsonDocument.Parse(gzip);
        foreach(var row in doc.RootElement.EnumerateArray())yield return [row.GetRawText()];
    }
}
