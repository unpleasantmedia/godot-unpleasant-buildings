using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A wall inside an enterable building: a straight line on one storey, from <see cref="A"/> to
/// <see cref="B"/> (in the building's own space, on the ground: X and Z as a Vector2's X and Y), from
/// that storey's floor up to its ceiling, <see cref="Thickness"/> thick. Drawn in the 3D view with
/// Walls, a storey picked.
/// </summary>
[Tool]
[GlobalClass]
public partial class InteriorWall : Resource
{
    private int _storey;
    private Vector2 _a;
    private Vector2 _b;
    private float _thickness = 0.1f;
    private Godot.Collections.Array<InteriorOpening> _openings = [];

    /// <summary>Which storey it is on, from 0 (the ground floor).</summary>
    [Export]
    public int Storey
    {
        get => _storey;
        set => Set(ref _storey, Mathf.Max(0, value));
    }

    [Export(PropertyHint.None, "suffix:m")]
    public Vector2 A
    {
        get => _a;
        set => Set(ref _a, value);
    }

    [Export(PropertyHint.None, "suffix:m")]
    public Vector2 B
    {
        get => _b;
        set => Set(ref _b, value);
    }

    [Export(PropertyHint.Range, "0.05,0.5,0.01,suffix:m")]
    public float Thickness
    {
        get => _thickness;
        set => Set(ref _thickness, Mathf.Max(0.05f, value));
    }

    /// <summary>Its doorways and arches: placed in the 3D view (Doorway, Arch).</summary>
    [Export]
    public Godot.Collections.Array<InteriorOpening> Openings
    {
        get => _openings;
        set
        {
            foreach (var o in _openings)
                if (o is not null && o.IsConnected(Resource.SignalName.Changed, new Callable(this, Resource.MethodName.EmitChanged)))
                    o.Disconnect(Resource.SignalName.Changed, new Callable(this, Resource.MethodName.EmitChanged));
            _openings = value ?? [];
            foreach (var o in _openings)
                if (o is not null && !o.IsConnected(Resource.SignalName.Changed, new Callable(this, Resource.MethodName.EmitChanged)))
                    o.Connect(Resource.SignalName.Changed, new Callable(this, Resource.MethodName.EmitChanged));
            EmitChanged();
        }
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
