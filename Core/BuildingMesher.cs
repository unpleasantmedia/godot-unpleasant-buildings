using System.Collections.Generic;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>How a building is roofed, for its mesh.</summary>
/// <param name="Side">For gabled and shed roofs: which side of the outline turned outward (see <see cref="BuildingMesher.Outward"/>) it goes by.</param>
public readonly record struct RoofSettings(RoofType Type, int Side, float Pitch, float Overhang, float Parapet, Material? Material);

/// <summary>
/// Makes a building's meshes from its outline. Textures are laid out in metres: across along its
/// walls, round the building, and up from height 0; on flat faces, by X and Z; on slopes, along
/// and up them.
/// </summary>
public static partial class BuildingMesher
{
    /// <summary>How thick a pitched roof is (its edge's fascia), in metres.</summary>
    public const float RoofThickness = 0.15f;

    /// <summary>How thick a flat roof's parapet is, in metres.</summary>
    public const float ParapetThickness = 0.2f;

    /// <summary>
    /// Its shell: walls along each side from its floor up to its top (and on up to meet a pitched
    /// roof), its roof, and a plinth below the walls down to its bottom. A surface for each material.
    /// </summary>
    /// <param name="holes">Its doors and windows, on the sides of its outline turned outward.</param>
    /// <param name="enterable">With an inside (inner walls, a floor and a ceiling); without, each door and window is closed off at the back of its wall.</param>
    /// <param name="innerFaces">What the walls' inner faces are made of, when enterable.</param>
    /// <param name="open">Cut open at the top (in the editor, to see in): no roof, the walls level.</param>
    public static ArrayMesh Shell(Vector2[] outline, float bottom, float floor, float top, Material? walls, Material? plinth, RoofSettings roof, List<Hole> holes, OpeningMaterials openings, bool enterable, Material? innerFaces = null, bool open = false, RoomSet? rooms = null, List<DoorLeaf>? hung = null)
    {
        var mesh = new ArrayMesh();
        var ring = Outward(outline);
        var wall = new Faces();
        var roofing = new Faces();
        var inside = new Faces();

        if (open)
        {
            wall.Holed(ring, floor, _ => top, static (_, _) => [0f, 1f], holes);
            // The walls' tops, seen from above.
            var rim = RoofShape.Reached(ring, -WallThickness);
            for (var i = 0; i < ring.Length; i++)
            {
                var j = (i + 1) % ring.Length;
                Vector3 At(Vector2 p) => new(p.X, top, p.Y);
                wall.Quad(At(ring[i]), At(ring[j]), At(rim[j]), At(rim[i]), Vector3.Up, ring[i], ring[j], rim[j], rim[i]);
            }
        }
        else if (roof.Type == RoofType.Flat)
        {
            var parapet = Mathf.Max(0f, roof.Parapet);
            wall.Holed(ring, floor, _ => top + parapet, static (_, _) => [0f, 1f], holes);
            if (parapet > 0.001f)
            {
                // The parapet: in from the walls, its inner face, and a cap on it.
                var inner = RoofShape.Reached(ring, -ParapetThickness);
                wall.Band(inner, top, top + parapet, inward: true);
                for (var i = 0; i < ring.Length; i++)
                {
                    var j = (i + 1) % ring.Length;
                    Vector3 At(Vector2 p) => new(p.X, top + parapet, p.Y);
                    wall.Quad(At(ring[i]), At(ring[j]), At(inner[j]), At(inner[i]), Vector3.Up, ring[i], ring[j], inner[j], inner[i]);
                }

                roofing.Cap(inner, top, up: true);
            }
            else
            {
                roofing.Cap(ring, top, up: true);
            }
        }
        else
        {
            var shape = new RoofShape(ring, roof.Type, roof.Side, roof.Pitch, roof.Overhang);
            // Walls up to the roof: flat under eaves, up to the ridge under gables, rising under a shed roof.
            wall.Holed(ring, floor, p => top + Mathf.Max(0f, shape.HeightAt(p)), shape.Breaks, holes);
            Pitched(roofing, shape, top);
        }

        var lined = Inside(wall, inside, ring, floor, open ? top + CeilingGap : top, holes, enterable, rooms);
        wall.AddTo(mesh, walls);
        inside.AddTo(mesh, innerFaces ?? walls);
        lined?.AddTo(mesh, rooms!.Walls, innerFaces ?? walls);
        roofing.AddTo(mesh, roof.Material ?? walls);
        Fill(mesh, ring, holes, openings, enterable ? hung : null, enterable);
        if (!enterable)
            Close(mesh, ring, holes, openings.Backing);

        if (floor - bottom > 0.001f)
        {
            var below = new Faces();
            below.Band(ring, bottom, floor);
            below.AddTo(mesh, plinth);
        }

        return mesh;
    }

    /// <summary>A pitched roof: its slopes, their underside, and a fascia round its edge.</summary>
    private static void Pitched(Faces faces, RoofShape shape, float top)
    {
        var lift = new Vector3(0f, top, 0f);
        var down = new Vector3(0f, -RoofThickness, 0f);
        foreach (var (slope, outline) in shape.Faces())
        {
            var normal = shape.Normal(slope);
            var corners = Geometry2D.TriangulatePolygon(outline);
            for (var i = 0; i + 2 < corners.Length; i += 3)
            {
                var a = outline[corners[i]];
                var b = outline[corners[i + 1]];
                var c = outline[corners[i + 2]];
                var pa = shape.On(slope, a) + lift;
                var pb = shape.On(slope, b) + lift;
                var pc = shape.On(slope, c) + lift;
                var ua = shape.Uv(slope, a);
                var ub = shape.Uv(slope, b);
                var uc = shape.Uv(slope, c);
                faces.Triangle(pa, pb, pc, normal, ua, ub, uc);
                faces.Triangle(pa + down, pb + down, pc + down, -normal, ua, ub, uc);
            }
        }

        // The fascia: round the edge, from the underside up to the top, bending where the roof does.
        var edge = shape.Edge;
        var along = 0f;
        for (var i = 0; i < edge.Length; i++)
        {
            var a = edge[i];
            var b = edge[(i + 1) % edge.Length];
            var normal = Out(a, b);
            var length = a.DistanceTo(b);
            var ts = shape.Breaks(a, b);
            for (var k = 0; k + 1 < ts.Count; k++)
            {
                var p = a.Lerp(b, ts[k]);
                var q = a.Lerp(b, ts[k + 1]);
                var hp = top + shape.HeightAt(p);
                var hq = top + shape.HeightAt(q);
                var up = along + length * ts[k];
                var uq = along + length * ts[k + 1];
                faces.Quad(new Vector3(p.X, hp - RoofThickness, p.Y), new Vector3(q.X, hq - RoofThickness, q.Y), new Vector3(q.X, hq, q.Y), new Vector3(p.X, hp, p.Y), normal,
                    new Vector2(up, 0f), new Vector2(uq, 0f), new Vector2(uq, -RoofThickness), new Vector2(up, -RoofThickness));
            }

            along += length;
        }
    }

    /// <summary>An outline turned so its sides' outer faces are on their right, going round (counter-clockwise from above).</summary>
    public static Vector2[] Outward(Vector2[] outline)
    {
        var ring = (Vector2[])outline.Clone();
        if (SignedArea(ring) < 0f)
            System.Array.Reverse(ring);
        return ring;
    }

    /// <summary>Which side of the outline turned outward an outline's side is (the same, unless turning it reversed it).</summary>
    public static int OutwardSide(Vector2[] outline, int side)
    {
        var n = outline.Length;
        return SignedArea(outline) < 0f ? ((n - 2 - side) % n + n) % n : side;
    }

    /// <summary>The outline's area, signed: positive one way round, negative the other.</summary>
    public static float SignedArea(Vector2[] outline)
    {
        var sum = 0f;
        for (var i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2f;
    }

    /// <summary>The outer face's direction of the side from a to b, on a ring made by <see cref="Outward"/>.</summary>
    public static Vector3 Out(Vector2 a, Vector2 b)
    {
        var d = (b - a).Normalized();
        return new Vector3(d.Y, 0f, -d.X);
    }

    /// <summary>Triangles of one surface, being gathered.</summary>
    private sealed partial class Faces
    {
        private readonly List<Vector3> _points = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<Vector2> _uvs = [];
        private readonly List<Color> _colours = [];
        private bool _coloured;

        /// <summary>The colour of the corners added from now on (null: none given): see WindowColour.</summary>
        public Color? Colour { get; set; }

        /// <summary>Faces standing round the ring, outward (or inward), from one height to another.</summary>
        public void Band(Vector2[] ring, float from, float to, bool inward = false) =>
            Profiled(ring, from, _ => to, static (_, _) => [0f, 1f], inward);

        /// <summary>
        /// Faces standing round the ring from a height up to a top that changes along each side as
        /// it says, straight between the breaks it gives (fractions of the way along a side).
        /// </summary>
        public void Profiled(Vector2[] ring, float from, System.Func<Vector2, float> top, System.Func<Vector2, Vector2, List<float>> breaks, bool inward = false)
        {
            var along = 0f;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var length = a.DistanceTo(b);
                var normal = inward ? -Out(a, b) : Out(a, b);
                var ts = breaks(a, b);
                for (var k = 0; k + 1 < ts.Count; k++)
                {
                    var p = a.Lerp(b, ts[k]);
                    var q = a.Lerp(b, ts[k + 1]);
                    var tp = top(p);
                    var tq = top(q);
                    var up = along + length * ts[k];
                    var uq = along + length * ts[k + 1];
                    Quad(new Vector3(p.X, from, p.Y), new Vector3(q.X, from, q.Y), new Vector3(q.X, tq, q.Y), new Vector3(p.X, tp, p.Y), normal,
                        new Vector2(up, -from), new Vector2(uq, -from), new Vector2(uq, -tq), new Vector2(up, -tp));
                }

                along += length;
            }
        }

        /// <summary>The ring filled in, flat, at a height, facing up or down.</summary>
        public void Cap(Vector2[] ring, float height, bool up)
        {
            var corners = Geometry2D.TriangulatePolygon(ring);
            var normal = up ? Vector3.Up : Vector3.Down;
            for (var i = 0; i + 2 < corners.Length; i += 3)
            {
                var a = ring[corners[i]];
                var b = ring[corners[i + 1]];
                var c = ring[corners[i + 2]];
                Triangle(new Vector3(a.X, height, a.Y), new Vector3(b.X, height, b.Y), new Vector3(c.X, height, c.Y), normal, a, b, c);
            }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Triangle(a, b, c, normal, ua, ub, uc);
            Triangle(a, c, d, normal, ua, uc, ud);
        }

        /// <summary>One triangle, wound to face along its normal (Godot draws clockwise faces, seen from the front); none if it has no area.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var cross = (b - a).Cross(c - a);
            if (cross.LengthSquared() < 1e-10f)
                return;
            if (cross.Dot(normal) > 0f)
            {
                (b, c) = (c, b);
                (ub, uc) = (uc, ub);
            }

            _points.AddRange([a, b, c]);
            _coloured |= Colour is not null;
            var colour = Colour ?? Colors.White;
            _colours.AddRange([colour, colour, colour]);
            _normals.AddRange([normal, normal, normal]);
            _uvs.AddRange([ua, ub, uc]);
        }

        public void AddTo(ArrayMesh mesh, Material? material)
        {
            if (_points.Count == 0)
                return;
            // Through a SurfaceTool, for tangents (normal maps need them), and shared corners.
            var tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            for (var i = 0; i < _points.Count; i++)
            {
                tool.SetNormal(_normals[i]);
                tool.SetUV(_uvs[i]);
                if (_coloured)
                    tool.SetColor(_colours[i]);
                tool.AddVertex(_points[i]);
            }

            tool.Index();
            tool.GenerateTangents();
            if (material is not null)
                tool.SetMaterial(material);
            tool.Commit(mesh);
        }
    }
}
