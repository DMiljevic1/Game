# Builds the survival-horror house and exports it for Unity.
#
#   blender --background --factory-startup --python Blender/house_build.py
#
# Everything is generated as explicit quads with hand-assigned UVs rather than
# unwrapped, because the whole model shares ONE 512x512 atlas and one material:
# that is what keeps the style uniform across future props and lets Unity batch
# the lot into a single draw call.
#
# Texel density is fixed at one atlas tile per 2 m, so a plank is the same size
# on the floor as it is on the wall. Faces are cut on the global 2 m grid, which
# also means a wall split around a doorway keeps its texture continuous.
#
# Blender is Z-up; Unity is Y-up. Blender +Y becomes Unity +Z on FBX import.
# Every object here is left with an identity rotation (door leaves are built
# already-oriented around their hinge) so nothing depends on how the exporter
# bakes rotations.

import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import house_atlas as atlas  # noqa: E402

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEL_DIR = os.path.join(PROJECT, "Assets", "Models", "House")
BLEND_OUT = os.path.join(PROJECT, "Blender", "House.blend")

# --- dimensions -------------------------------------------------------------
# Matched to the prototype building already in SampleScene so the two read as
# the same world: 3.0 m walls, 0.15 m thick, 1.2 x 2.2 m door openings.
W, D = 12.0, 9.0          # exterior footprint
T = 0.15                  # wall thickness
H = 3.0                   # wall height (floor top to ceiling)
FLOOR_TOP = 0.02          # interior floor surface
DOOR_W, DOOR_H = 1.2, 2.2
WIN_W = 1.3
WIN_SILL, WIN_HEAD = 1.0, 2.1
CELL = 2.0                # world metres per atlas tile
UV_INSET = 1.5 / atlas.SIZE   # keeps mip bleed out of neighbouring tiles

# Corridor runs the full width; four rooms open off it, one door each.
COR_S, COR_N = 3.6, 5.4   # centre lines of the two partition walls
SPLIT_S = 7.0             # living | kitchen
SPLIT_N = 5.5             # bedroom | bathroom

TILE_UV = 1.0 / atlas.GRID


# --- mesh builder -----------------------------------------------------------

class Builder:
    def __init__(self):
        self.verts = []
        self.faces = []
        self.uvs = []          # one (u, v) per loop, parallel to faces

    def _uv(self, tile, fu0, fv0, fu1, fv1):
        col, row = tile % atlas.GRID, tile // atlas.GRID
        u0 = col * TILE_UV + UV_INSET
        v0 = (atlas.GRID - 1 - row) * TILE_UV + UV_INSET
        span = TILE_UV - 2 * UV_INSET
        return ((u0 + span * fu0, v0 + span * fv0),
                (u0 + span * fu1, v0 + span * fv0),
                (u0 + span * fu1, v0 + span * fv1),
                (u0 + span * fu0, v0 + span * fv1))

    def quad(self, p0, p1, p2, p3, tile, uv):
        base = len(self.verts)
        self.verts.extend((p0, p1, p2, p3))
        self.faces.append((base, base + 1, base + 2, base + 3))
        self.uvs.extend(uv)

    def tri(self, p0, p1, p2, tile, uv):
        base = len(self.verts)
        self.verts.extend((p0, p1, p2))
        self.faces.append((base, base + 1, base + 2))
        self.uvs.extend(uv[:3])

    def plane(self, origin, u_dir, v_dir, u_len, v_len, tile, stretch=False):
        """A rectangle, subdivided so the atlas tile repeats every CELL metres.

        u_dir/v_dir are unit axis vectors; their cross product is the outward
        normal, so callers control winding by choosing the axis order.
        """
        if u_len <= 1e-6 or v_len <= 1e-6:
            return
        if stretch:
            cuts_u = [(0.0, u_len, 0.0, 1.0)]
            cuts_v = [(0.0, v_len, 0.0, 1.0)]
        else:
            # World coordinate along each axis, so neighbouring pieces line up.
            u_start = sum(origin[i] * u_dir[i] for i in range(3))
            v_start = sum(origin[i] * v_dir[i] for i in range(3))
            cuts_u = _cells(u_start, u_len)
            cuts_v = _cells(v_start, v_len)

        for a0, a1, fu0, fu1 in cuts_u:
            for b0, b1, fv0, fv1 in cuts_v:
                def pt(a, b):
                    return tuple(origin[i] + u_dir[i] * a + v_dir[i] * b for i in range(3))
                self.quad(pt(a0, b0), pt(a1, b0), pt(a1, b1), pt(a0, b1),
                          tile, self._uv(tile, fu0, fv0, fu1, fv1))

    def box(self, lo, hi, tiles, stretch=False):
        """Axis-aligned box. `tiles` maps face keys (+x -x +y -y +z -z) to atlas
        slots; a face left out of the map is not emitted at all, which is how
        hidden faces (under floors, inside walls) stay off the vertex budget."""
        x0, y0, z0 = lo
        x1, y1, z1 = hi
        dx, dy, dz = x1 - x0, y1 - y0, z1 - z0
        X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)
        nX, nY = (-1, 0, 0), (0, -1, 0)
        faces = {
            "+x": ((x1, y0, z0), Y, Z, dy, dz),
            "-x": ((x0, y1, z0), nY, Z, dy, dz),
            "+y": ((x1, y1, z0), nX, Z, dx, dz),
            "-y": ((x0, y0, z0), X, Z, dx, dz),
            "+z": ((x0, y0, z1), X, Y, dx, dy),
            "-z": ((x0, y1, z0), X, nY, dx, dy),
        }
        for key, tile in tiles.items():
            origin, u_dir, v_dir, u_len, v_len = faces[key]
            self.plane(origin, u_dir, v_dir, u_len, v_len, tile, stretch)


def _cells(start, length, cell=CELL):
    """Split [start, start+length] on the global grid, returning
    (offset0, offset1, uv_frac0, uv_frac1) per piece."""
    out = []
    pos = 0.0
    while pos < length - 1e-6:
        world = start + pos
        next_edge = (math.floor(world / cell) + 1) * cell
        seg = min(next_edge - world, length - pos)
        f0 = (world - math.floor(world / cell) * cell) / cell
        out.append((pos, pos + seg, f0, f0 + seg / cell))
        pos += seg
    return out


# --- building pieces --------------------------------------------------------

def wall(b, along, line, start, end, z0, z1, tiles, openings=()):
    """A wall running along 'x' or 'y', centred on `line`, with rectangular
    holes punched for doors and windows.

    Each hole becomes up to three solid pieces (left, right, over) plus an
    under-piece for windows; every piece is a box, so the reveals around an
    opening have real thickness like the rest of the wall.
    """
    half = T / 2
    holes = sorted(openings, key=lambda o: o[0])
    cursor = start
    for centre, width, sill, head in holes:
        a, bnd = centre - width / 2, centre + width / 2
        if a > cursor:
            _wall_box(b, along, line, cursor, a, z0, z1, tiles, half)
        if sill > z0:
            _wall_box(b, along, line, a, bnd, z0, sill, tiles, half)
        if head < z1:
            _wall_box(b, along, line, a, bnd, head, z1, tiles, half)
        cursor = bnd
    if cursor < end:
        _wall_box(b, along, line, cursor, end, z0, z1, tiles, half)


def _wall_box(b, along, line, a, bnd, z0, z1, tiles, half):
    if bnd - a <= 1e-6 or z1 - z0 <= 1e-6:
        return
    if along == "x":
        b.box((a, line - half, z0), (bnd, line + half, z1), tiles)
    else:
        b.box((line - half, a, z0), (line + half, bnd, z1), tiles)


def opening_trim(b, along, line, centre, width, z0, z1, tile=atlas.TRIM):
    """Jambs and a head board lining an opening, standing slightly proud of the
    wall on both sides — cheap, but it is what stops a punched hole reading as
    a hole in a cardboard box."""
    j = 0.06                      # trim thickness
    p = T / 2 + 0.02              # how far it stands off the wall face
    a, bnd = centre - width / 2, centre + width / 2
    faces_side = {"+x": tile, "-x": tile, "+y": tile, "-y": tile, "+z": tile, "-z": tile}
    if along == "x":
        b.box((a - j, line - p, z0), (a, line + p, z1), faces_side)
        b.box((bnd, line - p, z0), (bnd + j, line + p, z1), faces_side)
        b.box((a - j, line - p, z1), (bnd + j, line + p, z1 + j), faces_side)
    else:
        b.box((line - p, a - j, z0), (line + p, a, z1), faces_side)
        b.box((line - p, bnd, z0), (line + p, bnd + j, z1), faces_side)
        b.box((line - p, a - j, z1), (line + p, bnd + j, z1 + j), faces_side)


def window(b, along, line, centre, tile_glass=atlas.GLASS):
    """Glass pane plus a cross mullion, sitting inside a punched opening."""
    a, bnd = centre - WIN_W / 2, centre + WIN_W / 2
    g = 0.02
    pane = {"+y": tile_glass, "-y": tile_glass} if along == "x" else {"+x": tile_glass, "-x": tile_glass}
    m = {k: atlas.TRIM for k in ("+x", "-x", "+y", "-y", "+z", "-z")}
    mid_z = (WIN_SILL + WIN_HEAD) / 2
    if along == "x":
        b.box((a, line - g, WIN_SILL), (bnd, line + g, WIN_HEAD), pane, stretch=True)
        b.box((a, line - 0.04, mid_z - 0.03), (bnd, line + 0.04, mid_z + 0.03), m)
        b.box((centre - 0.03, line - 0.04, WIN_SILL), (centre + 0.03, line + 0.04, WIN_HEAD), m)
    else:
        b.box((line - g, a, WIN_SILL), (line + g, bnd, WIN_HEAD), pane, stretch=True)
        b.box((line - 0.04, a, mid_z - 0.03), (line + 0.04, bnd, mid_z + 0.03), m)
        b.box((line - 0.04, centre - 0.03, WIN_SILL), (line + 0.04, centre + 0.03, WIN_HEAD), m)
    opening_trim(b, along, line, centre, WIN_W, WIN_SILL, WIN_HEAD)


def roof_slab(b, corners, thickness, top_tile, under_tile, edge_tile):
    """A sloped slab from four top-face corners, extruded straight down. Two of
    these plus the gable ends make the roof; keeping it a real slab means the
    eaves have a visible edge instead of a paper-thin silhouette."""
    top = list(corners)
    bot = [(x, y, z - thickness) for (x, y, z) in top]
    b.quad(top[0], top[1], top[2], top[3], top_tile, b._uv(top_tile, 0, 0, 1, 1))
    b.quad(bot[3], bot[2], bot[1], bot[0], under_tile, b._uv(under_tile, 0, 0, 1, 1))
    for i in range(4):
        j = (i + 1) % 4
        b.quad(top[j], top[i], bot[i], bot[j], edge_tile, b._uv(edge_tile, 0, 0, 1, 0.15))


# --- the house --------------------------------------------------------------

def build_house():
    b = Builder()

    ext = {"outside": atlas.SIDING, "inside": atlas.WALL_PLASTER}
    plaster = atlas.WALL_PLASTER

    # Foundation plinth. Only the top and the four sides are ever seen.
    b.box((-0.2, -0.2, -0.35), (W + 0.2, D + 0.2, 0.0),
          {"+x": atlas.CONCRETE, "-x": atlas.CONCRETE, "+y": atlas.CONCRETE,
           "-y": atlas.CONCRETE, "+z": atlas.CONCRETE})

    # Room floors. Interior only, so no side or bottom faces.
    rooms = [
        ("living",  (T, T), (SPLIT_S - T / 2, COR_S - T / 2), atlas.FLOOR_WOOD),
        ("kitchen", (SPLIT_S + T / 2, T), (W - T, COR_S - T / 2), atlas.FLOOR_TILE),
        ("hall",    (T, COR_S + T / 2), (W - T, COR_N - T / 2), atlas.FLOOR_WORN),
        ("bedroom", (T, COR_N + T / 2), (SPLIT_N - T / 2, D - T), atlas.FLOOR_WOOD),
        ("bath",    (SPLIT_N + T / 2, COR_N + T / 2), (W - T, D - T), atlas.FLOOR_TILE),
    ]
    for _, (x0, y0), (x1, y1), tile in rooms:
        b.box((x0, y0, 0.0), (x1, y1, FLOOR_TOP), {"+z": tile})

    z0, z1 = FLOOR_TOP, H
    door_head = FLOOR_TOP + DOOR_H

    # Exterior walls. The front door is at the west end of the corridor, so you
    # walk in and every room is one door away.
    south = {"-y": ext["outside"], "+y": ext["inside"], "+z": atlas.WOOD_DARK}
    north = {"+y": ext["outside"], "-y": ext["inside"], "+z": atlas.WOOD_DARK}
    west = {"-x": ext["outside"], "+x": ext["inside"], "+z": atlas.WOOD_DARK}
    east = {"+x": ext["outside"], "-x": ext["inside"], "+z": atlas.WOOD_DARK}

    wall(b, "x", T / 2, 0.0, W, z0, z1, south,
         [(3.0, WIN_W, WIN_SILL, WIN_HEAD), (9.5, WIN_W, WIN_SILL, WIN_HEAD)])
    wall(b, "x", D - T / 2, 0.0, W, z0, z1, north,
         [(2.5, WIN_W, WIN_SILL, WIN_HEAD), (9.0, WIN_W, WIN_SILL, WIN_HEAD)])
    wall(b, "y", T / 2, 0.0, D, z0, z1, west,
         [(4.5, DOOR_W, z0, door_head)])
    wall(b, "y", W - T / 2, 0.0, D, z0, z1, east,
         [(1.8, WIN_W, WIN_SILL, WIN_HEAD), (7.0, WIN_W, WIN_SILL, WIN_HEAD)])

    both = {"+x": plaster, "-x": plaster, "+y": plaster, "-y": plaster, "+z": atlas.WOOD_DARK}

    # Corridor partitions, each carrying two doorways.
    wall(b, "x", COR_S, T, W - T, z0, z1, both,
         [(2.5, DOOR_W, z0, door_head), (9.5, DOOR_W, z0, door_head)])
    wall(b, "x", COR_N, T, W - T, z0, z1, both,
         [(2.5, DOOR_W, z0, door_head), (9.0, DOOR_W, z0, door_head)])
    # Room dividers.
    wall(b, "y", SPLIT_S, T, COR_S - T / 2, z0, z1, both)
    wall(b, "y", SPLIT_N, COR_N + T / 2, D - T, z0, z1, both)

    # Trim: the five doorways and the six windows.
    for centre in (2.5, 9.5):
        opening_trim(b, "x", COR_S, centre, DOOR_W, z0, door_head)
    for centre in (2.5, 9.0):
        opening_trim(b, "x", COR_N, centre, DOOR_W, z0, door_head)
    opening_trim(b, "y", T / 2, 4.5, DOOR_W, z0, door_head)

    for centre in (3.0, 9.5):
        window(b, "x", T / 2, centre)
    for centre in (2.5, 9.0):
        window(b, "x", D - T / 2, centre)
    for centre in (1.8, 7.0):
        window(b, "y", W - T / 2, centre)

    # Ceiling slab, capping the walls. Seen from below only.
    b.box((0.0, 0.0, H), (W, D, H + 0.15), {"-z": atlas.CEILING})

    # Gable roof, ridge running east-west over the corridor.
    eave_z, ridge_z = H + 0.15, H + 0.15 + 1.5
    ridge_y = D / 2
    over = 0.5
    roof_slab(b, [(-over, -over, eave_z), (W + over, -over, eave_z),
                  (W + over, ridge_y + 0.06, ridge_z), (-over, ridge_y + 0.06, ridge_z)],
              0.12, atlas.SHINGLE, atlas.WOOD_DARK, atlas.WOOD_DARK)
    roof_slab(b, [(-over, ridge_y - 0.06, ridge_z), (W + over, ridge_y - 0.06, ridge_z),
                  (W + over, D + over, eave_z), (-over, D + over, eave_z)],
              0.12, atlas.SHINGLE, atlas.WOOD_DARK, atlas.WOOD_DARK)

    # Gable ends: triangles closing the loft above each end wall.
    def gable(x, normal_out):
        u = (0, 1, 0) if normal_out > 0 else (0, -1, 0)
        p_lo = (x, 0.0, eave_z) if normal_out > 0 else (x, D, eave_z)
        p_hi = (x, D, eave_z) if normal_out > 0 else (x, 0.0, eave_z)
        apex = (x, ridge_y, ridge_z)
        uv = b._uv(atlas.SIDING, 0, 0, 1, 1)
        b.tri(p_lo, p_hi, apex, atlas.SIDING, uv)
        del u
    gable(W, +1)
    gable(0.0, -1)

    # Chimney on the kitchen side.
    b.box((9.6, 1.4, H), (10.4, 2.2, ridge_z + 0.7),
          {"+x": atlas.BRICK, "-x": atlas.BRICK, "+y": atlas.BRICK,
           "-y": atlas.BRICK, "+z": atlas.CONCRETE})

    # Front step.
    b.box((-0.9, 3.7, -0.35), (0.0, 5.3, 0.02),
          {"-x": atlas.CONCRETE, "+y": atlas.CONCRETE, "-y": atlas.CONCRETE,
           "+z": atlas.CONCRETE})

    return b


def build_door(direction):
    """A door leaf hinged at the local origin.

    `direction` is the way the leaf points when shut, in Blender axes. The leaf
    is generated already pointing that way instead of rotating the object,
    because DoorInteraction swings the hinge by a fixed +90 deg about Unity's
    up axis: baking the orientation into the mesh is what decides which side
    each door opens towards, with no exported rotation to get lost in FBX.
    """
    b = Builder()
    leaf, thick, height = 1.16, 0.07, DOOR_H - 0.06
    gap = 0.02
    wood = {k: atlas.DOOR_WOOD for k in ("+x", "-x", "+y", "-y", "+z", "-z")}
    metal = {k: atlas.METAL for k in ("+x", "-x", "+y", "-y", "+z", "-z")}

    if direction == "+x":
        lo, hi = (gap, -thick / 2, 0.0), (gap + leaf, thick / 2, height)
        knob = (gap + leaf - 0.16, gap + leaf - 0.06)
    elif direction == "-x":
        lo, hi = (-gap - leaf, -thick / 2, 0.0), (-gap, thick / 2, height)
        knob = (-gap - leaf + 0.06, -gap - leaf + 0.16)
    else:  # "+y"
        lo, hi = (-thick / 2, gap, 0.0), (thick / 2, gap + leaf, height)
        knob = (gap + leaf - 0.16, gap + leaf - 0.06)

    b.box(lo, hi, wood, stretch=True)
    kz0, kz1 = 1.00, 1.10
    if direction == "+y":
        b.box((-thick / 2 - 0.05, knob[0], kz0), (thick / 2 + 0.05, knob[1], kz1), metal)
    else:
        b.box((knob[0], -thick / 2 - 0.05, kz0), (knob[1], thick / 2 + 0.05, kz1), metal)
    return b


# --- Blender scene ----------------------------------------------------------

def make_object(name, builder, location, material):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(builder.verts, [], builder.faces)
    mesh.update()

    uv_layer = mesh.uv_layers.new(name="UVMap")
    uv_layer.data.foreach_set("uv", [c for uv in builder.uvs for c in uv])

    # Flat shading everywhere: the hard facets are half the look.
    for poly in mesh.polygons:
        poly.use_smooth = False

    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    obj.location = location
    bpy.context.collection.objects.link(obj)
    mesh.validate(verbose=False)
    return obj


def make_material(texture_path):
    mat = bpy.data.materials.new("House_Atlas")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.1

    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(texture_path)
    tex.interpolation = "Closest"     # point filtering, same as in Unity
    tex.location = (-400, 200)
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def main():
    os.makedirs(MODEL_DIR, exist_ok=True)
    texture_path = os.path.join(MODEL_DIR, "House_Atlas.png")
    atlas.write_png(texture_path, atlas.build_atlas())
    print("[house] atlas ->", texture_path)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    material = make_material(texture_path)

    house = make_object("House", build_house(), (0.0, 0.0, 0.0), material)

    # (name, hinge x, hinge y, leaf direction). The direction decides the swing:
    # opening rotates the leaf from +x to -y, +y to +x, -x to +y.
    doors = [
        ("Door_Front",   T / 2, 3.9, "+y"),   # swings in, into the corridor
        ("Door_Living",  1.9, COR_S, "+x"),   # swings into the living room
        ("Door_Kitchen", 8.9, COR_S, "+x"),   # into the kitchen
        ("Door_Bedroom", 3.1, COR_N, "-x"),   # into the bedroom
        ("Door_Bath",    9.6, COR_N, "-x"),   # into the bathroom
    ]
    door_objs = [make_object(name, build_door(d), (x, y, FLOOR_TOP), material)
                 for name, x, y, d in doors]

    total_tris = sum(len(o.data.polygons) + sum(1 for p in o.data.polygons if len(p.vertices) == 4)
                     for o in [house] + door_objs)
    print("[house] objects: %d, faces: %d, tris: ~%d"
          % (1 + len(door_objs),
             sum(len(o.data.polygons) for o in [house] + door_objs), total_tris))

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_OUT)
    print("[house] blend ->", BLEND_OUT)

    for obj in bpy.context.scene.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = house

    fbx_path = os.path.join(MODEL_DIR, "House.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=True,      # safe here: every object is unrotated
        object_types={"MESH"},
        use_mesh_modifiers=False,
        mesh_smooth_type="FACE",
        use_tspace=False,
        add_leaf_bones=False,
        path_mode="STRIP",
        axis_forward="-Z",
        axis_up="Y",
    )
    print("[house] fbx ->", fbx_path)


if __name__ == "__main__":
    main()
