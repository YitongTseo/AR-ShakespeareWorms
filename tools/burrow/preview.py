"""Blender preview of a baked burrow network (run inside Blender):
  /Applications/Blender.app/Contents/MacOS/Blender -b -P preview.py -- mesh=<.ply> net=<.json> out=<prefix> [views=wearer,outside,up] [samples=48] [pct=60]
"""
import json, math, os, sys
import bpy
import numpy as np
from mathutils import Vector

args = dict(a.split("=", 1) for a in sys.argv[sys.argv.index("--") + 1:])
views = args.get("views", "wearer,outside,up").split(",")
samples = int(args.get("samples", 48)); pct = int(args.get("pct", 60))
net = json.loads(open(args["net"]).read())
p = net["params"]

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = "CYCLES"
sc.cycles.samples = samples; sc.cycles.use_denoising = True
sc.cycles.device = "CPU"
sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
sc.render.resolution_percentage = pct
sc.view_settings.view_transform = "AgX"; sc.view_settings.look = "AgX - Base Contrast"


def mat(name, **kw):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    for k, v in kw.items():
        b.inputs[k].default_value = v
    return m


silver = mat("silver", **{"Base Color": (0.62, 0.63, 0.66, 1), "Metallic": 1.0, "Roughness": 0.36})
# wire-wrap: a helical groove in (s, phi) UV space, pitch ~5 mm, like wire wound round a core
nt = silver.node_tree; nodes_, links = nt.nodes, nt.links
uvn = nodes_.new("ShaderNodeUVMap"); sep = nodes_.new("ShaderNodeSeparateXYZ"); links.new(uvn.outputs[0], sep.inputs[0])
vc = nodes_.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
sc_ = nodes_.new("ShaderNodeSeparateColor"); links.new(vc.outputs[0], sc_.inputs[0])
cx = nodes_.new("ShaderNodeMath"); cx.operation = "MULTIPLY_ADD"; cx.inputs[1].default_value = 2.0; cx.inputs[2].default_value = -1.0; links.new(sc_.outputs[0], cx.inputs[0])
sy = nodes_.new("ShaderNodeMath"); sy.operation = "MULTIPLY_ADD"; sy.inputs[1].default_value = 2.0; sy.inputs[2].default_value = -1.0; links.new(sc_.outputs[1], sy.inputs[0])
at = nodes_.new("ShaderNodeMath"); at.operation = "ARCTAN2"; links.new(sy.outputs[0], at.inputs[0]); links.new(cx.outputs[0], at.inputs[1])
turn = nodes_.new("ShaderNodeMath"); turn.operation = "DIVIDE"; turn.inputs[1].default_value = 2 * math.pi; links.new(at.outputs[0], turn.inputs[0])
m1 = nodes_.new("ShaderNodeMath"); m1.operation = "DIVIDE"; m1.inputs[1].default_value = 0.0048; links.new(sep.outputs[0], m1.inputs[0])
m2 = nodes_.new("ShaderNodeMath"); m2.operation = "SUBTRACT"; links.new(m1.outputs[0], m2.inputs[0]); links.new(turn.outputs[0], m2.inputs[1])
m3 = nodes_.new("ShaderNodeMath"); m3.operation = "FRACT"; links.new(m2.outputs[0], m3.inputs[0])
m4 = nodes_.new("ShaderNodeMath"); m4.operation = "SUBTRACT"; m4.inputs[1].default_value = 0.5; links.new(m3.outputs[0], m4.inputs[0])
m5 = nodes_.new("ShaderNodeMath"); m5.operation = "ABSOLUTE"; links.new(m4.outputs[0], m5.inputs[0])
# fade the wrap out on the nodules / chambers (prox in blue) so swellings read as smooth cast metal
fade = nodes_.new("ShaderNodeMath"); fade.operation = "MULTIPLY"; links.new(m5.outputs[0], fade.inputs[0])
inv = nodes_.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0; links.new(sc_.outputs[2], inv.inputs[1]); links.new(inv.outputs[0], fade.inputs[1])
m5 = fade
bump = nodes_.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.55; bump.inputs["Distance"].default_value = 0.004
links.new(m5.outputs[0], bump.inputs["Height"]); links.new(bump.outputs[0], nodes_["Principled BSDF"].inputs["Normal"])
glass = mat("glass", **{"Base Color": (0.95, 0.97, 1.0, 1), "Roughness": 0.04, "IOR": 1.45, "Transmission Weight": 1.0})
dark = mat("dark", **{"Base Color": (0.01, 0.012, 0.02, 1), "Metallic": 0.4, "Roughness": 0.2,
                      "Emission Color": (0.05, 0.25, 0.45, 1), "Emission Strength": 0.6})
floor_m = mat("floor", **{"Base Color": (0.30, 0.30, 0.30, 1), "Roughness": 0.85})
wall_m = mat("wall", **{"Base Color": (0.42, 0.41, 0.40, 1), "Roughness": 0.95})
window_m = bpy.data.materials.new("window"); window_m.use_nodes = True
window_m.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (0.9, 0.95, 1.0, 1)
window_m.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 2.2
red = mat("cushion", **{"Base Color": (0.45, 0.03, 0.05, 1), "Roughness": 0.85})

# --- the burrow mesh (Unity Y-up -> Blender Z-up: (x, y, z) -> (x, -z, y))
bpy.ops.wm.ply_import(filepath=args["mesh"])
ob = bpy.context.selected_objects[0]
ob.rotation_euler = (math.radians(90), 0, 0)
ob.data.materials.append(silver)
for poly in ob.data.polygons: poly.use_smooth = True
bpy.ops.object.select_all(action="DESELECT")


def B(v):  # Unity -> Blender
    return Vector((v[0], -v[2], v[1]))


def sphere(loc, r, m, name="s", seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=seg, ring_count=seg // 2)
    o = bpy.context.active_object; o.name = name; o.data.materials.append(m)
    for poly in o.data.polygons: poly.use_smooth = True
    return o


for n in net["nodes"]:
    if n["kind"] != "mind": continue
    c = B(np.array(n["dome"])); rd = n["dome_r"]
    sphere(c, rd, glass, "dome")
    sphere(c - B(np.array(n["face"]) * rd * 0.2), rd * 0.68, dark, "eye", 16)

# --- room
ceil = p["ceiling"]
bpy.ops.mesh.primitive_plane_add(size=9, location=(0, 0, 0)); bpy.context.active_object.data.materials.append(floor_m)
bpy.ops.mesh.primitive_plane_add(size=9, location=(0, 0, ceil)); bpy.context.active_object.data.materials.append(wall_m)
for (x, y, rz) in [(4.5, 0, 90), (-4.5, 0, 90), (0, -4.5, 0)]:
    bpy.ops.mesh.primitive_plane_add(size=9, location=(x, y, ceil / 2), rotation=(math.radians(90), 0, math.radians(rz)))
    bpy.context.active_object.data.materials.append(wall_m)
# the window wall (the real room has a glass wall behind the sculpture)
bpy.ops.mesh.primitive_plane_add(size=9, location=(0, 4.5, ceil / 2), rotation=(math.radians(90), 0, 0))
bpy.context.active_object.data.materials.append(window_m)
# sculpture stand-in: cushion + a tangle
bpy.ops.mesh.primitive_cylinder_add(radius=0.32, depth=0.2, location=(0, 0, 0.1)); bpy.context.active_object.data.materials.append(red)
rng = np.random.default_rng(3)
for i in range(9):
    a = rng.uniform(0, 2 * math.pi); r = rng.uniform(0.05, 0.25)
    sphere((r * math.cos(a), r * math.sin(a), 0.24), 0.03, glass, "sd")
    sphere((r * math.cos(a), r * math.sin(a), 0.235), 0.018, dark, "se", 12)

# --- light: soft daylight through "windows" + a ceiling fill
def area(loc, rot, size, energy, col=(1, 1, 1)):
    bpy.ops.object.light_add(type="AREA", location=loc, rotation=rot)
    L = bpy.context.active_object; L.data.energy = energy; L.data.size = size; L.data.color = col
area((3.2, -2.5, 2.2), (math.radians(60), 0, math.radians(50)), 2.0, 700, (1.0, 0.95, 0.88))
area((-2.5, -3.0, 1.2), (math.radians(80), 0, math.radians(-40)), 2.5, 250, (0.85, 0.9, 1.0))
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.3, 0.32, 0.36, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.35

CAMS = dict(
    wearer=dict(loc=(1.45, -1.3, 1.5), target=(0.0, 0.1, 1.8), lens=20),
    outside=dict(loc=(3.6, -3.4, 1.25), target=(0.0, 0.0, 1.55), lens=26),
    up=dict(loc=(0.9, -0.7, 1.25), target=(0.0, 0.2, ceil - 0.3), lens=16),
    sculpture=dict(loc=(1.3, -1.1, 0.9), target=(0.0, 0.0, 0.45), lens=35),
)


def look_at(cam, tgt):
    d = Vector(tgt) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


for v in views:
    c = CAMS[v]
    cd = bpy.data.cameras.new(v); cam = bpy.data.objects.new(v, cd); sc.collection.objects.link(cam)
    cd.lens = c["lens"]; cd.sensor_width = 36; cd.clip_start = 0.02
    cam.location = c["loc"]; look_at(cam, c["target"]); sc.camera = cam
    sc.render.filepath = f"{args['out']}_{v}.png"
    bpy.ops.render.render(write_still=True)
    print("rendered", sc.render.filepath)
