"""Retain portable raw cohorts and unmodified readback files in a lossless archive."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError("Retained evidence is immutable.")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    files = []
    for label in ("baseline-a", "candidate-a", "candidate-b", "baseline-b"):
        path = args.root / f"{label}.json"
        report = json.loads(path.read_text(encoding="utf-8-sig"))
        assert report["SourceVersion"].split("+")[-1].isalnum()
        files.append(path)
        if report["Mode"] == "native":
            assert len(report["Results"]["Cases"]) == 18
            for case in report["Results"]["Cases"]:
                assert case["RepeatPixelsEqual"]
                image = Path(case["Image"])
                assert image.name == str(image), "Portable file name required"
                files.append(args.root / image)
    files.append(args.root / "protocol.json")
    index = []
    with zipfile.ZipFile(args.output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            data = path.read_bytes()
            # Own probe reports contain no source/runtime paths. Refuse accidental
            # path-bearing exports rather than silently rewriting raw measurements.
            if path.suffix == ".json":
                text = data.decode("utf-8-sig")
                assert not any(marker in text for marker in ("C:\\\\Users", "D:\\\\Code", "C:/Users", "D:/Code"))
            archive.writestr(path.name, data)
            index.append(dict(name=path.name, bytes=len(data), sha256=sha(data)))
        archive.writestr("index.json", json.dumps(index, indent=2) + "\n")
    with zipfile.ZipFile(args.output) as archive:
        assert archive.testzip() is None
        for item in index:
            assert sha(archive.read(item["name"])) == item["sha256"]
    args.output.with_suffix(".sha256").write_text(sha(args.output.read_bytes()) + "\n", encoding="utf-8")
    print(f"Retained and verified {len(index)} raw files; {args.output.stat().st_size} archive bytes.")
