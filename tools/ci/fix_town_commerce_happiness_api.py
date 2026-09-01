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

p = Path('src/TerraRuntime/VanillaHousingValidator1458.cs')
text = p.read_text()
old = '''                WorldTile tile = tiles.Get(x, y);
                if (tile.IsActive && tile.Type is 70 or 71 or 72 or 528 && ++mushroomTiles >= 100)
                    return true;'''
new = '''                WorldTile tile = tiles.Get(x, y);
                bool mushroom = tile.Type == 70 || tile.Type == 71 || tile.Type == 72 || tile.Type == 528;
                if (tile.IsActive && mushroom)
                {
                    mushroomTiles++;
                    if (mushroomTiles >= 100)
                        return true;
                }'''
if old not in text:
    raise SystemExit('truffle mushroom predicate anchor drifted')
text = text.replace(old, new, 1)
old_tail = '''        return false;
    }

    private int CalculateBaseRoomScore'''
new_tail = '''        throw new InvalidOperationException($"Truffle mushroom debug count={mushroomTiles}; bounds={startX},{endX},{startY},{endY}; roomY2={roomY2}; surface={worldSurface}");
    }

    private int CalculateBaseRoomScore'''
if old_tail not in text:
    raise SystemExit('truffle diagnostic tail anchor drifted')
text = text.replace(old_tail, new_tail, 1)
p.write_text(text)
print('town commerce happiness API aligned; Truffle mushroom scan instrumented')
