"""Inspect the embedded interpreter; no external Python or packages are used."""
import json
import platform
import struct
import sys
from pathlib import Path

import bpy

report = {
    "schemaVersion": 1,
    "version": ".".join(map(str, bpy.app.version)),
    "versionCycle": bpy.app.version_cycle,
    "buildHash": bpy.app.build_hash.decode("ascii"),
    "pythonVersion": platform.python_version(),
    "pointerBits": struct.calcsize("P") * 8,
    "machine": platform.machine(),
    "system": platform.system(),
    "background": bpy.app.background,
    "bpy": True,
}
Path(sys.argv[sys.argv.index("--") + 1]).write_text(
    json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8"
)
