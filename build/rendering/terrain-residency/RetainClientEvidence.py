"""Retain portable production-client observations and byte-exact original PNGs."""
import argparse
import hashlib
import io
import json
import runpy
import struct
import tarfile
import zlib
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def rgba8(data):
    """Decode the frozen capture encoder's RGBA8/filter-zero PNG without changing pixels."""
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    offset, compressed, dimensions = 8, bytearray(), None
    while offset < len(data):
        size = int.from_bytes(data[offset:offset + 4], "big")
        kind = data[offset + 4:offset + 8]
        payload = data[offset + 8:offset + 8 + size]
        crc = int.from_bytes(data[offset + 8 + size:offset + 12 + size], "big")
        assert zlib.crc32(kind + payload) == crc
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", payload)
            assert depth == 8 and color == 6 and compression == filtering == interlace == 0
            assert 0 < width <= 8192 and 0 < height <= 8192
            dimensions = width, height
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break
        offset += size + 12
    assert dimensions is not None
    width, height = dimensions
    stride = width * 4
    decoder = zlib.decompressobj()
    rows = decoder.decompress(compressed, (stride + 1) * height + 1)
    assert decoder.eof and len(rows) == (stride + 1) * height
    assert all(rows[y * (stride + 1)] == 0 for y in range(height))
    return b"".join(rows[y * (stride + 1) + 1:(y + 1) * (stride + 1)] for y in range(height))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--pixels", action="store_true", help="Retain exact decoded RGBA instead of redundant PNG compression streams.")
    args = parser.parse_args()
    collector = runpy.run_path(str(Path(__file__).resolve().parents[1] / "snapshot-publication/CollectNativeEvidence.py"))["collect"]
    labels = ("baseline-a", "candidate-a", "candidate-b", "baseline-b")
    variants = ("Fixed", "Sweep", "RendererCold")
    reports, manifests = {}, {}
    for variant in variants:
        for label in labels:
            name = f"{label}-{variant}"
            root = args.root / name
            manifests[name] = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
            reports[name] = collector(root)
            assert len(reports[name]["captures"]) == (1 if variant == "RendererCold" else 8)
            for portable, original in zip(reports[name]["captures"], manifests[name]["captures"]):
                assert portable["key"] == original["key"]
                portable["imageFile"] = original["image"]
                if args.pixels:
                    portable["pixelFile"] = f"pixels/{original['pixelSha256'].lower()}.rgba"
                    portable["pixelStorage"] = "Original row-major RGBA8; decoded bytes verified against native pixelSha256. Original PNG file hash retained separately; no pixel conversion, resampling or approval."
            reports[name]["performanceAcceptance"] = "unqualified: combined process window has a compiler-server CPU observation; statistical comparisons are advisory only"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    index, seen = [], set()
    with args.output.open("xb") as raw, tarfile.open(fileobj=raw, mode="w:xz", preset=8 if args.pixels else 6) as archive:
        def add(name, data):
            assert not name.startswith(("/", "\\")) and ".." not in Path(name).parts
            if name in seen:
                return
            seen.add(name)
            if name.endswith(".json"):
                text = data.decode("utf-8-sig")
                assert not any(m in text for m in ("C:\\\\Users", "D:\\\\Code", "C:/Users", "D:/Code"))
            info = tarfile.TarInfo(name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
            index.append(dict(name=name, bytes=len(data), sha256=sha(data)))
        for name, report in reports.items():
            add(name + ".json", (json.dumps(report, indent=2) + "\n").encode("utf-8"))
        add("protocol.json", (args.root / "protocol.json").read_bytes())
        for variant in variants:
            count = 1 if variant == "RendererCold" else 8
            for number in range(count):
                for label in labels:
                    name = f"{label}-{variant}"
                    case = manifests[name]["captures"][number]
                    image = case["image"]
                    data = (args.root / name / image).read_bytes()
                    assert sha(data).upper() == case["imageSha256"]
                    if args.pixels:
                        pixels = rgba8(data)
                        assert sha(pixels).upper() == case["pixelSha256"]
                        add(f"pixels/{case['pixelSha256'].lower()}.rgba", pixels)
                    else:
                        add(f"{name}/{image}", data)
                    for motion in case.get("motionImages", []):
                        image = motion["image"]
                        data = (args.root / name / image).read_bytes()
                        assert sha(data).upper() == motion["imageSha256"]
                        add(f"{name}/{image}", data)
        data = (json.dumps(index, indent=2) + "\n").encode("utf-8")
        info = tarfile.TarInfo("index.json")
        info.size = len(data)
        archive.addfile(info, io.BytesIO(data))
    expected = {item["name"]: item for item in index}
    verified = set()
    with tarfile.open(args.output, "r|xz") as archive:
        for member in archive:
            data = archive.extractfile(member).read()
            if member.name == "index.json":
                assert json.loads(data) == index
                continue
            item = expected[member.name]
            assert len(data) == item["bytes"] and sha(data) == item["sha256"]
            verified.add(member.name)
    assert len(verified) == len(index)
    args.output.with_name(args.output.name + ".sha256").write_text(sha(args.output.read_bytes()) + "\n", encoding="utf-8")
    print(f"Verified {len(reports)} complete matrices and {len(index)} original/projected files; archive bytes={args.output.stat().st_size}.")
