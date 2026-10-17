"""Export a two-metre test cube; all generated files belong to artifacts."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

import bpy
from mathutils import Matrix


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def verify_glb(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", data)
    assert (magic, version, length) == (0x46546C67, 2, len(data)), "Invalid GLB container"
    json_size, json_type = struct.unpack_from("<II", data, 12)
    assert json_type == 0x4E4F534A
    gltf = json.loads(data[20:20 + json_size])
    bin_size, bin_type = struct.unpack_from("<II", data, 20 + json_size)
    assert bin_type == 0x004E4942
    binary = data[28 + json_size:28 + json_size + bin_size]
    assert len(gltf["meshes"]) == len(gltf["materials"]) == 1
    assert len(gltf["meshes"][0]["primitives"]) == 1
    for node in gltf["nodes"]:
        assert all(key not in node for key in ("matrix", "translation", "rotation", "scale")), "Bake transforms before import"
    primitive = gltf["meshes"][0]["primitives"][0]
    assert primitive.get("mode", 4) == 4
    assert {"POSITION", "NORMAL", "TANGENT", "TEXCOORD_0"} <= set(primitive["attributes"])

    def accessor(index):
        acc = gltf["accessors"][index]
        view = gltf["bufferViews"][acc["bufferView"]]
        assert "sparse" not in acc
        components = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[acc["type"]]
        scalar = {5123: "H", 5125: "I", 5126: "f"}[acc["componentType"]]
        fmt = "<" + scalar * components
        stride = view.get("byteStride", struct.calcsize(fmt))
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        return [struct.unpack_from(fmt, binary, start + n * stride) for n in range(acc["count"])]

    vertices = accessor(primitive["attributes"]["POSITION"])
    indices = [x[0] for x in accessor(primitive["indices"])]
    assert len(indices) == 36 and all(i < len(vertices) for i in indices)
    low = [min(p[i] for p in vertices) for i in range(3)]
    high = [max(p[i] for p in vertices) for i in range(3)]
    assert low == [-1.0, 0.0, -1.0] and high == [1.0, 2.0, 1.0], (low, high)
    for n in range(0, len(indices), 3):
        a, b, c = [vertices[i] for i in indices[n:n + 3]]
        u = [b[i] - a[i] for i in range(3)]
        v = [c[i] - a[i] for i in range(3)]
        normal = [u[1]*v[2] - u[2]*v[1], u[2]*v[0] - u[0]*v[2], u[0]*v[1] - u[1]*v[0]]
        outward = [a[0], a[1] - 1, a[2]]
        assert sum(normal[i] * outward[i] for i in range(3)) > 0, "Outward CCW triangle required"
    assert all(math.isfinite(v) for point in vertices for v in point)
    return {"boundsMin": low, "boundsMax": high, "vertices": len(vertices), "triangles": 12,
            "materials": 1, "sha256": hashlib.sha256(data).hexdigest()}


def main():
    import sys
    parser = argparse.ArgumentParser()
    parser.add_argument("--request", required=True, type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    request = json.loads(args.request.read_text(encoding="utf-8-sig"))
    assert request["schemaVersion"] == 1 and Path(request["inputRoot"]).is_dir()
    destination = Path(request["outputRoot"]) / "source"
    destination.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    bpy.ops.mesh.primitive_cube_add(size=2, location=(0, 0, 1))
    obj = bpy.context.object
    obj.name = "ToolchainSmokeCube"
    obj.data.name = "ToolchainSmokeMesh"
    # The existing compiler reads mesh data, not glTF node transforms.
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    material = bpy.data.materials.new("ToolchainSmokeMaterial")
    material.use_nodes = True
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.25, 0.4, 0.6, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.7
    obj.data.materials.append(material)
    target = destination / "smoke.glb"
    result = bpy.ops.export_scene.gltf(
        filepath=str(target), export_format="GLB", use_selection=True,
        export_yup=True, export_apply=True, export_texcoords=True,
        export_normals=True, export_tangents=True, export_materials="EXPORT",
        export_animations=False, export_cameras=False, export_lights=False,
        export_extras=False, export_draco_mesh_compression_enable=False,
    )
    assert result == {"FINISHED"}, result
    geometry = verify_glb(target)
    write_json(destination / "smoke.asset.json", {
        "id": "mesh.toolchain.smoke", "type": "mesh", "source": "smoke.glb",
        "scale": 1.0, "materialReferences": ["material.toolchain.smoke"],
    })
    write_json(destination / "smoke.material.json", {
        "baseColorFactor": [0.25, 0.4, 0.6, 1.0], "metallicFactor": 0.0, "roughnessFactor": 0.7,
    })
    write_json(destination / "smoke-material.asset.json", {
        "id": "material.toolchain.smoke", "type": "material", "source": "smoke.material.json",
    })
    write_json(destination / "smoke-report.json", {
        "schemaVersion": 1, "blenderVersion": list(bpy.app.version),
        "units": "metres", "scale": 1.0, "geometry": geometry,
        "nodeTransformsBaked": True, "outwardCounterclockwise": True,
    })
    print("[blender:smoke] exported verified full-scale GLB and compiler source definitions")


if __name__ == "__main__":
    main()
