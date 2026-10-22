"""Portable V1 conveyor visual-kit validation and port-mating contract.

Pure authoring code: no bpy, renderer, ECS, inventory or runtime asset imports.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_KIT = ROOT / "assets/source/infrastructure/directorate/conveyors/conveyor-kit.v1.json"
DEFAULT_BOARD = ROOT / "docs/images/conveyor-greybox.svg"

REQUIRED_SLUGS = {
    "conveyor_straight_short",
    "conveyor_straight_standard",
    "conveyor_curve_90",
    "conveyor_ramp_up",
    "conveyor_ramp_down",
    "conveyor_merger_2to1",
    "conveyor_splitter_1to2",
    "conveyor_io_in",
    "conveyor_io_out",
    "conveyor_support_light",
    "conveyor_support_heavy",
    "conveyor_endcap",
}
DIRECTIONS = {"N": (0, 1), "E": (1, 0), "S": (0, -1), "W": (-1, 0)}
KINDS = {"input", "output", "cap"}
EXPECTED_PORTS = {
    "transport": (1, 1, 0),
    "turn": (1, 1, 0),
    "elevation": (1, 1, 0),
    "merge": (2, 1, 0),
    "split": (1, 2, 0),
    "building_to_belt": (1, 1, 0),
    "belt_to_building": (1, 1, 0),
    "structure": (0, 0, 0),
    "closure": (0, 0, 1),
}
MANDATORY_STATES = {
    "flowing", "idle_no_input", "disconnected", "congested",
    "blocked", "unpowered", "damaged", "critical", "destroyed",
}


def read_kit(path: Path = DEFAULT_KIT) -> dict:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def module_by_slug(kit: dict, slug: str) -> dict:
    return next(module for module in kit["modules"] if module["slug"] == slug)


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def validate(kit: dict) -> None:
    _require(kit.get("schema") == "forgeline.directorate.conveyor_visual_kit",
             "Unexpected conveyor source schema")
    _require(kit.get("schemaVersion") == 1, "Unexpected conveyor contract version")
    g = kit["geometry"]
    _require(g["gridMeters"] == 4 and g["shortLengthMeters"] == 4
             and g["standardLengthMeters"] == 8, "Grid/segment dimension mismatch")
    _require(g["roadPassageMinimumMeters"] == 5, "Road passage target changed")
    _require(g["assemblyUndersideYTierZeroMeters"] >=
             g["roadPassageMinimumMeters"] + g["roadPassageDesignMarginMeters"] - 1e-8,
             "Raised carrier violates road clearance margin")
    _require(g["assemblyUndersideYTierZeroMeters"] <
             g["beltSurfaceYTierZeroMeters"] < g["railTopYTierZeroMeters"],
             "Carrier cross-section is inverted")
    _require(g["outerAssemblyWidthMeters"] > g["transportSurfaceWidthMeters"] > 0,
             "Transport surface/frame widths incompatible")
    _require(g["elevationTierStepMeters"] == 2 and g["curveCenterlineRadiusMeters"] == 4,
             "Unsupported turn or tier standard")
    _require(0 < g["portPositionToleranceMeters"] <= 0.02 and
             0 < g["portAngleToleranceDegrees"] <= 1,
             "Port matching tolerances may not be loosened")
    _require(kit["engine"]["upAxis"] == "+Y" and kit["engine"]["forwardAxis"] == "+Z",
             "Engine axes changed")
    _require(kit["engine"]["cardinalRotationDegrees"] == [0, 90, 180, 270],
             "Free rotations not supported")
    _require(not set(kit["flow"]["beltResources"]) &
             set(kit["flow"]["ineligibleFamilies"]), "Conveyor material whitelist overlaps fluids")
    _require("fuel" in kit["flow"]["ineligibleFamilies"] and
             "volatiles" in kit["flow"]["ineligibleFamilies"],
             "Fluids must remain road/pipeline-only")
    _require(kit["materials"]["factionColorMaximumSurfaceFraction"] <= 0.08,
             "Faction paint dominates industrial material identity")

    states = kit["states"]
    names = [s["id"] for s in states]
    _require(len(names) == len(set(names)) and set(names) == MANDATORY_STATES,
             "Visual states are missing, duplicated, or ambiguous")
    modules = kit["modules"]
    slugs = [m["slug"] for m in modules]
    identities = [m["assetId"] for m in modules]
    _require(len(modules) == 12 and set(slugs) == REQUIRED_SLUGS and
             len(set(slugs)) == len(slugs) and len(set(identities)) == len(identities),
             "V1 module catalog is incomplete or has duplicated identity")

    for module in modules:
        slug = module["slug"]
        _require(module["assetId"].startswith("infrastructure.directorate.conveyor."),
                 f"{slug}: stable asset namespace changed")
        _require(module["sourceFormat"] == "planned_glb" and
                 module["runtimeStatus"] == "not_implemented",
                 f"{slug}: design metadata must not imply compiled gameplay assets")
        _require(module["allowedRotationsDegrees"] == [0, 90, 180, 270],
                 f"{slug}: invalid rotation vocabulary")
        _require(module["role"] in EXPECTED_PORTS, f"{slug}: unsupported function")
        _require(all(math.isfinite(x) and x > 0 for x in module["footprintMeters"]),
                 f"{slug}: nonpositive footprint")
        ports = module["ports"]
        counts = tuple(sum(p["kind"] == kind for p in ports)
                       for kind in ("input", "output", "cap"))
        _require(counts == EXPECTED_PORTS[module["role"]],
                 f"{slug}: incorrect directed port count")
        labels = [p["name"] for p in ports]
        _require(len(labels) == len(set(labels)), f"{slug}: duplicate port")
        for p in ports:
            _require(p["kind"] in KINDS and p["facing"] in DIRECTIONS,
                     f"{slug}: invalid direction or port kind")
            _require(p["role"] in ("belt", "building_socket"),
                     f"{slug}: invalid mate role")
            point = p["positionMeters"]
            _require(len(point) == 3 and all(isinstance(v, (int, float)) and
                     math.isfinite(v) for v in point), f"{slug}: nonfinite port")
            tier = p["relativeTier"]
            _require(isinstance(tier, int) and tier in (0, 1),
                     f"{slug}: unsupported tier")
            expected_y = g["beltSurfaceYTierZeroMeters"] + tier * g["elevationTierStepMeters"]
            _require(abs(point[1] - expected_y) <= 1e-7,
                     f"{slug}: port Y does not match elevation tier")

    curve = module_by_slug(kit, "conveyor_curve_90")
    _require(curve["radiusMeters"] == 4 and
             set(curve["handednessVariants"]) == {"left", "right"},
             "90-degree curve must have both hands")
    _require(curve["footprintMeters"] == [6.8, 6.8] and
             curve["footprintCenterXZ"] == [-2, 2],
             "Curve arc plus 1.4 m side-frame clearance does not fit footprint")
    up = module_by_slug(kit, "conveyor_ramp_up")
    down = module_by_slug(kit, "conveyor_ramp_down")
    _require(up["riseMeters"] == 2 and down["riseMeters"] == -2,
             "Tier transition rise changed")
    for slug, length in (("conveyor_straight_short", 4),
                         ("conveyor_straight_standard", 8)):
        segment = module_by_slug(kit, slug)
        _require(segment["lengthMeters"] == length, f"{slug}: spacing changed")
        _require(segment["ports"][0]["positionMeters"][2] == -length / 2 and
                 segment["ports"][1]["positionMeters"][2] == length / 2,
                 f"{slug}: endpoint centerlines do not mate")


def port_pose(port: dict, origin=(0.0, 0.0, 0.0), yaw_degrees=0, tier=0):
    """World XYZ and horizontal outward normal under engine-positive yaw."""
    _require(yaw_degrees in (0, 90, 180, 270), "Only cardinal yaw is legal")
    _require(isinstance(tier, int) and tier >= 0, "Negative/partial tier")
    x, y, z = port["positionMeters"]
    radians = math.radians(yaw_degrees)
    c, s = round(math.cos(radians)), round(math.sin(radians))
    fx, fz = DIRECTIONS[port["facing"]]
    position = (origin[0] + c*x + s*z,
                origin[1] + y + 2*tier,
                origin[2] - s*x + c*z)
    direction = (c*fx + s*fz, -s*fx + c*fz)
    return position, direction


def can_mate(out_port, in_port, out_origin=(0, 0, 0), in_origin=(0, 0, 0),
             out_yaw=0, in_yaw=0, out_tier=0, in_tier=0, tolerance=0.02):
    """Geometry-only validator; simulation ownership/resources are not inferred."""
    if not (out_port["kind"] == "output" and in_port["kind"] == "input" and
            out_port["role"] == in_port["role"] == "belt"):
        return False
    a, na = port_pose(out_port, out_origin, out_yaw, out_tier)
    b, nb = port_pose(in_port, in_origin, in_yaw, in_tier)
    return math.dist(a, b) <= tolerance and na[0] == -nb[0] and na[1] == -nb[1]


def validate_board(kit: dict, path: Path = DEFAULT_BOARD) -> None:
    board = Path(path).read_text(encoding="utf-8")
    _require(board.startswith("<svg") and board.rstrip().endswith("</svg>"),
             "Greybox board is missing/malformed")
    for module in kit["modules"]:
        _require(module["slug"].replace("_", " ").upper() in board,
                 f"Greybox board omitted {module['slug']}")
    _require("5.00 m MIN" in board and "UNDERSIDE +5.20 m" in board,
             "Greybox clearance annotations changed")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Validate preproduction conveyor visual contract")
    parser.add_argument("--kit", type=Path, default=DEFAULT_KIT)
    parser.add_argument("--board", type=Path, default=DEFAULT_BOARD)
    arguments = parser.parse_args()
    source = read_kit(arguments.kit)
    validate(source)
    validate_board(source, arguments.board)
    print(f"Conveyor visual contract OK: {len(source['modules'])} modules, "
          f"{len(source['states'])} states, dimensional ports and greybox board")
