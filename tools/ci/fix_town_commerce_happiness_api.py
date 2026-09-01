from pathlib import Path

p = Path('src/TerraRuntime/RuntimeTownCommerce1458.cs')
text = p.read_text()
text = text.replace(
    'return new VanillaTownHappinessResult1458(1f, false);',
    'return new VanillaTownHappinessResult1458(1f, false, 0, 0);')
old = '''        var happinessContext = new VanillaTownHappinessContext1458(
            Forest: scene.ShoppingZoneForest,
            Ocean: scene.ZoneBeach,
            Snow: scene.ZoneSnow,
            Desert: scene.ZoneDesert,
            Jungle: scene.ZoneJungle,
            Underground: scene.ShoppingZoneBelowSurface,
            Hallow: scene.ZoneHallow,
            Mushroom: scene.ZoneGlowshroom,
            Corruption: scene.ZoneCorrupt,
            Crimson: scene.ZoneCrimson,
            Dungeon: scene.ZoneDungeon,
            RemixWorld: world.RemixWorld,
            LoveStruck: false,
            Homeless: town.Homeless,
            DistanceFromHomeTiles: distanceFromHome,
            NpcsWithinHouse: house,
            NpcsWithinVillage: village);
        return VanillaTownHappiness1458.Evaluate(npcType, in happinessContext, nearby.ToArray());'''
new = '''        var happinessContext = new VanillaTownHappinessContext1458(
            RemixWorld: world.RemixWorld,
            LoveStruck: false,
            Homeless: town.Homeless,
            DistanceFromHomeTiles: distanceFromHome,
            NpcsWithinHouse: house,
            NpcsWithinVillage: village,
            Biomes: new VanillaTownHappinessBiomeState1458(
                Forest: scene.ShoppingZoneForest,
                Ocean: scene.ZoneBeach,
                Snow: scene.ZoneSnow,
                Desert: scene.ZoneDesert,
                Jungle: scene.ZoneJungle,
                Underground: scene.ShoppingZoneBelowSurface,
                Hallow: scene.ZoneHallow,
                Mushroom: scene.ZoneGlowshroom,
                Corruption: scene.ZoneCorrupt,
                Crimson: scene.ZoneCrimson,
                Dungeon: scene.ZoneDungeon));
        return VanillaTownHappiness1458.Resolve(npcType, in happinessContext, nearby.ToArray());'''
if old not in text:
    raise SystemExit('happiness context anchor drifted')
text = text.replace(old, new, 1)
p.write_text(text)
print('town commerce happiness API aligned')
