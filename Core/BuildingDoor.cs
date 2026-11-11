using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A door leaf of a building, in the game: hung in its doorway (its parent, +Z the doorway's front),
/// its origin on its hinge, swinging about it by <see cref="OpenAngle"/>. <see cref="Toggle"/> opens
/// or shuts it, with the other leaf of a pair. Used when the project gives no door of its own
/// (see <see cref="BuildingsLinks.Door"/>).
/// </summary>
public partial class BuildingDoor : AnimatableBody3D
{
    /// <summary>How long it takes to swing open or shut, in seconds.</summary>
    private const float SwingSeconds = 0.35f;

    private float _shut;
    private Tween? _swing;

    /// <summary>How far it swings open about its hinge, in degrees (turning from above, as Godot does).</summary>
    [Export(PropertyHint.Range, "-180,180,1,suffix:°")]
    public float OpenAngle { get; set; } = 90f;

    public bool IsOpen { get; private set; }

    public override void _Ready() => _shut = RotationDegrees.Y;

    /// <summary>Opens it if it is shut, shuts it if it is open, with the other of a pair.</summary>
    public void Toggle()
    {
        var open = !IsOpen;
        foreach (var leaf in GetParent().GetChildren().OfType<BuildingDoor>())
            leaf.Swing(open);
    }

    private void Swing(bool open)
    {
        IsOpen = open;
        _swing?.Kill();
        _swing = CreateTween();
        _swing.TweenProperty(this, "rotation_degrees:y", _shut + (open ? OpenAngle : 0f), SwingSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }
}
