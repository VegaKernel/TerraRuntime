from pathlib import Path

for path in (
    Path('.github/workflows/one-shot-town-commerce-housing-1458.yml'),
    Path('tools/ci/apply_town_commerce_housing_1458.py'),
    Path('tools/ci/fix_town_commerce_happiness_api.py'),
    Path('town-commerce-ci-diagnostics.txt'),
):
    if path.exists():
        raise SystemExit(f'temporary integration artifact remains: {path}')
print('town commerce branch is free of one-shot artifacts')
