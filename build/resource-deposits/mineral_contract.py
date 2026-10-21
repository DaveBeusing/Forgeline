"""Portable mineral master signatures; retain raw hashes for source-change checks."""
import hashlib
import json
from asset_contract import Catalog as BaseCatalog


class MineralCatalog(BaseCatalog):
    def signature(self, ids):
        raw, portable = {}, {}
        for path in self.files(ids):
            key = path.relative_to(self.root).as_posix()
            content = path.read_bytes()
            raw[key] = hashlib.sha256(content).hexdigest()
            if path.suffix.lower() in ('.json', '.gltf'):
                content = content.replace(b'\r\n', b'\n')
            portable[key] = hashlib.sha256(content).hexdigest()
        return hashlib.sha256(json.dumps(portable, sort_keys=True).encode()).hexdigest(), raw
