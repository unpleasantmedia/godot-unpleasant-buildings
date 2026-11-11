using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>How a staircase runs: in one flight, or turning at a landing.</summary>
public enum StairShape
{
    /// <summary>One flight, straight up.</summary>
    Straight,

    /// <summary>Two flights, the second turning a quarter at a square landing.</summary>
    L,

    /// <summary>Two flights side by side, the second coming back at a landing across both.</summary>
    U,
}

/// <summary>
/// A staircase inside an enterable building, from one storey's floor up to the next one's (a hole
/// cut in that floor over it): its foot's middle at <see cref="Foot"/> (in the building's own space,
/// on the ground: X and Z as a Vector2's X and Y), climbing towards <see cref="Heading"/>. Drawn in
/// the 3D view with Stairs, a storey picked: a line from its foot the way it climbs.
/// </summary>
[Tool]
[GlobalClass]
public partial class Staircase : Resource
{
    private int _storey;
    private StairShape _shape;
    private Vector2 _foot;
    private float _heading;
    private float _width = 1f;
    private float _tread = 0.28f;
    private int _turnAfter;
    private bool _turnLeft;
    private bool _solid = true;
    private bool _railing = true;
    private bool _holeRailing = true;

    /// <summary>Which storey it climbs from, from 0 (the ground floor), up to the one above.</summary>
    [Export]
    public int Storey
    {
        get => _storey;
        set => Set(ref _storey, Mathf.Max(0, value));
    }

    [Export]
    public StairShape Shape
    {
        get => _shape;
        set
        {
            Set(ref _shape, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>The middle of its lowest step's front.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public Vector2 Foot
    {
        get => _foot;
        set => Set(ref _foot, value);
    }

    /// <summary>The way it climbs (its first flight), in degrees round from the building's +Z.</summary>
    [Export(PropertyHint.Range, "-180,180,0.1,degrees")]
    public float Heading
    {
        get => _heading;
        set => Set(ref _heading, Mathf.Wrap(value, -180f, 180f));
    }

    /// <summary>How wide its flights are.</summary>
    [Export(PropertyHint.Range, "1,3,0.05,suffix:m")]
    public float Width
    {
        get => _width;
        set => Set(ref _width, Mathf.Max(StairPlan.MinWidth, value));
    }

    /// <summary>How deep each step is: deeper makes it longer and less steep.</summary>
    [Export(PropertyHint.Range, "0.15,0.35,0.01,suffix:m")]
    public float Tread
    {
        get => _tread;
        set => Set(ref _tread, Mathf.Clamp(value, 0.15f, 0.35f));
    }

    /// <summary>An L's or U's steps before its landing; 0 for half of them.</summary>
    [Export(PropertyHint.Range, "0,30,1")]
    public int TurnAfter
    {
        get => _turnAfter;
        set => Set(ref _turnAfter, Mathf.Max(0, value));
    }

    /// <summary>An L or U turning left at its landing (going up); right if not.</summary>
    [Export]
    public bool TurnLeft
    {
        get => _turnLeft;
        set => Set(ref _turnLeft, value);
    }

    /// <summary>Closed down to the floor under its steps; open treads on stringers if not.</summary>
    [Export]
    public bool Solid
    {
        get => _solid;
        set => Set(ref _solid, value);
    }

    /// <summary>A railing on its sides that aren't against a wall.</summary>
    [Export]
    public bool Railing
    {
        get => _railing;
        set => Set(ref _railing, value);
    }

    /// <summary>A railing round the hole in the floor above (but where the stairs come up, or against a wall).</summary>
    [Export]
    public bool HoleRailing
    {
        get => _holeRailing;
        set => Set(ref _holeRailing, value);
    }

    /// <summary>The way it climbs, on the ground.</summary>
    public Vector2 Forward => new(Mathf.Sin(Mathf.DegToRad(_heading)), Mathf.Cos(Mathf.DegToRad(_heading)));

    /// <summary>Its right, going up, on the ground.</summary>
    public Vector2 Right => new(-Forward.Y, Forward.X);

    /// <summary>A place in its own space (across to its right, along the way it climbs) in the building's, on the ground.</summary>
    public Vector2 At(float across, float along) => _foot + Right * across + Forward * along;

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        if ((name == PropertyName.TurnLeft || name == PropertyName.TurnAfter) && _shape == StairShape.Straight)
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
