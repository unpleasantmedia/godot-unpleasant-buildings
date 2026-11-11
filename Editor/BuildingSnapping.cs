#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Where a corner being drawn or dragged snaps to with Snap on: another building's corner; or the
/// line of a side (of another building, along it or past its ends); or a direction square to or in
/// line with the corner's own sides or another building's; or where two of those lines cross. All
/// within <see cref="Reach"/>. Worked out from above, in the world.
/// </summary>
public static class BuildingSnapping
{
    /// <summary>How near a corner must be to what it snaps to, in metres.</summary>
    public const float Reach = 0.5f;

    /// <summary>How far other buildings are looked at, past the corner, in metres.</summary>
    private const float Around = 60f;

    /// <summary>A line to snap to: through a point, one way (both ways along it), where it is shown from and to, and at what height in the world (its building's floor).</summary>
    public readonly record struct Guide(Vector2 Through, Vector2 Along, Vector2 From, Vector2 To, float Height);

    /// <summary>
    /// Where a corner of a building snaps to from a place (in the world, from above), and the guides
    /// it snapped to (to show). Its neighbours are the corners it is joined to (the one before it
    /// while drawing; both sides when dragging), each with the one beyond it, if any, for its side's direction.
    /// </summary>
    /// <param name="mouse">Where the mouse's ray meets the level of a height in the world, from above: each building's corners and sides are measured from where it meets their floor's level, where they are shown.</param>
    /// <param name="height">The level of the building's own floor, in the world.</param>
    public static (Vector2 At, List<Guide> Guides) Snap(Building building, System.Func<float, Vector2?> mouse, float height, IEnumerable<(Vector2 Corner, Vector2? Beyond)> neighbours)
    {
        var guides = new List<Guide>();
        if (mouse(height) is not { } at)
            return (default, guides);
        var others = Building.InTree
            .Where(b => b != building && GodotObject.IsInstanceValid(b) && b.IsInsideTree() && b.Outline.Length >= 3)
            .Select(b => (Outline: b.Outline.Select(c => Flat(b.ToGlobal(new Vector3(c.X, 0f, c.Y)))).ToArray(), Height: b.ToGlobal(new Vector3(0f, b.FloorHeight, 0f)).Y))
            .Where(o => o.Outline.Any(c => c.DistanceTo(at) < Around))
            .ToList();

        // Where the mouse is at a height (worked out once for each).
        var seen = new Dictionary<float, Vector2?>();
        Vector2? At(float h) => seen.TryGetValue(h, out var p) ? p : seen[h] = mouse(h);

        // Another building's corner: exactly onto it.
        var corner = others
            .SelectMany(o => o.Outline.Select(c => (Corner: c, Mouse: At(o.Height))))
            .Where(x => x.Mouse is { } m && x.Corner.DistanceTo(m) < Reach)
            .OrderBy(x => x.Corner.DistanceTo(x.Mouse!.Value))
            .Select(x => (Vector2?)x.Corner)
            .FirstOrDefault();
        if (corner is { } exact)
            return (exact, guides);

        var lines = new List<Guide>();
        var directions = new List<Vector2>();
        foreach (var (outline, level) in others)
        {
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Length];
                if (a.DistanceTo(b) < 0.01f)
                    continue;
                var along = (b - a).Normalized();
                lines.Add(new Guide(a, along, a, b, level));
                directions.Add(along);
            }
        }

        // Square to, or in line with, its own sides and other buildings' sides, from each corner it is joined to.
        foreach (var (near, beyond) in neighbours)
        {
            var own = beyond is { } far && far.DistanceTo(near) > 0.01f ? new[] { (near - far).Normalized() } : [];
            foreach (var direction in own.Concat(directions).Distinct())
            {
                lines.Add(new Guide(near, direction, near, near, height));
                lines.Add(new Guide(near, new Vector2(-direction.Y, direction.X), near, near, height));
            }
        }

        var close = lines
            .Where(g => At(g.Height) is not null)
            .Select(g => (Guide: g, Distance: Distance(g, At(g.Height)!.Value)))
            .Where(x => x.Distance < Reach)
            .OrderBy(x => x.Distance)
            .ToList();
        if (close.Count == 0)
            return (at, guides);

        // Two lines crossing near it: onto the crossing.
        var first = close[0].Guide;
        foreach (var (second, _) in close.Skip(1))
        {
            if (Mathf.Abs(Cross(first.Along, second.Along)) < 0.05f)
                continue;
            if (Crossing(first, second) is { } both && both.DistanceTo(At(first.Height)!.Value) < Reach * 1.5f)
                return (both, [Shown(first, both), Shown(second, both)]);
        }

        var onto = Onto(first, At(first.Height)!.Value);
        return (onto, [Shown(first, onto)]);
    }

    private static Vector2 Flat(Vector3 world) => new(world.X, world.Z);

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector2 Onto(Guide g, Vector2 p) => g.Through + g.Along * (p - g.Through).Dot(g.Along);

    private static float Distance(Guide g, Vector2 p) => Onto(g, p).DistanceTo(p);

    private static Vector2? Crossing(Guide a, Guide b)
    {
        var d = Cross(a.Along, b.Along);
        if (Mathf.Abs(d) < 1e-6f)
            return null;
        var t = Cross(b.Through - a.Through, b.Along) / d;
        return a.Through + a.Along * t;
    }

    /// <summary>A guide as shown: what it was made from, on to where the corner went.</summary>
    private static Guide Shown(Guide g, Vector2 to)
    {
        var points = new[] { g.From, g.To, to };
        var ts = points.Select(p => (p - g.Through).Dot(g.Along)).ToArray();
        var from = g.Through + g.Along * ts.Min();
        var end = g.Through + g.Along * ts.Max();
        return g with { From = from, To = end };
    }
}
#endif
