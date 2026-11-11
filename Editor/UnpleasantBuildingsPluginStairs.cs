#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Stairs, a storey picked: drawn as a line, clicking its foot and then the way it climbs (square
/// to the building's sides when near, Shift snapping its foot to the grid); the shape picked beside
/// the button. Clicking one selects it (its foot and its heading dragged as handles, the rest in the
/// Inspector); Ctrl-clicking one, or Delete with it selected, takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private Button? _stairsButton;
    private OptionButton? _stairShape;

    /// <summary>Whether stairs are being drawn.</summary>
    public bool DrawingStairs => _stairsButton?.ButtonPressed == true;

    /// <summary>Where the staircase being drawn has its foot, once clicked.</summary>
    public Vector2? StairFoot { get; private set; }

    /// <summary>The staircase being drawn as it would be now (or, before its foot is clicked, where the mouse is), if any.</summary>
    public Staircase? StairGhost { get; private set; }

    /// <summary>The selected staircase, if any.</summary>
    public Staircase? SelectedStairs { get; private set; }

    /// <summary>How far from the foot the heading handle sits.</summary>
    public const float HeadingReach = 1f;

    private void AddStairTools(Color accent)
    {
        _stairsButton = new Button
        {
            Text = "Stairs",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            Visible = false,
            TooltipText = "Click the foot of a staircase on the storey's floor, then click the way it climbs. Shift snaps its foot to the grid. Right-click or Escape stops.",
        };
        _stairsButton.AddThemeColorOverride("font_pressed_color", accent);
        _stairsButton.AddThemeColorOverride("font_hover_pressed_color", accent);
        _stairsButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.StairsToggled));
        _toolbar!.AddChild(_stairsButton);
        _stairShape = new OptionButton { FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "The shape of stairs drawn: one straight flight, or turning at a landing (L a quarter, U back)." };
        _stairShape.AddItem("Straight", (int)StairShape.Straight);
        _stairShape.AddItem("L", (int)StairShape.L);
        _stairShape.AddItem("U", (int)StairShape.U);
        _toolbar.AddChild(_stairShape);
    }

    /// <summary>Stairs and its shape shown with the storey picker; only for a storey with one above it.</summary>
    private void ShowStairTools(Building building, bool show)
    {
        if (_stairsButton is null)
            return;
        _stairsButton.Visible = _stairShape!.Visible = show;
        _stairsButton.Disabled = building.CutStorey < 0 || building.CutStorey >= building.Storeys - 1;
        if (!show || _stairsButton.Disabled)
            StopStairs();
    }

    private void StairsToggled(bool on)
    {
        StairFoot = null;
        StairGhost = null;
        if (on)
        {
            SetDrawing(false);
            StopPlacing();
            StopPlacingDetail();
            StopPlacingProjection();
            StopWalls();
            StopRooms();
            StopInner();
            _side?.SetPressedNoSignal(false);
        }

        _building?.UpdateGizmos();
    }

    private void StopStairs()
    {
        _stairsButton?.SetPressedNoSignal(false);
        StairFoot = null;
        StairGhost = null;
    }

    public void SelectStairs(Staircase? stairs)
    {
        SelectedStairs = stairs;
        if (stairs is not null)
        {
            Select(null);
            SelectDetail(null);
            SelectProjection(null);
            SelectedWall = null;
            SelectedRoom = null;
            SelectedInner = null;
        }

        QueueInspect();
        _building?.UpdateGizmos();
    }

    /// <summary>Whether one of the building's own (door, window, detail, balcony, wall or stairs) is selected or being placed: its corners and move handle hidden then.</summary>
    public bool EditingItem => SelectedItem is not null || SelectedWall is not null || SelectedStairs is not null || SelectedRoom is not null || DrawingRooms || SelectedInner is not null || PlacingInner is not null
        || Placing is not null || PlacingDetail is not null || PlacingProjection is not null || DrawingWalls || DrawingStairs;

    /// <summary>The handle being dragged (taken hold of here, before the editor's own move gizmo could), and what it was.</summary>
    private int? _dragHandle;
    private Variant _dragWas;

    /// <summary>
    /// The building's handles taken hold of first: a click on one starts dragging it here (the
    /// editor's own gizmo, if under it too, never getting the click); moves drag it; letting go
    /// makes it a step to undo; right-click or Escape puts it back.
    /// </summary>
    private bool HandleInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        if (_gizmos is null || building.GetGizmos().OfType<EditorNode3DGizmo>().FirstOrDefault(g => g.GetPlugin() == _gizmos) is not { } gizmo)
            return false;
        // Let go of where this never heard it (off the view, or the editor took it): the drag ended
        // there, so it is finished now, and this click or move goes on as any other.
        if (_dragHandle is { } stuck && (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            || @event is InputEventMouseMotion && !Input.IsMouseButtonPressed(MouseButton.Left)))
        {
            GD.Print($"[Unpleasant Buildings] Drag of handle {stuck} never let go of here: finished now.");
            _dragHandle = null;
            _gizmos._CommitHandle(gizmo, stuck, false, _dragWas, false);
        }

        if (_dragHandle is { } id)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    _gizmos._SetHandle(gizmo, id, false, camera, motion.Position);
                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                    _dragHandle = null;
                    _gizmos._CommitHandle(gizmo, id, false, _dragWas, false);
                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    _dragHandle = null;
                    _gizmos._CommitHandle(gizmo, id, false, _dragWas, true);
                    return true;
            }

            return false;
        }

        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, CtrlPressed: false } press
            || !BuildingGizmos.Shown.TryGetValue(building, out var shown))
            return false;
        int? nearest = null;
        var best = ClickReach;
        foreach (var (handle, at) in shown)
        {
            var world = building.ToGlobal(at);
            if (camera.IsPositionBehind(world))
                continue;
            var distance = camera.UnprojectPosition(world).DistanceTo(press.Position);
            if (distance < best)
            {
                best = distance;
                nearest = handle;
            }
        }

        if (nearest is not { } picked)
            return false;
        _dragHandle = picked;
        _dragWas = _gizmos._GetHandleValue(gizmo, picked, false);
        return true;
    }

    /// <summary>A foot's place on the floor: on the grid with Shift, otherwise to 5 cm.</summary>
    public static Vector2 SnapFoot(Vector2 p, bool grid) => grid ? (p / GridStep).Round() * GridStep : p.Snapped(new Vector2(0.05f, 0.05f));

    /// <summary>The heading (in degrees) from a foot towards a place: square to the building's sides when within a few degrees, otherwise to 5°.</summary>
    public static float SnapHeading(Building building, Vector2 foot, Vector2 toward)
    {
        var d = toward - foot;
        if (d.Length() < 0.01f)
            return 0f;
        d = d.Normalized();
        var best = Mathf.DegToRad(SquareWithin);
        Vector2? axis = null;
        for (var i = 0; i < building.Outline.Length; i++)
        {
            var side = (building.Outline[(i + 1) % building.Outline.Length] - building.Outline[i]).Normalized();
            foreach (var candidate in new[] { side, -side, side.Orthogonal(), -side.Orthogonal() })
            {
                var angle = Mathf.Abs(d.AngleTo(candidate));
                if (angle < best)
                {
                    best = angle;
                    axis = candidate;
                }
            }
        }

        var heading = Mathf.RadToDeg(Mathf.Atan2((axis ?? d).X, (axis ?? d).Y));
        return axis is null ? Mathf.Snapped(heading, 5f) : heading;
    }

    /// <summary>A staircase's own space, in the building's: X to its right, Y up from its floor, Z the way it climbs, from its foot.</summary>
    public static Transform3D StairSpace(Building building, Staircase s)
    {
        var r = s.Right;
        var f = s.Forward;
        return new Transform3D(new Basis(new Vector3(r.X, 0f, r.Y), Vector3.Up, new Vector3(f.X, 0f, f.Y)), new Vector3(s.Foot.X, building.StoreyFloor(s.Storey), s.Foot.Y));
    }

    /// <summary>The staircase of the picked storey under a place in the view, if any (the nearest).</summary>
    private static Staircase? StairsAtScreen(Building building, Camera3D camera, Vector2 screen)
    {
        if (building.CutStorey < 0)
            return null;
        var (from, direction) = LocalRay(building, camera, screen);
        Staircase? best = null;
        var nearest = float.PositiveInfinity;
        foreach (var s in building.PlacedStairs().Where(s => s.Storey == building.CutStorey))
        {
            var space = StairSpace(building, s);
            var inverse = space.AffineInverse();
            foreach (var c in building.LayOut(s).Cells)
            {
                var box = new Aabb(new Vector3(c.X0, 0f, c.Z0), new Vector3(c.X1 - c.X0, c.Top, c.Z1 - c.Z0)).Grow(0.02f);
                if (Hit(box, inverse * from, inverse.Basis * direction) is { } hit && (space * hit - from).Length() < nearest)
                {
                    nearest = (space * hit - from).Length();
                    best = s;
                }
            }
        }

        return best;
    }

    /// <summary>The view's clicks and moves for stairs; whether it used them.</summary>
    private bool StairsInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        var storey = building.CutStorey;
        if (DrawingStairs)
        {
            Staircase? Ghost(Vector2 screen, bool shift)
            {
                if (OnFloor(building, camera, screen) is not { } at)
                    return null;
                var foot = StairFoot ?? SnapFoot(at, shift);
                var ghost = new Staircase
                {
                    Storey = storey,
                    Shape = (StairShape)(_stairShape?.GetSelectedId() ?? 0),
                    Foot = foot,
                    Heading = StairFoot is null ? StairGhost?.Heading ?? 0f : SnapHeading(building, foot, at),
                };
                return ghost;
            }

            switch (@event)
            {
                case InputEventMouseMotion motion:
                    StairGhost = Ghost(motion.Position, motion.ShiftPressed);
                    building.UpdateGizmos();
                    return false;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                    if (Ghost(click.Position, click.ShiftPressed) is not { } placed)
                        return true;
                    if (StairFoot is null)
                    {
                        StairFoot = placed.Foot;
                        StairGhost = placed;
                        building.UpdateGizmos();
                        return true;
                    }

                    var undo = GetUndoRedo();
                    undo.CreateAction("Add stairs", customContext: building);
                    undo.AddDoProperty(building, Building.PropertyName.Stairs, new Godot.Collections.Array<Staircase>(building.Stairs.Append(placed).ToArray()));
                    undo.AddUndoProperty(building, Building.PropertyName.Stairs, building.Stairs);
                    undo.CommitAction();
                    StopStairs();
                    SelectStairs(placed);
                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    if (StairFoot is not null)
                        StairFoot = null;
                    else
                        StopStairs();
                    building.UpdateGizmos();
                    return true;
            }

            return false;
        }

        if (!DrawingWalls && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            if (!press.CtrlPressed && NearStairHandle(building, camera, press.Position))
                return false;
            if (StairsAtScreen(building, camera, press.Position) is { } hit)
            {
                if (press.CtrlPressed)
                    RemoveStairs(building, hit);
                else
                    SelectStairs(hit);
                return true;
            }

            if (SelectedStairs is not null)
                SelectStairs(null);
        }

        return false;
    }

    /// <summary>Takes a staircase away, as a step to undo.</summary>
    private void RemoveStairs(Building building, Staircase stairs)
    {
        var undo = GetUndoRedo();
        undo.CreateAction("Remove stairs", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Stairs, new Godot.Collections.Array<Staircase>(building.Stairs.Where(s => s != stairs).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Stairs, building.Stairs);
        undo.CommitAction();
        if (SelectedStairs == stairs)
            SelectStairs(null);
    }

    /// <summary>The selected staircase's handles, on its floor: its foot, a metre on the way it climbs, and its first step's sides (for its width).</summary>
    /// Then the end of its first flight (its steps' depth) and, for an L or U, its landing's far edge (where it turns).
    public static Vector2[] StairHandles(Building building, Staircase s)
    {
        var layout = building.LayOut(s);
        var end = layout.First * (layout.Cells[0].Z1 - layout.Cells[0].Z0);
        Vector2[] handles = [s.Foot, s.Foot + s.Forward * HeadingReach, s.At(s.Width / 2f, 0f), s.At(-s.Width / 2f, 0f), s.At(0f, end)];
        return s.Shape == StairShape.Straight ? handles : [.. handles, s.At(0f, end + s.Width)];
    }

    private bool NearStairHandle(Building building, Camera3D camera, Vector2 screen) =>
        SelectedStairs is { } s && StairHandles(building, s).Any(p =>
        {
            var world = building.ToGlobal(new Vector3(p.X, building.StoreyFloor(s.Storey), p.Y));
            return !camera.IsPositionBehind(world) && camera.UnprojectPosition(world).DistanceTo(screen) < ClickReach;
        });
}
#endif
