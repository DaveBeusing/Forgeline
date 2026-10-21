"""Copy only the shared industrial PBR dependency closure for independent compiles."""
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/blender'))
from asset_contract import Catalog, require
output = Path(sys.argv[1]).resolve()
require(output.is_relative_to(ROOT / 'artifacts') and not output.exists(), 'Use a fresh artifacts directory')
catalog = Catalog(ROOT / 'assets/source')
ids = catalog.closure(['material.world.prop.industrial'])
require(len(ids) == 4 and all(catalog.definitions[i][1]['type'] != 'mesh' for i in ids), 'Shared material closure changed')
catalog.copy_to(ids, output)
print('Prepared one industrial material and three unchanged PBR textures')
