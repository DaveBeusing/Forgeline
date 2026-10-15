"""Author text-free, native-size Directorate skin primitives (Python + Pillow).

Each editable SVG and PNG is emitted from the same integer-coordinate primitives.
Reference sheets are direction only: no pixels, labels, marks or game values are copied.
This tool is authoring-only; the existing Asset Compiler owns runtime compilation.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets/source/ui/directorate"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def palette():
    source = (ROOT / "src/ForgeLine.Presentation/ForgeLineUiPalette.cs").read_text()
    values = {}
    for name, channels in re.findall(r"Vector4 (\w+) = new\(([^)]+)\)", source):
        values[name] = tuple(round(float(c.strip().rstrip("f")) * 255) for c in channels.split(","))
    return values


class Drawing:
    def __init__(self, width, height):
        self.image = Image.new("RGBA", (width, height))
        self.draw = ImageDraw.Draw(self.image)
        self.svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">']

    def polygon(self, points, color):
        self.draw.polygon(points, fill=color)
        coordinates = " ".join(f"{x},{y}" for x, y in points)
        self.svg.append(f'<polygon points="{coordinates}" fill="rgb{color[:3]}" fill-opacity="{color[3] / 255:.8f}"/>')

    def rect(self, x, y, width, height, color):
        if width > 0 and height > 0:
            self.polygon([(x, y), (x + width - 1, y), (x + width - 1, y + height - 1), (x, y + height - 1)], color)

    def line(self, points, color, width=1):
        self.draw.line(points, fill=color, width=width)
        coordinates = " ".join(f"{x},{y}" for x, y in points)
        self.svg.append(f'<polyline points="{coordinates}" fill="none" stroke="rgb{color[:3]}" stroke-opacity="{color[3] / 255:.8f}" stroke-width="{width}" stroke-linecap="square"/>')


def outline(width, height, inset, cut):
    l, t, r, b = inset, inset, width - 1 - inset, height - 1 - inset
    cut = min(cut, (r - l) // 2, (b - t) // 2)
    return [(l + cut, t), (r - cut, t), (r, t + cut), (r, b - cut),
            (r - cut, b), (l + cut, b), (l, b - cut), (l, t + cut), (l + cut, t)]


def author(name, width, height, kind, colors, compact=False):
    d = Drawing(width, height)
    border, subtle = colors["Border"], colors["BorderSubtle"]
    if kind == "overlay":
        # Orthogonal state cues compose without repainting the text/value interior.
        if name == "hover":
            d.line([(10, 3), (width - 11, 3)], colors["TextSecondary"])
            d.line([(3, 10), (3, height - 11)], colors["TextSecondary"])
        elif name == "pressed":
            d.line(outline(width, height, 4, 3), colors["BackgroundDeep"], 2)
            d.line([(10, height - 5), (width - 11, height - 5)], border)
        elif name == "focus":
            color = colors["TextPrimary"]
            for x, y, sx, sy in [(2, 2, 1, 1), (width - 3, 2, -1, 1),
                                  (2, height - 3, 1, -1), (width - 3, height - 3, -1, -1)]:
                d.line([(x, y + sy * 7), (x, y), (x + sx * 7, y)], color, 2)
        elif name == "selected":
            d.rect(7, height - 5, width - 14, 2, colors["Accent"])
        elif name == "disabled":
            d.polygon(outline(width, height, 2, 4), (*colors["BackgroundDeep"][:3], 118))
            for y in range(8, height - 6, 6):
                d.line([(5, y), (9, y - 3)], colors["TextDisabled"])
        elif name in ("warning", "error", "pending", "accepted"):
            color = colors[{"warning": "Warning", "error": "Critical", "pending": "TextSecondary", "accepted": "Accent"}[name]]
            d.rect(3, 8, 2, height - 16, color)
            if name == "error":
                d.rect(7, 8, 2, height - 16, color)
            elif name == "pending":
                for y in range(8, height - 8, 6):
                    d.rect(3, y, 2, 3, colors["BackgroundDeep"])
    elif name == "checkmark":
        d.line([(6, 12), (10, 16), (18, 7)], colors["Accent"], 3)
    elif name == "toggle_thumb":
        d.polygon(outline(width, height, 2, 3), colors["TextSecondary"])
        d.line([(8, 6), (8, height - 7)], subtle)
        d.line([(11, 6), (11, height - 7)], subtle)
    elif name == "progress_fill":
        # Neutral coverage, multiplied by the semantic bar tint by the future renderer.
        d.rect(1, 1, width - 2, height - 2, (255, 255, 255, 255))
    elif name == "separator":
        d.rect(1, 1, width - 2, 1, border)
    else:
        cut = 3 if compact or height < 32 else 6
        hollow = name in ("portrait", "minimap")
        if not hollow:
            fill = colors["SurfaceRaised" if name in ("raised", "button", "button_compact", "tab", "row", "row_compact") else "Surface"]
            if name in ("inset", "input", "dropdown", "progress_track", "slider_track", "scrollbar_track"):
                fill = colors["BackgroundDeep"]
            d.polygon(outline(width, height, 2, cut), fill)
        d.line(outline(width, height, 2, cut), border)
        d.line(outline(width, height, 4, max(1, cut - 1)), subtle)
        d.line([(cut + 3, 3), (width - cut - 4, 3)], colors["TextMuted"])
        if name in ("primary", "dialog", "portrait", "minimap", "banner"):
            for x, y, sx, sy in [(5, 5, 1, 1), (width - 6, 5, -1, 1),
                                  (5, height - 6, 1, -1), (width - 6, height - 6, -1, -1)]:
                d.line([(x, y + sy * 8), (x + sx * 8, y)], colors["TextSecondary"])
            # Warm metal is restricted to a corner, keeping text interiors quiet.
            d.line([(width - 19, height - 4), (width - 10, height - 4)], colors["AccentDim"])
        if name in ("slider_thumb", "scrollbar_thumb"):
            mid = width // 2
            d.line([(mid - 2, 7), (mid - 2, height - 8)], border)
            d.line([(mid + 1, 7), (mid + 1, height - 8)], border)
    return d


SPECS = [
    ("panel", "primary", 256, 128, 16, 36, 36),
    ("panel", "compact", 128, 64, 10, 24, 24),
    ("panel", "raised", 128, 64, 10, 24, 24),
    ("panel", "inset", 64, 64, 8, 20, 20),
    ("panel", "dialog", 256, 160, 16, 40, 40),
    ("panel", "tooltip", 128, 64, 10, 24, 24),
    ("panel", "portrait", 96, 96, 16, 40, 40),
    ("panel", "minimap", 128, 128, 16, 40, 40),
    ("panel", "resource", 128, 40, 10, 24, 24),
    ("panel", "banner", 256, 48, 12, 28, 28),
    ("control", "button", 128, 40, 10, 28, 28),
    ("control", "button_compact", 96, 28, 8, 24, 24),
    ("control", "tab", 112, 32, 8, 24, 24),
    ("control", "row", 192, 32, 8, 24, 24),
    ("control", "row_compact", 128, 24, 6, 20, 20),
    ("control", "input", 192, 40, 10, 28, 28),
    ("control", "dropdown", 192, 40, 10, 28, 28),
    ("control", "checkbox", 24, 24, 6, 24, 24),
    ("control", "checkmark", 24, 24, 0, 24, 24),
    ("control", "toggle_track", 48, 24, 6, 36, 24),
    ("control", "toggle_thumb", 20, 20, 0, 20, 20),
    ("control", "slider_track", 128, 12, 4, 12, 12),
    ("control", "slider_thumb", 20, 28, 6, 20, 28),
    ("control", "scrollbar_track", 12, 96, 4, 12, 12),
    ("control", "scrollbar_thumb", 12, 32, 4, 12, 16),
    ("control", "separator", 64, 4, 1, 4, 4),
    ("progress", "progress_track", 128, 12, 4, 12, 12),
    ("progress", "progress_fill", 64, 8, 2, 6, 6),
] + [("overlay", name, 128, 40, 10, 24, 24) for name in
     ("hover", "pressed", "focus", "selected", "disabled", "warning", "error", "pending", "accepted")]


def contact_sheet(entries, path):
    # No resampling: all source previews are at native pixel size.
    cell_w, cell_h = 288, 210
    sheet = Image.new("RGB", (cell_w * 4, cell_h * ((len(entries) + 3) // 4)), "#252c2e")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()
    for index, entry in enumerate(entries):
        x, y = (index % 4) * cell_w, (index // 4) * cell_h
        draw.text((x + 8, y + 4), entry["id"].removeprefix("ui.skin.directorate."), font=font, fill="white")
        image = Image.open(OUT / entry["file"]).convert("RGBA")
        # Dark/light split exposes black contamination and alpha fringes.
        preview = Image.new("RGBA", image.size, "#11181a")
        ImageDraw.Draw(preview).rectangle((image.width // 2, 0, image.width, image.height), fill="#dad8cf")
        preview.alpha_composite(image)
        sheet.paste(preview.convert("RGB"), (x + 8, y + 26))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def state_preview(entries, path):
    """Inspection-only nine-slice composites; never imported by the player."""
    catalog = {entry["id"]: entry for entry in entries}

    def sliced(identifier, size):
        entry = catalog["ui.skin.directorate." + identifier]
        source = Image.open(OUT / entry["file"]).convert("RGBA")
        left, top, right, bottom = entry["insets"]
        if entry["scaling"] == "fixed":
            assert size == source.size
            return source
        assert size[0] >= entry["minimumSize"][0] and size[1] >= entry["minimumSize"][1]
        result = Image.new("RGBA", size)
        sx, sy = [0, left, source.width - right, source.width], [0, top, source.height - bottom, source.height]
        dx, dy = [0, left, size[0] - right, size[0]], [0, top, size[1] - bottom, size[1]]
        for row in range(3):
            for column in range(3):
                region = source.crop((sx[column], sy[row], sx[column + 1], sy[row + 1]))
                target = (dx[column + 1] - dx[column], dy[row + 1] - dy[row])
                result.paste(region.resize(target, Image.Resampling.NEAREST), (dx[column], dy[row]))
        return result

    sheet = Image.new("RGBA", (1000, 790), "#252c2e")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=14)
    examples = [("NORMAL", []), ("HOVER", ["hover"]), ("PRESSED", ["pressed"]),
                ("SELECTED + FOCUS", ["selected", "focus"]),
                ("WARNING + FOCUS", ["warning", "focus"]),
                ("REJECTED", ["error"]), ("PENDING", ["pending"]),
                ("ACCEPTED", ["accepted"]), ("DISABLED", ["disabled"])]
    for index, (label, states) in enumerate(examples):
        y = 18 + index * 57
        draw.text((12, y + 10), label, font=font, fill="white")
        for x, background in [(205, "#10181a"), (465, "#dedbd2")]:
            draw.rectangle((x, y, x + 240, y + 44), fill=background)
            button = sliced("control.button", (232, 40))
            for state in states:
                button.alpha_composite(sliced("overlay." + state, (232, 40)))
            sheet.alpha_composite(button, (x + 4, y + 2))
            ImageDraw.Draw(sheet).text((x + 21, y + 13), "LIVE ACTION", font=font,
                                       fill="#687375" if states == ["disabled"] else "#edf4f2")
        # Small geometry-only checks expose crossed corners without pretending a label fits.
        small = sliced("control.button", (28, 28))
        for state in states:
            small.alpha_composite(sliced("overlay." + state, (28, 28)))
        sheet.alpha_composite(small, (745, y + 7))
    draw.text((12, 555), "NATIVE SMALL PARTS / MINIMUM SLICES / TRANSPARENT FRAME CENTERS", font=font, fill="white")
    x = 12
    for identifier in ("control.checkbox", "control.toggle_track", "control.slider_track", "control.scrollbar_thumb", "panel.portrait", "panel.minimap"):
        entry = catalog["ui.skin.directorate." + identifier]
        image = Image.open(OUT / entry["file"]).convert("RGBA")
        draw.rectangle((x, 590, x + image.width, 590 + image.height), fill="#dedbd2")
        sheet.alpha_composite(image, (x, 590))
        x += image.width + 22
    draw.text((12, 740), "Inspection labels are not source artwork. Bright/dark compositing uses the same straight-alpha sources.", font=font, fill="white")
    sheet.convert("RGB").save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", type=Path, help="Optional authoring-only contact sheet")
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    colors = palette()
    entries = []
    for family, name, width, height, inset, min_w, min_h in SPECS:
        drawing = author(name, width, height, family, colors, "compact" in name)
        stem = f"{family}_{name}"
        png, svg = OUT / f"{stem}.png", OUT / f"{stem}.svg"
        drawing.image.save(png, optimize=False)
        svg.write_text("\n".join(drawing.svg + ["</svg>", ""]), encoding="utf-8", newline="\n")
        asset_id = f"ui.skin.directorate.{family}.{name}"
        metadata = {"id": asset_id, "type": "texture", "source": png.name,
                    "textureUsage": "displayImage", "textureColorSpace": "linear",
                    "textureGenerateMipmaps": False, "textureFormat": "rgba8Unorm"}
        (OUT / f"{stem}.asset.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        entries.append({"id": asset_id, "file": png.name, "editableSource": svg.name,
                        "width": width, "height": height, "insets": [inset] * 4,
                        "contentPadding": [0] * 4 if name in ("separator", "progress_fill") else [max(inset, 6)] * 4,
                        "minimumSize": [min_w, min_h],
                        "scaling": "fixed" if inset == 0 else "nine_slice",
                        "edgePolicy": "stretch", "centerPolicy": "stretch",
                        "alphaMode": "straight", "sha256": digest(png), "editableSha256": digest(svg)})
    catalog = {"schemaVersion": 1, "purpose": "ui-skin-authoring-contract",
               "runtimeLoader": "none; compile textures through the existing Asset Compiler",
               "referenceCatalog": "assets/reference/ui/directorate/catalog.json",
               "provenance": "Independent vector redraw informed by the reference styling; no reference pixels copied.",
               "rightsEvidence": "Reference catalog records not_recorded; no additional license or redistribution permission asserted.",
               "reviewStatus": "pending_human_review",
               "paletteSource": "src/ForgeLine.Presentation/ForgeLineUiPalette.cs",
               "sprites": entries,
               "states": {"normal": [], **{name: [f"ui.skin.directorate.overlay.{name}"] for name in
                          ("hover", "pressed", "focus", "selected", "disabled", "warning", "error", "pending", "accepted")},
                          "rejected": ["ui.skin.directorate.overlay.error"]},
               "compositionOrder": ["base", "hover_or_pressed", "selected", "semantic_status", "focus", "disabled", "live_text"],
               "disabledSuppresses": ["hover", "pressed", "pending", "accepted"],
               "futureControls": ["input", "dropdown", "checkbox", "toggle", "slider", "scrollbar"],
               "notes": ["Control glyphs and live labels are separate; dropdown needs a semantic chevron.",
                         "Normal means the base sprite with no overlay; focus and selected may coexist.",
                         "Do not scale below minimumSize; choose compact variants or relayout.",
                         "Bounds are the full PNG; the outermost pixel perimeter is transparent.",
                         "Atlas extrusion, renderer mapping and consumer integration are subsequent work."]}
    (OUT / "skin.catalog.json").write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")
    if args.preview:
        contact_sheet(entries, args.preview)
        state_preview(entries, args.preview.with_name(args.preview.stem + "-states.png"))
    print(f"Authored {len(entries)} text-free RGBA PNGs, editable SVGs and texture definitions.")


if __name__ == "__main__":
    main()
