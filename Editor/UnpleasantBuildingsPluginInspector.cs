#if TOOLS
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// The selected door, window, wall detail, balcony, awning or porch shown in the Inspector (the
/// building staying selected in the scene and the view), with how far it is from both ends of its
/// wall and buttons to centre it at the top; the building shown again once none is selected.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    private ItemInspector? _itemInspector;
    private SpinBox? _itemFromStart;
    private SpinBox? _itemFromEnd;
    private Resource? _itemShown;

    /// <summary>The selected one, of whichever kind.</summary>
    private Resource? SelectedItem => (Resource?)SelectedOpening ?? (Resource?)SelectedDetail ?? SelectedProjection;

    private static bool IsItem(GodotObject? o) => o is BuildingOpening or WallDetail or BuildingProjection or InteriorWall or Staircase or global::UnpleasantMedia.UnpleasantBuildings.Room or InteriorOpening;

    private void AddItemInspector()
    {
        _itemInspector = new ItemInspector();
        AddInspectorPlugin(_itemInspector);
    }

    private void RemoveItemInspector()
    {
        if (_itemInspector is not null)
            RemoveInspectorPlugin(_itemInspector);
        _itemInspector = null;
    }

    /// <summary>After a selection changes (once, however many changes it took): the Inspector shows the selected one, or the building again.</summary>
    private void QueueInspect() => CallDeferred(MethodName.Inspect);

    private void Inspect()
    {
        if (_building is null || !IsInstanceValid(_building))
            return;
        var shown = EditorInterface.Singleton.GetInspector().GetEditedObject();
        if ((SelectedItem ?? (Resource?)SelectedWall ?? (Resource?)SelectedStairs ?? (Resource?)SelectedRoom ?? SelectedInner) is { } item)
        {
            if (shown != item)
                EditorInterface.Singleton.InspectObject(item, "", true);
            return;
        }

        // Back to the building only from one of its own (not when something else was picked meanwhile).
        if (IsItem(shown) && EditorInterface.Singleton.GetSelection().GetSelectedNodes().Contains(_building))
            EditorInterface.Singleton.InspectObject(_building, "", true);
    }

    /// <summary>The top of the Inspector for one of a building's own: from start, from end, centring.</summary>
    public Control? ItemHeader(GodotObject o)
    {
        if (_building is null || o != SelectedItem)
            return null;
        var box = new VBoxContainer();
        var row = new HBoxContainer();
        box.AddChild(row);
        SpinBox Number(string label, StringName method)
        {
            row.AddChild(new Label { Text = label });
            var spin = new SpinBox { MinValue = -1000, MaxValue = 1000, Step = OpeningStep, Suffix = "m", SelectAllOnFocus = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            spin.Connect(Godot.Range.SignalName.ValueChanged, new Callable(this, method));
            row.AddChild(spin);
            return spin;
        }

        _itemFromStart = Number("From start", MethodName.ItemFromStartChanged);
        _itemFromEnd = Number("From end", MethodName.ItemFromEndChanged);
        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        var across = new Button { Text = "Centre Across", TooltipText = "Puts it in the middle of its wall, along it.", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        across.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.ItemCentreAcross));
        buttons.AddChild(across);
        var canCentreUp = o switch
        {
            BuildingOpening opening => opening.Kind == OpeningKind.Window,
            WallDetail detail => detail.Type?.HeightLocked != true,
            _ => false,
        };
        if (canCentreUp)
        {
            var up = new Button { Text = "Centre Up", TooltipText = "Puts it in the middle of its storey, up it.", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            up.Connect(BaseButton.SignalName.Pressed, new Callable(this, MethodName.ItemCentreUp));
            buttons.AddChild(up);
        }

        box.AddChild(new HSeparator());
        _itemShown = (Resource)o;
        _itemShown.Connect(Resource.SignalName.Changed, new Callable(this, MethodName.FillItemHeader));
        box.Connect(Node.SignalName.TreeExiting, new Callable(this, MethodName.ItemHeaderGone));
        FillItemHeader();
        return box;
    }

    private void ItemHeaderGone()
    {
        if (_itemShown is not null && IsInstanceValid(_itemShown) && _itemShown.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.FillItemHeader)))
            _itemShown.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.FillItemHeader));
        _itemShown = null;
        _itemFromStart = _itemFromEnd = null;
    }

    /// <summary>Where the shown one's ends are along its wall, from its start.</summary>
    private (float U0, float U1)? ItemSpan()
    {
        if (_building is null)
            return null;
        return SelectedItem switch
        {
            BuildingOpening o => (o.From, o.From + o.Width),
            WallDetail d => (Edges(_building, d).U0, Edges(_building, d).U1),
            BuildingProjection p => (p.From, p.From + p.Width),
            _ => null,
        };
    }

    private int ItemSide() => SelectedItem switch
    {
        BuildingOpening o => o.Side,
        WallDetail d => d.Side,
        BuildingProjection p => p.Side,
        _ => 0,
    };

    private void FillItemHeader()
    {
        if (_itemFromStart is null || _itemFromEnd is null || !IsInstanceValid(_itemFromStart) || _building is null || ItemSpan() is not { } span)
            return;
        _filling = true;
        _itemFromStart.SetValueNoSignal(span.U0);
        _itemFromEnd!.SetValueNoSignal(_building.SideLength(ItemSide()) - span.U1);
        _filling = false;
    }

    private void ItemFromStartChanged(double value)
    {
        switch (SelectedItem)
        {
            case BuildingOpening:
                FromStartChanged(value);
                break;
            case WallDetail:
                DetailFromStartChanged(value);
                break;
            case BuildingProjection:
                LedgeFromStartChanged(value);
                break;
        }
    }

    private void ItemFromEndChanged(double value)
    {
        switch (SelectedItem)
        {
            case BuildingOpening:
                FromEndChanged(value);
                break;
            case WallDetail:
                DetailFromEndChanged(value);
                break;
            case BuildingProjection:
                LedgeFromEndChanged(value);
                break;
        }
    }

    private void ItemCentreAcross()
    {
        switch (SelectedItem)
        {
            case BuildingOpening:
                CentreAcross();
                break;
            case WallDetail:
                DetailCentreAcross();
                break;
            case BuildingProjection:
                LedgeCentreAcross();
                break;
        }
    }

    private void ItemCentreUp()
    {
        switch (SelectedItem)
        {
            case BuildingOpening:
                CentreUp();
                break;
            case WallDetail:
                DetailCentreUp();
                break;
        }
    }
}
#endif
