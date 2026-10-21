import sys, shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools/blender'))
from asset_contract import Catalog,fixture_selection,read_geometry,write_glb,write_json
out=ROOT/'artifacts/se'
shutil.copytree(ROOT/'artifacts/sv',out)
# Diagnostic only: retain real baseline/first/reopened records; replace second
# with a canonical-float GLB representation of the already validated source.
shutil.copytree(ROOT/'artifacts/sc2/first/source',out/'source')
canonical=Catalog(ROOT/'assets/source')
copy=Catalog(out/'source')
ids,families=fixture_selection(canonical,ROOT/'build/scout/fixture.json',[])
for aid in families[0]['assets']:
    write_glb(copy.source(aid),read_geometry(canonical.source(aid),canonical.root,canonical.definitions[aid][1]))
write_json(out/'experiment.json',{'diagnosticOnly':True,'secondRuntimeOrigin':'canonical-float GLB reserialization, not independent Blender export','sourceSha':'20b00ace08089ad44798941a4cfc5668ea2ef06c'})
