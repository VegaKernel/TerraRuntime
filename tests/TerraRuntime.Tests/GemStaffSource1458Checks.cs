using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
namespace TerraRuntime.Tests;
internal static class GemStaffSource1458Checks
{
    internal static int Run()
    {
        int checks = 0, cases = 0, tick = -1;
        void Check(bool okay, string label) { if (!okay) throw new InvalidOperationException($"Gem source {label}: case={cases}, tick={tick}"); checks++; }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            bool windows = platform == "Windows";
            using (var data = Read("Metadata", platform))
            foreach (var row in data.RootElement.EnumerateArray())
            {
                int id = I(row, "id"), prefix = I(row, "prefix");
                var fields = row.GetProperty("fields");
                if (id == 6171) { Check(I(fields, "type") == 0 && !VanillaGemStaff1458.TryGetWeapon(new(id), out _), "deprecated canonical Air"); continue; }
                if (VanillaGemStaff1458.TryGetRobe(new(id), out var robe))
                {
                    if (prefix == 0) Check(robe.Defense == I(fields, "defense"), "robe source defense");
                    continue;
                }
                Check(VanillaGemStaff1458.TryResolveWeapon(new(id), new((byte)prefix), windows, out var weapon, out int crit), "source positive prefix");
                Check(weapon.BaseDamage == I(fields, "damage"), "prefixed damage");
                Check(weapon.ManaCost == I(fields, "mana") && weapon.UseTime == I(fields, "useTime") && weapon.Animation == I(fields, "useAnimation"), "prefix clocks/mana");
                Check(crit == I(fields, "crit"), "raw selected crit");
                Check(Bits(weapon.KnockBack) == I(fields.GetProperty("floatBits"), "knockBack") && Bits(weapon.ShootSpeed) == I(fields.GetProperty("floatBits"), "shootSpeed"), "prefix Single stats");
                Check(weapon.Projectile.Value == I(fields, "shoot") && weapon.AutoReuse == B(fields, "autoReuse"), "source projectile/reuse");
            }
            foreach (string family in new[] { "Behavior", "Prefix", "Refill" })
            {
            using var behavior = Read(family, platform);
            foreach (var row in behavior.RootElement.EnumerateArray())
            {
                var before = row.GetProperty("before");
                var random = new VanillaUnifiedRandom1458(I(row, "seed"));
                var state = State(before);
                int id = I(row, "id");
                var prefix = new PrefixId((byte)I(before.GetProperty("inventory")[0], "prefix"));
                int body = I(row.GetProperty("effectiveBody"), "type");
                Check(VanillaGemStaff1458.TryPackFeatures(new(id), new(body), out var features) && (int)features == I(row, "featureBits"), "original effective-body feature bits");
                Check(VanillaGemStaff1458.TryResolveWeapon(new(id), prefix, windows, out var weapon, out _), "supported source item");
                var beforeRandom = row.GetProperty("beforeRandom");
                Check(Seeds(random).SequenceEqual(beforeRandom.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32())) && Cursor(random) == beforeRandom.GetProperty("cursor").GetUInt32(), "initial full56/cursor");
                int bornCount = 0;
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    tick = I(phase, "tick");
                    var after = phase.GetProperty("state");
                    int mana = I(before, "statMana");
                    // The actual source43 publication identifies the prepared refill checkpoint.
                    // Pure staff clocks do not consume potion inventory; App owns that joint adoption.
                    foreach (var publication in row.GetProperty("publications").EnumerateArray())
                        if (I(publication, "packet") == 43 && publication.GetProperty("state").GetProperty("itemAnimation").GetInt32() == state.Animation &&
                            publication.GetProperty("random").GetProperty("cursor").GetUInt32() == Cursor(random) + 1)
                            mana = I(publication.GetProperty("state"), "statMana");
                    if (!B(row, "local") && row.GetProperty("mode").GetString() == "mana-flower" &&
                        I(before.GetProperty("inventory")[1], "stack") > I(after.GetProperty("inventory")[1], "stack"))
                    {
                        // Remote QuickMana has the same source refill, without local packet43.
                        // Bind its prepared amount to the matching actual local publication,
                        // rather than deriving it from the candidate payment result.
                        var sibling = behavior.RootElement.EnumerateArray().Single(candidate =>
                            I(candidate, "id") == id && I(candidate, "robe") == I(row, "robe") &&
                            B(candidate, "local") && candidate.GetProperty("mode").GetString() == "mana-flower");
                        mana = sibling.GetProperty("publications").EnumerateArray().First(publication =>
                            I(publication, "packet") == 43).GetProperty("state").GetProperty("statMana").GetInt32();
                    }
                    var facts = new PlayerGemStaffFacts1458(new(id), prefix, new(body), mana,
                        B(before, "slowMagicUse"), B(before, "controlUseItem"), B(before, "lastItemUseAttemptSuccess"),
                        B(before, "noItems"), B(before, "CCed"), false, B(row, "local"))
                    { ManaCostMultiplier = before.GetProperty("manaCost").GetSingle(), WindowsItemPrefixArithmetic = windows };
                    Check(VanillaGemStaffUse1458.TryStep(state, in facts, random.Next, out var actual), "admitted staff phase");
                    Check(actual.State == State(after), "source clocks " + actual.State + " expected " + State(after));
                    Check(actual.Mana == I(after, "statMana") && actual.SlowMagicUse == B(after, "slowMagicUse"), "source mana/slow");
                    Check(actual.LastUseSuccess == B(after, "lastItemUseAttemptSuccess"), "source success transition");
                    int offered = 0;
                    foreach (bool shoot in new[] { actual.MidpointShoot, actual.InitialShoot })
                    {
                        if (!shoot) continue;
                        float aimX = row.GetProperty("mode").GetString() == "left" ? -260f : 240f;
                        float aimY = -1f;
                        float scale = weapon.ShootSpeed / (float)Math.Sqrt(aimX*aimX + aimY*aimY);
                        var launches = new GemStaffLaunch1458[2];
                        int damage = Math.Max(1, (int)(weapon.BaseDamage * before.GetProperty("magicDamage").GetSingle() + 5E-06f));
                        Check(VanillaGemStaff1458.TryPlanVolley(new(id), new(body), 410f, 421f, aimX*scale, aimY*scale,
                            damage, weapon.KnockBack, random.NextDouble, launches, out int count), "ordered source volley");
                        for (int i = 0; i < count; i++)
                        {
                            var source = phase.GetProperty("born")[bornCount + offered + i];
                            var launch = launches[i];
                            Check(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(new(I(source,"type")), out int penetration),"source base penetration known");
                            var baseSpawn = new GemStaffFinalize1458(penetration,penetration,300,0);
                            Check(VanillaGemStaffProjectile1458.TryFinalize(new(I(source,"type")),features,in baseSpawn,out var finalized) && finalized.Penetration==I(source,"penetrate") && finalized.TimeLeft==I(source,"timeLeft"),"source finalized birth penetration/time");
                            Check(VanillaProjectileNpcCombatFacts.TryGetLocalNpcImmunityCooldown(new(I(source,"type")),out int cooldown) && cooldown==I(source,"localNPCHitCooldown") && B(source,"usesLocalNPCImmunity"),"source local immunity60");
                            Check(launch.Projectile.Value == I(source,"type") && launch.Damage == I(source,"damage") && Bits(launch.KnockBack) == Bits(source.GetProperty("knockBack").GetSingle()), "source birth identity/damage/KB");
                            Check(Bits(launch.Ai0) == source.GetProperty("aiBits")[0].GetInt32() && launch.Ai1 == source.GetProperty("ai")[1].GetSingle(), "source AI0/AI1");
                            var position=source.GetProperty("position");var velocity=source.GetProperty("velocity");
                            Check(Bits(launch.CenterX - I(source,"width")/2f) == I(position,"xBits") && Bits(launch.CenterY - I(source,"height")/2f) == I(position,"yBits"), "exact source birth origin");
                            Check(Bits(launch.VelocityX) == I(velocity,"xBits") && Bits(launch.VelocityY) == I(velocity,"yBits"), "exact source velocity");
                        }
                        offered += count;
                    }
                    bornCount += offered;
                    Check(bornCount == phase.GetProperty("born").GetArrayLength(), "no omitted or extra source births");
                    var expectedRandom=phase.GetProperty("random");
                    Check(Seeds(random).SequenceEqual(expectedRandom.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32())) && Cursor(random)==expectedRandom.GetProperty("cursor").GetUInt32(), "full56/cursor");
                    Check(random.Clone().Next()==I(phase,"next"), "following RNG");
                    state=actual.State;before=after;
                }
                cases++;
            }
        }
            }
        Check(cases==3150,"complete source profiles");
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var data = Read("Robes", platform);
            foreach (var row in data.RootElement.EnumerateArray())
            {
                var before = row.GetProperty("before");
                var armor = before.GetProperty("armor");
                var equipment = new List<PlayerEquipmentCommitRequest>();
                void Add(JsonElement slot, int position)
                {
                    if (I(slot,"type") == 0) return;
                    equipment.Add(new(default, (short)position, (short)I(slot,"stack"), (byte)I(slot,"prefix"),
                        (short)I(slot,"type"), B(slot,"favorite") ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0));
                }
                int index=0;foreach (var slot in armor.EnumerateArray()) Add(slot, VanillaPlayerItemSlotCatalog.ArmorStart+index++);
                int loadout=0;foreach(var source in before.GetProperty("loadouts").EnumerateArray())
                { index=0;foreach(var slot in source.EnumerateArray()) Add(slot, VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout*VanillaPlayerItemSlotCatalog.LoadoutStride+index++);loadout++; }
                var vanity = B(row,"local") ? new VanillaPlayerLocalVanityArmor1458(new(I(armor[10],"type")),new(I(armor[11],"type")),new(I(armor[12],"type"))) : VanillaPlayerLocalVanityArmor1458.Empty;
                var context = new VanillaPlayerCombatEquipmentContext(false,false,false) { LocalVanityArmor=vanity };
                Check(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment.ToArray(),in context,out var combat,out int manaBonus),"whole source robe build");
                Check(VanillaPlayerCombatEquipmentCatalog.TryCaptureEffectiveGemStaffBody(equipment.ToArray(),in context,out var effective) && effective.Value==I(row.GetProperty("effectiveBody"),"type"),"effective inheritance and local vanity");
                var after=row.GetProperty("after");
                Check(combat.Defense==I(after,"statDefense") && manaBonus==I(after,"statManaMax2")-I(before,"statManaMax"),"source robe defense/mana maximum");
                Check(Bits(combat.ManaCostMultiplier)==I(after.GetProperty("floatBits"),"manaCost"),"source robe mana-cost Single");
                Check(combat.MagicCrit==I(after,"magicCrit") && Bits(combat.MagicDamage)==Bits(after.GetProperty("magicDamage").GetSingle()),"source other represented magic facts");
                Check(row.GetProperty("beforeRandom").GetRawText()==row.GetProperty("afterRandom").GetRawText() && I(row,"beforeNext")==I(row,"afterNext"),"source pure equipment no RNG");
                var wrong = new PlayerEquipmentCommitRequest(default, VanillaPlayerItemSlotCatalog.ArmorStart, 1, 0, (short)I(row,"robeId"), 0);
                Check(!VanillaPlayerCombatEquipmentCatalog.TryBuild(new[]{wrong},in context,out _,out _),"configured wrong-role refusal");
            }
        }
        foreach(string platform in new[]{"Linux","Windows"})
        {
            using var data=Read("Contact",platform);
            foreach(var row in data.RootElement.EnumerateArray())
            {
                var random=new VanillaUnifiedRandom1458(I(row,"seed"));
                var source=row.GetProperty("before");var owner=row.GetProperty("beforeOwner");
                var combat=VanillaPlayerCombatSnapshot.Baseline with { MagicCrit=I(owner,"magicCrit"),
                    ArmorPenetration=I(owner,"armorPenetration"), MagicDamage=owner.GetProperty("magicDamage").GetSingle() };
                int beforeLife=I(row.GetProperty("beforeNpc"),"life");
                if(I(row,"immunity")==0)
                {
                    int crit=random.Next(1,101), variation=random.Next(-15,16);
                    Check(VanillaCombatFacts.TryResolveGemStaffPveHit(new(I(source,"type")),I(source,"damage"),in combat,
                        (GemStaffFeatures1458)I(row,"mask"),crit,variation,out var hit),"source live-owner gem hit facts");
                    // Original NPC1 StrikeNPC consumer in this explicit defense20/HP5000 context.
                    // This is not a substitute for actual App prepared-strike/loot/currentness proof.
                    int effectiveDefense=Math.Max(0,I(row.GetProperty("beforeNpc"),"defense")-hit.ArmorPenetration);
                    int actual=Math.Max(1,(int)(hit.Damage-effectiveDefense*.5f))*(hit.Critical?2:1);
                    Check(beforeLife-I(row.GetProperty("afterNpc"),"life")==actual,"original NPC1 applied damage");
                    _=random.NextDouble();_=random.NextDouble();
                    Check(I(row.GetProperty("after"),"penetrate")==0 && row.GetProperty("afterLocalImmunity")[0].GetInt32()==60,"source terminal penetration/local60");
                }
                else Check(beforeLife==I(row.GetProperty("afterNpc"),"life"),"blocked source local immunity nohit");
                var afterRandom=row.GetProperty("afterRandom");
                Check(Seeds(random).SequenceEqual(afterRandom.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32())) && Cursor(random)==afterRandom.GetProperty("cursor").GetUInt32(),"source hit RNG order/full56");
                Check(random.Clone().Next()==I(row,"afterNext"),"source hit following RNG");
            }
        }
        Console.WriteLine($"GEM_STAFF rows={cases} checks={checks}");
        return checks;
    }
    private static JsonDocument Read(string family,string platform)
    {
        using var input=typeof(GemStaffSource1458Checks).Assembly.GetManifestResourceStream($"GemStaff{family}{platform}1458")!;
        using var gzip=new GZipStream(input,CompressionMode.Decompress);return JsonDocument.Parse(gzip);
    }
    private static int I(JsonElement e,string name)=>e.GetProperty(name).GetInt32();
    private static bool B(JsonElement e,string name)=>e.GetProperty(name).GetBoolean();
    private static int Bits(float value)=>BitConverter.SingleToInt32Bits(value);
    private static PlayerSelectedConsumableState1458 State(JsonElement e)=>new(I(e,"itemTime"),I(e,"itemTimeMax"),I(e,"itemAnimation"),I(e,"itemAnimationMax"),B(e,"releaseUseItem"),I(e,"potionDelay"),I(e,"revolverCritChanceBonus"));
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="seedArray")]private static extern ref int[] Seeds(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inext")]private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}
