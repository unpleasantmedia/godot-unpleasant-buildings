using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Where a group of buildings goes (a street, a block, a town): with it selected, Draw in the 3D
/// view's toolbar draws a new building under it, its first corner where the ground is clicked; and
/// Bake Buildings bakes their look into a few meshes, for the game (see BakedBuildingsNode).
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/unpleasant_buildings/Icons/Building.svg")]
public partial class BuildingCluster : Node3D
{
    /// <summary>How big the squares are that its buildings are baked in (merged by material), in metres.</summary>
    [Export(PropertyHint.Range, "8,512,1,suffix:m")]
    public float BakeSquare { get; set; } = 64f;

    /// <summary>
    /// Baked, how near something that needs it solid (see BuildingsLinks.Anchors) a building is
    /// built in the game (its collision, doors and details), in metres; it is let go of again a
    /// margin further off.
    /// </summary>
    [Export(PropertyHint.Range, "10,500,1,suffix:m")]
    public float CollisionRange { get; set; } = 80f;

    [Export(PropertyHint.Range, "0,200,1,suffix:m")]
    public float CollisionMargin { get; set; } = 30f;

    /// <summary>
    /// How far past its buildings' walls no foliage is painted, in metres, for those without a buffer
    /// of their own (see Building.OwnFoliageBuffer): told on as each is placed (BuildingsLinks.Placed).
    /// </summary>
    [ExportGroup("Foliage")]
    [Export(PropertyHint.Range, "0,100,0.5,or_greater,suffix:m")]
    public float FoliageBuffer
    {
        get => _foliageBuffer;
        set
        {
            _foliageBuffer = Mathf.Max(0f, value);
            if (!IsInsideTree())
                return;
            foreach (var building in Buildings)
                building.FoliageBufferChanged();
        }
    }

    private float _foliageBuffer = 4f;

    /// <summary>Bakes its buildings' look (see BakedBuildingsNode), for the game to draw them faster; the level to be saved after.</summary>
    [ExportToolButton("Bake Buildings", Icon = "Bake")]
    public Callable BakeButton => Callable.From(Bake);

    private void Bake()
    {
        if (!Engine.IsEditorHint() || GetTree().EditedSceneRoot is not { } root || root.SceneFilePath.Length == 0)
        {
            GD.PushWarning("Bake Buildings: save the level first.");
            return;
        }

        BakedBuildingsNode.Bake(this, root);
    }

    /// <summary>Its buildings: those under it, at any depth.</summary>
    public IEnumerable<Building> Buildings => FindChildren("*", "", true, false).OfType<Building>();

    /// <summary>The cluster a node is in (itself, or the nearest above it), if any.</summary>
    public static BuildingCluster? Of(Node? node)
    {
        for (var at = node; at is not null; at = at.GetParent())
            if (at is BuildingCluster cluster)
                return cluster;
        return null;
    }
}
