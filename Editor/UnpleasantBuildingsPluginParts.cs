#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Drawing a building's parts beside its main one (see <see cref="BuildingWing"/>): the floor to draw
/// on, picked in the toolbar, and Draw Part, which draws a new part's outline on that floor. Above the
/// ground floor, a part has to be attached to another (touching one on its floor, or resting on one
/// on the floor below): one that isn't, once finished, is taken away again with a word why.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private SpinBox? _partFloor;
    private Button? _drawPart;

    /// <summary>Whether a part is being drawn.</summary>
    public bool DrawingPart => _drawPart?.ButtonPressed == true;

    /// <summary>The part being drawn (its number in the building's Parts), once its first corner is down.</summary>
    public int? DrawnPart { get; private set; }

    /// <summary>The floor new parts are drawn on.</summary>
    public int PartFloor => (int)(_partFloor?.Value ?? 0);

    /// <summary>How near another part's corner or side a part's corner is pulled onto it, in metres.</summary>
    private const float PartSnapReach = 0.4f;

    private void AddPartTools(Color accent)
    {
        _toolbar!.AddChild(new VSeparator());
        _toolbar.AddChild(new Label { Text = "Floor" });
        _partFloor = new SpinBox
        {
            MinValue = 0, MaxValue = 40, Step = 1, Value = 0,
            TooltipText = "The floor new parts are drawn on, from 0 (the ground floor). Above it, a part has to touch a part on its floor, or rest on one on the floor below.",
        };
        _partFloor.Connect(Godot.Range.SignalName.ValueChanged, new Callable(this, MethodName.PartFloorChanged));
        _toolbar.AddChild(_partFloor);
        _drawPart = new Button
        {
            Text = "Draw Part",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            TooltipText = "Click corners on the floor picked to draw a new part of the building. Click its first corner, or press Enter, to finish.",
        };
        _drawPart.AddThemeColorOverride("font_pressed_color", accent);
        _drawPart.AddThemeColorOverride("font_hover_pressed_color", accent);
        _drawPart.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.DrawPartToggled));
        _toolbar.AddChild(_drawPart);
    }

    private void PartFloorChanged(double value) => _building?.UpdateGizmos();

    private void DrawPartToggled(bool on)
    {
        if (on)
        {
            SetDrawing(false);
            StopPlacing();
            StopPlacingDetail();
            StopPlacingProjection();
            Select(null);
            SelectDetail(null);
            SelectProjection(null);
            Next = null;
            DrawnPart = null;
        }
        else
        {
            FinishPart();
        }

        _building?.UpdateGizmos();
    }

    /// <summary>Stops drawing a part (finishing it), if one is being drawn.</summary>
    private void StopDrawingPart()
    {
        if (_drawPart is { ButtonPressed: true })
            _drawPart.ButtonPressed = false;
    }

    /// <summary>A part drawn to the end: kept if it has three corners and is attached; taken away (a step to undo) if not.</summary>
    private void FinishPart()
    {
        Guides.Clear();
        Next = null;
        if (DrawnPart is not { } index || _building is not { } building || !IsInstanceValid(building) || index >= building.Parts.Count)
        {
            DrawnPart = null;
            return;
        }

        DrawnPart = null;
        var part = building.Parts[index];
        string? why = part.Outline.Length < 3 ? "it needs three corners at least"
            : !Building.Simple(part.Outline) ? "its sides cross"
            : !building.Attached(part.Outline, part.Floor, index) ? $"on floor {part.Floor} it has to touch a part on that floor, or rest on one on the floor below"
            : null;
        if (why is null)
            return;
        GD.PushWarning($"Draw Part: the part wasn't kept: {why}.");
        var parts = building.Parts.Duplicate();
        parts.RemoveAt(index);
        var undo = GetUndoRedo();
        undo.CreateAction("Remove unattached part", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Parts, parts);
        undo.AddUndoProperty(building, Building.PropertyName.Parts, building.Parts);
        // Its walls gone (with what is on them); those after them numbered down by as many.
        var first = building.FirstSide(index);
        var count = part.Outline.Length;
        Renumber(undo, building, (side, _) => side < first ? (side, 0f) : side < first + count ? null : (side - count, 0f));
        undo.CommitAction();
    }

    /// <summary>Drawing a part: each click a corner on the floor picked; its first corner again, Enter, Escape or a right-click to finish.</summary>
    private int DrawPart(Camera3D camera, InputEvent @event)
    {
        var building = _building!;
        var floor = PartFloor;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                Next = PartPoint(building, camera, motion.Position, floor) ?? Next;
                building.UpdateGizmos();
                return (int)AfterGuiInput.Pass;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                // On its first corner: done.
                if (DrawnPart is { } drawn && drawn < building.Parts.Count && building.Parts[drawn].Outline.Length >= 3
                    && NearestPartCorner(camera, click.Position) is { Part: var p, Corner: 0 } && p == drawn)
                {
                    StopDrawingPart();
                    return (int)AfterGuiInput.Stop;
                }

                if (PartPoint(building, camera, click.Position, floor) is not { } point)
                    return (int)AfterGuiInput.Stop;
                var undo = GetUndoRedo();
                if (DrawnPart is { } index && index < building.Parts.Count)
                {
                    var part = building.Parts[index];
                    undo.CreateAction("Add part corner", customContext: building);
                    undo.AddDoProperty(part, BuildingWing.PropertyName.Outline, (Vector2[])[.. part.Outline, point]);
                    undo.AddUndoProperty(part, BuildingWing.PropertyName.Outline, part.Outline);
                    undo.CommitAction();
                }
                else
                {
                    // Its first corner: the part made, on the floor picked.
                    var part = new BuildingWing { Floor = floor, Outline = [point] };
                    var parts = building.Parts.Duplicate();
                    parts.Add(part);
                    undo.CreateAction("Draw part", customContext: building);
                    undo.AddDoProperty(building, Building.PropertyName.Parts, parts);
                    undo.AddUndoProperty(building, Building.PropertyName.Parts, building.Parts);
                    undo.AddDoReference(part);
                    undo.CommitAction();
                    DrawnPart = parts.Count - 1;
                }

                Next = point;
                return (int)AfterGuiInput.Stop;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
            case InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Escape }:
                StopDrawingPart();
                return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    /// <summary>Where a part's corner drawn at a place in the view goes, in the building's own space: on the level of the floor it is on.</summary>
    private Vector2? PartPoint(Building building, Camera3D camera, Vector2 screen, int floor)
    {
        var level = new Plane(building.GlobalBasis.Y.Normalized(), building.ToGlobal(new Vector3(0f, building.StoreyFloor(floor), 0f)));
        if (level.IntersectsRay(camera.ProjectRayOrigin(screen), camera.ProjectRayNormal(screen)) is not { } world)
            return null;
        return PlacePartCorner(building, world, DrawnPart ?? -2, Input.IsKeyPressed(Key.Shift), camera, screen);
    }

    /// <summary>
    /// Where a part's corner goes, from where the mouse puts it: onto a corner of the building's other
    /// parts (its main one too) within reach, else onto one of their sides, so parts meet; else as any
    /// corner goes (see <see cref="Place"/>).
    /// </summary>
    public Vector2 PlacePartCorner(Building building, Vector3 world, int part, bool shift, Camera3D? camera, Vector2 screen)
    {
        var at = Place(building, world, shift, [], camera, screen);
        if (shift)
            return at;
        Vector2? best = null;
        var reach = PartSnapReach;
        foreach (var (index, outline, _, _) in building.AllParts())
        {
            if (index == part)
                continue;
            foreach (var c in outline)
            {
                if (c.DistanceTo(at) < reach)
                    (best, reach) = (c, c.DistanceTo(at));
            }
        }

        if (best is { } corner)
            return corner;
        reach = PartSnapReach;
        foreach (var (index, outline, _, _) in building.AllParts())
        {
            if (index == part)
                continue;
            for (var i = 0; i < outline.Length; i++)
            {
                var on = Geometry2D.GetClosestPointToSegment(at, outline[i], outline[(i + 1) % outline.Length]);
                if (on.DistanceTo(at) < reach)
                    (best, reach) = (on, on.DistanceTo(at));
            }
        }

        return best ?? at;
    }

    /// <summary>A part's corner nearest a place in the view, if one is within reach.</summary>
    private (int Part, int Corner)? NearestPartCorner(Camera3D camera, Vector2 screen)
    {
        var building = _building!;
        (int, int)? best = null;
        var reach = ClickReach;
        for (var p = 0; p < building.Parts.Count; p++)
        {
            if (building.Parts[p] is not { } part)
                continue;
            var floor = building.StoreyFloor(part.Floor);
            for (var i = 0; i < part.Outline.Length; i++)
            {
                var world = building.ToGlobal(new Vector3(part.Outline[i].X, floor, part.Outline[i].Y));
                if (camera.IsPositionBehind(world))
                    continue;
                var d = camera.UnprojectPosition(world).DistanceTo(screen);
                if (d < reach)
                {
                    reach = d;
                    best = (p, i);
                }
            }
        }

        return best;
    }

    /// <summary>Ctrl-click on a part's corner: the corner taken away (a part keeps three at least).</summary>
    private bool RemovePartCorner(Camera3D camera, InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, CtrlPressed: true } click
            || NearestPartCorner(camera, click.Position) is not { } hit)
            return false;
        var part = _building!.Parts[hit.Part];
        if (part.Outline.Length <= 3)
            return false;
        var outline = part.Outline.ToList();
        outline.RemoveAt(hit.Corner);
        var undo = GetUndoRedo();
        undo.CreateAction("Remove part corner", customContext: _building);
        undo.AddDoProperty(part, BuildingWing.PropertyName.Outline, (Vector2[])[.. outline]);
        undo.AddUndoProperty(part, BuildingWing.PropertyName.Outline, part.Outline);
        // The side starting at it goes (with what is on it); the walls after it are numbered one down.
        Renumber(undo, _building, Removed(_building.FirstSide(hit.Part) + hit.Corner));
        undo.CommitAction();
        return true;
    }
}
#endif
