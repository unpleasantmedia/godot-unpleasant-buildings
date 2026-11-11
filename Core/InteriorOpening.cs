using System.Collections.Generic;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>What an opening in an interior wall is.</summary>
public enum InteriorOpeningKind
{
    /// <summary>A square-headed opening with a frame round it (a door to come).</summary>
    Doorway,

    /// <summary>An opening with a curved head and no frame.</summary>
    Arch,
}

/// <summary>The curve of an arch's head.</summary>
public enum ArchShape
{
    /// <summary>A half circle as wide as the arch.</summary>
    Round,

    /// <summary>A shallow curve rising <see cref="InteriorOpening.Rise"/> above its sides.</summary>
    Segmental,
}

/// <summary>
/// A doorway or an arch through an interior wall: <see cref="From"/> along it from its start
/// (<see cref="InteriorWall.A"/>), <see cref="Width"/> wide, from the floor up to
/// <see cref="Height"/>. Placed in the 3D view with Doorway or Arch, a storey picked.
/// </summary>
[Tool]
[GlobalClass]
public partial class InteriorOpening : Resource
{
    private InteriorOpeningKind _kind;
    private float _from;
    private float _width = 0.9f;
    private float _height = 2.1f;
    private ArchShape _shape;
    private float _rise = 0.3f;
    private bool _hasDoor = true;
    private bool _hingeRight;
    private bool _opensBack;

    [Export]
    public InteriorOpeningKind Kind
    {
        get => _kind;
        set
        {
            Set(ref _kind, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>How far along its wall from the wall's start its left side is.</summary>
    [Export(PropertyHint.Range, "0,50,0.05,or_greater,suffix:m")]
    public float From
    {
        get => _from;
        set => Set(ref _from, Mathf.Max(0f, value));
    }

    [Export(PropertyHint.Range, "0.4,4,0.05,suffix:m")]
    public float Width
    {
        get => _width;
        set => Set(ref _width, Mathf.Max(0.4f, value));
    }

    /// <summary>How high it is from the floor (to the top of an arch's curve).</summary>
    [Export(PropertyHint.Range, "1,4,0.05,suffix:m")]
    public float Height
    {
        get => _height;
        set => Set(ref _height, Mathf.Max(1f, value));
    }

    /// <summary>An arch's head: a half circle, or a shallow curve.</summary>
    [Export]
    public ArchShape Shape
    {
        get => _shape;
        set
        {
            Set(ref _shape, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>How far a segmental arch's head rises above its sides.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01,suffix:m")]
    public float Rise
    {
        get => _rise;
        set => Set(ref _rise, Mathf.Max(0.05f, value));
    }

    /// <summary>For a doorway: with a door in it (opening in the game).</summary>
    [Export]
    public bool HasDoor
    {
        get => _hasDoor;
        set
        {
            Set(ref _hasDoor, value);
            NotifyPropertyListChanged();
        }
    }

    /// <summary>Its door hung at its end towards its wall's end B rather than towards A.</summary>
    [Export]
    public bool HingeRight
    {
        get => _hingeRight;
        set => Set(ref _hingeRight, value);
    }

    /// <summary>Its door swinging to its wall's back rather than its front.</summary>
    [Export]
    public bool OpensBack
    {
        get => _opensBack;
        set => Set(ref _opensBack, value);
    }

    /// <summary>A new one of a kind, of the usual size.</summary>
    public static InteriorOpening Make(InteriorOpeningKind kind) => kind == InteriorOpeningKind.Arch
        ? new InteriorOpening { Kind = kind, Width = 1.2f, Height = 2.3f }
        : new InteriorOpening { Kind = kind, Width = 0.9f, Height = 2.1f };

    /// <summary>
    /// Its outline on its wall's face: u along from the wall's start, v up from the floor (its foot
    /// a little below the floor, to cut the wall cleanly), no higher than the given most.
    /// </summary>
    public Vector2[] Outline(float most)
    {
        var (u0, u1) = (_from, _from + _width);
        var top = Mathf.Min(_height, most);
        const float below = -0.01f;
        if (_kind == InteriorOpeningKind.Doorway)
            return [new(u0, below), new(u1, below), new(u1, top), new(u0, top)];
        var half = _width / 2f;
        var middle = (u0 + u1) / 2f;
        var points = new List<Vector2> { new(u0, below), new(u1, below) };
        const int steps = 16;
        if (_shape == ArchShape.Round)
        {
            var spring = Mathf.Max(0f, top - half);
            for (var k = 0; k <= steps; k++)
            {
                var angle = Mathf.Pi * k / steps;
                points.Add(new Vector2(middle + half * Mathf.Cos(angle), spring + Mathf.Min(half, top) * Mathf.Sin(angle)));
            }
        }
        else
        {
            var rise = Mathf.Min(_rise, top);
            // A circle through both springings and the crown.
            var radius = (half * half + rise * rise) / (2f * rise);
            var centre = top - radius;
            var reach = Mathf.Asin(Mathf.Clamp(half / radius, -1f, 1f));
            for (var k = 0; k <= steps; k++)
            {
                var angle = Mathf.Lerp(-reach, reach, (float)k / steps);
                points.Add(new Vector2(middle + radius * Mathf.Sin(angle), centre + radius * Mathf.Cos(angle)));
            }

            // From the right side over to the left: the angle above runs left to right, so reverse it.
            points.Reverse(2, steps + 1);
        }

        return [.. points];
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        var name = property["name"].AsStringName();
        if (name == PropertyName.Shape && _kind != InteriorOpeningKind.Arch
            || name == PropertyName.Rise && (_kind != InteriorOpeningKind.Arch || _shape != ArchShape.Segmental)
            || name == PropertyName.HasDoor && _kind != InteriorOpeningKind.Doorway
            || (name == PropertyName.HingeRight || name == PropertyName.OpensBack) && (_kind != InteriorOpeningKind.Doorway || !_hasDoor))
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
