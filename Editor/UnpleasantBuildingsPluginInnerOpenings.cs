#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Doorways and arches in interior walls, a storey picked: with Doorway or Arch on, one follows the
/// mouse along the walls and a click puts it there. Clicking one selects it (its middle dragged to
/// slide it, its sides and top to size it; the rest in the Inspector); Ctrl-clicking one, or Delete
/// with it selected, takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private Button? _doorwayButton;
    private Button? _archButton;

    /// <summary>The kind being placed, if one is.</summary>
    public InteriorOpeningKind? PlacingInner => _doorwayButton?.ButtonPressed == true ? InteriorOpeningKind.Doorway : _archButton?.ButtonPressed == true ? InteriorOpeningKind.Arch : null;

    /// <summary>Where the one being placed would go now (its wall, and it), if anywhere.</summary>
    public (InteriorWall Wall, InteriorOpening Opening)? InnerGhost { get; private set; }

    /// <summary>The selected doorway or arch, if any.</summary>
    public InteriorOpening? SelectedInner { get; private set; }

    private void AddInnerOpeningTools(Color accent)
    {
        Button Make(string text, string tip)
        {
            var button = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "FlatButton", Visible = false, TooltipText = tip };
            button.AddThemeColorOverride("font_pressed_color", accent);
            button.AddThemeColorOverride("font_hover_pressed_color", accent);
            _toolbar!.AddChild(button);
            return button;
        }

        _doorwayButton = Make("Doorway", "Click an interior wall of the storey to put a doorway in it. Right-click or Escape stops.");
        _archButton = Make("Arch", "Click an interior wall of the storey to put an arch in it. Right-click or Escape stops.");
        _doorwayButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.DoorwayToggled));
        _archButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.ArchToggled));
    }

    private void ShowInnerOpeningTools(Building building, bool show)
    {
        if (_doorwayButton is null)
            return;
        _doorwayButton.Visible = _archButton!.Visible = show;
        _doorwayButton.Disabled = _archButton.Disabled = building.CutStorey < 0;
        if (!show || building.CutStorey < 0)
            StopInner();
    }

    private void DoorwayToggled(bool on) => InnerToggled(on, _archButton!);

    private void ArchToggled(bool on) => InnerToggled(on, _doorwayButton!);

    private void InnerToggled(bool on, Button other)
    {
        InnerGhost = null;
        if (on)
        {
            other.SetPressedNoSignal(false);
            SetDrawing(false);
            StopPlacing();
            StopPlacingDetail();
            StopPlacingProjection();
            StopWalls();
            StopStairs();
            StopRooms();
            _side?.SetPressedNoSignal(false);
        }

        _building?.UpdateGizmos();
    }

    private void StopInner()
    {
        _doorwayButton?.SetPressedNoSignal(false);
        _archButton?.SetPressedNoSignal(false);
        InnerGhost = null;
    }

    public void SelectInner(InteriorOpening? opening)
    {
        SelectedInner = opening;
        if (opening is not null)
        {
            Select(null);
            SelectDetail(null);
            SelectProjection(null);
            SelectedWall = null;
            SelectedStairs = null;
            SelectedRoom = null;
        }

        QueueInspect();
        _building?.UpdateGizmos();
    }

    /// <summary>The wall a doorway or arch is in.</summary>
    public static InteriorWall? WallOf(Building building, InteriorOpening opening) =>
        building.Walls.FirstOrDefault(w => w is not null && w.Openings.Contains(opening));

    /// <summary>The interior wall of the picked storey under a place in the view, and where on it (along from its start, up from its floor), if any.</summary>
    private static (InteriorWall Wall, float U, float V)? OnInnerWall(Building building, Camera3D camera, Vector2 screen)
    {
        if (WallAtScreen(building, camera, screen) is not { } wall)
            return null;
        var (from, direction) = LocalRay(building, camera, screen);
        var space = WallSpace(building, wall);
        var inverse = space.AffineInverse();
        var (f, d) = (inverse * from, inverse.Basis * direction);
        // Where the ray meets the wall's middle plane.
        if (Mathf.Abs(d.Z) < 1e-6f)
            return null;
        var t = -f.Z / d.Z;
        var at = f + d * t;
        return (wall, at.X, at.Y);
    }

    /// <summary>A new one of a kind centred on a place along a wall (to 5 cm), kept inside it; none where the wall is too short.</summary>
    private static InteriorOpening? InnerAt(InteriorWall wall, InteriorOpeningKind kind, float u)
    {
        var opening = InteriorOpening.Make(kind);
        var length = wall.A.DistanceTo(wall.B);
        if (opening.Width > length - 0.1f)
            return null;
        opening.From = Mathf.Clamp(Mathf.Snapped(u - opening.Width / 2f, OpeningStep), 0.05f, length - opening.Width - 0.05f);
        return opening;
    }

    /// <summary>The doorway or arch under a place in the view, if any.</summary>
    private static InteriorOpening? InnerUnder(Building building, Camera3D camera, Vector2 screen)
    {
        if (OnInnerWall(building, camera, screen) is not { } at)
            return null;
        return at.Wall.Openings.FirstOrDefault(o => o is not null && at.U >= o.From && at.U <= o.From + o.Width && at.V <= o.Height + 0.02f);
    }

    /// <summary>The view's clicks and moves for doorways and arches; whether it used them.</summary>
    private bool InnerOpeningInput(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        if (PlacingInner is { } kind)
        {
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    InnerGhost = OnInnerWall(building, camera, motion.Position) is { } at && InnerAt(at.Wall, kind, at.U) is { } ghost ? (at.Wall, ghost) : null;
                    building.UpdateGizmos();
                    return false;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                    if (OnInnerWall(building, camera, click.Position) is { } hit && InnerAt(hit.Wall, kind, hit.U) is { } made)
                    {
                        var undo = GetUndoRedo();
                        undo.CreateAction(kind == InteriorOpeningKind.Arch ? "Add arch" : "Add doorway", customContext: building);
                        undo.AddDoProperty(hit.Wall, InteriorWall.PropertyName.Openings, new Godot.Collections.Array<InteriorOpening>(hit.Wall.Openings.Append(made).ToArray()));
                        undo.AddUndoProperty(hit.Wall, InteriorWall.PropertyName.Openings, hit.Wall.Openings);
                        undo.CommitAction();
                        StopInner();
                        SelectInner(made);
                    }

                    return true;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                    StopInner();
                    building.UpdateGizmos();
                    return true;
            }

            return false;
        }

        if (DrawingWalls || DrawingStairs || DrawingRooms || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
            return false;
        if (InnerUnder(building, camera, press.Position) is { } under)
        {
            if (press.CtrlPressed)
                RemoveInner(building, under);
            else
                SelectInner(under);
            return true;
        }

        if (SelectedInner is not null)
            SelectInner(null);
        return false;
    }

    /// <summary>Takes a doorway or arch away, as a step to undo.</summary>
    private void RemoveInner(Building building, InteriorOpening opening)
    {
        if (WallOf(building, opening) is not { } wall)
            return;
        var undo = GetUndoRedo();
        undo.CreateAction(opening.Kind == InteriorOpeningKind.Arch ? "Remove arch" : "Remove doorway", customContext: building);
        undo.AddDoProperty(wall, InteriorWall.PropertyName.Openings, new Godot.Collections.Array<InteriorOpening>(wall.Openings.Where(o => o != opening).ToArray()));
        undo.AddUndoProperty(wall, InteriorWall.PropertyName.Openings, wall.Openings);
        undo.CommitAction();
        if (SelectedInner == opening)
            SelectInner(null);
    }
}
#endif
