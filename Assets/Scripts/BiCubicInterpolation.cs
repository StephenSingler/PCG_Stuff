using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class BicubicInterpolation : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int terrainResolution = 513; // Must be 2^n + 1
    public float terrainSize = 50f;
    public float heightScale = 5f;

    [Header("Noise Settings")]
    public int noiseResolution = 64; // Low-res grid for interpolation
    public float noiseScale = 1f;

    private Terrain terrain;

    void Start()
    {
        terrain = GetComponent<Terrain>();
        GenerateTerrain();
    }

    void GenerateTerrain()
    {
        float[,] lowRes = GenerateLowResNoise(noiseResolution);
        float[,] highRes = BicubicUpsample(lowRes, terrainResolution);

        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = terrainResolution;
        terrainData.size = new Vector3(terrainSize, heightScale, terrainSize);
        terrainData.SetHeights(0, 0, highRes);

        terrain.terrainData = terrainData;
    }

    float[,] GenerateLowResNoise(int res)
    {
        float[,] noise = new float[res, res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                noise[x, y] = Mathf.PerlinNoise(x * noiseScale / res, y * noiseScale / res);
            }
        }
        return noise;
    }

    // Bicubic interpolation
    float[,] BicubicUpsample(float[,] source, int targetRes)
    {
        int srcRes = source.GetLength(0);
        float[,] result = new float[targetRes, targetRes];
        float scale = (float)(srcRes - 1) / (targetRes - 1);

        for (int y = 0; y < targetRes; y++)
        {
            for (int x = 0; x < targetRes; x++)
            {
                float gx = x * scale;
                float gy = y * scale;

                int gxi = Mathf.FloorToInt(gx);
                int gyi = Mathf.FloorToInt(gy);

                float dx = gx - gxi;
                float dy = gy - gyi;

                // Sample 4x4 neighborhood
                float[] arr = new float[4];
                for (int m = -1; m <= 2; m++)
                {
                    float[] col = new float[4];
                    for (int n = -1; n <= 2; n++)
                    {
                        int ix = Mathf.Clamp(gxi + n, 0, srcRes - 1);
                        int iy = Mathf.Clamp(gyi + m, 0, srcRes - 1);
                        col[n + 1] = source[ix, iy];
                    }
                    arr[m + 1] = CubicInterpolate(col[0], col[1], col[2], col[3], dx);
                }
                result[x, y] = CubicInterpolate(arr[0], arr[1], arr[2], arr[3], dy);
            }
        }
        return result;
    }

    // Cubic interpolation function
    float CubicInterpolate(float v0, float v1, float v2, float v3, float t)
    {
        float P = (v3 - v2) - (v0 - v1);
        float Q = (v0 - v1) - P;
        float R = v2 - v0;
        float S = v1;
        return P * t * t * t + Q * t * t + R * t + S;
    }
}
