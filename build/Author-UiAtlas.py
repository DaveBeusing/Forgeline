"""Deterministically pack validated Directorate sources; player consumes compiled containers only."""
import hashlib
import json
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "assets/source/ui/directorate"
OUT = BASE / "atlas"
SIZE, PAD = 1024, 2


def write(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    OUT.mkdir(exist_ok=True)
    groups = {"core": [], "icons": [], "portraits": []}
    for folder, catalog in [(BASE, "skin.catalog.json"), (BASE / "artwork", "artwork.catalog.json")]:
        for sprite in json.loads((folder / catalog).read_text())["sprites"]:
            if sprite.get("availability", "runtime_required") != "runtime_required":
                continue
            path = folder / sprite["file"]
            assert hashlib.sha256(path.read_bytes()).hexdigest() == sprite["sha256"]
            group = "core" if folder == BASE else "portraits" if sprite["logicalRole"].startswith("portrait.") else "icons"
            groups[group].append((sprite, path))
    pages, sprites, lineage = [], [], []
    for group, entries in groups.items():
        page, x, y, row, index = None, 0, 0, 0, 0

        def finish():
            if page is not None:
                name = f"{group}_{index}.png"
                page.save(OUT / name, optimize=False)
                write(OUT / f"{group}_{index}.asset.json", {
                    "id": pages[-1]["id"], "type": "texture", "source": name,
                    "textureUsage": "displayImage", "textureColorSpace": "linear",
                    "textureGenerateMipmaps": False, "textureFormat": "bc7Unorm" if group == "portraits" else "rgba8Unorm",
                    **({"textureCompressionQuality": "balanced"} if group == "portraits" else {})})

        for entry, path in sorted(entries, key=lambda e: e[0]["id"]):
            image = Image.open(path).convert("RGBA")
            w, h = image.size
            assert (w, h) == (entry["width"], entry["height"])
            assert w + PAD * 2 <= SIZE and h + PAD * 2 <= SIZE
            if x + w + PAD * 2 > SIZE:
                x, y, row = 0, y + row, 0
            if page is None or y + h + PAD * 2 > SIZE:
                finish()
                index += 1
                page, x, y, row = Image.new("RGBA", (SIZE, SIZE)), 0, 0, 0
                pages.append({"id": f"ui.atlas.directorate.{group}.p{index}", "width": SIZE, "height": SIZE, "group": group})
            # Extrude source boundary pixels, with no color conversion or alpha multiplication.
            page.paste(image, (x + PAD, y + PAD))
            for offset in range(PAD):
                page.paste(image.crop((0, 0, w, 1)), (x + PAD, y + offset))
                page.paste(image.crop((0, h - 1, w, h)), (x + PAD, y + PAD + h + offset))
                page.paste(image.crop((0, 0, 1, h)), (x + offset, y + PAD))
                page.paste(image.crop((w - 1, 0, w, h)), (x + PAD + w + offset, y + PAD))
            for dx, sx in [(0, 0), (PAD + w, w - 1)]:
                for dy, sy in [(0, 0), (PAD + h, h - 1)]:
                    page.paste(image.getpixel((sx, sy)), (x + dx, y + dy, x + dx + PAD, y + dy + PAD))
            inset = lambda values: dict(zip(["left", "top", "right", "bottom"], values))
            hotspot = entry.get("hotspot", [-1, -1])
            sprites.append({"id": entry["id"], "page": len(pages) - 1, "x": x + PAD, "y": y + PAD,
                "width": w, "height": h, "logicalWidth": w, "logicalHeight": h, "padding": PAD,
                "insets": inset(entry["insets"]), "contentPadding": inset(entry["contentPadding"]),
                "nineSlice": entry["scaling"] == "nine_slice", "hotspotX": hotspot[0], "hotspotY": hotspot[1]})
            lineage.append({"id": entry["id"], "source": path.relative_to(BASE).as_posix(), "sha256": entry["sha256"]})
            x += w + PAD * 2
            row = max(row, h + PAD * 2)
        finish()
    write(OUT / "directorate.skin.json", {"schemaVersion": 1, "pages": pages, "sprites": sprites})
    write(OUT / "directorate.asset.json", {"id": "ui.skin.directorate.compiled", "type": "uiSkin", "source": "directorate.skin.json"})
    write(OUT / "lineage.json", {"schemaVersion": 1, "packing": "ordinal ID shelves; group isolation; 1024 square RGBA; two-pixel extrusion; mipless", "sources": lineage})
    print(f"Packed {len(sprites)} sprites into {len(pages)} pages; {len(pages) * SIZE * SIZE * 4} decoded bytes.")


if __name__ == "__main__":
    main()
