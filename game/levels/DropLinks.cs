using System.Collections.Generic;
using System.Linq;
using Godot;
using RND.Core;

namespace RND.Levels;

/// <summary>
/// Adds one-way "drop down" links from every ledge on a baked navmesh to the ground below it, so AI
/// can hop off platforms instead of walking the long way round. Everything is derived from the
/// navmesh and level collision, so any level (hand-made or generated) gets them without placing
/// links by hand.
/// </summary>
public static class DropLinks
{
	public static void Generate(NavigationRegion3D region, float minDrop = 0.4f, float maxDrop = 3f, float spacing = 1f)
	{
		NavigationMesh mesh = region.NavigationMesh;
		Vector3[] vertices = mesh.GetVertices();
		PhysicsDirectSpaceState3D space = region.GetWorld3D().DirectSpaceState;
		float stepOut = mesh.AgentRadius + 0.6f; // from the (eroded) navmesh edge to just past the real ledge

		// Edges used by exactly one polygon are the navmesh outline: walls and ledges.
		var edgeUses = new Dictionary<(int, int), int>();
		for (int p = 0; p < mesh.GetPolygonCount(); p++)
			foreach (var edge in Edges(mesh.GetPolygon(p)))
				edgeUses[edge] = edgeUses.GetValueOrDefault(edge) + 1;

		for (int p = 0; p < mesh.GetPolygonCount(); p++)
		{
			int[] polygon = mesh.GetPolygon(p);
			Vector3 centre = polygon.Aggregate(Vector3.Zero, (sum, i) => sum + vertices[i]) / polygon.Length;

			foreach (var edge in Edges(polygon))
			{
				if (edgeUses[edge] != 1)
					continue;

				Vector3 a = vertices[edge.Item1];
				Vector3 b = vertices[edge.Item2];
				Vector3 outward = new Vector3(b.Z - a.Z, 0f, a.X - b.X).Normalized();
				if (outward.Dot((a + b) / 2f - centre) < 0f)
					outward = -outward;

				int samples = Mathf.Max(1, Mathf.FloorToInt(a.DistanceTo(b) / spacing));
				for (int s = 0; s < samples; s++)
				{
					Vector3 start = region.ToGlobal(a.Lerp(b, (s + 0.5f) / samples));
					Vector3 outwardWorld = (region.GlobalBasis * outward).Normalized();
					if (!TryFindLanding(space, start, outwardWorld * stepOut, minDrop, maxDrop, out Vector3 landing))
						continue;

					region.AddChild(new NavigationLink3D
					{
						StartPosition = region.ToLocal(start),
						EndPosition = region.ToLocal(landing),
						Bidirectional = false,
					});
				}
			}
		}
	}

	private static bool TryFindLanding(PhysicsDirectSpaceState3D space, Vector3 edge, Vector3 step, float minDrop, float maxDrop, out Vector3 landing)
	{
		landing = default;

		// Something waist-high beyond the edge means it's a wall, not a ledge.
		Vector3 waist = edge + Vector3.Up * 0.5f;
		if (space.IntersectRay(PhysicsRayQueryParameters3D.Create(waist, waist + step, Layers.World)).Count > 0)
			return false;

		Vector3 beyond = waist + step;
		var down = space.IntersectRay(PhysicsRayQueryParameters3D.Create(beyond, beyond + Vector3.Down * (maxDrop + 1f), Layers.World));
		if (down.Count == 0)
			return false;

		Vector3 ground = (Vector3)down["position"];
		float drop = edge.Y - ground.Y;
		if (drop < minDrop || drop > maxDrop || ((Vector3)down["normal"]).Y < 0.7f)
			return false;

		landing = ground;
		return true;
	}

	private static IEnumerable<(int, int)> Edges(int[] polygon)
	{
		for (int i = 0; i < polygon.Length; i++)
		{
			int a = polygon[i], b = polygon[(i + 1) % polygon.Length];
			yield return a < b ? (a, b) : (b, a);
		}
	}
}
