"""Retain selected asset-residency records with hashes and explicit path redaction."""
from __future__ import annotations
import gzip
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ARTIFACTS = ROOT / "artifacts"
OUTPUT = ROOT / "docs/rendering/asset-residency/evidence"
TEXT_SUFFIXES = {".json", ".jsonl", ".log", ".csv", ".ps1"}

def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def redact(data: bytes) -> bytes:
    text = data.decode("utf-8-sig")
    # Both ordinary logs and JSON-escaped Windows paths can contain a user profile.
    text = re.sub(r"(?i)[a-z]:[\\/]+Users[\\/]+[^\\/\r\n\" ]+", "<user-profile>", text)
    text = re.sub(r"(?i)<user-profile>[\\/]+[.][^\\/\r\n\" ]+(?:[\\/]+[^\r\n\" ]*)?", "<external-tooling>", text)
    return text.encode("utf-8")

def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    selected = []
    for path in ARTIFACTS.iterdir():
        if not path.name.startswith("asset-residency-"):
            continue
        if path.is_file() and path.suffix in TEXT_SUFFIXES:
            selected.append(path)
        elif path.is_dir() and ("-native-" in path.name or "-probe-" in path.name):
            selected.extend(p for p in path.rglob("*") if p.is_file()
                            and not {"bin", "obj"}.intersection(p.relative_to(path).parts)
                            and (p.suffix in TEXT_SUFFIXES or p.suffix == ".png"))
        elif path.is_dir() and "-package-" in path.name and path.name.endswith("-smoke"):
            selected.extend(p for p in path.iterdir() if p.is_file() and p.suffix in TEXT_SUFFIXES)
        elif path.is_dir() and "-package-" in path.name:
            selected.extend(p for p in path.rglob("*") if p.is_file() and p.name in
                            {"build-provenance.json", "asset-qualification.json", "asset-compilation.jsonl"})
    records = []
    for source in sorted(set(selected)):
        original = source.read_bytes()
        retained = original if source.suffix == ".png" else redact(original)
        digest = sha(retained)
        destination = OUTPUT / "objects" / (digest + (".png" if source.suffix == ".png" else ".gz"))
        destination.parent.mkdir(parents=True, exist_ok=True)
        payload = retained if source.suffix == ".png" else gzip.compress(retained, mtime=0)
        destination.write_bytes(payload)
        records.append({"source": source.relative_to(ROOT).as_posix(),
                        "originalBytes": len(original), "originalSha256": sha(original),
                        "retainedBytes": len(retained), "retainedSha256": digest,
                        "redacted": retained != original, "object": destination.relative_to(OUTPUT).as_posix(),
                        "storedSha256": sha(payload)})
    index = {"schemaVersion": 1,
             "method": "PNG bytes retained unchanged. Text decoded as UTF-8, BOM removed and private user-profile/tool-runtime paths redacted; original and retained hashes are distinct. Text is gzip-compressed with mtime zero. No timing samples, image pixels or assertion results edited.",
             "records": records}
    (OUTPUT / "index.json").write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
    print(f"Retained {len(records)} records in {len(set(r['object'] for r in records))} content-addressed objects.")

if __name__ == "__main__":
    main()
