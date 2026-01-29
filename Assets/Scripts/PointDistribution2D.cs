using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class PointDistribution2D : MonoBehaviour
{
    [Header("Area Settings")]
    public Vector2 areaSize = new Vector2(10f, 10f);

    [Header("Point Settings")]
    [Min(1)] public int pointCount = 100;

    [Header("Prefab Settings")]
    public GameObject prefab; // Prefab to spawn at each point
    public Transform parent;  // Optional parent for spawned objects

    [Header("Distribution Type")]
    public bool usePoissonDisk = false;

    [Header("Density / Clustering")]
    [Range(0f, 1f)]
    [Tooltip("0 = uniform, 1 = highly clustered")]
    public float clustering = 0.25f;

    [Header("Poisson Disk Settings")]
    [Tooltip("Base minimum distance between points")]
    public float basePoissonRadius = 1f;

    [Header("Grounding (Raycast Down)")]
    [Tooltip("Only colliders on these layers will be considered 'ground' (set this to your Terrain/Ground layer).")]
    public LayerMask groundMask = ~0; // default: everything

    [Header("Water Culling (Optional)")]
    public LayerMask waterMask = 0; // set to Water layer
    public bool skipIfUnderwater = true;


    [Tooltip("Raycast starts at (pointXZ + up * raycastHeight). Keep this above your terrain.")]
    public float raycastHeight = 200f;

    [Tooltip("How far down we search for ground from the raycast start.")]
    public float maxDropDistance = 500f;

    [Tooltip("Optional vertical offset after hitting ground (eg. half your prefab height).")]
    public float yOffset = 0f;

    [Tooltip("If true, points that don't hit ground won't spawn anything.")]
    public bool skipIfNoGroundHit = true;

    [Header("Debug")]
    public bool regenerate = false;
    public bool drawGizmos = true;

    public List<Vector2> points = new List<Vector2>();
    private List<GameObject> spawnedObjects = new List<GameObject>();

    void OnValidate()
    {
        if (regenerate)
        {
            regenerate = false;
            GeneratePoints();
            SpawnPrefabs();
        }
    }

    void Start()
    {
        GeneratePoints();
        SpawnPrefabs();
    }

    public void GeneratePoints()
    {
        points.Clear();

        if (usePoissonDisk)
        {
            points = PoissonDiskSampling.GeneratePoints(
                Mathf.Lerp(basePoissonRadius * 1.5f, basePoissonRadius * 0.3f, clustering),
                areaSize,
                30,
                pointCount
            );
        }
        else
        {
            points = GenerateHaltonPoints();
        }
    }

    bool TryGetGroundedPosition(Vector2 p, out Vector3 groundedPos)
    {
        Vector3 worldXZ = new Vector3(p.x, 0f, p.y);

        Vector3 origin = worldXZ + Vector3.up * raycastHeight;
        float distance = raycastHeight + maxDropDistance;

        // 1) Find ground (terrain/land) position
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit groundHit, distance, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundedPos = worldXZ;
            return false;
        }

        // 2) If enabled, check if water surface is above that ground point
        if (skipIfUnderwater && waterMask.value != 0)
        {
            // Cast down but only as far as the ground hit point
            float toGround = groundHit.distance;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit waterHit, toGround, waterMask, QueryTriggerInteraction.Ignore))
            {
                // Water is between origin and ground -> ground is underwater at this XZ
                groundedPos = worldXZ;
                return false;
            }
        }

        groundedPos = groundHit.point + Vector3.up * yOffset;
        return true;
    }



    public void SpawnPrefabs()
    {
        // Clear existing objects
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(obj);
                else
#endif
                    Destroy(obj);
            }
        }
        spawnedObjects.Clear();

        if (prefab == null) return;

        foreach (var p in points)
        {
            if (TryGetGroundedPosition(p, out Vector3 spawnPos))
            {
                GameObject obj = Instantiate(prefab, spawnPos, Quaternion.identity, parent);
                spawnedObjects.Add(obj);
            }
        }

    }

    #region Halton

    List<Vector2> GenerateHaltonPoints()
    {
        List<Vector2> result = new List<Vector2>();

        for (int i = 0; i < pointCount; i++)
        {
            float x = Halton(i + 1, 2);
            float y = Halton(i + 1, 3);

            Vector2 p = new Vector2(x, y);

            // Add clustering via jitter
            float jitterStrength = clustering * 0.5f;
            p += Random.insideUnitCircle * jitterStrength;

            p.x = Mathf.Clamp01(p.x);
            p.y = Mathf.Clamp01(p.y);

            // Scale to area
            p = new Vector2(
                (p.x - 0.5f) * areaSize.x,
                (p.y - 0.5f) * areaSize.y
            );

            result.Add(p);
        }

        return result;
    }

    float Halton(int index, int baseValue)
    {
        float result = 0f;
        float f = 1f / baseValue;

        while (index > 0)
        {
            result += f * (index % baseValue);
            index /= baseValue;
            f /= baseValue;
        }

        return result;
    }

    #endregion

    void OnDrawGizmos()
    {
        if (!drawGizmos || points == null) return;

        // Area box centered at world origin
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(
            Vector3.zero,
            new Vector3(areaSize.x, 0f, areaSize.y)
        );

        foreach (var p in points)
        {
            if (TryGetGroundedPosition(p, out Vector3 grounded))
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(grounded, 0.08f);
            }
            else
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(new Vector3(p.x, 0f, p.y), 0.08f);
            }
        }
    }

}

/// <summary>
/// Simple Poisson Disk Sampling (Bridson-style)
/// </summary>
public static class PoissonDiskSampling
{
    public static List<Vector2> GeneratePoints(
        float radius,
        Vector2 regionSize,
        int rejectionSamples,
        int maxPoints
    )
    {
        float cellSize = radius / Mathf.Sqrt(2);

        int gridWidth = Mathf.CeilToInt(regionSize.x / cellSize);
        int gridHeight = Mathf.CeilToInt(regionSize.y / cellSize);

        int[,] grid = new int[gridWidth, gridHeight];
        List<Vector2> points = new List<Vector2>();
        List<Vector2> spawnPoints = new List<Vector2>();

        spawnPoints.Add(Vector2.zero);

        while (spawnPoints.Count > 0 && points.Count < maxPoints)
        {
            int spawnIndex = Random.Range(0, spawnPoints.Count);
            Vector2 spawnCenter = spawnPoints[spawnIndex];
            bool accepted = false;

            for (int i = 0; i < rejectionSamples; i++)
            {
                float angle = Random.value * Mathf.PI * 2;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 candidate = spawnCenter + dir * Random.Range(radius, 2 * radius);

                if (IsValid(candidate, regionSize, cellSize, radius, points, grid))
                {
                    points.Add(candidate);
                    spawnPoints.Add(candidate);

                    int x = (int)((candidate.x + regionSize.x / 2) / cellSize);
                    int y = (int)((candidate.y + regionSize.y / 2) / cellSize);
                    grid[x, y] = points.Count;

                    accepted = true;
                    break;
                }
            }

            if (!accepted)
            {
                spawnPoints.RemoveAt(spawnIndex);
            }
        }

        return points;
    }

    static bool IsValid(
        Vector2 candidate,
        Vector2 regionSize,
        float cellSize,
        float radius,
        List<Vector2> points,
        int[,] grid
    )
    {
        if (candidate.x < -regionSize.x / 2 || candidate.x > regionSize.x / 2 ||
            candidate.y < -regionSize.y / 2 || candidate.y > regionSize.y / 2)
            return false;

        int cellX = (int)((candidate.x + regionSize.x / 2) / cellSize);
        int cellY = (int)((candidate.y + regionSize.y / 2) / cellSize);

        int searchStartX = Mathf.Max(0, cellX - 2);
        int searchEndX = Mathf.Min(cellX + 2, grid.GetLength(0) - 1);
        int searchStartY = Mathf.Max(0, cellY - 2);
        int searchEndY = Mathf.Min(cellY + 2, grid.GetLength(1) - 1);

        for (int x = searchStartX; x <= searchEndX; x++)
        {
            for (int y = searchStartY; y <= searchEndY; y++)
            {
                int index = grid[x, y] - 1;
                if (index != -1)
                {
                    float sqrDist = (candidate - points[index]).sqrMagnitude;
                    if (sqrDist < radius * radius)
                        return false;
                }
            }
        }

        return true;
    }
}
