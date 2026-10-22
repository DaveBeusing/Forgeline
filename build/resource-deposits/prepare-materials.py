"""Prepare one shared, mesh-free material source catalog for native qualification."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, require, write_json

target = Path(sys.argv[1]).resolve()
require(target.is_relative_to(ROOT / 'artifacts'), 'Material output must remain inside artifacts')
require(not target.exists(), 'Use fresh material source output')
catalog = Catalog(ROOT / 'assets/source')
names = ('ferrous_ore', 'silicates', 'volatiles', 'rare_elements')
ids = catalog.closure(['material.world.resource.' + name for name in names])
require(len(ids) == 16 and
        sum(catalog.definitions[aid][1]['type'] == 'material' for aid in ids) == 4 and
        sum(catalog.definitions[aid][1]['type'] == 'texture' for aid in ids) == 12,
        'Expected four mineral materials and twelve family-specific textures')
signature, hashes = catalog.signature(ids)
catalog.copy_to(ids, target)
require(catalog.signature(ids)[0] == signature, 'Canonical material source changed')
write_json(target / 'material-source.json', {'schemaVersion': 1, 'assets': ids, 'sourceHashes': hashes})
print('Prepared four mineral materials and twelve family-specific textures without mesh records')
