using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SineWavePerlinOctaves : MonoBehaviour
{
    [Header("Wave Settings")]
    public float amplitude = 1f;      // Base sine amplitude
    public float frequency = 1f;      // Base sine frequency
    public float speed = 1f;          // Sine animation speed

    [Header("Perlin Noise Settings")]
    public float noiseAmplitude = 0.5f;  // Overall max noise amplitude
    public int octaves = 3;               // Number of noise layers
    public float lacunarity = 2f;         // Frequency multiplier per octave
    public float persistence = 0.5f;      // Amplitude multiplier per octave
    public float baseNoiseFrequency = 1f; // Base noise frequency
    public float noiseSpeed = 1f;         // Animation speed of noise

    [Header("Line Settings")]
    public int numPoints = 10;
    public int samplesPerSegment = 50;
    public float horizontalSpacing = 1f;

    private LineRenderer lineRenderer;
    private int totalSamples;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        totalSamples = (numPoints - 1) * samplesPerSegment;
        lineRenderer.positionCount = totalSamples;
    }

    void Update()
    {
        DrawWave();
    }

    void DrawWave()
    {
        int index = 0;
        for (int i = 0; i < numPoints - 1; i++)
        {
            for (int j = 0; j < samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;
                float x = (i + t) * horizontalSpacing;

                // Base sine component
                float sineY = amplitude * Mathf.Sin((x * frequency) + Time.time * speed);

                // Multi-octave Perlin noise
                float perlinY = 0f;
                float amp = noiseAmplitude;
                float freq = baseNoiseFrequency;

                for (int o = 0; o < octaves; o++)
                {
                    // Unity PerlinNoise returns 0..1, so remap to -0.5..0.5
                    float noise = (Mathf.PerlinNoise(x * freq, Time.time * noiseSpeed) - 0.5f) * 2f;
                    perlinY += noise * amp;

                    freq *= lacunarity;      // Increase frequency for next octave
                    amp *= persistence;      // Decrease amplitude for next octave
                }

                float y = sineY + perlinY;

                lineRenderer.SetPosition(index, new Vector3(x, y, 0));
                index++;
            }
        }
    }
}
