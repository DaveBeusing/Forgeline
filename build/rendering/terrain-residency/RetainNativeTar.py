"""Lossless native archive, grouping equal camera cases for cross-file compression.

No pixels are transformed. Every decompressed original file is hash-verified.
The archive uses ordinary tar/xz with no machine paths or user metadata.
"""
import argparse
import hashlib
import io
import json
import tarfile
from pathlib import Path


def digest(data):
    return hashlib.sha256(data).hexdigest()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    labels = ("baseline-a", "candidate-a", "candidate-b", "baseline-b")
    reports = [json.loads((args.root / f"{label}.json").read_text(encoding="utf-8-sig")) for label in labels]
    assert all(r["Mode"] == "native" and len(r["Results"]["Cases"]) == 18 for r in reports)
    paths = [args.root / f"{label}.json" for label in labels] + [args.root / "protocol.json"]
    for cases in zip(*(r["Results"]["Cases"] for r in reports)):
        assert len({(c["Site"], c["Distance"], c["Moving"]) for c in cases}) == 1
        for case in cases:
            assert case["RepeatPixelsEqual"]
            name = case["Image"]
            assert Path(name).name == name
            paths.append(args.root / name)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    index = []
    with args.output.open("xb") as raw, tarfile.open(fileobj=raw, mode="w:xz", preset=6) as archive:
        for path in paths:
            data = path.read_bytes()
            if path.suffix == ".json":
                text = data.decode("utf-8-sig")
                assert not any(m in text for m in ("C:\\\\Users", "D:\\\\Code", "C:/Users", "D:/Code"))
            info = tarfile.TarInfo(path.name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
            index.append(dict(name=path.name, bytes=len(data), sha256=digest(data)))
        data = (json.dumps(index, indent=2) + "\n").encode("utf-8")
        info = tarfile.TarInfo("index.json")
        info.size = len(data)
        archive.addfile(info, io.BytesIO(data))
    expected = {item["name"]: item for item in index}
    verified = set()
    with tarfile.open(args.output, "r|xz") as archive:
        for member in archive:
            assert member.isfile() and Path(member.name).name == member.name
            data = archive.extractfile(member).read()
            if member.name == "index.json":
                assert json.loads(data) == index
                continue
            item = expected[member.name]
            assert len(data) == item["bytes"] and digest(data) == item["sha256"]
            verified.add(member.name)
    assert len(verified) == len(index)
    args.output.with_name(args.output.name + ".sha256").write_text(digest(args.output.read_bytes()) + "\n", encoding="utf-8")
    print(f"Reconstructed and verified {len(index)} original files; archive bytes={args.output.stat().st_size}.")
