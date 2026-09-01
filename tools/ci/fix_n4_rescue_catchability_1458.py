from pathlib import Path

p = Path('src/TerraRuntime.Core/Npcs/VanillaNpcCatchCatalog1458.cs')
text = p.read_text()
text = text.replace('if (!itemType.IsAssigned)', 'if (itemType.IsNone)')
p.write_text(text)

p = Path('src/TerraRuntime/RuntimeTownNpcRescueService1458.cs')
text = p.read_text()
text = text.replace(
    'Terraria.Protocol.Multiplicity.TerrariaNpcTalkCodec.MaximumNpcSlots',
    'TerraRuntime.Protocol.Multiplicity.TerrariaNpcTalkCodec.MaximumNpcSlots')
p.write_text(text)
print('N4 compatibility fixes applied; workflow trigger v2')
