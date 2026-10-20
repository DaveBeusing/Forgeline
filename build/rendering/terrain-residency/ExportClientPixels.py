"""Export viewable PNGs from retained exact client pixels, without accepting goldens."""
import argparse
import hashlib
import json
import struct
import tarfile
import zlib
from pathlib import Path
from RetainClientEvidence import rgba8


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--case", help="Exact capture key; omit to export all retained views.")
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError("Use a fresh preview directory.")
    reports, pixels = {}, {}
    with tarfile.open(args.archive, "r|xz") as archive:
        for member in archive:
            assert member.isfile() and member.size <= 64 * 1024 * 1024
            data = archive.extractfile(member).read()
            if member.name.startswith("pixels/"):
                pixels[member.name] = data
            elif member.name.endswith(".json") and member.name not in ("index.json", "protocol.json"):
                reports[Path(member.name).stem] = json.loads(data)
    exported = []
    for name, report in reports.items():
        for capture in report["captures"]:
            if args.case and args.case != capture["key"]:
                continue
            data = pixels[capture["pixelFile"]]
            assert hashlib.sha256(data).hexdigest().upper() == capture["pixelSha256"]
            width, height = capture["actualSurface"]["width"], capture["actualSurface"]["height"]
            assert 0 < width <= 8192 and 0 < height <= 8192 and len(data) == width * height * 4
            stride = width * 4
            rows = b"".join(b"\0" + data[y * stride:(y + 1) * stride] for y in range(height))
            png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            png += chunk(b"IDAT", zlib.compress(rows, 6)) + chunk(b"IEND", b"")
            assert rgba8(png) == data
            path = args.output / name / Path(capture["imageFile"]).name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(png)
            exported.append(dict(file=path.relative_to(args.output).as_posix(), pixelSha256=capture["pixelSha256"],
                                 originalPngSha256=capture["imageSha256"], exportedPngSha256=hashlib.sha256(png).hexdigest()))
    assert exported, "No retained capture matches."
    (args.output / "export.json").write_text(json.dumps(dict(
        policy="Exact native pixels; re-encoded PNG container can differ. No resampling, colour conversion or approval.",
        captures=exported), indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(exported)} images with exact native pixel hashes.")
