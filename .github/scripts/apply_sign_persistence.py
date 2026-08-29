from pathlib import Path

path = Path("src/TerraRuntime/TerrariaServerHost.cs")
text = path.read_text(encoding="utf-8")

if "            signStore: signStore);" in text:
    print("sign persistence host wiring already applied")
    raise SystemExit(0)

old = '''        var worldSaveService = new RuntimeWorldTileChestSaveService(
            options.WorldPath,
            world.Envelope,
            world.Header,
            worldSaveTemplate,
            world.Tiles,
            chestStore,
            worldClock: worldClock);
'''
new = '''        var worldSaveService = new RuntimeWorldTileChestSaveService(
            options.WorldPath,
            world.Envelope,
            world.Header,
            worldSaveTemplate,
            world.Tiles,
            chestStore,
            worldClock: worldClock,
            signStore: signStore);
'''

count = text.count(old)
if count != 1:
    raise SystemExit(f"expected exactly one world save service construction, found {count}")

path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("applied sign persistence host wiring")
