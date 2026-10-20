"""Portable lineage ignores only checkout CRLF conversion in JSON/glTF text."""
import hashlib
import json


def portable_signature(catalog, ids):
    records = {}
    for path in catalog.files(ids):
        content = path.read_bytes()
        if path.suffix.lower() in ('.json', '.gltf'):
            content = content.replace(b'\r\n', b'\n')
        records[path.relative_to(catalog.root).as_posix()] = hashlib.sha256(content).hexdigest()
    return hashlib.sha256(json.dumps(records, sort_keys=True).encode()).hexdigest()
