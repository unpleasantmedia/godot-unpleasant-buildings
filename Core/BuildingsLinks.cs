using System;
using System.Collections.Generic;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// What the plugin can't know itself, a project tells it here (once, for example from a module
/// initializer). All of it may be left unset.
/// </summary>
public static class BuildingsLinks
{
    /// <summary>
    /// Where a ray meets the ground in a scene: its scene's root, where the ray starts, and which way
    /// it goes; null where it meets none. Used to draw outlines on a terrain that has no collision in
    /// the editor. Without it, outlines are drawn on whatever is solid, or on a flat ground at height 0.
    /// </summary>
    public static Func<Node, Vector3, Vector3, Vector3?>? Ground { get; set; }

    /// <summary>
    /// The ground's height under a place in the world (only its X and Z count): its scene's root, and
    /// the place; null where there is no ground. A building's floor and plinth are fitted to it.
    /// Without it, the ground is taken to be at the building's own height.
    /// </summary>
    public static Func<Node, Vector3, float?>? Height { get; set; }

    /// <summary>
    /// Makes a door leaf's body, in the game: the project's own kind of door (to be opened its own
    /// way). The plugin gives it its place (its origin on the hinge, in a doorway node whose +Z is the
    /// doorway's front), its mesh and its collision, and sets an <c>OpenAngle</c> property on it if it
    /// has one (degrees about its hinge, positive turning from above, as Godot does). Without it, each
    /// leaf is a <see cref="BuildingDoor"/>.
    /// </summary>
    public static Func<AnimatableBody3D>? Door { get; set; }

    /// <summary>
    /// Whether another tool is using the 3D view's clicks now (a props plugin with a prop in hand,
    /// say): the building's own tools leave them alone meanwhile.
    /// </summary>
    public static Func<bool>? Busy { get; set; }

    /// <summary>
    /// Told whenever a building is built again (drawn, moved, changed), and when one is taken away: for
    /// a project to keep other things (foliage, say) off where it stands. See <see cref="Building.Footprint"/>.
    /// </summary>
    public static Action<Building>? Placed { get; set; }

    public static Action<Building>? Removed { get; set; }

    /// <summary>
    /// Where things are that need baked buildings near them solid (the player, people walking, cars),
    /// in the world, given a node in the scene: a baked building within its cluster's Collision
    /// Range of one is built (its collision, doors and details), and let go of again when far from
    /// all. Without it, the camera's place.
    /// </summary>
    public static Func<Node, IEnumerable<Vector3>>? Anchors { get; set; }

    /// <summary>Where baked buildings' meshes are saved (see BakedBuildingsNode); without it, res://BakedBuildings.</summary>
    public static string? BakedFolder { get; set; }
}
