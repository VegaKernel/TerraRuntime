using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using Xunit;
using Fixture = TerraRuntime.Tests.BotAmmoIndependentSource1458Tests.Fixture;

namespace TerraRuntime.Tests;

public sealed class BotAmmoConservationAtomic1458Tests
{
    [Fact]
    public void Source_conservation_projection_is_owned_before_observers()
    {
        using var resource = typeof(BotAmmoConservationAtomic1458Tests).Assembly.GetManifestResourceStream("BotAmmoConservationConfigured1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int compared=0;
        foreach(var row in document.RootElement.EnumerateArray())
        {
            int weapon=row.GetProperty("weapon").GetInt32(), ammo=row.GetProperty("ammo").GetInt32();
            var f=new Fixture(weapon,20,ammo,conservationSeed:row.GetProperty("seed").GetInt32());
            if(row.GetProperty("magicQuiver").GetBoolean())
                Assert.True(f.Players.SetItem(f.Id,new((short)(VanillaPlayerItemSlotCatalog.ArmorStart+3),VanillaItemIds.MagicQuiver,1,default,0)));
            int expected=row.GetProperty("ammoAfter").GetProperty("stack").GetInt32();
            int next=row.GetProperty("publications")[0].GetProperty("next").GetInt32();
            f.Events.Items.Clear();
            bool observed=false;
            f.Events.Observe=_=>{
                observed=true;
                Assert.True(f.Players.TryGetItem(f.Id,54,out var current));
                Assert.Equal(expected,current.Stack);
                Assert.Equal(next,f.ConservationRandom.Clone().Next());
                AssertSourceCursor(row.GetProperty("publications")[0].GetProperty("random"),f.ConservationRandom);
                Assert.Equal(1,f.Projectiles.AppliedSpawns);
                Assert.True(f.Shots.TryGetActive(0,out var born));
                Assert.True(f.Shots.TryGetCombatTrustedOwner(born.Handle,out var owner));
                Assert.Equal(f.Bot.Player,owner);
                Assert.Equal(row.GetProperty("weaponStats").GetProperty("useTime").GetInt32(),f.Bot.NextAttackTick);
                Assert.Equal(row.GetProperty("weaponStats").GetProperty("useAnimation").GetInt32(),f.Bot.UseItemUntilTick);
            };
            Assert.Equal(RuntimeBotActionResult.Success.Status,f.Attack().Status);
            Assert.True(observed);
            Assert.Equal(expected==20?0:1,f.Events.Items.Count);
            Assert.Equal(next,f.ConservationRandom.Clone().Next());
            compared++;
        }
        Assert.Equal(27,compared);
    }

    private static void AssertSourceCursor(JsonElement source, VanillaUnifiedRandom1458 current)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        Assert.Equal(source.GetProperty("cursor").GetUInt32(),(uint)typeof(VanillaUnifiedRandom1458).GetField("inext",flags)!.GetValue(current)!);
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),(int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",flags)!.GetValue(current)!);
    }

    [Fact]
    public void Conserved_use_retains_census_cursor_cadence_and_callback_custody()
    {
        foreach(var mode in new[]{"plain","ammo","unrelated","actor","cursor","reentry","throwMove","throwSpawn","providerAmmo","providerUnrelated","providerCursor","automaticCursor","pool"})
        {
            var f=new Fixture(98,1,97,capacity:mode=="pool"?1:3,conservationSeed:1);
            var before=f.ConservationRandom.Clone();
            var expected=before.Clone();expected.Next(0,3);
            if(mode=="automaticCursor")
            {
                f.Bot.Configuration=f.Bot.Configuration with {WeaponPolicy=RuntimeBotWeaponPolicy.Automatic};
                Assert.True(f.Players.SetItem(f.Id,new(2,new(98),1,default,0)));
                Assert.True(f.Players.SetItem(f.Id,new(1,new(39),1,default,0)));
                Assert.True(f.Players.SetItem(f.Id,new(55,new(40),20,default,0)));
                Assert.True(f.Players.TryTeleport(f.TargetId,500,160));
            }
            if(mode=="pool")
                Assert.True(f.Shots.TrySpawn(0,new(new(14),f.Bot.Player.Slot.Value,170,181,1,0,default,0,1,0,0),out _));
            bool provider=false;
            f.OnTick=()=>{
                if(provider)return;provider=true;
                if(mode=="providerAmmo")Assert.True(f.Players.SetItem(f.Id,new(54,new(97),77,default,0)));
                if(mode=="providerUnrelated")Assert.True(f.Players.SetItem(f.Id,new(55,new(97),77,default,0)));
                if(mode is "providerCursor" or "automaticCursor")f.ConservationRandom.Next();
            };
            bool first=true;
            f.Events.Observe=kind=>{
                if(mode is "providerAmmo" or "providerUnrelated" && kind=="item")return;
                if(!first)return;first=false;
                Assert.True(f.ConservationRandom.HasSameState(expected));
                Assert.True(f.Players.TryGetItem(f.Id,54,out var retained));Assert.Equal(1,retained.Stack);
                Assert.Equal(8,f.Bot.NextAttackTick);Assert.Equal(8,f.Bot.UseItemUntilTick);
                Assert.Equal(1,f.Projectiles.AppliedSpawns);
                if(mode=="ammo")Assert.True(f.Players.SetItem(f.Id,new(54,new(97),77,default,0)));
                if(mode=="unrelated")Assert.True(f.Players.SetItem(f.Id,new(55,new(97),77,default,0)));
                if(mode=="cursor")f.ConservationRandom.Next();
                if(mode=="actor"){Assert.True(f.Players.Despawn(f.Id));Assert.True(f.Players.Create(f.Id,160,160).IsCreated);Assert.True(f.Players.SetItem(f.Id,new(54,new(97),77,default,0)));}
                if(mode=="reentry"){f.Bot.NextAttackTick=0;Assert.Equal(RuntimeBotActionResult.Pending().Status,f.Attack().Status);}
                if(mode=="throwMove")throw new InvalidOperationException("conserved move");
            };
            if(mode=="throwSpawn")f.Events.Observe=kind=>{if(kind=="spawn")throw new InvalidOperationException("conserved spawn");};
            f.Events.Seen.Clear();
            if(mode.StartsWith("throw"))Assert.Throws<InvalidOperationException>(()=>f.Attack());else _=f.Attack();
            if(mode is "providerAmmo" or "providerUnrelated" or "providerCursor" or "automaticCursor" or "pool")
            {
                if(mode is "providerAmmo" or "providerUnrelated")Assert.Equal(new[]{"item"},f.Events.Seen);else Assert.Empty(f.Events.Seen);
                Assert.Equal(0,f.Projectiles.AppliedSpawns);Assert.Equal(0,f.Bot.NextAttackTick);
                var refused=before.Clone();if(mode is "providerCursor" or "automaticCursor")refused.Next();Assert.True(f.ConservationRandom.HasSameState(refused));
                if(mode=="automaticCursor"){Assert.True(f.Players.TryGetItem(f.Id,55,out var arrow));Assert.Equal(20,arrow.Stack);}
            }
            else
            {
                Assert.Equal(1,f.Projectiles.AppliedSpawns);
                Assert.Equal(mode is "ammo" or "unrelated" or "actor"?1:0,f.Events.Items.Count);
                if(mode=="actor")Assert.DoesNotContain("spawn",f.Events.Seen);else Assert.Contains("spawn",f.Events.Seen);
                var after=expected.Clone();if(mode=="cursor")after.Next();Assert.True(f.ConservationRandom.HasSameState(after));
            }
            Assert.True(f.Players.TryGetItem(f.Id,54,out var final));
            Assert.Equal(mode is "ammo" or "actor" or "providerAmmo"?77:1,final.Stack);
        }
    }
}
