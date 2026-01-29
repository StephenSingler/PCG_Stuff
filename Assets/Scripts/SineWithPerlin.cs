using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SineWithPerlin : MonoBehaviour
{
    [Header("Wave Settings")]
    public float amplitude = 1f;
    public float frequency = 1f;
    public float speed = 1f;

    [Header("Perlin Noise Settings")]
    public float noiseAmplitude = 0.5f;   // Height of noise
    public float noiseFrequency = 1f;     // Frequency along x-axis
    public float noiseSpeed = 1f;         // How fast noise evolves over time

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

                // Sine component
                float sineY = amplitude * Mathf.Sin((x * frequency) + Time.time * speed);

                // Perlin noise component (remapped from 0..1 to -0.5..0.5)
                float perlinY = (Mathf.PerlinNoise(x * noiseFrequency, Time.time * noiseSpeed) - 0.5f) * 2f * noiseAmplitude;

                float y = sineY + perlinY;

                lineRenderer.SetPosition(index, new Vector3(x, y, 0));
                index++;
            }
        }
    }
}
