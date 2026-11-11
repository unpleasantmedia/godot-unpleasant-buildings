#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A building's lines (its outline at its floor and top, and its corners), its corners as handles,
/// and a handle in the middle of each side that adds a corner there when dragged. Clicking its
/// walls selects it.
/// </summary>
[Tool]
public partial class BuildingGizmos : EditorNode3DGizmoPlugin
{
    private static UnpleasantBuildingsPlugin? _plugin => UnpleasantBuildingsPlugin.Current;

    /// <summary>The ids of the handles in the middle of sides start here: side i's is this plus i.</summary>
    private const int Middles = 100000;

    /// <summary>While a middle handle is dragged: the corner it added, which follows the mouse.</summary>
    private int? _added;

    public BuildingGizmos()
    {
        CreateMaterial("lines", new Color(1f, 0.75f, 0.3f));
        CreateMaterial("next", new Color(1f, 0.75f, 0.3f, 0.5f));
        CreateMaterial("crossed", new Color(1f, 0.25f, 0.2f));
        CreateMaterial("side", new Color(0.3f, 0.8f, 1f));
        CreateMaterial("grid", new Color(1f, 1f, 1f, 0.12f));
        CreateMaterial("guides", new Color(0.4f, 1f, 0.5f));
        CreateMaterial("opening", new Color(0.3f, 0.8f, 1f));
        CreateHandleMaterial("opening_handles");
        CreateHandleMaterial("corners");
        CreateHandleMaterial("middles");
        CreateHandleMaterial("move_handles");
        // Move handles blue, to tell them from the ones that size or shape.
        GetMaterial("move_handles").AlbedoColor = new Color(0.25f, 0.55f, 1f);
        // Rooms' names: facing the view, over everything.
        _label = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 1f, 1f, 0.9f),
            RenderPriority = 10,
        };
        // Middle handles smaller and fainter than corners.
        GetMaterial("middles").AlbedoColor = new Color(1f, 1f, 1f, 0.5f);
    }

    public override string _GetGizmoName() => "Building";

    public override bool _HasGizmo(Node3D forNode3D)
    {
        return forNode3D is Building;
    }

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        if (gizmo.GetNode3D() is not Building building)
            return;
        Shown[building] = [];
        var outline = building.Outline;
        var floor = building.FloorHeight;
        var top = building.TopHeight;
        Vector3 At(Vector2 p, float y) => new(p.X, y, p.Y);

        var lines = new List<Vector3>();
        var closed = outline.Length >= 3 && !(_plugin?.Drawing ?? false);
        var sides = closed ? outline.Length : outline.Length - 1;
        for (var i = 0; i < sides; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            lines.AddRange([At(a, floor), At(b, floor)]);
            if (closed)
                lines.AddRange([At(a, top), At(b, top)]);
        }

        if (closed)
            foreach (var c in outline)
                lines.AddRange([At(c, floor), At(c, top)]);
        // Red while its sides cross (it isn't built then).
        var crossed = outline.Length >= 3 && !Building.Simple(outline);
        if (lines.Count > 0)
            gizmo.AddLines([.. lines], GetMaterial(crossed ? "crossed" : "lines", gizmo));
        _plugin?.ShowProblem(building);
        _plugin?.ShowSideButton(building);
        _plugin?.ShowInteriorTools(building);

        // The side the roof goes by, along the top of the walls.
        if (closed && !crossed && building.RoofHasSide)
        {
            var side = building.UsedRoofSide;
            var lift = new Vector3(0f, 0.05f, 0f);
            gizmo.AddLines([At(outline[side], top) + lift, At(outline[(side + 1) % outline.Length], top) + lift], GetMaterial("side", gizmo));
        }

        // While drawing: from the last corner to the mouse, and on to the first.
        if (_plugin is { Drawing: true, Next: { } next } && outline.Length > 0)
        {
            var preview = new List<Vector3> { At(outline[^1], floor), At(next, floor) };
            if (outline.Length >= 2)
                preview.AddRange([At(next, floor), At(outline[0], floor)]);
            gizmo.AddLines([.. preview], GetMaterial("next", gizmo));
        }

        // Its own handles (corners, side middles, and moving it) not while one of its doors, windows,
        // details, balconies, walls or stairs is being edited or placed.
        var own = !(_plugin?.EditingItem ?? false);
        if (own && outline.Length > 0)
        {
            AddHandles(gizmo, [.. outline.Select(c => At(c, floor))], GetMaterial("corners", gizmo), [.. Enumerable.Range(0, outline.Length)]);
        }

        if (own && closed)
        {
            var corner = new Vector2(outline.Min(c => c.X), outline.Min(c => c.Y));
            AddHandles(gizmo, [At(corner, floor)], GetMaterial("move_handles", gizmo), [MoveHandle]);
            var middles = Enumerable.Range(0, outline.Length).Select(i => At((outline[i] + outline[(i + 1) % outline.Length]) / 2f, floor)).ToArray();
            AddHandles(gizmo, middles, GetMaterial("middles", gizmo), [.. Enumerable.Range(Middles, outline.Length)]);
        }

        if (UnpleasantBuildingsPlugin.Selected(building))
        {
            Grid(gizmo, building, building.CutStorey >= 0 ? building.StoreyFloor(building.CutStorey) : floor);
            // What the corner being drawn or dragged snapped to.
            if (_plugin is { Guides.Count: > 0 } plugin)
            {
                var shown = new List<Vector3>();
                var lift = new Vector3(0f, 0.03f, 0f);
                foreach (var guide in plugin.Guides)
                {
                    var from = building.ToLocal(new Vector3(guide.From.X, 0f, guide.From.Y));
                    var to = building.ToLocal(new Vector3(guide.To.X, 0f, guide.To.Y));
                    shown.AddRange([new Vector3(from.X, floor, from.Z) + lift, new Vector3(to.X, floor, to.Z) + lift]);
                }

                gizmo.AddLines([.. shown], GetMaterial("guides", gizmo));
            }
        }

        Openings(gizmo, building);
        Details(gizmo, building);
        Ledges(gizmo, building);
        Interior(gizmo, building);
        Stairs(gizmo, building);
        RoomOutlines(gizmo, building);
        InnerOpenings(gizmo, building);

        if (building.Mesh is { } mesh)
            gizmo.AddCollisionTriangles(mesh.GenerateTriangleMesh());
    }

    private readonly StandardMaterial3D _label;

    /// <summary>The ids of the selected doorway's or arch's handles start here: its middle, its left and right sides, its top.</summary>
    private const int InnerHandleIds = 800000;

    /// <summary>The doorway or arch being placed, and the selected one, outlined on both faces of their wall; the selected one's handles on its face.</summary>
    private void InnerOpenings(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.CutStorey < 0)
            return;
        void Outline(InteriorWall wall, InteriorOpening o, string material)
        {
            var space = UnpleasantBuildingsPlugin.WallSpace(building, wall);
            var most = building.StoreyHeight - BuildingMesher.FloorThickness - 0.05f;
            var outline = o.Outline(most);
            var lines = new List<Vector3>();
            foreach (var z in new[] { -wall.Thickness / 2f - 0.01f, wall.Thickness / 2f + 0.01f })
                for (var i = 0; i < outline.Length; i++)
                {
                    var (a, b) = (outline[i], outline[(i + 1) % outline.Length]);
                    lines.AddRange([space * new Vector3(a.X, Mathf.Max(a.Y, 0f), z), space * new Vector3(b.X, Mathf.Max(b.Y, 0f), z)]);
                }

            gizmo.AddLines([.. lines], GetMaterial(material, gizmo));
        }

        if (plugin.InnerGhost is { } ghost)
            Outline(ghost.Wall, ghost.Opening, "next");
        if (plugin.SelectedInner is { } s && UnpleasantBuildingsPlugin.WallOf(building, s) is { } wall && wall.Storey == building.CutStorey)
        {
            Outline(wall, s, "opening");
            var space = UnpleasantBuildingsPlugin.WallSpace(building, wall);
            var z = wall.Thickness / 2f + 0.02f;
            var top = Mathf.Min(s.Height, building.StoreyHeight - BuildingMesher.FloorThickness - 0.05f);
            Vector3[] points = [space * new Vector3(s.From + s.Width / 2f, top / 2f, z), space * new Vector3(s.From, top / 2f, z), space * new Vector3(s.From + s.Width, top / 2f, z), space * new Vector3(s.From + s.Width / 2f, top, z)];
            AddHandles(gizmo, points, GetMaterial("opening_handles", gizmo), [InnerHandleIds, InnerHandleIds + 1, InnerHandleIds + 2, InnerHandleIds + 3]);
        }
    }

    /// <summary>The selected doorway's or arch's handle dragged, on its wall's middle plane: its middle slides it, its sides and top size it (to 5 cm).</summary>
    private void DragInner(Building building, int which, Camera3D camera, Vector2 point)
    {
        if (_plugin?.SelectedInner is not { } o || UnpleasantBuildingsPlugin.WallOf(building, o) is not { } wall)
            return;
        var inverse = (building.GlobalTransform * UnpleasantBuildingsPlugin.WallSpace(building, wall)).AffineInverse();
        var (f, d) = (inverse * camera.ProjectRayOrigin(point), inverse.Basis * camera.ProjectRayNormal(point));
        if (Mathf.Abs(d.Z) < 1e-6f)
            return;
        var at = f + d * (-f.Z / d.Z);
        var length = wall.A.DistanceTo(wall.B);
        var step = UnpleasantBuildingsPlugin.OpeningStep;
        switch (which)
        {
            case 0:
                o.From = Mathf.Clamp(Mathf.Snapped(at.X - o.Width / 2f, step), 0f, Mathf.Max(0f, length - o.Width));
                break;
            case 1:
            {
                var right = o.From + o.Width;
                var from = Mathf.Clamp(Mathf.Snapped(at.X, step), 0f, right - 0.4f);
                o.From = from;
                o.Width = right - from;
                break;
            }
            case 2:
                o.Width = Mathf.Clamp(Mathf.Snapped(at.X, step), o.From + 0.4f, length) - o.From;
                break;
            case 3:
                o.Height = Mathf.Clamp(Mathf.Snapped(at.Y, step), 1f, building.StoreyHeight - BuildingMesher.FloorThickness - 0.05f);
                break;
        }
    }

    private static void CommitInner(InteriorOpening o, Godot.Collections.Array was, bool cancel)
    {
        var now = new Godot.Collections.Array { o.From, o.Width, o.Height };
        o.From = was[0].AsSingle();
        o.Width = was[1].AsSingle();
        o.Height = was[2].AsSingle();
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction(o.Kind == InteriorOpeningKind.Arch ? "Move arch" : "Move doorway");
        foreach (var (name, k) in new[] { (InteriorOpening.PropertyName.From, 0), (InteriorOpening.PropertyName.Width, 1), (InteriorOpening.PropertyName.Height, 2) })
        {
            undo.AddDoProperty(o, name, now[k]);
            undo.AddUndoProperty(o, name, was[k]);
        }

        undo.CommitAction();
    }

    /// <summary>The picked storey's rooms: their outlines on the floor (the selected one's as an opening's), and their names over them.</summary>
    private void RoomOutlines(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.CutStorey < 0 || !building.Enterable)
            return;
        var floor = building.StoreyFloor(building.CutStorey) + 0.03f;
        var inner = building.InnerRing();
        foreach (var r in building.Rooms.Where(r => r is not null && r.Storey == building.CutStorey))
        {
            if (building.SpaceAround(r.Storey, r.Seed, inner) is not { } space)
                continue;
            var lines = new List<Vector3>();
            foreach (var ring in space.Holes.Prepend(space.Outer))
                for (var i = 0; i < ring.Length; i++)
                {
                    var (a, b) = (ring[i], ring[(i + 1) % ring.Length]);
                    lines.AddRange([new Vector3(a.X, floor, a.Y), new Vector3(b.X, floor, b.Y)]);
                }

            var selected = plugin.SelectedRoom == r;
            gizmo.AddLines([.. lines], GetMaterial(selected ? "opening" : "next", gizmo));
            if (r.Name.Length > 0)
                gizmo.AddMesh(new TextMesh { Text = r.Name, FontSize = 64, PixelSize = 0.004f, Depth = 0f }, _label, new Transform3D(Basis.Identity, new Vector3(r.Seed.X, floor + 0.4f, r.Seed.Y)));
        }
    }

    /// <summary>The id of the handle that moves the whole building (at its outline's bounds' corner).</summary>
    private const int MoveHandle = 900000;

    /// <summary>The handles each building was last drawn with (id, in its own space): for the plugin to take hold of them before the editor's own gizmo does.</summary>
    public static readonly Dictionary<Building, List<(int Id, Vector3 At)>> Shown = [];

    /// <summary>The ids of handles that move something (rather than size or shape it): drawn blue.</summary>
    private static bool Moves(int id) =>
        id == MoveHandle || id == InnerHandleIds || id == StairHandleIds || id == LedgeHandleIds + 3 || id == DetailHandleIds || id == OpeningHandles + 4;

    /// <summary>Handles added (move handles in blue), and kept for the plugin.</summary>
    private void AddHandles(EditorNode3DGizmo gizmo, Vector3[] points, Material material, int[] ids)
    {
        var building = (Building)gizmo.GetNode3D();
        if (!Shown.TryGetValue(building, out var shown))
            Shown[building] = shown = [];
        var moving = Enumerable.Range(0, ids.Length).Where(i => Moves(ids[i])).ToList();
        var rest = Enumerable.Range(0, ids.Length).Where(i => !Moves(ids[i])).ToList();
        if (rest.Count > 0)
            gizmo.AddHandles([.. rest.Select(i => points[i])], material, [.. rest.Select(i => ids[i])]);
        if (moving.Count > 0)
            gizmo.AddHandles([.. moving.Select(i => points[i])], GetMaterial("move_handles", gizmo), [.. moving.Select(i => ids[i])]);
        for (var i = 0; i < ids.Length; i++)
            shown.Add((ids[i], points[i]));
    }

    /// <summary>The building's move handle dragged: the whole building along its floor's level, the handle under the mouse (on the grid with Shift, otherwise to 5 cm).</summary>
    private static void DragBuilding(Building building, Camera3D camera, Vector2 point)
    {
        if (building.Outline.Length < 3)
            return;
        var corner = new Vector2(building.Outline.Min(c => c.X), building.Outline.Min(c => c.Y));
        var handle = building.ToGlobal(new Vector3(corner.X, building.FloorHeight, corner.Y));
        var level = new Plane(Vector3.Up, handle);
        if (level.IntersectsRay(camera.ProjectRayOrigin(point), camera.ProjectRayNormal(point)) is not { } at)
            return;
        var step = Input.IsKeyPressed(Key.Shift) ? UnpleasantBuildingsPlugin.GridStep : 0.05f;
        at = new Vector3(Mathf.Snapped(at.X, step), at.Y, Mathf.Snapped(at.Z, step));
        building.GlobalPosition += new Vector3(at.X - handle.X, 0f, at.Z - handle.Z);
    }

    /// <summary>The ids of the selected door's or window's handles start here: its left, right, top and bottom edges, then its middle.</summary>
    private const int OpeningHandles = 300000;

    /// <summary>While its middle is dragged: where on the opening it was taken hold of, from its left edge and bottom.</summary>
    private Vector2 _grip;

    /// <summary>The door or window being placed, and the selected one on every storey it is on (with its handles on its own).</summary>
    private void Openings(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.Outline.Length < 3)
            return;
        var lines = new List<Vector3>();
        void Outline(BuildingOpening o, int storey)
        {
            var v0 = building.StoreyFloor(storey) + o.Bottom;
            var lift = building.SideOut(o.Side) * 0.03f;
            var corners = new[] { (o.From, v0), (o.From + o.Width, v0), (o.From + o.Width, v0 + o.Height), (o.From, v0 + o.Height) }
                .Select(c => building.OnSide(o.Side, c.Item1, c.Item2) + lift).ToArray();
            for (var k = 0; k < 4; k++)
                lines.AddRange([corners[k], corners[(k + 1) % 4]]);
        }

        if (plugin.Ghost is { } ghost && ghost.Opening.Side < building.Outline.Length)
            Outline(ghost.Opening, ghost.Storey);
        if (plugin.SelectedOpening is { } selected && selected.Side < building.Outline.Length)
        {
            foreach (var (o, storey) in building.Placed().Where(p => p.Opening == selected))
                Outline(o, storey);
            var lift = building.SideOut(selected.Side) * 0.03f;
            var v0 = building.StoreyFloor(selected.Storey) + selected.Bottom;
            var (u0, u1, v1) = (selected.From, selected.From + selected.Width, v0 + selected.Height);
            var points = new List<Vector3>
            {
                building.OnSide(selected.Side, u0, (v0 + v1) / 2f),
                building.OnSide(selected.Side, u1, (v0 + v1) / 2f),
                building.OnSide(selected.Side, (u0 + u1) / 2f, v1),
                building.OnSide(selected.Side, (u0 + u1) / 2f, v0),
                building.OnSide(selected.Side, (u0 + u1) / 2f, (v0 + v1) / 2f),
            };
            var ids = Enumerable.Range(OpeningHandles, 5).ToList();
            // A door's bottom is on its floor: no handle for it.
            if (selected.Kind == OpeningKind.Door)
            {
                points.RemoveAt(3);
                ids.RemoveAt(3);
            }

            AddHandles(gizmo, [.. points.Select(p => p + lift)], GetMaterial("opening_handles", gizmo), [.. ids]);
        }

        if (lines.Count > 0)
            gizmo.AddLines([.. lines], GetMaterial("opening", gizmo));
    }

    /// <summary>Where the mouse is on the selected opening's wall: along it from its start, and up (in the building's space).</summary>
    private static Vector2? OnWall(Building building, BuildingOpening o, Camera3D camera, Vector2 point)
    {
        var inverse = building.GlobalTransform.AffineInverse();
        var from = inverse * camera.ProjectRayOrigin(point);
        var direction = inverse.Basis * camera.ProjectRayNormal(point);
        var a = building.OnSide(o.Side, 0f, 0f);
        var plane = new Plane(building.SideOut(o.Side), a);
        if (plane.IntersectsRay(from, direction) is not { } hit)
            return null;
        var d = building.OnSide(o.Side, 1f, 0f) - a;
        return new Vector2((hit - a).Dot(d), hit.Y);
    }

    /// <summary>A selected opening's handle dragged: its edges or its middle to the mouse, in 10 cm steps.</summary>
    private void DragOpening(Building building, int which, Camera3D camera, Vector2 point)
    {
        if (_plugin?.SelectedOpening is not { } o || OnWall(building, o, camera, point) is not { } at)
            return;
        var floor = building.StoreyFloor(o.Storey);
        var length = building.SideLength(o.Side);
        var step = UnpleasantBuildingsPlugin.OpeningStep;
        // Kept off the wall's ends, by the margin (rounded in to the steps).
        var (first, _) = UnpleasantBuildingsPlugin.Room(length, 0f);
        var last = Mathf.Floor((length - BuildingOpening.Margin) / step + 0.001f) * step;
        const float min = 0.2f;
        static float S(float x) => UnpleasantBuildingsPlugin.Step(x);
        var (u0, u1) = (o.From, o.From + o.Width);
        var (v0, v1) = (o.Bottom, o.Bottom + o.Height);
        switch (which)
        {
            case 0:
                u0 = Mathf.Clamp(S(at.X), first, u1 - min);
                break;
            case 1:
                u1 = Mathf.Clamp(S(at.X), u0 + min, last);
                break;
            case 2:
                v1 = Mathf.Max(S(at.Y - floor), v0 + min);
                break;
            case 3:
                v0 = Mathf.Clamp(S(at.Y - floor), 0f, v1 - min);
                break;
            default:
            {
                var width = u1 - u0;
                u0 = Mathf.Clamp(S(at.X - _grip.X), first, Mathf.Max(first, last - width));
                u1 = u0 + width;
                if (o.Kind == OpeningKind.Window)
                {
                    var height = v1 - v0;
                    v0 = Mathf.Max(0f, S(at.Y - floor - _grip.Y));
                    v1 = v0 + height;
                }

                break;
            }
        }

        o.From = u0;
        o.Width = u1 - u0;
        o.Height = v1 - v0;
        if (o.Kind == OpeningKind.Window)
            o.Sill = v0;
    }

    /// <summary>The ids of the selected staircase's handles start here: its foot, then its heading.</summary>
    private const int StairHandleIds = 700000;

    /// <summary>The staircase being drawn (its steps' outlines), and the selected one's, with its handles.</summary>
    private void Stairs(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.CutStorey < 0)
            return;
        void Outline(Staircase s, string material)
        {
            var lines = new List<Vector3>();
            var space = UnpleasantBuildingsPlugin.StairSpace(building, s);
            foreach (var c in building.LayOut(s).Cells)
                BoxLines(lines, space, new Aabb(new Vector3(c.X0, 0f, c.Z0), new Vector3(c.X1 - c.X0, c.Top, c.Z1 - c.Z0)));
            gizmo.AddLines([.. lines], GetMaterial(material, gizmo));
        }

        if (plugin.DrawingStairs && plugin.StairGhost is { } ghost)
        {
            Outline(ghost, "next");
            if (plugin.StairFoot is null)
            {
                var floor = building.StoreyFloor(building.CutStorey) + 0.02f;
                var p = ghost.Foot;
                gizmo.AddLines([new(p.X - 0.15f, floor, p.Y), new(p.X + 0.15f, floor, p.Y), new(p.X, floor, p.Y - 0.15f), new(p.X, floor, p.Y + 0.15f)], GetMaterial("next", gizmo));
            }
        }

        if (plugin.SelectedStairs is { } s && s.Storey == building.CutStorey && building.PlacedStairs().Contains(s))
        {
            Outline(s, "opening");
            var floor = building.StoreyFloor(s.Storey) + 0.02f;
            var handles = UnpleasantBuildingsPlugin.StairHandles(building, s);
            gizmo.AddLines([new(handles[0].X, floor, handles[0].Y), new(handles[1].X, floor, handles[1].Y)], GetMaterial("opening", gizmo));
            AddHandles(gizmo, [.. handles.Select(h => new Vector3(h.X, floor, h.Y))], GetMaterial("opening_handles", gizmo), [.. Enumerable.Range(StairHandleIds, handles.Length)]);
        }
    }

    /// <summary>The selected staircase's foot (moved) or heading (turned) dragged.</summary>
    private void DragStairs(Building building, int which, Camera3D camera, Vector2 point)
    {
        if (_plugin?.SelectedStairs is not { } s)
            return;
        var inverse = building.GlobalTransform.AffineInverse();
        var from = inverse * camera.ProjectRayOrigin(point);
        var direction = (inverse.Basis * camera.ProjectRayNormal(point)).Normalized();
        if (new Plane(Vector3.Up, building.StoreyFloor(s.Storey)).IntersectsRay(from, direction) is not { } at)
            return;
        var p = new Vector2(at.X, at.Z);
        if (which == 0)
            s.Foot = UnpleasantBuildingsPlugin.SnapFoot(p, Input.IsKeyPressed(Key.Shift));
        else if (which == 4)
        {
            // Its first flight's end where the mouse is along it: its steps that deep.
            var steps = building.LayOut(s).First;
            s.Tread = Mathf.Snapped((p - s.Foot).Dot(s.Forward) / steps, 0.01f);
        }
        else if (which == 5)
        {
            // Its landing's far edge where the mouse is along it: that many steps before it.
            var layout = building.LayOut(s);
            s.TurnAfter = Mathf.Clamp(Mathf.RoundToInt(((p - s.Foot).Dot(s.Forward) - s.Width) / (layout.Cells[0].Z1 - layout.Cells[0].Z0)), 1, layout.Steps - 2);
        }
        else if (which >= 2)
            // Wider or narrower either side of its middle, to 5 cm.
            s.Width = Mathf.Max(StairPlan.MinWidth, Mathf.Snapped(2f * Mathf.Abs((p - s.Foot).Dot(s.Right)), 0.05f));
        else
            s.Heading = UnpleasantBuildingsPlugin.SnapHeading(building, s.Foot, p);
    }

    private static void CommitStairs(Staircase s, Godot.Collections.Array was, bool cancel)
    {
        var now = new Godot.Collections.Array { s.Foot, s.Heading, s.Width, s.Tread, s.TurnAfter };
        s.Foot = was[0].AsVector2();
        s.Heading = was[1].AsSingle();
        s.Width = was[2].AsSingle();
        s.Tread = was[3].AsSingle();
        s.TurnAfter = was[4].AsInt32();
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction("Move stairs");
        undo.AddDoProperty(s, Staircase.PropertyName.Foot, now[0]);
        undo.AddDoProperty(s, Staircase.PropertyName.Heading, now[1]);
        undo.AddUndoProperty(s, Staircase.PropertyName.Foot, was[0]);
        undo.AddUndoProperty(s, Staircase.PropertyName.Heading, was[1]);
        undo.AddDoProperty(s, Staircase.PropertyName.Width, now[2]);
        undo.AddUndoProperty(s, Staircase.PropertyName.Width, was[2]);
        undo.AddDoProperty(s, Staircase.PropertyName.Tread, now[3]);
        undo.AddUndoProperty(s, Staircase.PropertyName.Tread, was[3]);
        undo.AddDoProperty(s, Staircase.PropertyName.TurnAfter, now[4]);
        undo.AddUndoProperty(s, Staircase.PropertyName.TurnAfter, was[4]);
        undo.CommitAction();
    }

    /// <summary>The ids of the selected interior wall's handles start here: its ends A and B.</summary>
    private const int WallHandleIds = 600000;

    /// <summary>The interior wall being drawn (from its start to the mouse), and the selected one's outline and ends.</summary>
    private void Interior(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.CutStorey < 0)
            return;
        var floor = building.StoreyFloor(building.CutStorey) + 0.02f;
        Vector3 At(Vector2 p) => new(p.X, floor, p.Y);
        var lines = new List<Vector3>();
        if (plugin.DrawingWalls && plugin.WallNext is { } next)
        {
            if (plugin.WallStart is { } start)
                lines.AddRange([At(start), At(next)]);
            // A small cross where the next end would go.
            lines.AddRange([At(next + new Vector2(-0.15f, 0f)), At(next + new Vector2(0.15f, 0f)), At(next + new Vector2(0f, -0.15f)), At(next + new Vector2(0f, 0.15f))]);
        }

        if (lines.Count > 0)
            gizmo.AddLines([.. lines], GetMaterial("next", gizmo));
        if (plugin.SelectedWall is { } w && w.Storey == building.CutStorey && w.A.DistanceTo(w.B) > 0.01f)
        {
            var length = w.A.DistanceTo(w.B);
            var box = new Aabb(new Vector3(-w.Thickness / 2f, 0f, -w.Thickness / 2f), new Vector3(length + w.Thickness, building.StoreyHeight - BuildingMesher.FloorThickness, w.Thickness));
            var outline = new List<Vector3>();
            BoxLines(outline, UnpleasantBuildingsPlugin.WallSpace(building, w), box);
            gizmo.AddLines([.. outline], GetMaterial("opening", gizmo));
            AddHandles(gizmo, [At(w.A), At(w.B)], GetMaterial("opening_handles", gizmo), [WallHandleIds, WallHandleIds + 1]);
        }
    }

    /// <summary>The selected interior wall's end dragged, snapping as when drawn (from its other end).</summary>
    private void DragWall(Building building, int which, Camera3D camera, Vector2 point)
    {
        if (_plugin?.SelectedWall is not { } w)
            return;
        var inverse = building.GlobalTransform.AffineInverse();
        var from = inverse * camera.ProjectRayOrigin(point);
        var direction = (inverse.Basis * camera.ProjectRayNormal(point)).Normalized();
        if (new Plane(Vector3.Up, building.StoreyFloor(w.Storey)).IntersectsRay(from, direction) is not { } at)
            return;
        var shift = Input.IsKeyPressed(Key.Shift);
        var p = UnpleasantBuildingsPlugin.SnapWall(building, w.Storey, new Vector2(at.X, at.Z), which == 0 ? w.B : w.A, shift, w);
        if (which == 0)
            w.A = p;
        else
            w.B = p;
    }

    private static void CommitWall(InteriorWall w, Godot.Collections.Array was, bool cancel)
    {
        var now = new Godot.Collections.Array { w.A, w.B };
        w.A = was[0].AsVector2();
        w.B = was[1].AsVector2();
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction("Move interior wall");
        undo.AddDoProperty(w, InteriorWall.PropertyName.A, now[0]);
        undo.AddDoProperty(w, InteriorWall.PropertyName.B, now[1]);
        undo.AddUndoProperty(w, InteriorWall.PropertyName.A, was[0]);
        undo.AddUndoProperty(w, InteriorWall.PropertyName.B, was[1]);
        undo.CommitAction();
    }

    /// <summary>The ids of the selected balcony's or awning's handles start here: its left and right edges, how far out it reaches, its middle.</summary>
    private const int LedgeHandleIds = 500000;

    /// <summary>A balcony or awning being put on (its box), and the selected one on every storey it is on (with its handles on its own).</summary>
    private void Ledges(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.Outline.Length < 3)
            return;
        var lines = new List<Vector3>();
        if (plugin.ProjectionGhost is { } ghost && ghost.Projection.Side < building.Outline.Length)
            BoxLines(lines, UnpleasantBuildingsPlugin.SideSpace(building, ghost.Projection.Side), UnpleasantBuildingsPlugin.LedgeBox(building, ghost.Projection, ghost.Storey));
        if (plugin.SelectedProjection is { } selected && selected.Side < building.Outline.Length)
        {
            foreach (var (p, storey) in building.PlacedProjections().Where(x => x.Projection == selected))
                BoxLines(lines, UnpleasantBuildingsPlugin.SideSpace(building, p.Side), UnpleasantBuildingsPlugin.LedgeBox(building, p, storey));
            var handles = UnpleasantBuildingsPlugin.LedgeHandles(building, selected);
            AddHandles(gizmo, [.. handles.Select(h => h.At)], GetMaterial("opening_handles", gizmo), [.. handles.Select(h => LedgeHandleIds + h.Id)]);
        }

        if (lines.Count > 0)
            gizmo.AddLines([.. lines], GetMaterial("opening", gizmo));
    }

    /// <summary>The selected balcony's or awning's handle dragged, in 10 cm steps: an edge, how far out it reaches, or its middle (along and up its wall).</summary>
    private void DragLedge(Building building, int which, Camera3D camera, Vector2 point)
    {
        if (_plugin?.SelectedProjection is not { } p)
            return;
        var inverse = building.GlobalTransform.AffineInverse();
        var from = inverse * camera.ProjectRayOrigin(point);
        var direction = (inverse.Basis * camera.ProjectRayNormal(point)).Normalized();
        var space = UnpleasantBuildingsPlugin.SideSpace(building, p.Side);
        var box = UnpleasantBuildingsPlugin.LedgeBox(building, p, p.Storey);
        var length = building.SideLength(p.Side);
        static float S(float x) => UnpleasantBuildingsPlugin.Step(x);
        if (which == 2)
        {
            // How far out: on the level of its middle.
            var level = new Plane(space.Basis.Y.Normalized(), space * box.GetCenter());
            if (level.IntersectsRay(from, direction) is { } at)
                p.Depth = Mathf.Max(0.2f, S((space.AffineInverse() * at).Z));
            return;
        }

        var (u0, u1) = (p.From, p.From + p.Width);
        if (which is 0 or 1)
        {
            // An edge: the point along the side's line (through the box's middle) nearest the mouse's ray,
            // so it drags the same from any angle, not only where the ray meets the wall well.
            var axis = space.Basis.X.Normalized();
            var origin = space * new Vector3(0f, box.GetCenter().Y, box.GetCenter().Z);
            var w = origin - from;
            var b = axis.Dot(direction);
            var denominator = 1f - b * b;
            if (denominator < 1e-4f)
                return;
            var t = (b * w.Dot(direction) - w.Dot(axis)) / denominator;
            var along = t / space.Basis.X.Length();
            if (which == 0)
                u0 = Mathf.Clamp(S(along), 0f, u1 - 0.3f);
            else
                u1 = Mathf.Clamp(S(along), u0 + 0.3f, length);
            p.From = u0;
            p.Width = u1 - u0;
            return;
        }

        var wall = new Plane(space.Basis.Z.Normalized(), space.Origin);
        if (wall.IntersectsRay(from, direction) is not { } on)
            return;
        var local = space.AffineInverse() * on;
        switch (which)
        {
            default:
            {
                var width = u1 - u0;
                u0 = Mathf.Clamp(S(local.X - width / 2f), 0f, Mathf.Max(0f, Mathf.Floor((length - width) / UnpleasantBuildingsPlugin.OpeningStep + 0.001f) * UnpleasantBuildingsPlugin.OpeningStep));
                u1 = u0 + width;
                p.Up = S(local.Y - (box.GetCenter().Y - box.Position.Y) + p.Reach.Below - building.StoreyFloor(p.Storey));
                break;
            }
        }

        p.From = u0;
        p.Width = u1 - u0;
    }

    /// <summary>The selected balcony's or awning's drag let go: as a step to undo (or put back, if cancelled).</summary>
    private void CommitLedge(Godot.Collections.Array was, bool cancel)
    {
        if (_plugin?.SelectedProjection is not { } p)
            return;
        StringName[] names = [BuildingProjection.PropertyName.From, BuildingProjection.PropertyName.Width, BuildingProjection.PropertyName.Depth, BuildingProjection.PropertyName.Up];
        var now = names.Select(n => p.Get(n)).ToArray();
        for (var k = 0; k < names.Length; k++)
            p.Set(names[k], was[k]);
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction("Change balcony or awning");
        for (var k = 0; k < names.Length; k++)
        {
            undo.AddDoProperty(p, names[k], now[k]);
            undo.AddUndoProperty(p, names[k], was[k]);
        }

        undo.CommitAction();
    }

    /// <summary>The ids of the selected wall detail's handles start here: its middle, its width, its height.</summary>
    private const int DetailHandleIds = 400000;

    /// <summary>A box's edges, in the building's space, from a box in a detail's space.</summary>
    private static void BoxLines(List<Vector3> lines, Transform3D t, Aabb box)
    {
        var c = new Vector3[8];
        for (var k = 0; k < 8; k++)
            c[k] = t * new Vector3((k & 1) == 0 ? box.Position.X : box.End.X, (k & 2) == 0 ? box.Position.Y : box.End.Y, (k & 4) == 0 ? box.Position.Z : box.End.Z);
        int[] edges = [0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7];
        lines.AddRange(edges.Select(e => c[e]));
    }

    /// <summary>Where a picked detail would go (a cross at its origin), and the selected one's box and handles.</summary>
    private void Details(EditorNode3DGizmo gizmo, Building building)
    {
        if (_plugin is not { } plugin || !UnpleasantBuildingsPlugin.Selected(building) || building.Outline.Length < 3)
            return;
        var lines = new List<Vector3>();
        // The one being put on: its outline where it would go.
        if (plugin.DetailGhost is { Type: { } type } ghost && building.Fits(ghost))
            BoxLines(lines, building.DetailTransform(ghost), UnpleasantBuildingsPlugin.TypeSize(type));

        if (plugin.SelectedDetail is { } selected && building.Fits(selected))
        {
            BoxLines(lines, building.DetailTransform(selected), building.DetailBounds(selected));
            var handles = UnpleasantBuildingsPlugin.DetailHandles(building, selected);
            AddHandles(gizmo, [.. handles.Select(h => h.At)], GetMaterial("opening_handles", gizmo), [.. handles.Select(h => DetailHandleIds + h.Id)]);
        }

        if (lines.Count > 0)
            gizmo.AddLines([.. lines], GetMaterial("opening", gizmo));
    }

    /// <summary>The selected detail's handle dragged: its middle along and up a wall (onto another, if the mouse is over it), or its width or height, in 10 cm steps.</summary>
    private void DragDetail(Building building, int which, Camera3D camera, Vector2 point)
    {
        if (_plugin?.SelectedDetail is not { } d)
            return;
        var inverse = building.GlobalTransform.AffineInverse();
        var from = inverse * camera.ProjectRayOrigin(point);
        var direction = (inverse.Basis * camera.ProjectRayNormal(point)).Normalized();
        if (which == 0)
        {
            if (building.WallAt(from, direction) is not { } hit)
                return;
            var storey = Mathf.Clamp(Mathf.FloorToInt((hit.V - _grip.Y - building.FloorHeight) / building.StoreyHeight), 0, building.Storeys - 1);
            d.Side = hit.Side;
            d.Storey = storey;
            d.Along = UnpleasantBuildingsPlugin.Step(hit.U - _grip.X * building.DetailAcross(d));
            // One locked to its height keeps it (on whichever storey it is moved to).
            d.Up = d.Type?.HeightLocked == true ? d.Type.Up : UnpleasantBuildingsPlugin.Step(hit.V - _grip.Y - building.StoreyFloor(storey));
            return;
        }

        // Width and height: on its wall's level, out from its origin.
        var t = building.DetailTransform(d);
        var plane = new Plane(t.Basis.Z.Normalized(), t.Origin);
        if (plane.IntersectsRay(from, direction) is not { } at)
            return;
        var local = t.AffineInverse() * at;
        if (which == 1)
            d.Width = Mathf.Max(0.1f, UnpleasantBuildingsPlugin.Step(local.X));
        else
            d.Height = Mathf.Max(0.1f, UnpleasantBuildingsPlugin.Step(local.Y));
    }

    /// <summary>The selected detail's drag let go: as a step to undo (or put back, if cancelled).</summary>
    private void CommitDetail(Godot.Collections.Array was, bool cancel)
    {
        if (_plugin?.SelectedDetail is not { } d)
            return;
        StringName[] names = [WallDetail.PropertyName.Side, WallDetail.PropertyName.Storey, WallDetail.PropertyName.Along, WallDetail.PropertyName.Up, WallDetail.PropertyName.Width, WallDetail.PropertyName.Height];
        var now = names.Select(n => d.Get(n)).ToArray();
        for (var k = 0; k < names.Length; k++)
            d.Set(names[k], was[k]);
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction("Change wall detail");
        for (var k = 0; k < names.Length; k++)
        {
            undo.AddDoProperty(d, names[k], now[k]);
            undo.AddUndoProperty(d, names[k], was[k]);
        }

        undo.CommitAction();
    }

    /// <summary>A selected opening's drag let go: as a step to undo (or put back, if cancelled).</summary>
    private void CommitOpening(Godot.Collections.Array was, bool cancel)
    {
        if (_plugin?.SelectedOpening is not { } o)
            return;
        StringName[] names = [BuildingOpening.PropertyName.From, BuildingOpening.PropertyName.Width, BuildingOpening.PropertyName.Height, BuildingOpening.PropertyName.Sill];
        var now = names.Select(n => o.Get(n)).ToArray();
        for (var k = 0; k < names.Length; k++)
            o.Set(names[k], was[k]);
        if (cancel)
            return;
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction("Change opening");
        for (var k = 0; k < names.Length; k++)
        {
            undo.AddDoProperty(o, names[k], now[k]);
            undo.AddUndoProperty(o, names[k], was[k]);
        }

        undo.CommitAction();
    }

    /// <summary>How far the grid reaches past the outline (and the corner being drawn), in metres.</summary>
    private const float GridMargin = 6f;

    /// <summary>The most lines the grid has each way (a huge building gets a grid round only part of it).</summary>
    private const int GridMost = 200;

    /// <summary>The grid at the building's floor, in its own space, round its outline: what corners snap to with Shift.</summary>
    private void Grid(EditorNode3DGizmo gizmo, Building building, float height)
    {
        var step = UnpleasantBuildingsPlugin.GridStep;
        var points = building.Outline.ToList();
        points.Add(Vector2.Zero);
        if (_plugin is { Drawing: true, Next: { } next })
            points.Add(next);
        var low = points.Aggregate((a, b) => a.Min(b)) - new Vector2(GridMargin, GridMargin);
        var high = points.Aggregate((a, b) => a.Max(b)) + new Vector2(GridMargin, GridMargin);
        low = (low / step).Floor() * step;
        high = (high / step).Ceil() * step;
        high = high.Min(low + new Vector2(GridMost * step, GridMost * step));
        var lines = new List<Vector3>();
        for (var x = low.X; x <= high.X + 0.001f; x += step)
            lines.AddRange([new Vector3(x, height, low.Y), new Vector3(x, height, high.Y)]);
        for (var z = low.Y; z <= high.Y + 0.001f; z += step)
            lines.AddRange([new Vector3(low.X, height, z), new Vector3(high.X, height, z)]);
        gizmo.AddLines([.. lines], GetMaterial("grid", gizmo));
    }

    public override string _GetHandleName(EditorNode3DGizmo gizmo, int handleId, bool secondary) => handleId switch
    {
        MoveHandle => "Move building",
        >= InnerHandleIds => new[] { "Opening", "Left side", "Right side", "Top" }[handleId - InnerHandleIds],
        >= StairHandleIds => new[] { "Stairs' foot", "Stairs' heading", "Stairs' width", "Stairs' width", "Stairs' length", "Stairs' turn" }[handleId - StairHandleIds],
        >= WallHandleIds => handleId == WallHandleIds ? "Wall start" : "Wall end",
        >= LedgeHandleIds => new[] { "Left edge", "Right edge", "Out", "Balcony or awning" }[handleId - LedgeHandleIds],
        >= DetailHandleIds => new[] { "Detail", "Width", "Height" }[handleId - DetailHandleIds],
        >= OpeningHandles => new[] { "Left edge", "Right edge", "Top edge", "Bottom edge", "Opening" }[handleId - OpeningHandles],
        >= Middles => "New corner",
        _ => $"Corner {handleId + 1}",
    };

    public override Variant _GetHandleValue(EditorNode3DGizmo gizmo, int handleId, bool secondary)
    {
        if (handleId == MoveHandle)
            return ((Building)gizmo.GetNode3D()).GlobalPosition;

        if (handleId >= InnerHandleIds && _plugin?.SelectedInner is { } inner)
            return new Godot.Collections.Array { inner.From, inner.Width, inner.Height };

        if (handleId >= StairHandleIds && _plugin?.SelectedStairs is { } stairs)
            return new Godot.Collections.Array { stairs.Foot, stairs.Heading, stairs.Width, stairs.Tread, stairs.TurnAfter };

        if (handleId >= WallHandleIds && _plugin?.SelectedWall is { } wall)
            return new Godot.Collections.Array { wall.A, wall.B };

        if (handleId >= LedgeHandleIds && _plugin?.SelectedProjection is { } ledge)
            return new Godot.Collections.Array { ledge.From, ledge.Width, ledge.Depth, ledge.Up };

        if (handleId >= DetailHandleIds && _plugin?.SelectedDetail is { } d)
        {
            // Taken hold of where it is now, from its origin: along its X and up.
            var building = (Building)gizmo.GetNode3D();
            var middle = building.DetailBounds(d).GetCenter();
            _grip = new Vector2(middle.X, middle.Y);
            return new Godot.Collections.Array { d.Side, d.Storey, d.Along, d.Up, d.Width, d.Height };
        }

        if (handleId >= OpeningHandles && _plugin?.SelectedOpening is { } o)
        {
            // Taken hold of by its middle.
            _grip = new Vector2(o.Width / 2f, o.Height / 2f);
            return new Godot.Collections.Array { o.From, o.Width, o.Height, o.Sill };
        }

        return ((Building)gizmo.GetNode3D()).Outline;
    }

    public override void _SetHandle(EditorNode3DGizmo gizmo, int handleId, bool secondary, Camera3D camera, Vector2 point)
    {
        var building = (Building)gizmo.GetNode3D();
        if (handleId == MoveHandle)
        {
            DragBuilding(building, camera, point);
            return;
        }

        if (handleId >= InnerHandleIds)
        {
            DragInner(building, handleId - InnerHandleIds, camera, point);
            return;
        }

        if (handleId >= StairHandleIds)
        {
            DragStairs(building, handleId - StairHandleIds, camera, point);
            return;
        }

        if (handleId >= WallHandleIds)
        {
            DragWall(building, handleId - WallHandleIds, camera, point);
            return;
        }

        if (handleId >= LedgeHandleIds)
        {
            DragLedge(building, handleId - LedgeHandleIds, camera, point);
            return;
        }

        if (handleId >= DetailHandleIds)
        {
            DragDetail(building, handleId - DetailHandleIds, camera, point);
            return;
        }

        if (handleId >= OpeningHandles)
        {
            DragOpening(building, handleId - OpeningHandles, camera, point);
            return;
        }

        // On the level of its floor, where the handles are, so the corner stays under the mouse
        // (on the ground below, it ended up further along the mouse's ray).
        var floor = new Plane(building.GlobalBasis.Y.Normalized(), building.ToGlobal(new Vector3(0f, building.FloorHeight, 0f)));
        if (floor.IntersectsRay(camera.ProjectRayOrigin(point), camera.ProjectRayNormal(point)) is not { } world)
            return;
        var outline = building.Outline.ToList();
        int index;
        if (handleId >= Middles)
        {
            if (_added is null)
            {
                _added = handleId - Middles + 1;
                // In first where the mouse is, so snapping knows the corners either side of it.
                outline.Insert(_added.Value, UnpleasantBuildingsPlugin.Flat(building.ToLocal(world)));
                building.Outline = [.. outline];
            }

            index = _added.Value;
        }
        else
        {
            index = handleId;
        }

        if (index >= outline.Count)
            return;
        // Joined to the corners either side of it.
        var n = outline.Count;
        var at = _plugin?.Place(building, world, Input.IsKeyPressed(Key.Shift), [((index - 1 + n) % n, (index - 2 + n) % n), ((index + 1) % n, (index + 2) % n)], camera, point)
            ?? UnpleasantBuildingsPlugin.Snapped(UnpleasantBuildingsPlugin.Flat(building.ToLocal(world)), Input.IsKeyPressed(Key.Shift));
        outline[index] = at;
        building.Outline = [.. outline];
    }

    public override void _CommitHandle(EditorNode3DGizmo gizmo, int handleId, bool secondary, Variant restore, bool cancel)
    {
        var building = (Building)gizmo.GetNode3D();
        if (handleId == MoveHandle)
        {
            var now = building.GlobalPosition;
            building.GlobalPosition = restore.AsVector3();
            if (cancel)
                return;
            var undo = EditorInterface.Singleton.GetEditorUndoRedo();
            undo.CreateAction("Move building", UndoRedo.MergeMode.Disable, building);
            undo.AddDoProperty(building, Node3D.PropertyName.GlobalPosition, now);
            undo.AddUndoProperty(building, Node3D.PropertyName.GlobalPosition, restore);
            undo.CommitAction();
            return;
        }

        if (handleId >= InnerHandleIds && handleId != MoveHandle)
        {
            if (_plugin?.SelectedInner is { } movedInner)
                CommitInner(movedInner, restore.AsGodotArray(), cancel);
            return;
        }

        if (handleId >= StairHandleIds)
        {
            if (_plugin?.SelectedStairs is { } moved2)
                CommitStairs(moved2, restore.AsGodotArray(), cancel);
            return;
        }

        if (handleId >= WallHandleIds)
        {
            if (_plugin?.SelectedWall is { } moved)
                CommitWall(moved, restore.AsGodotArray(), cancel);
            return;
        }

        if (handleId >= LedgeHandleIds)
        {
            CommitLedge(restore.AsGodotArray(), cancel);
            return;
        }

        if (handleId >= DetailHandleIds)
        {
            CommitDetail(restore.AsGodotArray(), cancel);
            return;
        }

        if (handleId >= OpeningHandles)
        {
            CommitOpening(restore.AsGodotArray(), cancel);
            return;
        }

        var was = restore.AsVector2Array();
        var added = _added;
        _added = null;
        _plugin?.Guides.Clear();
        if (cancel)
        {
            building.Outline = was;
            return;
        }

        if (handleId >= Middles && added is { } k && k > 0 && k < building.Outline.Length)
        {
            // A side split: what was past the new corner goes onto the new side after it.
            var split = building.Outline[k - 1].DistanceTo(building.Outline[k]);
            _plugin?.SetOutline("Add building corner", building.Outline, was, building, UnpleasantBuildingsPlugin.Inserted(k, split));
            return;
        }

        _plugin?.SetOutline("Move building corner", building.Outline, was, building);
    }
}
#endif
