using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>An interior wall as built: on the ground from A to B, so thick, from a floor up to a ceiling.</summary>
/// <param name="Lid">Where its top face is: under its top when it reaches into the floor above (its sides meeting the wall above with no gap; its top just under that floor, not on it).</param>
/// <param name="Openings">Its doorways and arches, in it.</param>
public readonly record struct InnerWall(Vector2 A, Vector2 B, float Thickness, float Floor, float Ceiling, float? Lid = null, InteriorOpening[]? Openings = null);

/// <summary>The materials of a building's inside.</summary>
public readonly record struct InteriorMaterials(Material? Walls, Material? Floors, Material? Ceilings, Material? Frames = null, Material? Doors = null, Material? Glass = null);

public static partial class BuildingMesher
{
    /// <summary>How thick the floors between storeys are, in metres (from the floor above down to the ceiling below).</summary>
    public const float FloorThickness = 0.2f;

    /// <summary>
    /// An enterable building's inside: a floor on every storey and a ceiling under every floor above
    /// (and under the top, unless it is cut open there), and its interior walls.
    /// </summary>
    /// <param name="floors">Each shown storey's floor height.</param>
    /// <param name="top">Where the top ceiling goes; none if null (cut open, in the editor).</param>
    public static void Interior(ArrayMesh mesh, Vector2[] outline, float[] floors, float? top, List<InnerWall> walls, InteriorMaterials materials, List<Vector2[]>[]? holes = null, RoomSet? rooms = null, List<DoorLeaf>? hung = null)
    {
        var inner = RoofShape.Reached(Outward(outline), -WallThickness);
        var floorFaces = new Faces();
        var ceilingFaces = new Faces();
        var wallFaces = new Faces();
        var roomFloors = new Dictionary<int, Faces>();
        var roomCeilings = new Dictionary<int, Faces>();
        Faces Of(Dictionary<int, Faces> all, int room) => all.TryGetValue(room, out var f) ? f : all[room] = new Faces();
        // A storey's floor or ceiling: each room's part with its own faces, the rest with the fallback's.
        void Level(Faces fallback, Dictionary<int, Faces> perRoom, int storey, List<Vector2[]> cut, float height, bool up)
        {
            var mine = rooms?.Shapes.Where(s => s.Storey == storey).ToList() ?? [];
            CapHoled(fallback, inner, [.. cut, .. mine.Select(s => s.Outer)], height, up);
            foreach (var s in mine)
                CapHoled(Of(perRoom, s.Index), s.Outer, [.. cut, .. s.Holes], height, up);
            // A free-standing wall's place in a room: the fallback's (under the wall).
            foreach (var s in mine)
                foreach (var h in s.Holes)
                    CapHoled(fallback, h, cut, height, up);
        }

        for (var k = 0; k < floors.Length; k++)
        {
            var cut = holes?[k] ?? [];
            Level(floorFaces, roomFloors, k, cut, floors[k], up: true);
            if (k > 0)
                Level(ceilingFaces, roomCeilings, k - 1, cut, floors[k] - FloorThickness, up: false);
            // The holes' edges, through the floor (down to the ceiling under it).
            foreach (var hole in cut)
                HoleEdges(floorFaces, inner, hole, k > 0 ? floors[k] - FloorThickness : floors[k] - 0.01f, floors[k]);
        }

        if (top is { } t)
            Level(ceilingFaces, roomCeilings, floors.Length - 1, [], t, up: false);

        var routed = rooms is null ? null : new Routed(wallFaces, rooms);
        var frameFaces = new Faces();
        var (leafFaces, leafGlass) = (new Faces(), new Faces());
        var leafMaterials = new LeafMaterials(materials.Doors, materials.Glass, materials.Frames);
        foreach (var w in walls)
            InnerWallMesh(wallFaces, routed, frameFaces, w, rooms, hung, leafFaces, leafGlass, leafMaterials);
        frameFaces.AddTo(mesh, materials.Frames);
        leafFaces.AddTo(mesh, materials.Doors);
        leafGlass.AddTo(mesh, materials.Glass);

        floorFaces.AddTo(mesh, materials.Floors);
        ceilingFaces.AddTo(mesh, materials.Ceilings);
        wallFaces.AddTo(mesh, materials.Walls);
        foreach (var (room, faces) in roomFloors)
            faces.AddTo(mesh, rooms!.Floors[room] ?? materials.Floors);
        foreach (var (room, faces) in roomCeilings)
            faces.AddTo(mesh, rooms!.Ceilings[room] ?? materials.Ceilings);
        routed?.AddTo(mesh, rooms!.Walls, materials.Walls);
    }

    /// <summary>
    /// A hole's edges through a floor, facing into it: but not where it runs along an outer wall's
    /// inner face (there the wall's face is already, and the two would flicker against each other).
    /// </summary>
    private static void HoleEdges(Faces faces, Vector2[] inner, Vector2[] hole, float from, float to)
    {
        bool OnWall(Vector2 a, Vector2 b)
        {
            for (var i = 0; i < inner.Length; i++)
            {
                var (p, q) = (inner[i], inner[(i + 1) % inner.Length]);
                if (Geometry2D.GetClosestPointToSegment(a, p, q).DistanceTo(a) < 0.01f && Geometry2D.GetClosestPointToSegment(b, p, q).DistanceTo(b) < 0.01f)
                    return true;
            }

            return false;
        }

        var ring = Outward(hole);
        var along = 0f;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            var length = a.DistanceTo(b);
            if (!OnWall(a, b))
                faces.Quad(new Vector3(a.X, from, a.Y), new Vector3(b.X, from, b.Y), new Vector3(b.X, to, b.Y), new Vector3(a.X, to, a.Y), -Out(a, b),
                    new Vector2(along, -from), new Vector2(along + length, -from), new Vector2(along + length, -to), new Vector2(along, -to));
            along += length;
        }
    }

}

public partial class Building
{
    private Godot.Collections.Array<InteriorWall> _walls = [];
    private Material? _interiorWallMaterial;
    private Material? _floorMaterial;
    private Material? _ceilingMaterial;
    private int _cut = -1;

    /// <summary>Its interior walls, on every storey: drawn in the 3D view (Walls, with a storey picked). Only built when it is enterable.</summary>
    [ExportGroup("Interior")]
    [Export]
    public Godot.Collections.Array<InteriorWall> Walls
    {
        get => _walls;
        set
        {
            foreach (var w in _walls.Where(w => w is not null))
                if (w.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    w.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            _walls = value ?? [];
            foreach (var w in _walls.Where(w => w is not null))
                if (!w.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild)))
                    w.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.QueueBuild));
            QueueBuild();
            UpdateGizmos();
        }
    }

    /// <summary>What interior walls (and the outer walls' inner faces) are made of; with none, its walls'.</summary>
    [Export]
    public Material? InteriorWallMaterial
    {
        get => _interiorWallMaterial;
        set
        {
            _interiorWallMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What its floors are made of; with none, its interior walls'.</summary>
    [Export]
    public Material? FloorMaterial
    {
        get => _floorMaterial;
        set
        {
            _floorMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>What its ceilings are made of; with none, its interior walls'.</summary>
    [Export]
    public Material? CeilingMaterial
    {
        get => _ceilingMaterial;
        set
        {
            _ceilingMaterial = value;
            QueueBuild();
        }
    }

    /// <summary>
    /// In the editor: the storey being worked on, everything above it (and the roof) left out to see
    /// into it; -1 for all of it. Not saved, and never in the game.
    /// </summary>
    public int CutStorey
    {
        get => Engine.IsEditorHint() ? _cut : -1;
        set
        {
            var cut = value < 0 ? -1 : Mathf.Min(value, _storeys - 1);
            if (cut == _cut)
                return;
            _cut = cut;
            QueueBuild();
            UpdateGizmos();
            HideAbove();
        }
    }

    /// <summary>How many storeys are built: all, or up to the one cut at.</summary>
    private int ShownStoreys => CutStorey >= 0 ? Mathf.Min(_storeys, CutStorey + 1) : _storeys;

    /// <summary>Where a storey's ceiling is: under the floor above, or under the top.</summary>
    public float CeilingOf(int storey) => storey < _storeys - 1 ? StoreyFloor(storey + 1) - BuildingMesher.FloorThickness : TopHeight - CeilingGap;

    private const float CeilingGap = 0.1f;

    /// <summary>Where a storey's interior walls reach: up through the floor above (so none shows a gap where a stair's hole is beside it), or the top ceiling.</summary>
    private float WallTop(int storey) => storey < _storeys - 1 ? StoreyFloor(storey + 1) : CeilingOf(storey);

    /// <summary>A storey's interior walls' top face: 5 mm under the floor above (on it, the two would flicker), or at their top.</summary>
    private float? WallLid(int storey) => storey < _storeys - 1 ? StoreyFloor(storey + 1) - 0.005f : null;

    /// <summary>Its inside, onto what it built: only when it is enterable.</summary>
    private void BuildInterior(ArrayMesh mesh, RoomSet? rooms, List<DoorLeaf>? hung, bool open)
    {
        if (!_enterable)
            return;
        // Cut open at the storey being worked on (in the editor), or whole (as baked).
        var cut = open ? CutStorey : -1;
        var shown = cut >= 0 ? Mathf.Min(_storeys, cut + 1) : _storeys;
        var floors = Enumerable.Range(0, shown).Select(StoreyFloor).ToArray();
        var walls = _walls.Where(w => w is not null && w.Storey < shown)
            .Select(w => new InnerWall(w.A, w.B, w.Thickness, StoreyFloor(w.Storey), cut == w.Storey ? StoreyFloor(w.Storey) + _storeyHeight - BuildingMesher.FloorThickness : WallTop(w.Storey), cut == w.Storey ? null : WallLid(w.Storey),
                [.. w.Openings.Where(o => o is not null)]))
            .ToList();
        var wallMaterial = _interiorWallMaterial ?? _wallMaterial;
        var inner = RoofShape.Reached(BuildingMesher.Outward(_outline), -BuildingMesher.WallThickness);
        var holes = Enumerable.Range(0, shown).Select(k => k > 0 ? StairHoles(k, inner) : []).ToArray();
        BuildingMesher.Interior(mesh, _outline, floors, cut >= 0 ? null : TopHeight - CeilingGap, walls,
            new InteriorMaterials(wallMaterial, _floorMaterial ?? wallMaterial, _ceilingMaterial ?? wallMaterial, _frameMaterial ?? GD.Load<Material>($"{Materials}/Frame.tres"),
                _doorMaterial ?? GD.Load<Material>($"{Materials}/Door.tres"), _glassMaterial ?? GD.Load<Material>($"{Materials}/Glass.tres")), holes, rooms, hung);
        var stairs = PlacedStairs().Where(s => s.Storey < shown)
            .Select(s => new BuildingMesher.PlacedStairs(s, LayOut(s), StoreyFloor(s.Storey), s.Storey + 1 < shown ? StoreyFloor(s.Storey + 1) : null,
                (a, b) => AgainstWall(s.Storey, a, b, inner), (a, b) => AgainstWall(s.Storey + 1, a, b, inner)))
            .ToList();
        var stairMaterial = _stairMaterial ?? _floorMaterial ?? wallMaterial;
        BuildingMesher.AllStairs(mesh, stairs, new StairMaterials(stairMaterial, _stairRailingMaterial ?? stairMaterial));
    }
}
