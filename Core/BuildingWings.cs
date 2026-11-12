using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>A building's parts beside its main one (see <see cref="BuildingWing"/>), and how they hang together.</summary>
public partial class Building
{
    private Godot.Collections.Array<BuildingWing> _parts = [];

    /// <summary>Its parts beside its main one (its <see cref="Outline"/>): drawn in the 3D view with Draw Part, on the floor picked.</summary>
    [ExportGroup("Parts")]
    [Export]
    public Godot.Collections.Array<BuildingWing> Parts
    {
        get => _parts;
        set
        {
            foreach (var p in _parts.Where(p => p is not null))
                if (p.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.PartChanged)))
                    p.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.PartChanged));
            _parts = value ?? [];
            foreach (var p in _parts.Where(p => p is not null))
                if (!p.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.PartChanged)))
                    p.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.PartChanged));
            PartChanged();
        }
    }

    private void PartChanged()
    {
        QueueBuild();
        UpdateGizmos();
        UpdateConfigurationWarnings();
    }

    /// <summary>Its parts with an outline to build, each with the storeys it spans (its main part first, as part -1).</summary>
    public IEnumerable<(int Index, Vector2[] Outline, int Floor, int Top)> AllParts()
    {
        if (_outline.Length >= 3)
            yield return (-1, _outline, 0, _storeys);
        for (var i = 0; i < _parts.Count; i++)
        {
            if (_parts[i] is { Outline.Length: >= 3 } part)
                yield return (i, part.Outline, part.Floor, part.Top);
        }
    }

    /// <summary>How many storeys tall it is, all its parts together.</summary>
    public int AllStoreys => AllParts().Select(p => p.Top).DefaultIfEmpty(_storeys).Max();

    /// <summary>How far two outlines may be apart and still count as touching, in metres.</summary>
    public const float TouchReach = 0.05f;

    /// <summary>
    /// Whether an outline, on a floor, would be attached to the rest of the building: on the ground
    /// floor, always; above it, overlapping or touching a part on that floor, or resting on (overlapping)
    /// one on the floor below. Itself (a part being changed) left out.
    /// </summary>
    public bool Attached(Vector2[] outline, int floor, int except = int.MinValue)
    {
        if (floor <= 0)
            return true;
        if (outline.Length < 3)
            return false;
        foreach (var (index, other, from, top) in AllParts())
        {
            if (index == except)
                continue;
            // Beside it on its own floor: touching will do.
            if (from <= floor && floor < top && Touches(outline, other))
                return true;
            // On top of it, its floor below: resting on it.
            if (from <= floor - 1 && floor - 1 < top && Overlaps(outline, other))
                return true;
        }

        return false;
    }

    /// <summary>Whether two outlines share some area (more than a touch).</summary>
    public static bool Overlaps(Vector2[] a, Vector2[] b) =>
        Geometry2D.IntersectPolygons(a, b).Any(p => Mathf.Abs(BuildingMesher.SignedArea(p)) > 0.01f);

    /// <summary>Whether two outlines overlap or come within <see cref="TouchReach"/> of each other.</summary>
    public static bool Touches(Vector2[] a, Vector2[] b)
    {
        if (Overlaps(a, b))
            return true;
        for (var i = 0; i < a.Length; i++)
        {
            var (a0, a1) = (a[i], a[(i + 1) % a.Length]);
            for (var j = 0; j < b.Length; j++)
            {
                var (b0, b1) = (b[j], b[(j + 1) % b.Length]);
                if (Geometry2D.SegmentIntersectsSegment(a0, a1, b0, b1).VariantType != Variant.Type.Nil)
                    return true;
                if (Geometry2D.GetClosestPointToSegment(a0, b0, b1).DistanceTo(a0) < TouchReach
                    || Geometry2D.GetClosestPointToSegment(b0, a0, a1).DistanceTo(b0) < TouchReach)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every part's block for its mesh, its main one first: its outline and the heights it spans, cut
    /// off at a height (the building cut open lower down). Only the main one if it has no others.
    /// </summary>
    private List<PartBlock> PartBlocks(float cut)
    {
        var blocks = new List<PartBlock> { new(_outline, FloorHeight, Mathf.Min(TopHeight, cut), _roof == RoofType.Flat || MainCarried()) };
        foreach (var part in _parts)
        {
            if (part is { Outline.Length: >= 3 } && Simple(part.Outline))
                blocks.Add(new PartBlock(part.Outline, StoreyFloor(part.Floor), Mathf.Min(StoreyFloor(part.Top), cut), PartRoof(part).Type == RoofType.Flat));
        }

        return blocks;
    }

    /// <summary>The parts beside its main one, for its mesh (the blocks from <see cref="PartBlocks"/>, in the same order).</summary>
    private List<WingBlock> Wings(List<PartBlock> blocks, float cut)
    {
        var wings = new List<WingBlock>();
        var index = 1;
        foreach (var part in _parts)
        {
            if (part is not { Outline.Length: >= 3 } || !Simple(part.Outline))
                continue;
            var number = _parts.IndexOf(part);
            var holes = PartHoles(number).Where(h => h.V0 < cut).ToList();
            wings.Add(new WingBlock(index, blocks[index], part.Floor == 0, StoreyFloor(part.Top) <= cut, part.UndersideMaterial, PartRoof(part), holes));
            index++;
        }

        return wings;
    }

    /// <summary>
    /// A wall of the building, by its number: its main part's sides first (0 on), then each part's in
    /// turn (see <see cref="WallSide"/>). Doors, windows, details and balconies are on a wall by this
    /// number, so they can be on any part's.
    /// </summary>
    /// <param name="Part">Its part (-1: the main one).</param>
    /// <param name="Local">Its side in that part's outline.</param>
    /// <param name="Floor">The storey its part starts on.</param>
    /// <param name="Top">The storey above its part's top one.</param>
    public readonly record struct Wall(int Part, int Local, Vector2[] Outline, int Floor, int Top);

    /// <summary>How many walls it has, its main part's and every other part's (see <see cref="WallSide"/>).</summary>
    public int SideCount => _outline.Length + _parts.Sum(p => p?.Outline.Length ?? 0);

    /// <summary>The number of a part's first wall (see <see cref="WallSide"/>): 0 for its main one.</summary>
    public int FirstSide(int part)
    {
        var first = _outline.Length;
        for (var i = 0; i < part && i < _parts.Count; i++)
            first += _parts[i]?.Outline.Length ?? 0;
        return part < 0 ? 0 : first;
    }

    /// <summary>A wall by its number (see <see cref="Wall"/>), if it has one of that number.</summary>
    public Wall? WallSide(int side)
    {
        if (side < 0)
            return null;
        if (side < _outline.Length)
            return new Wall(-1, side, _outline, 0, _storeys);
        var rest = side - _outline.Length;
        for (var i = 0; i < _parts.Count; i++)
        {
            var count = _parts[i]?.Outline.Length ?? 0;
            if (rest < count)
                return new Wall(i, rest, _parts[i].Outline, _parts[i].Floor, _parts[i].Top);
            rest -= count;
        }

        return null;
    }

    /// <summary>The storey a height on a wall is in (see <see cref="WallSide"/>), kept to its part's storeys.</summary>
    public int StoreyOn(int side, float v)
    {
        var (floor, top) = WallSide(side) is { } w ? (w.Floor, w.Top) : (0, _storeys);
        return Mathf.Clamp(Mathf.FloorToInt((v - FloorHeight) / _storeyHeight), floor, Mathf.Max(floor, top - 1));
    }

    /// <summary>A part's roof: its own settings, or its building's; the side it goes by from its own outline (its longest, unless set).</summary>
    private RoofSettings PartRoof(BuildingWing part)
    {
        var own = part.OwnRoof;
        var side = own ? part.RoofSide : -1;
        var outline = part.Outline;
        if (side < 0 || side >= outline.Length)
        {
            side = 0;
            for (var i = 1; i < outline.Length; i++)
                if (outline[i].DistanceTo(outline[(i + 1) % outline.Length]) > outline[side].DistanceTo(outline[(side + 1) % outline.Length]))
                    side = i;
        }

        return new RoofSettings(own ? part.Roof : _roof, BuildingMesher.OutwardSide(outline, side),
            own ? part.RoofPitch : _roofPitch, own ? part.RoofOverhang : _roofOverhang, own ? part.ParapetHeight : _parapetHeight,
            own ? part.RoofMaterial ?? _roofMaterial : _roofMaterial);
    }

    /// <summary>Whether another part stands on its main one's roof (which is flat then, so the two don't run through each other).</summary>
    private bool MainCarried() =>
        _parts.Any(p => p is { Outline.Length: >= 3 } && p.Floor <= _storeys && p.Top > _storeys && Overlaps(p.Outline, _outline));

    /// <summary>
    /// What it adds of its own (a detail's scene, a doorway) owned by it, in the editor, wherever nothing
    /// owns it yet: a click in the 3D view on one goes up by owners to a node of the scene, and with no
    /// owner it stopped at the internal node itself ("Node is internal"). The 3D editor then worked on
    /// that node, not the building: no handles drawn, its clicks not passed on, the bottom panel closed.
    /// Internal children are not saved when owned by a node below the scene's root; when it is the root
    /// itself (a building edited in a scene of its own), they would be, so they are left unowned then.
    /// </summary>
    internal void OwnInternal(Node node)
    {
        if (!Engine.IsEditorHint() || !node.IsInsideTree() || Owner is null)
            return;
        foreach (var n in node.FindChildren("*", "", true, false).Prepend(node))
        {
            if (n.Owner is null)
                n.Owner = this;
        }
    }

    /// <summary>
    /// Whether a place on a wall (along it from its start, and up) has another part right outside it:
    /// the two are one shape there, and whatever is on the wall at that place would be inside.
    /// </summary>
    public bool CoveredAt(int side, float along, float v)
    {
        if (WallSide(side) is not { } w)
            return false;
        var p = OnSide(side, along, v);
        var o = SideOut(side).Normalized() * 0.05f;
        var q = new Vector2(p.X + o.X, p.Z + o.Z);
        foreach (var (index, outline, floor, top) in AllParts())
        {
            if (index == w.Part)
                continue;
            var (low, high) = index < 0 ? (FloorHeight, TopHeight) : (StoreyFloor(floor), StoreyFloor(top));
            if (v > low && v < high && Geometry2D.IsPointInPolygon(q, outline))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether any of a stretch of wall (from one place along it to another, from one height to
    /// another) has another part right outside it: looked at in steps across it, edges included.
    /// </summary>
    public bool CoveredOver(int side, float from, float to, float v0, float v1)
    {
        const float inset = 0.02f;
        var (a, b) = (Mathf.Min(from, to) + inset, Mathf.Max(from, to) - inset);
        var (c, d) = (Mathf.Min(v0, v1) + inset, Mathf.Max(v0, v1) - inset);
        if (b < a)
            (a, b) = ((from + to) / 2f, (from + to) / 2f);
        if (d < c)
            (c, d) = ((v0 + v1) / 2f, (v0 + v1) / 2f);
        var across = Mathf.Max(1, Mathf.CeilToInt((b - a) / 0.1f));
        var up = Mathf.Max(1, Mathf.CeilToInt((d - c) / 0.25f));
        for (var i = 0; i <= across; i++)
            for (var j = 0; j <= up; j++)
                if (CoveredAt(side, Mathf.Lerp(a, b, (float)i / across), Mathf.Lerp(c, d, (float)j / up)))
                    return true;
        return false;
    }

    /// <summary>Parts that are not attached to the rest (moved or changed since), by their number in <see cref="Parts"/>.</summary>
    public IEnumerable<int> LooseParts() =>
        Enumerable.Range(0, _parts.Count).Where(i => _parts[i] is { Outline.Length: >= 3 } p && !Attached(p.Outline, p.Floor, i));
}
