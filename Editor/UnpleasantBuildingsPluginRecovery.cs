#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Keeping hold of the selected building whatever order the editor tells this things in: as the
/// selection changes, the building selected is the one worked on, its toolbar and dock shown; and
/// a click in a 3D view, a building selected, that the editor did not pass on here (it stopped,
/// for a reason of its own) is taken here all the same, and the editor told to pass them on again.
/// Each time that happens it says so in the Output, to know it happens and how often.
/// </summary>
public partial class UnpleasantBuildingsPlugin
{
    /// <summary>The last few events the editor passed on to <see cref="_Forward3DGuiInput"/> (by their instances), to tell those it did not.</summary>
    private readonly Queue<ulong> _forwarded = new();

    private void Forwarded(InputEvent @event)
    {
        _forwarded.Enqueue(@event.GetInstanceId());
        while (_forwarded.Count > 32)
            _forwarded.Dequeue();
    }

    /// <summary>The 3D views' surfaces watched for clicks, with their view's number.</summary>
    private readonly List<(Control Surface, int View)> _watched = [];

    /// <summary>Watches the 3D views' surfaces (once the editor has made them).</summary>
    private void WatchViews()
    {
        for (var i = 0; i < 4; i++)
        {
            // The surface the editor's view takes its input on: beside its viewport's container.
            if (EditorInterface.Singleton.GetEditorViewport3D(i) is not { } viewport
                || viewport.GetParent()?.GetParent() is not Control view)
                continue;
            var number = i;
            foreach (var surface in view.GetChildren().OfType<Control>().Where(c => c is not SubViewportContainer))
            {
                // By the method's name, not a lambda: a lambda's delegate is lost as the code is
                // reloaded (and the view errors at each click after); a name is found again.
                var call = new Callable(this, ViewInputs[number]);
                if (!surface.IsConnected(Control.SignalName.GuiInput, call))
                    surface.Connect(Control.SignalName.GuiInput, call);
                _watched.Add((surface, i));
                _calls.Add(call);
            }
        }
    }

    private readonly List<Callable> _calls = [];

    private void UnwatchViews()
    {
        for (var k = 0; k < _watched.Count; k++)
            if (IsInstanceValid(_watched[k].Surface) && _watched[k].Surface.IsConnected(Control.SignalName.GuiInput, _calls[k]))
                _watched[k].Surface.Disconnect(Control.SignalName.GuiInput, _calls[k]);
        _watched.Clear();
        _calls.Clear();
    }

    /// <summary>The methods taking each view's clicks, by name (C# callables cannot be bound to an argument).</summary>
    private static readonly StringName[] ViewInputs = [MethodName.View0Input, MethodName.View1Input, MethodName.View2Input, MethodName.View3Input];

    private void View0Input(InputEvent @event) => SurfaceInput(@event, 0);
    private void View1Input(InputEvent @event) => SurfaceInput(@event, 1);
    private void View2Input(InputEvent @event) => SurfaceInput(@event, 2);
    private void View3Input(InputEvent @event) => SurfaceInput(@event, 3);

    /// <summary>A click in a 3D view: looked at once the editor is done with it (whichever order it is told in).</summary>
    private void SurfaceInput(InputEvent @event, int view)
    {
        // Left clicks only (the right button is the view's to look about with).
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            Callable.From(() => Unforwarded(@event, view)).CallDeferred();
    }

    /// <summary>If a building is selected and the editor never passed the click on here: taken here, and the editor told to pass them on again.</summary>
    private void Unforwarded(InputEvent @event, int view)
    {
        // Passed on after all; or another tool had the view's clicks (props being put down), taking them first.
        if (_forwarded.Contains(@event.GetInstanceId()) || BuildingsLinks.Busy?.Invoke() == true)
            return;
        if (SelectedBuilding() is not { } building || EditorInterface.Singleton.GetEditorViewport3D(view)?.GetCamera3D() is not { } camera)
            return;
        GD.Print("[Unpleasant Buildings] The editor did not pass a click in the 3D view on to the building plugin: taken back.");
        Adopt(building);
        _Forward3DGuiInput(camera, @event);
        // The editor told to pass them on again, as if the building were picked anew.
        EditorInterface.Singleton.EditNode(building);
    }

    /// <summary>The selected building worked on, its toolbar and dock shown (as when the editor hands it over).</summary>
    private void Adopt(Building building)
    {
        if (_building != building)
            _Edit(building);
        if (_toolbar is not null && !_toolbar.Visible)
            _toolbar.Visible = true;
        ShowDetailDock(true);
    }

    /// <summary>As the selection changes: the building selected (alone) is the one worked on, its tools shown.</summary>
    private void Resync()
    {
        if (SelectedBuilding() is { } building)
            Adopt(building);
    }
}
#endif
