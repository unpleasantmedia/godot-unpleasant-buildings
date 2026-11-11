#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Rooms, a storey picked: with Rooms on, clicking the floor where there is no room makes one of the
/// space round it (between the walls), and clicking in a room selects it (its name and materials in
/// the Inspector); Ctrl-clicking one, or Delete with it selected, takes it away.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private Button? _roomsButton;

    /// <summary>Whether rooms are being made and picked.</summary>
    public bool DrawingRooms => _roomsButton?.ButtonPressed == true;

    /// <summary>The selected room, if any.</summary>
    public Room? SelectedRoom { get; private set; }

    private void AddRoomTools(Color accent)
    {
        _roomsButton = new Button
        {
            Text = "Rooms",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            ThemeTypeVariation = "FlatButton",
            Visible = false,
            TooltipText = "Click the floor between walls to make a room of that space, or click a room to select it (its name and materials in the Inspector). Ctrl-click a room to take it away.",
        };
        _roomsButton.AddThemeColorOverride("font_pressed_color", accent);
        _roomsButton.AddThemeColorOverride("font_hover_pressed_color", accent);
        _roomsButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.RoomsToggled));
        _toolbar!.AddChild(_roomsButton);
    }

    private void ShowRoomTools(Building building, bool show)
    {
        if (_roomsButton is null)
            return;
        _roomsButton.Visible = show;
        _roomsButton.Disabled = building.CutStorey < 0;
        if (!show || _roomsButton.Disabled)
            StopRooms();
    }

    private void RoomsToggled(bool on)
    {
        if (on)
        {
            SetDrawing(false);
            StopPlacing();
            StopPlacingDetail();
            StopPlacingProjection();
            StopWalls();
            StopStairs();
            StopInner();
            _side?.SetPressedNoSignal(false);
        }
        else
        {
            SelectRoom(null);
        }

        _building?.UpdateGizmos();
    }

    private void StopRooms()
    {
        _roomsButton?.SetPressedNoSignal(false);
    }

    public void SelectRoom(Room? room)
    {
        SelectedRoom = room;
        if (room is not null)
        {
            Select(null);
            SelectDetail(null);
            SelectProjection(null);
            SelectedWall = null;
            SelectedStairs = null;
            SelectedInner = null;
        }

        QueueInspect();
        _building?.UpdateGizmos();
    }

    /// <summary>The room on the picked storey a place on its floor is in, if any.</summary>
    private static Room? RoomUnder(Building building, Vector2 p)
    {
        var inner = building.InnerRing();
        foreach (var r in building.Rooms.Where(r => r is not null && r.Storey == building.CutStorey))
            if (building.SpaceAround(r.Storey, r.Seed, inner) is { } space && Geometry2D.IsPointInPolygon(p, space.Outer) && !space.Holes.Any(h => Geometry2D.IsPointInPolygon(p, h)))
                return r;
        return null;
    }

    /// <summary>The view's clicks for rooms; whether it used them.</summary>
    private bool RoomsInput(Camera3D camera, InputEvent @event)
    {
        if (!DrawingRooms || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            if (DrawingRooms && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } or InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                StopRooms();
                SelectRoom(null);
                return true;
            }

            return false;
        }

        var building = _building!;
        if (OnFloor(building, camera, click.Position) is not { } at)
            return true;
        if (RoomUnder(building, at) is { } room)
        {
            if (click.CtrlPressed)
                RemoveRoom(building, room);
            else
                SelectRoom(room);
            return true;
        }

        if (click.CtrlPressed || building.SpaceAround(building.CutStorey, at) is null)
        {
            SelectRoom(null);
            return true;
        }

        var made = new Room { Storey = building.CutStorey, Seed = at, Name = $"Room {building.Rooms.Count + 1}" };
        var undo = GetUndoRedo();
        undo.CreateAction("Add room", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Rooms, new Godot.Collections.Array<Room>(building.Rooms.Append(made).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Rooms, building.Rooms);
        undo.CommitAction();
        SelectRoom(made);
        return true;
    }

    /// <summary>Takes a room away, as a step to undo.</summary>
    private void RemoveRoom(Building building, Room room)
    {
        var undo = GetUndoRedo();
        undo.CreateAction("Remove room", customContext: building);
        undo.AddDoProperty(building, Building.PropertyName.Rooms, new Godot.Collections.Array<Room>(building.Rooms.Where(r => r != room).ToArray()));
        undo.AddUndoProperty(building, Building.PropertyName.Rooms, building.Rooms);
        undo.CommitAction();
        if (SelectedRoom == room)
            SelectRoom(null);
    }
}
#endif
