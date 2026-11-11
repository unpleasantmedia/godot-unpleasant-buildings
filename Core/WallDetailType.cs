using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A kind of wall detail (an AC unit, a drainpipe, a sign): a scene whose +Z faces out of the wall
/// and whose X runs along it. One that sizes itself by a property of its root (shutters by their
/// width, a pillar by its length) names it here, and is sized with handles in the 3D view.
/// </summary>
[Tool]
[GlobalClass]
public partial class WallDetailType : Resource
{
    /// <summary>What the dock calls it; with none, its scene's file name.</summary>
    [Export]
    public string Name { get; set; } = "";

    /// <summary>Which section of the dock it is in.</summary>
    [Export]
    public string Category { get; set; } = "";

    [Export]
    public PackedScene? Scene { get; set; }

    /// <summary>The property of its scene's root that makes it wider, along the wall, in metres (a whole number of them for a property that takes whole numbers); empty if it can't be.</summary>
    [Export]
    public string WidthProperty { get; set; } = "";

    /// <summary>The property of its scene's root that makes it taller, up the wall, in metres; empty if it can't be.</summary>
    [Export]
    public string HeightProperty { get; set; } = "";

    /// <summary>Whether it always sits at <see cref="Up"/> above its storey's floor (a drainpipe): moved only along walls and between storeys.</summary>
    [Export]
    public bool HeightLocked { get; set; }

    /// <summary>For one whose height is locked: how far its origin is above its storey's floor, in metres.</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float Up { get; set; }

    /// <summary>
    /// How far in front of its origin the wall is in its scene, in metres (along +Z): made for walls
    /// whose face isn't on the origin, it is moved back by this, so it sits on the wall all the same.
    /// </summary>
    [Export(PropertyHint.None, "suffix:m")]
    public float WallOffset { get; set; }

    /// <summary>What the dock shows of it.</summary>
    public string Shown => Name.Length > 0 ? Name : Scene?.ResourcePath.GetFile().GetBaseName() ?? "(none)";
}
