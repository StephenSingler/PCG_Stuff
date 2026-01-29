using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BasicSineWave : MonoBehaviour
{
    [Header("Wave Settings")]
    public float amplitude = 1f;           // Vertical scale
    public float frequency = 1f;           // Oscillations per unit
    public float speed = 1f;               // Animation speed
    public int numPoints = 10;             // Number of gradient points
    public int samplesPerSegment = 50;     // Points between gradient points
    public float horizontalSpacing = 1f;   // Distance between gradient points

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
        DrawSineWave();
    }

    void DrawSineWave()
    {
        int index = 0;

        for (int i = 0; i < numPoints - 1; i++)
        {
            for (int j = 0; j < samplesPerSegment; j++)
            {
                float t = (float)j / samplesPerSegment;
                float x = (i + t) * horizontalSpacing;
                float y = amplitude * Mathf.Sin((x * frequency) + Time.time * speed);

                lineRenderer.SetPosition(index, new Vector3(x, y, 0));
                index++;
            }
        }
    }
}
