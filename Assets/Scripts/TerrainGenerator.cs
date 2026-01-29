using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
public int depth = 20;

public int width = 256;
public int height = 256;

public float scale = 20f;

public float offsetX = 100f;
public float offsetY = 100f;

public float speed = 10.0f;


void Start()
    {
            offsetX = Random.Range(0, 9999f);
            offsetY = Random.Range(0, 9999f);
    }
void Update()
    {
        Terrain terrain = GetComponent<Terrain>();
        terrain.terrainData = GenerateTerrain(terrain.terrainData);

        //to animate the terrain offset over time
        offsetX += Time.deltaTime * speed;
    }
TerrainData GenerateTerrain (TerrainData terrainData)
    {
        terrainData.heightmapResolution = width +1;

        terrainData.size = new Vector3(width, depth, height);
        terrainData.SetHeights(0, 0, GenerateHeights());
        return terrainData;
    }
float [,] GenerateHeights()
    {
        float [,] heights = new float[width, height];
        for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    heights[x, y] = CalculateHeight(x, y);//some perlin noise value
                }

            }
    return heights;
    }
float CalculateHeight(int x, int y)
    {
        float xCoord = (float)x / width * scale + offsetX;
        float yCoord = (float)y / height * scale + offsetY;

        return Mathf.PerlinNoise(xCoord, yCoord);
    }

}

