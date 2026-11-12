using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>One part of a building, for its mesh: its outline, and the heights it spans in the building's own space.</summary>
/// <param name="Flat">Whether its roof is flat.</param>
public readonly record struct PartBlock(Vector2[] Outline, float Bottom, float Top, bool Flat = false);

/// <summary>A part beside a building's main one, for its mesh (see <see cref="BuildingWing"/>).</summary>
/// <param name="Index">Its place among all the building's blocks (the main one first), to leave itself out of what covers it.</param>
/// <param name="Grounded">Whether it starts on the ground floor: a plinth down to the ground, rather than an underside.</param>
/// <param name="Capped">Whether its top is closed (not when the building is cut open lower down).</param>
/// <param name="Roof">Its roof (its own settings, or its building's); flat wherever another part carries on above it.</param>
/// <param name="Holes">Its doors and windows, on the sides of its outline turned outward.</param>
public readonly record struct WingBlock(int Index, PartBlock Block, bool Grounded, bool Capped, Material? Underside, RoofSettings Roof, List<Hole> Holes);

public static partial class BuildingMesher
{
    /// <summary>How far out from a wall another part is looked for, to tell whether the wall is against it, in metres.</summary>
    private const float CoverProbe = 0.02f;

    /// <summary>
    /// Where a side of a ring (turned outward: see <see cref="Outward"/>) has another of its building's
    /// parts right outside it, and over what heights: there the two are one shape, and no wall
    /// stands between them. Found a little way out from the side, so a part sharing the side counts.
    /// </summary>
    public static List<Covered> CoveredBy(Vector2 a, Vector2 b, IEnumerable<PartBlock> others)
    {
        var found = new List<Covered>();
        var d = b - a;
        var lengthSquared = d.LengthSquared();
        if (lengthSquared < 1e-8f)
            return found;
        var out3 = Out(a, b);
        var shift = new Vector2(out3.X, out3.Z) * CoverProbe;
        var (sa, sb) = (a + shift, b + shift);
        foreach (var other in others)
        {
            if (other.Outline.Length < 3 || other.Top <= other.Bottom)
                continue;
            foreach (var piece in Geometry2D.IntersectPolylineWithPolygon(new[] { sa, sb }, other.Outline))
            {
                if (piece.Length < 2)
                    continue;
                var t0 = Mathf.Clamp((piece[0] - sa).Dot(d) / lengthSquared, 0f, 1f);
                var t1 = Mathf.Clamp((piece[^1] - sa).Dot(d) / lengthSquared, 0f, 1f);
                if (t1 < t0)
                    (t0, t1) = (t1, t0);
                if (t1 - t0 > 1e-4f)
                    found.Add(new Covered(t0, t1, other.Bottom, other.Top, other.Flat));
            }
        }

        return found;
    }

    /// <summary>
    /// A building's parts beside its main one, added to its mesh: each walled from its floor to its
    /// top, but not where another part lies against it (they are one shape); on the ground, a plinth
    /// down to the building's bottom; above it, closed underneath where nothing below holds it up;
    /// and roofed: flat where another part carries on above it, else as its roof settings say.
    /// </summary>
    /// <param name="all">Every part of the building (its main one first), each where it is.</param>
    public static void Wings(ArrayMesh mesh, IReadOnlyList<WingBlock> wings, IReadOnlyList<PartBlock> all, float bottom, Material? walls, Material? plinth, OpeningMaterials openings)
    {
        var wall = new Faces();
        var below = new Faces();
        var undersides = new Dictionary<Material, Faces>();
        var undersideWalls = new Faces();
        // Roofs by material (none: the walls').
        var roofs = new List<(Material? Material, Faces Faces)>();
        foreach (var wing in wings)
        {
            var block = wing.Block;
            if (block.Outline.Length < 3 || block.Top <= block.Bottom)
                continue;
            var ring = Outward(block.Outline);
            var others = all.Where((_, i) => i != wing.Index).ToList();
            // What carries on above it (its top flat then: under that one, and round it).
            var over = others.Where(o => o.Bottom <= block.Top + 0.01f && o.Top > block.Top + 0.01f && Building.Overlaps(o.Outline, block.Outline)).Select(o => o.Outline).ToList();
            var roof = wing.Roof;
            System.Func<Vector2, Vector2, List<Covered>> covered = (a, b) => CoveredBy(a, b, others);
            var roofing = roofs.FirstOrDefault(x => x.Material == roof.Material).Faces;
            if (roofing is null)
                roofs.Add((roof.Material, roofing = new Faces()));
            if (!wing.Capped)
            {
                wall.Holed(ring, block.Bottom, _ => block.Top, static (_, _) => [0f, 1f], wing.Holes, covered: covered);
            }
            else if (over.Count > 0 || roof.Type == RoofType.Flat)
            {
                // Flat: with its parapet, unless another part stands on it.
                var parapet = over.Count > 0 ? 0f : Mathf.Max(0f, roof.Parapet);
                if (parapet > 0.001f)
                {
                    Parapet(wall, roofing, ring, block.Bottom, block.Top, parapet, wing.Holes, covered, RoofRing(wing.Index, all));
                }
                else
                {
                    wall.Holed(ring, block.Bottom, _ => block.Top, static (_, _) => [0f, 1f], wing.Holes, covered: covered);
                    foreach (var open in Without(block.Outline, over))
                        roofing.Cap(open, block.Top, up: true);
                }
            }
            else
            {
                // Pitched: walls up to it (to the ridge under gables), as the main part's.
                var shape = new RoofShape(ring, roof.Type, roof.Side, roof.Pitch, roof.Overhang);
                wall.Holed(ring, block.Bottom, p => block.Top + Mathf.Max(0f, shape.HeightAt(p)), shape.Breaks, wing.Holes, covered: covered);
                Pitched(roofing, shape, block.Top);
            }

            // Its doors and windows framed and glazed, closed off at the back (it has no inside).
            if (wing.Holes.Count > 0)
            {
                // Lined through the wall round each (without, the frame stood in a gap in the wall).
                Reveals(wall, ring, wing.Holes);
                Fill(mesh, ring, wing.Holes, openings);
                Close(mesh, ring, wing.Holes, openings.Backing);
            }

            if (wing.Grounded)
            {
                if (block.Bottom - bottom > 0.001f)
                    below.Band(ring, bottom, block.Bottom);
            }
            else
            {
                // Underneath, where no part on the floor below holds it up.
                var holding = others.Where(o => o.Bottom < block.Bottom - 0.01f && o.Top >= block.Bottom - 0.01f).Select(o => o.Outline);
                var into = wing.Underside is { } u ? (undersides.TryGetValue(u, out var f) ? f : undersides[u] = new Faces()) : undersideWalls;
                foreach (var open in Without(block.Outline, holding))
                    into.Cap(open, block.Bottom, up: false);
            }
        }

        wall.AddTo(mesh, walls);
        below.AddTo(mesh, plinth ?? walls);
        undersideWalls.AddTo(mesh, walls);
        foreach (var (material, faces) in undersides)
            faces.AddTo(mesh, material);
        foreach (var (material, faces) in roofs)
            faces.AddTo(mesh, material ?? walls);
    }

    /// <summary>
    /// A covered stretch raised to the top of a parapet where what covers it has a flat roof at the
    /// same height (the two roofs one, no parapet between them).
    /// </summary>
    public static System.Func<Vector2, Vector2, List<Covered>>? Raised(System.Func<Vector2, Vector2, List<Covered>>? covered, float top, float parapet) =>
        covered is null ? null : (a, b) => covered(a, b).Select(c => c.Flat && Mathf.Abs(c.V1 - top) < 0.01f ? c with { V1 = top + parapet } : c).ToList();

    /// <summary>
    /// The outline its flat roof's parapet goes round, turned outward: its own, joined with every
    /// part's whose flat roof is at the same height and lies against it (and theirs, in turn), so
    /// their parapets run round them as one.
    /// </summary>
    public static Vector2[] RoofRing(int index, IReadOnlyList<PartBlock> all)
    {
        var own = all[index];
        var group = new List<int> { index };
        for (var k = 0; k < group.Count; k++)
        {
            var at = all[group[k]];
            for (var i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (!group.Contains(i) && o.Flat && o.Outline.Length >= 3 && Mathf.Abs(o.Top - own.Top) < 0.01f && Building.Touches(at.Outline, o.Outline))
                    group.Add(i);
            }
        }

        var union = Outward(own.Outline);
        foreach (var i in group.Skip(1))
        {
            var merged = Geometry2D.MergePolygons(union, Outward(all[i].Outline)).ToList();
            // Its outer one (holes run clockwise); apart (only near, not on, each other): left as it was.
            var outer = merged.Where(m => m.Length >= 3 && !Geometry2D.IsPolygonClockwise(m)).ToList();
            if (outer.Count == 1)
                union = Outward(outer[0]);
        }

        return union;
    }

    /// <summary>
    /// A flat roof with a parapet round it: walls from a height up to its top, and its share of the
    /// parapet's inner face and cap and of the roof inside it. The parapet goes round the roof's
    /// outline (see <see cref="RoofRing"/>): where another flat roof at its height lies against it,
    /// the two are one roof, their parapet round both.
    /// </summary>
    private static void Parapet(Faces wall, Faces roofing, Vector2[] ring, float from, float top, float parapet, List<Hole> holes, System.Func<Vector2, Vector2, List<Covered>>? covered, Vector2[]? roofRing)
    {
        var high = top + parapet;
        wall.Holed(ring, from, _ => high, static (_, _) => [0f, 1f], holes, covered: Raised(covered, top, parapet));
        var round = roofRing ?? ring;
        var inner = RoofShape.Reached(round, -ParapetThickness);
        Vector3 At(Vector2 p, float h) => new(p.X, h, p.Y);
        // The roof inside the parapet: its share.
        foreach (var piece in Geometry2D.IntersectPolygons(ring, inner))
        {
            if (piece.Length >= 3 && !Geometry2D.IsPolygonClockwise(piece))
                roofing.Cap(piece, top, up: true);
        }

        var along = 0f;
        for (var i = 0; i < round.Length; i++)
        {
            var j = (i + 1) % round.Length;
            var length = round[i].DistanceTo(round[j]);
            var inward = -Out(round[i], round[j]);
            // Its inner face: the stretches of it over this part.
            foreach (var piece in Geometry2D.IntersectPolylineWithPolygon(new[] { inner[i], inner[j] }, ring))
            {
                if (piece.Length < 2)
                    continue;
                var (n0, n1) = (piece[0], piece[^1]);
                var (u0, u1) = (along + n0.DistanceTo(inner[i]), along + n1.DistanceTo(inner[i]));
                wall.Quad(At(n0, top), At(n1, top), At(n1, high), At(n0, high), inward, new(u0, -top), new(u1, -top), new(u1, -high), new(u0, -high));
            }

            // Its cap: the stretch between the outline and its inner face, over this part.
            foreach (var piece in Geometry2D.IntersectPolygons(new[] { round[i], round[j], inner[j], inner[i] }, ring))
            {
                if (piece.Length >= 3 && !Geometry2D.IsPolygonClockwise(piece))
                    wall.Cap(piece, high, up: true);
            }

            along += length;
        }
    }

    /// <summary>
    /// What is left of an outline once others are taken out of it: its pieces, without holes (where
    /// one lies wholly inside it, the piece round it is kept whole: what it would leave is hidden in that one).
    /// </summary>
    private static List<Vector2[]> Without(Vector2[] outline, IEnumerable<Vector2[]> cutters)
    {
        var pieces = new List<Vector2[]> { outline };
        foreach (var cutter in cutters)
        {
            var next = new List<Vector2[]>();
            foreach (var piece in pieces)
            {
                // Read once (each read of Godot's list makes the polygons anew, so none would be itself).
                var result = Geometry2D.ClipPolygons(piece, cutter).ToList();
                // A hole lies inside another of the results: left out, the piece round it kept as it is.
                next.AddRange(result.Where((r, i) => r.Length >= 3 && !result.Where((_, j) => j != i).Any(o => Geometry2D.IsPointInPolygon(r[0], o))));
            }

            pieces = next;
        }

        return pieces.Where(p => Mathf.Abs(SignedArea(p)) > 0.01f).ToList();
    }
}
