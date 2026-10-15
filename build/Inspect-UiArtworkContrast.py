"""Inspect unchanged source sprites over native terrain at actual pixel sizes.

Composites are authoring inspection aids, not integrated GPU UI captures.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets/source/ui/directorate/artwork"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists() and any(args.output.iterdir()):
        raise ValueError("Inspection output must be empty; retained evidence is never replaced.")
    manifest = json.loads((args.native_root / "manifest.json").read_text())
    assert manifest["status"] == "structural-pass" and not manifest["software"]
    case = manifest["captures"][0]
    assert case["repeatPixelsEqual"]
    image_path = args.native_root / case["image"]
    assert sha(image_path) == case["imageSha256"].lower()
    image = Image.open(image_path).convert("RGBA")
    assert image.size == (1920, 1080)
    args.output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(image_path, args.output / "native-background.png")
    shutil.copyfile(args.native_root / "manifest.json", args.output / "native-manifest.json")
    # Viewed native fixture regions: open brighter ground and a cast shadow.
    regions = [("bright-ground", (220, 70, 296, 146)), ("shadow-ground", (628, 344, 704, 420))]
    tiles = []
    for name, bounds in regions:
        tile = image.crop(bounds)
        tile.save(args.output / (name + ".png"))
        tiles.append(tile)
    catalog = json.loads((SOURCE / "artwork.catalog.json").read_text())
    sprites = catalog["sprites"]
    font = ImageFont.load_default(size=12)
    for family in sorted({s["logicalRole"].split(".")[0] for s in sprites} - {"portrait"}):
        roles = sorted({s["logicalRole"] for s in sprites if s["logicalRole"].startswith(family + ".")})
        sheet = Image.new("RGB", (1070, 40 + len(roles) * 84), "#273033")
        draw = ImageDraw.Draw(sheet)
        draw.text((8, 8), "Source contrast composite; native 16/24/32/48/64 pixels, bright/shadow terrain; no live UI integration.", font=font, fill="white")
        for index, role in enumerate(roles):
            y = 40 + index * 84
            draw.text((8, y + 8), role, font=font, fill="white")
            x = 270
            for entry in sorted([s for s in sprites if s["logicalRole"] == role], key=lambda s: s["width"]):
                sprite_path = SOURCE / entry["file"]
                assert sha(sprite_path) == entry["sha256"]
                sprite = Image.open(sprite_path).convert("RGBA")
                for tile in tiles:
                    background = tile.copy()
                    background.alpha_composite(sprite, ((76 - sprite.width) // 2, (76 - sprite.height) // 2))
                    sheet.paste(background.convert("RGB"), (x, y))
                    x += 79
        sheet.save(args.output / (family + ".png"))
    files = [{"file": p.name, "sha256": sha(p), "bytes": p.stat().st_size} for p in sorted(args.output.glob("*.png"))]
    (args.output / "evidence.json").write_text(json.dumps({
        "schemaVersion": 1, "method": "Source sprite compositing over unchanged native-size terrain crops; inspection only.",
        "sourceCommit": manifest["commit"], "runtimeFingerprint": manifest["runtimeFingerprint"],
        "nativeCase": case["key"], "nativeManifestSha256": sha(args.native_root / "manifest.json"),
        "regions": regions, "files": files, "humanReview": "pending", "integratedGpuUiAcceptance": "not_in_scope",
        "exceptions": "Patrol remains unavailable; 16-pixel cursors remain inspection-only."
    }, indent=2) + "\n", encoding="utf-8", newline="\n")
    count = sum(not s["logicalRole"].startswith("portrait.") for s in sprites)
    print(f"Inspected {count} pictogram variants over two actual native terrain regions.")


if __name__ == "__main__":
    main()
