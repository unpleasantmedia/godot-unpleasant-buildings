using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A door leaf to hang in the game: which doorway it is in (leaves of a pair share one), where that
/// doorway is (in the building's own space: +Z its front), where its hinge is in the doorway, how
/// far it swings open, its mesh (in the building's own space), and its box (centre and size, in its
/// own space: from its hinge).
/// </summary>
public sealed record DoorLeaf(int Doorway, Transform3D DoorwayAt, Vector3 Hinge, float OpenAngle, ArrayMesh Mesh, Vector3 BoxCentre, Vector3 BoxSize);

/// <summary>What a door leaf is made of.</summary>
public readonly record struct LeafMaterials(Material? Door, Material? Glass, Material? Handle);

public static partial class BuildingMesher
{
    /// <summary>How thick an interior door's leaf is.</summary>
    private const float InnerLeafThickness = 0.04f;

    /// <summary>
    /// A leaf in a wall's frame: from a to b along it, v0 to v1 up, w0 to w1 out; glazed if asked
    /// (and big enough), its handle near the edge away from its hinge, on its front (and back, if
    /// asked). Into the faces given.
    /// </summary>
    private static void LeafFaces(Faces doors, Faces glass, Faces handles, WallFrame f, float a, float b, float v0, float v1, float w0, float w1, bool glazed, bool hingeAtA, bool bothSides)
    {
        if (glazed && b - a > 2f * Stile + 0.1f && v1 - v0 > KickPanel + Stile + 0.2f)
        {
            doors.Box(f, a, a + Stile, v0, v1, w0, w1);
            doors.Box(f, b - Stile, b, v0, v1, w0, w1);
            doors.Box(f, a + Stile, b - Stile, v1 - Stile, v1, w0, w1);
            doors.Box(f, a + Stile, b - Stile, v0, v0 + KickPanel, w0, w1);
            glass.Pane(f, a + Stile, b - Stile, v0 + KickPanel, v1 - Stile, (w0 + w1) / 2f);
        }
        else
        {
            doors.Box(f, a, b, v0, v1, w0, w1);
        }

        var hu = hingeAtA ? b - 0.08f : a + 0.08f;
        var hv = v0 + 1.0f;
        handles.Box(f, hu - 0.06f, hu + 0.06f, hv - 0.015f, hv + 0.015f, w1, w1 + 0.05f);
        if (bothSides)
            handles.Box(f, hu - 0.06f, hu + 0.06f, hv - 0.015f, hv + 0.015f, w0 - 0.05f, w0);
    }

    /// <summary>
    /// A leaf to hang (its faces in its own mesh, not the building's): its doorway's frame (at its
    /// middle along it, on its floor, +Z its front, the wall's out), its hinge on the edge at a or b
    /// (half way through it), swinging to the front or the back.
    /// </summary>
    private static DoorLeaf HungLeaf(int doorway, WallFrame f, float doorwayU, float a, float b, float v0, float v1, float w0, float w1, bool glazed, bool hingeAtA, bool toFront, bool bothSides, LeafMaterials materials)
    {
        // The doorway: X along the wall (or against it, so that X, up and out turn as Godot's axes do), Y up, Z out.
        var along = f.Along;
        var x = along.Cross(Vector3.Up).Dot(f.Out) > 0f ? along : -along;
        var at = new Transform3D(new Basis(x, Vector3.Up, f.Out), f.At(doorwayU, v0, 0f));
        var hingeU = hingeAtA ? a : b;
        var hinge = at.AffineInverse() * f.At(hingeU, v0, (w0 + w1) / 2f);
        // Its free edge's way from the hinge, along the doorway's X: swinging that edge to the back (-Z) turns it one way, to the front the other.
        var freeAlongX = ((hingeAtA ? b - a : a - b) > 0f) == (x == along);
        var angle = (freeAlongX ? 90f : -90f) * (toFront ? -1f : 1f);

        var (doors, glass, handles) = (new Faces(), new Faces(), new Faces());
        LeafFaces(doors, glass, handles, f, a, b, v0, v1, w0, w1, glazed, hingeAtA, bothSides);
        var mesh = new ArrayMesh();
        doors.AddTo(mesh, materials.Door);
        glass.AddTo(mesh, materials.Glass);
        handles.AddTo(mesh, materials.Handle);

        // Its box in its own space (from its hinge, axes as the doorway's).
        var leafAt = at * new Transform3D(Basis.Identity, hinge);
        var centre = leafAt.AffineInverse() * f.At((a + b) / 2f, (v0 + v1) / 2f, (w0 + w1) / 2f);
        return new DoorLeaf(doorway, at, hinge, angle, mesh, centre, new Vector3(Mathf.Abs(b - a), v1 - v0, Mathf.Abs(w1 - w0)));
    }
}

public partial class Building
{
    /// <summary>The door leaves hung in the game (their doorways, under it), to be taken down when it is built again.</summary>
    private readonly List<Node3D> _doorways = [];

    /// <summary>In the game: each leaf hung in its doorway, a body that swings (the project's own door, if it gives one; see <see cref="BuildingsLinks.Door"/>).</summary>
    private void HangDoors(List<DoorLeaf> leaves)
    {
        // Taken out at once (their names free for the new ones), freed after.
        foreach (var d in _doorways.Where(IsInstanceValid))
        {
            RemoveChild(d);
            d.QueueFree();
        }

        _doorways.Clear();
        foreach (var group in leaves.GroupBy(l => l.Doorway))
        {
            var doorway = new Node3D { Name = $"Doorway{group.Key}", Transform = group.First().DoorwayAt };
            foreach (var leaf in group)
            {
                var body = BuildingsLinks.Door?.Invoke() ?? new BuildingDoor();
                body.Transform = new Transform3D(Basis.Identity, leaf.Hinge);
                if (body.Get("OpenAngle").VariantType != Variant.Type.Nil)
                    body.Set("OpenAngle", leaf.OpenAngle);
                // The mesh is in the building's space: put back there from the leaf's.
                body.AddChild(new MeshInstance3D { Mesh = leaf.Mesh, Transform = (leaf.DoorwayAt * body.Transform).AffineInverse() });
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = leaf.BoxSize }, Position = leaf.BoxCentre });
                doorway.AddChild(body);
            }

            AddChild(doorway, false, InternalMode.Back);
            _doorways.Add(doorway);
        }
    }
}
