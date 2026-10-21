"""Portable prop master signatures; retain raw hashes for source-change checks."""
import hashlib
import json
from pathlib import Path
from asset_contract import Catalog as BaseCatalog


class PropCatalog(BaseCatalog):
    _frozen = None

    @classmethod
    def freeze(cls, root):
        cls._frozen = None
        catalog = cls(root)
        catalog._definition_hashes = {aid: hashlib.sha256(path.read_bytes()).hexdigest()
            for aid, (path, _) in catalog.definitions.items()}
        cls._frozen = catalog

    def __init__(self, root):
        frozen = type(self)._frozen
        if frozen is not None and Path(root).resolve() == frozen.root:
            self.root = frozen.root
            self.definitions = frozen.definitions
            self._definition_hashes = frozen._definition_hashes
        else:
            super().__init__(root)
            self._definition_hashes = None

    def signature(self, ids):
        if self._definition_hashes is not None:
            for aid in ids:
                actual = hashlib.sha256(self.definitions[aid][0].read_bytes()).hexdigest()
                if actual != self._definition_hashes[aid]:
                    raise ValueError("Canonical definition changed during frozen batch: " + aid)
        raw, portable = {}, {}
        for path in self.files(ids):
            key = path.relative_to(self.root).as_posix()
            content = path.read_bytes()
            raw[key] = hashlib.sha256(content).hexdigest()
            if path.suffix.lower() in ('.json', '.gltf'):
                content = content.replace(b'\r\n', b'\n')
            portable[key] = hashlib.sha256(content).hexdigest()
        return hashlib.sha256(json.dumps(portable, sort_keys=True).encode()).hexdigest(), raw
