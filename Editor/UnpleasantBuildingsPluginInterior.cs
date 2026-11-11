#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// The inside of an enterable building: a storey picked in the toolbar cuts away everything above
/// it (in the editor only); Walls then draws interior walls on it, click by click (each click a
/// wall's end and the next one's start; right-click or Escape ends a run), snapping to the outer
/// walls' inner faces, other walls and their ends, square to the building's sides, and (with Shift)
/// to the grid. Clicking a wall selects it (its ends dragged as handles, its thickness in the
/// Inspector); Ctrl-clicking one, or Delete with it selected, takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private Label? _storeyLabel;
    private OptionButton? _storeyPick;
    private Button? _wallsButton;

    /// <summary>Within how far a wall's end snaps to a wall, an end or a corner, in metres.</summary>
    private const float WallReach = 0.3f;

    /// <summary>Within how many degrees of square to the building's sides a wall drawn is squared.</summary>
    private const float SquareWithin = 6f;

    /// <summary>Whether interior walls are being drawn.</summary>
    public bool DrawingWalls => _wallsButton?.ButtonPressed == true;

    /// <summary>Where the wall being drawn starts, if one is.</summary>
    public Vector2? WallStart { get; private set; }

    /// <summary>Where the next wall end would go now, while drawing walls.</summary>
    public Vector2? WallNext { get; private set; }

    /// <summary>The selected interior wall, if any.</summary>
    public InteriorWall? SelectedWall { get; private set; }

    private void AddInteriorTools(Color accent)
    {
        _storeyLabel = new Label { Text = "Storey", Visible = false };
        _toolbar!.AddChild(_storeyLabel);
        _storeyPick = new OptionButton { FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "The storey to work on: everything above it, and the roof, is left out in the editor." };
        _storeyPick.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.StoreyPicked));
        _toolbar.AddChild(_storeyPick);
        _wallsButton = new Button
        {
            Text = "Walls",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            Visible = false,
            TooltipText = "Click on the storey's floor to draw interior walls: each click ends one and starts the next. Right-click or Escape ends a run. Shift snaps to the grid.",
        };
        _wallsButton.AddThemeColorOverride("font_pressed_color", accent);
        _wallsButton.AddThemeColorOverride("font_hover_pressed_color", accent);
        _wallsButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.WallsToggled));
        _toolbar.AddChild(_wallsButton);
    }

    /// <summary>The storey picker and Walls shown for enterable buildings, the picker's storeys as the building's.</summary>
    public void ShowInteriorTools(Building building)
    {
        if (_storeyPick is null || building != _building)
            return;
        var show = building.Enterable && building.Outline.Length >= 3;
        _storeyLabel!.Visible = _storeyPick.Visible = _wallsButton!.Visible = show;
        ShowStairTools(building, show);
        ShowRoomTools(building, show);
        ShowInnerOpeningTools(building, show);
        if (!show)
        {
            if (building.CutStorey >= 0)
                building.CutStorey = -1;
            StopWalls();
            return;
        }

        if (_storeyPick.ItemCount != building.Storeys + 1)
        {
            _storeyPick.Clear();
            _storeyPick.AddItem("All", 0);
            for (var s = 0; s < building.Storeys; s++)
                _storeyPick.AddItem(s == 0 ? "Ground" : $"{s}", s + 1);
        }

        _storeyPick.Select(building.CutStorey + 1);
        _wallsButton.Disabled = building.CutStorey < 0;
    }

    private void StoreyPicked(long index)
    {
        if (_building is null)
            return;
        _building.CutStorey = (int)index - 1;
        WallStart = null;
        if (_building.CutStorey < 0)
            StopWalls();
        if (SelectedWall is { } w && w.Storey != _building.CutStorey)
            SelectWall(null);
        if (SelectedStairs is { } s && s.Storey != _building.CutStorey)
            SelectStairs(null);
        if (SelectedRoom is { } r && r.Storey != _building.CutStorey)
            SelectRoom(null);
        if (SelectedInner is { } io && WallOf(_building, io)?.Storey != _building.CutStorey)
            SelectInner(null);
        StairFoot = null;
        ShowInteriorTools(_building);
    }

    private void WallsToggled(bool on)
    {
        WallStart = WallNext = null;
        if (on)
        {
            SetDrawing(false);
            StopPlacing();
            StopPlacingDetail();
            StopPlacingProjection();
            StopStairs();
            StopRooms();
            StopInner();
            _side?.SetPressedNoSignal(false);
        }

        _building?.UpdateGizmos();
    }

    private void StopWalls()
    {
        _wallsButton?.SetPressedNoSignal(false);
        WallStart = WallNext = null;
    }

    /// <summary>The building let go of (or another picked): shown whole again, nothing of its inside being worked on.</summary>
    private void LeaveInterior()
    {
        StopWalls();
        SelectWall(null);
        StopStairs();
        SelectStairs(null);
        StopRooms();
        SelectRoom(null);
        StopInner();
        SelectInner(null);
        if (_building is not null && IsInstanceValid(_building))
            _building.CutStorey = -1;
    }

    public void SelectWall(InteriorWall? wall)
    {
        SelectedWall = wall;
        if (wall is not null)
        {
            Select(null);
            SelectDetail(null);
            SelectProjection(null);
            SelectedStairs = null;
            SelectedRoom = null;
            SelectedInner = null;
        }

        QueueInspect();
        _building?.UpdateGizmos();
    }

    /// <summary>Where the mouse is on the picked storey's floor, in the building's own space (on the ground).</summary>
    private static Vector2? OnFloor(Building building, Camera3D camera, Vector2 screen)
    {
        var (from, direction) = LocalRay(building, camera, screen);
        var floor = new Plane(Vector3.Up, building.StoreyFloor(Mathf.Max(0, building.CutStorey)));
        return floor.IntersectsRay(from, direction) is { } at ? new Vector2(at.X, at.Z) : null;
    }

    /// <summary>
    /// Where a wall's end goes for a place on the floor: onto another wall's end or an inner corner;
    /// square to the building's sides from where it starts; onto the outer walls' inner faces or
    /// another wall; and with Shift, onto the grid.
    /// </summary>
    public static Vector2 SnapWall(Building building, int storey, Vector2 p, Vector2? from, bool grid, InteriorWall? moving = null)
    {
        var inner = RoofShape.Reached(BuildingMesher.Outward(building.Outline), -BuildingMesher.WallThickness);
        var others = building.Walls.Where(w => w is not null && w.Storey == storey && w != moving).ToList();
        foreach (var point in others.SelectMany(w => new[] { w.A, w.B }).Concat(inner))
            if (point.DistanceTo(p) < WallReach)
                return point;

        // Square to the sides (along one, or across it) from its start.
        Vector2? axis = null;
        if (from is { } start && p.DistanceTo(start) > 0.05f)
        {
            var direction = (p - start).Normalized();
            var best = Mathf.DegToRad(SquareWithin);
            for (var i = 0; i < building.Outline.Length; i++)
            {
                var side = (building.Outline[(i + 1) % building.Outline.Length] - building.Outline[i]).Normalized();
                foreach (var candidate in new[] { side, side.Orthogonal() })
                {
                    var angle = Mathf.Abs(Mathf.Asin(Mathf.Clamp(direction.Cross(candidate), -1f, 1f)));
                    if (angle < best)
                    {
                        best = angle;
                        axis = candidate.Dot(direction) < 0f ? -candidate : candidate;
                    }
                }
            }

            if (axis is { } a)
                p = start + a * (p - start).Dot(a);
        }

        // Onto a wall's line: the inner faces, or another interior wall (where the squared line meets it, if squared).
        var lines = new List<(Vector2, Vector2)>();
        for (var i = 0; i < inner.Length; i++)
            lines.Add((inner[i], inner[(i + 1) % inner.Length]));
        lines.AddRange(others.Select(w => (w.A, w.B)));
        Vector2? snapped = null;
        var nearest = WallReach;
        foreach (var (a, b) in lines)
        {
            Vector2? on;
            if (axis is { } along && from is { } start2)
                on = Geometry2D.LineIntersectsLine(start2, along, a, b - a).VariantType == Variant.Type.Nil ? null : Geometry2D.LineIntersectsLine(start2, along, a, b - a).AsVector2();
            else
                on = Geometry2D.GetClosestPointToSegment(p, a, b);
            if (on is not { } q || q.DistanceTo(p) >= nearest || Geometry2D.GetClosestPointToSegment(q, a, b).DistanceTo(q) > 0.01f)
                continue;
            nearest = q.DistanceTo(p);
            snapped = q;
        }

        if (snapped is { } s)
            return s;
        if (grid)
            return (p / GridStep).Round() * GridStep;
        return p.Snapped(new Vector2(0.05f, 0.05f));
    }

    /// <summary>The interior wall of the picked storey under a place in the view, if any (the nearest).</summary>
    private static InteriorWall? WallAtScreen(Building building, Camera3D camera, Vector2 screen)
    {
        if (building.CutStorey < 0)
            return null;
        var (from, direction) = LocalRay(building, camera, screen);
        InteriorWall? best = null;
        var nearest = float.PositiveInfinity;
        foreach (var w in building.Walls.Where(w => w is not null && w.Storey == building.CutStorey))
        {
            var length = w.A.DistanceTo(w.B);
            if (length < 0.01f)
                continue;
            var space = WallSpace(building, w);
            var inverse = space.AffineInverse();
            var box = new Aabb(new Vector3(-w.Thickness / 2f, 0f, -w.Thickness / 2f), new Vector3(length + w.Thickness, building.StoreyHeight - BuildingMesher.FloorThickness, w.Thickness)).Grow(0.03f);
            if (Hit(box, inverse * from, inverse.Basis * direction) is { } hit && (space * hit - from).Length() < nearest)
            {
                nearest = (space * hit - from).Length();
                best = w;
            }
        }

        return best;
    }

    /// <summary>A wall's own space, in the building's: X along it from A, Y up from its floor, Z across it.</summary>
    public static Transform3D WallSpace(Building building, InteriorWall w)
    {
        var d = (w.B - w.A).Normalized();
        return new Transform3D(new Basis(new Vector3(d.X, 0f, d.Y), Vector3.Up, new Vector3(-d.Y, 0f, d.X)), new Vector3(w.A.X, building.StoreyFloor(w.Storey), w.A.Y));
    }

    /// <summary>The view's clicks and moves for the inside; whether it used them.</summary>
    private bool InteriorInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        if (building.CutStorey < 0 || !building.Enterable)
            return false;
        var storey = building.CutStorey;
        if (DrawingWalls)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    WallNext = OnFloor(building, camera, motion.Position) is { } at ? SnapWall(building, storey, at, WallStart, motion.ShiftPressed) : null;
                    building.UpdateGizmos();
                    return false;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                    if (OnFloor(building, camera, click.Position) is not { } point)
                        return true;
                    var end = SnapWall(building, storey, point, WallStart, click.ShiftPressed);
                    if (WallStart is { } start && start.DistanceTo(end) > 0.05f)
                    {
                        var wall = new InteriorWall { Storey = storey, A = start, B = end };
                        var undo = GetUndoRedo();
                        undo.CreateAction("Add interior wall", customContext: building);
                        undo.AddDoProperty(building, Building.PropertyName.Walls, new Godot.Collections.Array<InteriorWall>(building.Walls.Append(wall).ToArray()));
                        undo.AddUndoProperty(building, Building.PropertyName.Walls, building.Walls);
                        undo.CommitAction();
                    }

                    WallStart = end;
                    building.UpdateGizmos();
                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    if (WallStart is not null)
                        WallStart = null;
                    else
                        StopWalls();
                    building.UpdateGizmos();
                    return true;
            }

            return false;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            if (!press.CtrlPressed && NearWallHandle(building, camera, press.Position))
                return false;
            if (WallAtScreen(building, camera, press.Position) is { } hit)
            {
                if (press.CtrlPressed)
                    RemoveWall(building, hit);
                else
                    SelectWall(hit);
                return true;
            }

            if (SelectedWall is not null)
                SelectWall(null);
        }

        return false;
    }

    /// <summary>Takes an interior wall away, as a step to undo.</summary>
    private void RemoveWall(Building building, InteriorWall wall)
    {
        var undo = GetUndoRedo();
        undo.CreateAction("Remove interior wall", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Walls, new Godot.Collections.Array<InteriorWall>(building.Walls.Where(w => w != wall).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Walls, building.Walls);
        undo.CommitAction();
        if (SelectedWall == wall)
            SelectWall(null);
    }

    private bool NearWallHandle(Building building, Camera3D camera, Vector2 screen) =>
        SelectedWall is { } w && new[] { w.A, w.B }.Any(p =>
        {
            var world = building.ToGlobal(new Vector3(p.X, building.StoreyFloor(w.Storey), p.Y));
            return !camera.IsPositionBehind(world) && camera.UnprojectPosition(world).DistanceTo(screen) < ClickReach;
        });
}
#endif
