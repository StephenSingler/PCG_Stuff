using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Terrain))]
public class BasicPerlinTerrain : MonoBehaviour
{
    [Header("Manual Generation")]
    [Tooltip("Toggle this ON to regenerate once in the editor. It will auto-reset to OFF.")]
    public bool regenerate = false;

    [Header("Terrain Resolution")]
    [Range(33, 1025)]
    public int resolution = 513;

    [Header("Noise Settings")]
    public float scale = 20f;
    [Range(1, 8)]
    public int octaves = 4;
    [Range(0.01f, 1f)]
    public float persistence = 0.5f;
    [Range(1f, 4f)]
    public float lacunarity = 2f;

    [Header("Height Settings")]
    [Range(0f, 1f)]
    public float heightScale = 0.2f;

    private Terrain terrain;
    private TerrainData terrainData;

    void OnValidate()
    {
        // Don't do anything automatically while playing.
        if (Application.isPlaying) return;

        // Only regenerate when explicitly requested.
        if (!regenerate) return;

        regenerate = false;

        Initialize();
        GenerateTerrain();
    }

    void Initialize()
    {
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;

        // Ensure valid resolution (safe since you said you won't change it after sculpting).
        resolution = Mathf.ClosestPowerOfTwo(resolution - 1) + 1;
        terrainData.heightmapResolution = resolution;
    }

    void GenerateTerrain()
    {
        float[,] heights = new float[resolution, resolution];

        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                float amplitude = 1f;
                float frequency = 1f;
                float noiseHeight = 0f;

                for (int i = 0; i < octaves; i++)
                {
                    float nx = x / (float)(resolution - 1);
                    float nz = z / (float)(resolution - 1);

                    float sampleX = nx * scale * frequency;
                    float sampleZ = nz * scale * frequency;

                    float perlin = Mathf.PerlinNoise(sampleX, sampleZ);
                    noiseHeight += perlin * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                heights[z, x] = Mathf.Clamp01(noiseHeight * heightScale);
            }
        }

        terrainData.SetHeights(0, 0, heights);
    }
}
