"""Retain selected resource-scheduling records with hashes and explicit path redaction."""
from __future__ import annotations
import gzip
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ARTIFACTS = ROOT / "artifacts"
OUTPUT = ROOT / "docs/rendering/resource-scheduling/evidence"
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
        if path.is_file() and (path.name.startswith("gpu-lifecycle-") or path.name.startswith("run-gpu-lifecycle-")):
            if path.suffix in TEXT_SUFFIXES and path.name != "gpu-lifecycle-pr-body.txt":
                selected.append(path)
        elif path.is_dir() and path.name.startswith("gpu-lifecycle-") and ("-native-" in path.name or path.name.endswith("-native") or "-upload-" in path.name or path.name == "gpu-lifecycle-probe-validation"):
            selected.extend(p for p in path.iterdir() if p.is_file() and (p.suffix in TEXT_SUFFIXES or p.suffix == ".png"))
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


def static_geometry_archive(extract: bool = False) -> None:
    """Keep this lane's immutable JSON inside its delegated qualification document."""
    import base64
    import io
    import sys
    import zipfile

    document = ROOT / "docs/rendering/resource-scheduling/StaticGeometryQualification.md"
    marker = "<!-- static-geometry-evidence-v1 -->"
    text = document.read_text(encoding="utf-8")
    if extract:
        section = text.split(marker, 1)[1]
        expected = re.search(r"Evidence archive SHA-256: `([0-9a-f]{64})`", section).group(1)
        encoded = section.split("```base64\n", 1)[1].split("\n```", 1)[0]
        payload = base64.b64decode(encoded, validate=True)
        if sha(payload) != expected:
            raise ValueError("Evidence archive hash mismatch")
        destination = ARTIFACTS / "static-geometry-retained"
        destination.mkdir(exist_ok=False)
        with zipfile.ZipFile(io.BytesIO(payload)) as archive:
            for entry in archive.infolist():
                target = (destination / entry.filename).resolve()
                if not target.is_relative_to(destination.resolve()):
                    raise ValueError("Archive entry escapes destination")
            archive.extractall(destination)
        index = json.loads((destination / "index.json").read_bytes())
        for record in index["records"]:
            if sha((destination / record["path"]).read_bytes()) != record["sha256"]:
                raise ValueError("Retained record hash mismatch")
        print(f"Verified and extracted {len(index['records'])} records to {destination.name}.")
        return

    source_root = ARTIFACTS / "static-geometry"
    selected = sorted(p for p in source_root.rglob("*.json") if
                      p.name == "measurement.json" or
                      (p.name == "manifest.json" and p.parent.name.startswith("matched-")) or
                      (p.name.endswith(".provenance.json") and p.name.startswith("matched-")) or
                      "comparison" in p.name or p.name == "validation.json")
    if not selected:
        raise ValueError("No static geometry evidence")
    data = {p.relative_to(source_root).as_posix(): p.read_bytes() for p in selected}
    index = {"schemaVersion": 1, "method": "Original JSON bytes retained unchanged, compressed losslessly in this document; no samples removed or relabelled.",
             "records": [{"path": name, "bytes": len(raw), "sha256": sha(raw)} for name, raw in data.items()]}
    data["index.json"] = (json.dumps(index, indent=2) + "\n").encode()
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, raw in data.items():
            entry = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, raw)
    payload = buffer.getvalue()
    prefix = text.split(marker, 1)[0].rstrip()
    encoded = base64.b64encode(payload).decode("ascii")
    document.write_text(prefix + "\n\n" + marker + "\n\n## Retained raw evidence\n\n"
                        + "Lossless ZIP embedded here to keep evidence inside this package's exact write scope. "
                        + "Run `python build/rendering/resource-scheduling/Retain-Evidence.py --extract-static-geometry` "
                        + "to verify the archive and each indexed record, then extract to a fresh `artifacts/static-geometry-retained` directory.\n\n"
                        + f"Evidence archive SHA-256: `{sha(payload)}`\n\n"
                        + f"Records: {len(index['records'])}; archive bytes: {len(payload)}.\n\n```base64\n{encoded}\n```\n", encoding="utf-8", newline="\n")
    print(f"Embedded {len(index['records'])} immutable JSON records ({len(payload)} compressed bytes).")

if __name__ == "__main__":
    import sys
    if "--static-geometry" in sys.argv: static_geometry_archive()
    elif "--extract-static-geometry" in sys.argv: static_geometry_archive(extract=True)
    else: main()
