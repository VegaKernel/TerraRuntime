#!/usr/bin/env python3
from pathlib import Path


def replace_once(path: str, old: str, new: str, label: str) -> None:
    p = Path(path)
    text = p.read_text(encoding='utf-8-sig')
    if new in text:
        return
    if text.count(old) != 1:
        raise SystemExit(f'{label}: anchor missing or ambiguous')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

server = 'src/TerraRuntime/ServerRuntimeState.cs'
replace_once(server,
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcShimmerService1458? _townShimmer;',
    '    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcCombat1458? _townCombat;\n    private readonly RuntimeTownNpcShimmerService1458? _townShimmer;',
    'combat field')
replace_once(server,
    '        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts = null,\n        bool townInitialRaining = false,',
    '        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts = null,\n        RuntimeTownNpcCombatWorldFacts1458? townCombatWorldFacts = null,\n        bool townInitialRaining = false,',
    'combat constructor parameter')
replace_once(server,
    '        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts\n            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)\n            : null;\n        _housingValidator = worldTiles is not null && townNpcs is not null',
    '        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts\n            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)\n            : null;\n        _townCombat = worldTiles is not null &&\n            townNpcs is not null &&\n            _worldProgression is not null &&\n            townCombatWorldFacts is RuntimeTownNpcCombatWorldFacts1458 combatFacts\n                ? new RuntimeTownNpcCombat1458(\n                    townNpcs, _npcs, _projectiles, worldTiles, in combatFacts, _worldProgression, expertMode, masterMode)\n                : null;\n        _housingValidator = worldTiles is not null && townNpcs is not null',
    'combat initialization')
replace_once(server,
    '        if (_townMoveIn is null && _townSchedule is null)\n            return;',
    '        if (_townMoveIn is null && _townSchedule is null && _townCombat is null)\n            return;',
    'town lifecycle gate')
replace_once(server,
    '            _townSchedule.Tick(in scheduleConditions, _townPlayerBounds.AsSpan(0, boundsCount));\n        }\n    }',
    '            _townSchedule.Tick(in scheduleConditions, _townPlayerBounds.AsSpan(0, boundsCount));\n        }\n\n        _townCombat?.Tick();\n    }',
    'town combat tick')

host = 'src/TerraRuntime/TerrariaServerHost.cs'
replace_once(host,
    '            townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townInitialRaining: world.RuntimeMetadata.Raining,',
    '            townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townCombatWorldFacts: RuntimeTownNpcCombatWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townInitialRaining: world.RuntimeMetadata.Raining,',
    'host combat facts')

roadmap = Path('docs/roadmap/npc-ai-parity.md')
text = roadmap.read_text(encoding='utf-8-sig')
note = '  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, and an authoritative projectile-combat slice for Merchant/Nurse/Arms Dealer/Guide are implemented; social/emote/melee/special town branches remain open;'
if note not in text:
    anchor = '- [ ] town AI, housing and schedules;'
    if text.count(anchor) != 1:
        raise SystemExit('roadmap town AI anchor missing or ambiguous')
    text = text.replace(anchor, anchor + '\n' + note, 1)
    roadmap.write_text(text, encoding='utf-8')

print('Town NPC combat rebased integration applied')
