using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A hole in a wall, for a door or window: on a side of the outline turned outward (see
/// <see cref="BuildingMesher.Outward"/>), from U0 to U1 along it (from its start) and from V0 to V1
/// up (in the building's own space).
/// </summary>
public readonly record struct Hole(int Side, float U0, float U1, float V0, float V1, BuildingOpening Opening);

/// <summary>The materials of a building's doors and windows.</summary>
public readonly record struct OpeningMaterials(Material? Frame, Material? Glass, Material? Door, Material? Backing);

public static partial class BuildingMesher
{
    /// <summary>How thick walls are, at their doors and windows, in metres.</summary>
    public const float WallThickness = 0.25f;

    /// <summary>How far frames are kept in from the sides of their holes, in metres.</summary>
    private const float Clear = 0.001f;

    /// <summary>How far under the top of the walls the ceiling inside is, in metres.</summary>
    private const float CeilingGap = 0.1f;

    /// <summary>How far in from a wall's face a frame is, in metres.</summary>
    private const float FrameInset = 0.08f;

    /// <summary>How deep a frame is, in metres.</summary>
    private const float FrameDepth = 0.07f;

    /// <summary>How wide a frame's bars are, in metres.</summary>
    private const float FrameBar = 0.06f;

    /// <summary>How wide the bars between a window's panes are, in metres.</summary>
    private const float PaneBar = 0.04f;

    /// <summary>How thick a door leaf is, in metres.</summary>
    private const float LeafThickness = 0.05f;

    /// <summary>How wide a glazed door's wood is round its glass, in metres (and at the bottom, its kick panel).</summary>
    private const float Stile = 0.12f;

    private const float KickPanel = 0.35f;

    /// <summary>Where a side's wall is in the building: from its start a, along d, and out n (from above).</summary>
    private readonly record struct WallFrame(Vector2 A, Vector2 D, Vector2 N)
    {
        /// <summary>A place: u along the wall, v up (in the building's own space), w out from its face.</summary>
        public Vector3 At(float u, float v, float w)
        {
            var p = A + D * u + N * w;
            return new Vector3(p.X, v, p.Y);
        }

        public Vector3 Along => new(D.X, 0f, D.Y);

        public Vector3 Out => new(N.X, 0f, N.Y);
    }

    private static WallFrame FrameOf(Vector2[] ring, int side)
    {
        var a = ring[side];
        var d = (ring[(side + 1) % ring.Length] - a).Normalized();
        return new WallFrame(a, d, new Vector2(d.Y, -d.X));
    }

    /// <summary>
    /// The inside of the shell: the walls' inner faces (a wall's thickness in, with the same holes),
    /// a floor and a ceiling; and the reveals round each hole, through the wall.
    /// </summary>
    /// <returns>With rooms: the faces that go to them (those in none are in <paramref name="inside"/>).</returns>
    private static Routed? Inside(Faces walls, Faces inside, Vector2[] ring, float floor, float top, List<Hole> holes, bool enterable, RoomSet? rooms = null)
    {
        Reveals(walls, ring, holes);
        if (!enterable)
            return null;
        var inner = RoofShape.Reached(ring, -WallThickness);
        // The holes, moved to the inner faces: along each from its own start.
        var innerHoles = holes.Select(h =>
        {
            var shift = (inner[h.Side] - ring[h.Side]).Dot(FrameOf(ring, h.Side).D);
            return h with { U0 = h.U0 - shift, U1 = h.U1 - shift };
        }).ToList();
        // The ceiling a little under the roof (at the same height, a material drawn both ways flickers against it).
        var ceiling = top - CeilingGap;
        if (rooms is null)
        {
            inside.Holed(inner, floor, _ => ceiling, static (_, _) => [0f, 1f], innerHoles, inward: true);
            return null;
        }

        // Split where the interior walls meet them and at each storey's floor, each part to the room it faces.
        var routed = new Routed(inside, rooms);
        Faces.HoledRouted(routed, inner, floor, ceiling, rooms.Breaks, innerHoles, rooms.Levels, inward: true);
        return routed;
    }

    /// <summary>The reveals round each hole: its sides, top and bottom, through the wall.</summary>
    private static void Reveals(Faces walls, Vector2[] ring, List<Hole> holes)
    {
        foreach (var h in holes)
        {
            var f = FrameOf(ring, h.Side);
            var t = -WallThickness;
            // Left and right, facing into the hole; the top facing down, the bottom up.
            walls.Quad(f.At(h.U0, h.V0, 0f), f.At(h.U0, h.V0, t), f.At(h.U0, h.V1, t), f.At(h.U0, h.V1, 0f), f.Along,
                new(0f, -h.V0), new(WallThickness, -h.V0), new(WallThickness, -h.V1), new(0f, -h.V1));
            walls.Quad(f.At(h.U1, h.V0, 0f), f.At(h.U1, h.V0, t), f.At(h.U1, h.V1, t), f.At(h.U1, h.V1, 0f), -f.Along,
                new(0f, -h.V0), new(WallThickness, -h.V0), new(WallThickness, -h.V1), new(0f, -h.V1));
            walls.Quad(f.At(h.U0, h.V1, 0f), f.At(h.U1, h.V1, 0f), f.At(h.U1, h.V1, t), f.At(h.U0, h.V1, t), Vector3.Down,
                new(h.U0, 0f), new(h.U1, 0f), new(h.U1, WallThickness), new(h.U0, WallThickness));
            walls.Quad(f.At(h.U0, h.V0, 0f), f.At(h.U1, h.V0, 0f), f.At(h.U1, h.V0, t), f.At(h.U0, h.V0, t), Vector3.Up,
                new(h.U0, 0f), new(h.U1, 0f), new(h.U1, WallThickness), new(h.U0, WallThickness));
        }
    }

    /// <summary>
    /// The building being made's number, from 1 to 255, for its windows' glass: set as its mesh is made
    /// (on the main thread, one building at a time).
    /// </summary>
    [System.ThreadStatic]
    internal static int WindowSeed;

    /// <summary>A number from some others, the same every run (HashCode's is not).</summary>
    internal static uint StableHash(params int[] values)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var value in values)
                hash = (hash ^ (uint)value) * 16777619u;
            return hash;
        }
    }

    /// <summary>
    /// A window's glass colour, for a glass that lights some windows at night (as Glass-Mirror-Lit
    /// does: see its shader): its own number in red, its building's in green, blue none. Its number
    /// from where it is in its wall, so each window is lit (or not) and goes dark by itself, and the
    /// same after baking. Other glass takes no notice of it.
    /// </summary>
    private static Color WindowColour(Hole h)
    {
        var id = StableHash(h.Side, Mathf.RoundToInt(h.U0 * 10f), Mathf.RoundToInt(h.V0 * 10f)) % 254u + 1u;
        return new Color(id / 255f, WindowSeed / 255f, 0f);
    }

    /// <summary>What fills each hole: frames, sills, panes and glass for windows; frames, leaves and handles for doors.</summary>
    /// <param name="hung">In the game: where the doors' leaves go instead of into the mesh (each to hang and swing).</param>
    /// <param name="inside">Whether it can be gone into: its doors with handles inside too.</param>
    private static void Fill(ArrayMesh mesh, Vector2[] ring, List<Hole> holes, OpeningMaterials materials, List<DoorLeaf>? hung = null, bool inside = false)
    {
        var frames = new Faces();
        var glass = new Faces();
        var doors = new Faces();
        var back = -FrameInset - FrameDepth;
        var front = -FrameInset;
        foreach (var h in holes)
        {
            var f = FrameOf(ring, h.Side);
            // A millimetre in from the hole's sides and top, so the frame's faces aren't on the reveal's.
            var (u0, u1, v0, v1) = (h.U0 + Clear, h.U1 - Clear, h.V0, h.V1 - Clear);
            var o = h.Opening;
            // The frame: round the hole, but for a door not along the bottom.
            frames.Box(f, u0, u0 + FrameBar, v0, v1, back, front);
            frames.Box(f, u1 - FrameBar, u1, v0, v1, back, front);
            frames.Box(f, u0 + FrameBar, u1 - FrameBar, v1 - FrameBar, v1, back, front);

            if (o.Kind == OpeningKind.Window)
            {
                frames.Box(f, u0 + FrameBar, u1 - FrameBar, v0, v0 + FrameBar, back, front);
                // The sill: under it, reaching out past the wall.
                // Its top a little above the bottom of the hole, and its back a little behind the frame's
                // front (on them, both faces flicker against each other).
                frames.Box(f, u0 - 0.05f, u1 + 0.05f, v0 - 0.05f, v0 + 0.02f, front - 0.005f, 0.06f);
                var (iu0, iu1, iv0, iv1) = (u0 + FrameBar, u1 - FrameBar, v0 + FrameBar, v1 - FrameBar);
                for (var k = 1; k < o.Panes.X; k++)
                {
                    var u = Mathf.Lerp(iu0, iu1, k / (float)o.Panes.X);
                    frames.Box(f, u - PaneBar / 2f, u + PaneBar / 2f, iv0, iv1, back + 0.01f, front - 0.01f);
                }

                for (var k = 1; k < o.Panes.Y; k++)
                {
                    var v = Mathf.Lerp(iv0, iv1, k / (float)o.Panes.Y);
                    frames.Box(f, iu0, iu1, v - PaneBar / 2f, v + PaneBar / 2f, back + 0.01f, front - 0.01f);
                }

                glass.Colour = WindowColour(h);
                glass.Pane(f, iu0, iu1, iv0, iv1, (back + front) / 2f);
                glass.Colour = null;
                continue;
            }

            // A door's leaves, in the frame, flush with its front.
            var (lu0, lu1, lv0, lv1) = (u0 + FrameBar, u1 - FrameBar, v0, v1 - FrameBar);
            var leaves = o.Leaves switch
            {
                DoorLeaves.Single => new[] { (lu0, lu1) },
                DoorLeaves.Double => [(lu0, (lu0 + lu1) / 2f - 0.003f), ((lu0 + lu1) / 2f + 0.003f, lu1)],
                _ => [],
            };
            var lw0 = front - LeafThickness;
            var doorway = hung is null ? 0 : hung.Select(l => l.Doorway).DefaultIfEmpty(-1).Max() + 1;
            for (var k = 0; k < leaves.Length; k++)
            {
                var (a, b) = leaves[k];
                // Hung on the edge away from its handle: a single door's left (or right), a pair's outer edges.
                var hingeAtA = leaves.Length == 1 ? !o.HingeRight : k == 0;
                if (hung is not null)
                    hung.Add(HungLeaf(doorway, f, (lu0 + lu1) / 2f, a, b, lv0, lv1, lw0, front, o.Glazed, hingeAtA, o.OpensOut, inside,
                        new LeafMaterials(materials.Door, materials.Glass, materials.Frame)));
                else
                    LeafFaces(doors, glass, frames, f, a, b, lv0, lv1, lw0, front, o.Glazed, hingeAtA, inside);
            }
        }

        frames.AddTo(mesh, materials.Frame);
        doors.AddTo(mesh, materials.Door);
        glass.AddTo(mesh, materials.Glass);
    }

    /// <summary>For a building that can't be gone into: each hole closed off at the back of its wall, dark, so the hollow inside doesn't show.</summary>
    private static void Close(ArrayMesh mesh, Vector2[] ring, List<Hole> holes, Material? backing)
    {
        var backs = new Faces();
        foreach (var h in holes)
        {
            var f = FrameOf(ring, h.Side);
            var w = -WallThickness;
            backs.Quad(f.At(h.U0, h.V0, w), f.At(h.U1, h.V0, w), f.At(h.U1, h.V1, w), f.At(h.U0, h.V1, w), f.Out,
                new(h.U0, -h.V0), new(h.U1, -h.V0), new(h.U1, -h.V1), new(h.U0, -h.V1));
        }

        backs.AddTo(mesh, backing);
    }

    private sealed partial class Faces
    {
        /// <summary>
        /// Walls round the ring, as <see cref="Profiled"/>, with holes cut in them: each side in
        /// upright strips between the places its top bends and its holes start and end, each strip
        /// built round the holes in it.
        /// </summary>
        public void Holed(Vector2[] ring, float from, System.Func<Vector2, float> top, System.Func<Vector2, Vector2, List<float>> breaks, List<Hole> holes, bool inward = false)
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
                var ts = breaks(a, b).Concat(mine.SelectMany(h => new[] { h.U0 / length, h.U1 / length })).Where(t => t >= 0f && t <= 1f).Distinct().OrderBy(t => t).ToList();
                for (var k = 0; k + 1 < ts.Count; k++)
                {
                    var (t0, t1) = (ts[k], ts[k + 1]);
                    if (t1 - t0 < 1e-5f)
                        continue;
                    var p = a.Lerp(b, t0);
                    var q = a.Lerp(b, t1);
                    var (tp, tq) = (top(p), top(q));
                    var (up, uq) = (along + length * t0, along + length * t1);
                    var middle = (t0 + t1) / 2f * length;
                    var cuts = mine.Where(h => h.U0 < middle && h.U1 > middle).OrderBy(h => h.V0).ToList();
                    var at = from;
                    foreach (var h in cuts)
                    {
                        if (h.V0 > at)
                            Quad(new Vector3(p.X, at, p.Y), new Vector3(q.X, at, q.Y), new Vector3(q.X, h.V0, q.Y), new Vector3(p.X, h.V0, p.Y), normal,
                                new(up, -at), new(uq, -at), new(uq, -h.V0), new(up, -h.V0));
                        at = Mathf.Max(at, h.V1);
                    }

                    if (at < Mathf.Min(tp, tq))
                        Quad(new Vector3(p.X, at, p.Y), new Vector3(q.X, at, q.Y), new Vector3(q.X, tq, q.Y), new Vector3(p.X, tp, p.Y), normal,
                            new(up, -at), new(uq, -at), new(uq, -tq), new(up, -tp));
                }

                along += length;
            }
        }

        /// <summary>A box in a wall: from u0 to u1 along it, v0 to v1 up, w0 to w1 out from its face.</summary>
        public void Box(WallFrame f, float u0, float u1, float v0, float v1, float w0, float w1)
        {
            var c = new Vector3[8];
            for (var k = 0; k < 8; k++)
                c[k] = f.At((k & 1) == 0 ? u0 : u1, (k & 2) == 0 ? v0 : v1, (k & 4) == 0 ? w0 : w1);
            Vector2 Uv(float x, float y) => new(x, -y);
            // Front and back, left and right, top and bottom.
            Quad(c[4], c[5], c[7], c[6], f.Out, Uv(u0, v0), Uv(u1, v0), Uv(u1, v1), Uv(u0, v1));
            Quad(c[0], c[1], c[3], c[2], -f.Out, Uv(u0, v0), Uv(u1, v0), Uv(u1, v1), Uv(u0, v1));
            Quad(c[0], c[4], c[6], c[2], -f.Along, Uv(w0, v0), Uv(w1, v0), Uv(w1, v1), Uv(w0, v1));
            Quad(c[1], c[5], c[7], c[3], f.Along, Uv(w0, v0), Uv(w1, v0), Uv(w1, v1), Uv(w0, v1));
            Quad(c[2], c[3], c[7], c[6], Vector3.Up, Uv(u0, w0), Uv(u1, w0), Uv(u1, w1), Uv(u0, w1));
            Quad(c[0], c[1], c[5], c[4], Vector3.Down, Uv(u0, w0), Uv(u1, w0), Uv(u1, w1), Uv(u0, w1));
        }

        /// <summary>A pane of glass in a wall, seen from both sides: from u0 to u1, v0 to v1, at w out from its face.</summary>
        public void Pane(WallFrame f, float u0, float u1, float v0, float v1, float w)
        {
            var (a, b, c, d) = (f.At(u0, v0, w), f.At(u1, v0, w), f.At(u1, v1, w), f.At(u0, v1, w));
            Quad(a, b, c, d, f.Out, new(u0, -v0), new(u1, -v0), new(u1, -v1), new(u0, -v1));
            Quad(a, b, c, d, -f.Out, new(u0, -v0), new(u1, -v0), new(u1, -v1), new(u0, -v1));
        }
    }
}
