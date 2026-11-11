using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>A room's shape on its storey's floor: the space round its seed, and any walls standing free inside it (as holes).</summary>
public sealed record RoomShape(int Index, int Storey, Vector2[] Outer, List<Vector2[]> Holes)
{
    public bool Contains(Vector2 p) => Geometry2D.IsPointInPolygon(p, Outer) && !Holes.Any(h => Geometry2D.IsPointInPolygon(p, h));
}

/// <summary>
/// A building's rooms as built: their shapes, their materials (null where the building's are used),
/// where the walls meet (so faces are split there, each part getting the room it faces), and the
/// storeys' heights.
/// </summary>
public sealed class RoomSet
{
    public required List<RoomShape> Shapes { get; init; }
    public required Material?[] Walls { get; init; }
    public required Material?[] Floors { get; init; }
    public required Material?[] Ceilings { get; init; }

    /// <summary>The storeys' floors, from the lowest: faces are split at each.</summary>
    public required float[] Levels { get; init; }

    /// <summary>Where along a line (as fractions of the way from a to b) a wall meets it.</summary>
    public required System.Func<Vector2, Vector2, List<float>> Breaks { get; init; }

    /// <summary>The room a place on a storey is in (its index among the building's rooms), or -1.</summary>
    public int At(Vector2 p, int storey) => Shapes.FirstOrDefault(s => s.Storey == storey && s.Contains(p))?.Index ?? -1;

    /// <summary>The room a place at a height is in, or -1.</summary>
    public int At(Vector2 p, float y)
    {
        var storey = -1;
        for (var k = 0; k < Levels.Length; k++)
            if (y >= Levels[k] - 0.001f)
                storey = k;
        return storey < 0 ? -1 : At(p, storey);
    }
}

public static partial class BuildingMesher
{
    /// <summary>How far in front of a face the room it faces is looked for.</summary>
    private const float FacingReach = 0.05f;

    /// <summary>Faces that go to whichever room they face: those in no room to the fallback.</summary>
    private sealed class Routed(Faces fallback, RoomSet rooms)
    {
        public readonly Dictionary<int, Faces> Rooms = [];

        public Faces For(Vector3 middle, Vector3 normal)
        {
            var p = new Vector2(middle.X + normal.X * FacingReach, middle.Z + normal.Z * FacingReach);
            var room = rooms.At(p, middle.Y);
            if (room < 0)
                return fallback;
            if (!Rooms.TryGetValue(room, out var faces))
                Rooms[room] = faces = new Faces();
            return faces;
        }

        public void AddTo(ArrayMesh mesh, Material?[] materials, Material? fallbackMaterial)
        {
            foreach (var (room, faces) in Rooms)
                faces.AddTo(mesh, materials[room] ?? fallbackMaterial);
        }
    }

    private sealed partial class Faces
    {
        /// <summary>
        /// As <see cref="Holed"/> (with a level top), each strip also split at the given heights,
        /// and every quad going to the faces the router picks for it.
        /// </summary>
        public static void HoledRouted(Routed route, Vector2[] ring, float from, float top, System.Func<Vector2, Vector2, List<float>> breaks, List<Hole> holes, float[] levels, bool inward)
        {
            var along = 0f;
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var length = a.DistanceTo(b);
                if (length < 1e-4f)
                    continue;
                var normal = inward ? -Out(a, b) : Out(a, b);
                var mine = holes.Where(h => h.Side == i).ToList();
                var ts = breaks(a, b).Append(0f).Append(1f).Concat(mine.SelectMany(h => new[] { h.U0 / length, h.U1 / length }))
                    .Where(t => t >= 0f && t <= 1f).Distinct().OrderBy(t => t).ToList();
                for (var k = 0; k + 1 < ts.Count; k++)
                {
                    var (t0, t1) = (ts[k], ts[k + 1]);
                    if (t1 - t0 < 1e-5f)
                        continue;
                    var p = a.Lerp(b, t0);
                    var q = a.Lerp(b, t1);
                    var (up, uq) = (along + length * t0, along + length * t1);
                    var middle = (t0 + t1) / 2f * length;
                    var cuts = mine.Where(h => h.U0 < middle && h.U1 > middle).OrderBy(h => h.V0).ToList();
                    void Strip(float lo, float hi)
                    {
                        // Split at each storey's floor between, each part to the room it faces.
                        var marks = levels.Where(l => l > lo + 1e-4f && l < hi - 1e-4f).Prepend(lo).Append(hi).ToList();
                        for (var m = 0; m + 1 < marks.Count; m++)
                        {
                            var (v0, v1) = (marks[m], marks[m + 1]);
                            var mid = new Vector3((p.X + q.X) / 2f, (v0 + v1) / 2f, (p.Y + q.Y) / 2f);
                            route.For(mid, normal).Quad(new Vector3(p.X, v0, p.Y), new Vector3(q.X, v0, q.Y), new Vector3(q.X, v1, q.Y), new Vector3(p.X, v1, p.Y), normal,
                                new(up, -v0), new(uq, -v0), new(uq, -v1), new(up, -v1));
                        }
                    }

                    var at = from;
                    foreach (var h in cuts)
                    {
                        if (h.V0 > at)
                            Strip(at, h.V0);
                        at = Mathf.Max(at, h.V1);
                    }

                    if (at < top)
                        Strip(at, top);
                }

                along += length;
            }
        }
    }
}

public partial class Building
{
    private Godot.Collections.Array<Room> _rooms = [];

    /// <summary>Its rooms, on every storey: made in the 3D view (Rooms, with a storey picked). Only built when it is enterable.</summary>
    [ExportGroup("Interior")]
    [Export]
    public Godot.Collections.Array<Room> Rooms
    {
        get => _rooms;
        set
        {
            foreach (var r in _rooms.Where(r => r is not null))
                if (r.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    r.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            _rooms = value ?? [];
            foreach (var r in _rooms.Where(r => r is not null))
                if (!r.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    r.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>The outer walls' inner faces, on the ground: the ring inside them.</summary>
    public Vector2[] InnerRing() => RoofShape.Reached(BuildingMesher.Outward(_outline), -BuildingMesher.WallThickness);

    /// <summary>An interior wall's outline on the ground (reaching half its thickness past each end, as built).</summary>
    public static Vector2[] WallFootprint(InteriorWall w)
    {
        var length = w.A.DistanceTo(w.B);
        var d = length < 0.001f ? Vector2.Right : (w.B - w.A) / length;
        var n = new Vector2(d.Y, -d.X) * (w.Thickness / 2f);
        var a = w.A - d * (w.Thickness / 2f);
        var b = w.B + d * (w.Thickness / 2f);
        return [a - n, b - n, b + n, a + n];
    }

    /// <summary>
    /// The space round a place on a storey, closed off by the outer walls and the storey's interior
    /// walls; none if the place is outside them or in a wall.
    /// </summary>
    public (Vector2[] Outer, List<Vector2[]> Holes)? SpaceAround(int storey, Vector2 seed, Vector2[]? inner = null)
    {
        inner ??= InnerRing();
        if (!Geometry2D.IsPointInPolygon(seed, inner))
            return null;
        var outer = inner;
        var pending = _walls.Where(w => w is not null && w.Storey == storey && w.A.DistanceTo(w.B) > 0.01f).Select(WallFootprint).ToList();
        var holes = new List<Vector2[]>();
        // Walls cutting into it narrow it to the side the seed is on; walls standing free inside it
        // are holes, until (taken again) a wall that joins them to its edge has cut it.
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var foot in pending.ToList())
            {
                var parts = Geometry2D.ClipPolygons(outer, foot).ToList();
                var holed = parts.Count > 1 && parts.Any(r => parts.Any(o => o != r && Geometry2D.IsPointInPolygon(r[0], o)));
                if (holed)
                    continue;
                pending.Remove(foot);
                changed = true;
                if (parts.FirstOrDefault(p => Geometry2D.IsPointInPolygon(seed, p)) is not { } kept)
                    return null;
                outer = kept;
            }
        }

        foreach (var foot in pending)
        {
            if (Geometry2D.IsPointInPolygon(seed, foot))
                return null;
            holes.Add(foot);
        }

        return (outer, holes);
    }

    /// <summary>Its rooms' shapes, materials, and where its walls meet, as built.</summary>
    private RoomSet Rooms_()
    {
        var inner = InnerRing();
        var shapes = new List<RoomShape>();
        for (var i = 0; i < _rooms.Count; i++)
        {
            var r = _rooms[i];
            if (r is null || r.Storey >= _storeys || SpaceAround(r.Storey, r.Seed, inner) is not { } space)
                continue;
            // Two rooms in one space: the first has it.
            if (shapes.Any(s => s.Storey == r.Storey && s.Contains(r.Seed)))
                continue;
            shapes.Add(new RoomShape(i, r.Storey, space.Outer, space.Holes));
        }

        var walls = _walls.Where(w => w is not null && w.A.DistanceTo(w.B) > 0.01f).ToList();
        List<float> Breaks(Vector2 a, Vector2 b)
        {
            var result = new List<float>();
            var length = a.DistanceTo(b);
            if (length < 1e-4f)
                return result;
            var d = (b - a) / length;
            foreach (var w in walls)
            {
                var wl = w.A.DistanceTo(w.B);
                var wd = (w.B - w.A) / wl;
                if (Mathf.Abs(d.Cross(wd)) < 0.05f)
                    continue;
                if (Geometry2D.LineIntersectsLine(a, d, w.A, wd).Obj is not Vector2 hit)
                    continue;
                // Only where the wall reaches the line (its ends go half its thickness past, and it may end at the far face of another).
                var along = (hit - w.A).Dot(wd);
                var reach = w.Thickness / 2f + 0.35f;
                if (along < -reach || along > wl + reach)
                    continue;
                // At both of its faces, measured along the line.
                var half = w.Thickness / 2f / Mathf.Abs(d.Cross(wd));
                var t = (hit - a).Dot(d);
                result.Add((t - half) / length);
                result.Add((t + half) / length);
            }

            return result;
        }

        return new RoomSet
        {
            Shapes = shapes,
            Walls = [.. _rooms.Select(r => r?.WallMaterial)],
            Floors = [.. _rooms.Select(r => r?.FloorMaterial)],
            Ceilings = [.. _rooms.Select(r => r?.CeilingMaterial)],
            Levels = [.. Enumerable.Range(0, _storeys).Select(StoreyFloor)],
            Breaks = Breaks,
        };
    }

    /// <summary>The room a place is in (in the world), if any: in the game too.</summary>
    public Room? RoomAt(Vector3 globalPosition)
    {
        if (!_enterable)
            return null;
        var local = ToLocal(globalPosition);
        var rooms = Rooms_();
        var index = rooms.At(new Vector2(local.X, local.Z), local.Y + 0.01f);
        return index >= 0 ? _rooms[index] : null;
    }
}
