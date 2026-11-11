using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// A building's surfaces, found by a ray (for putting things against its floors, walls and
/// ceilings in the editor, where it has no collision), and the things put in it (under it in the
/// scene) hidden above the storey being worked on.
/// </summary>
public partial class Building
{
    /// <summary>Its mesh's triangles, to find where a ray meets it: made when first asked, after each build.</summary>
    private TriangleMesh? _triangles;

    /// <summary>The things in it hidden above the storey being worked on, to show again.</summary>
    private readonly List<Node3D> _hiddenAbove = [];

    /// <summary>Where a ray (in the world) first meets the building as built (cut open, in the editor, as shown), and the face's normal there (in the world); none if it misses.</summary>
    public (Vector3 Point, Vector3 Normal)? Raycast(Vector3 from, Vector3 direction)
    {
        if (Mesh is null)
            return null;
        _triangles ??= Mesh.GenerateTriangleMesh();
        if (_triangles is null)
            return null;
        var inverse = GlobalTransform.AffineInverse();
        var hit = _triangles.IntersectRay(inverse * from, (inverse.Basis * direction).Normalized());
        if (hit.Count == 0)
            return null;
        var point = GlobalTransform * hit["position"].AsVector3();
        var normal = (GlobalBasis.Inverse().Transposed() * hit["normal"].AsVector3()).Normalized();
        // Facing the ray (the faces are one-sided; seen from behind, the other way).
        if (normal.Dot(direction) > 0f)
            normal = -normal;
        return (point, normal);
    }

    /// <summary>The nearest building in a scene a ray meets, where, and the face's normal there.</summary>
    public static (Building Building, Vector3 Point, Vector3 Normal)? RaycastAll(Node root, Vector3 from, Vector3 direction)
    {
        (Building, Vector3, Vector3)? best = null;
        var nearest = float.PositiveInfinity;
        foreach (var b in InTree.Where(b => IsInstanceValid(b) && b.IsInsideTree() && (b == root || root.IsAncestorOf(b))))
        {
            if (b.Raycast(from, direction) is not { } hit)
                continue;
            var distance = hit.Point.DistanceTo(from);
            if (distance < nearest)
            {
                nearest = distance;
                best = (b, hit.Point, hit.Normal);
            }
        }

        return best;
    }

    /// <summary>
    /// In the editor, with a storey picked: the things put in it (its children, or theirs: props in
    /// a props node under it) standing at or above the next storey's floor hidden; the rest shown.
    /// </summary>
    private void HideAbove()
    {
        ShowHidden();
        if (CutStorey < 0 || CutStorey >= _storeys - 1)
            return;
        var above = StoreyFloor(CutStorey + 1) - 0.05f;
        foreach (var thing in PutInside())
        {
            if (!thing.Visible || ToLocal(thing.GlobalPosition).Y < above)
                continue;
            thing.Visible = false;
            _hiddenAbove.Add(thing);
        }
    }

    private void ShowHidden()
    {
        foreach (var thing in _hiddenAbove.Where(IsInstanceValid))
            thing.Visible = true;
        _hiddenAbove.Clear();
    }

    /// <summary>The things put in it: its own children in the scene (not its internal ones; props put straight in it), or, for one of the scene's own holding others (a props node), those.</summary>
    private IEnumerable<Node3D> PutInside()
    {
        foreach (var child in GetChildren().OfType<Node3D>())
        {
            // A prop (an instance of a scene) is one thing, whatever is inside it; a node of the
            // scene's own holding others (a props node) holds things.
            var inner = child.SceneFilePath.Length > 0 ? [] : child.GetChildren().OfType<Node3D>().ToList();
            if (inner.Count == 0)
                yield return child;
            else
                foreach (var thing in inner)
                    yield return thing;
        }
    }

    /// <summary>Never saved hidden: shown for a save, hidden again after it.</summary>
    private void Saving(bool before)
    {
        if (before)
            ShowHidden();
        else
            HideAbove();
    }
}
