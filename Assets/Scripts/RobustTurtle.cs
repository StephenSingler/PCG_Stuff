using UnityEngine;
using System.Collections.Generic;
using System.Text;

public class RobustTurtle : MonoBehaviour
{
    // =================================================
    // Turtle settings
    // =================================================
    [Header("Turtle Settings")]
    public float step = 1f;
    public float angle = 25f;

    // which symbols should draw forward
    public string drawSymbols = "FGXYAB";

    // =================================================
    // Perlin Random Angles
    // =================================================
    [Header("Noise Angle Settings")]

    [Tooltip("If enabled, yaw and pitch angles are driven by smooth Perlin noise instead of Random.Range. Produces organic, continuous variation.")]
    public bool usePerlinAngles = true;

    [Tooltip("Maximum angle in degrees. Actual angle will vary smoothly between -maxAngle and +maxAngle.")]
    public float maxAngle = 25f;      // degrees, e.g. 25

    [Tooltip("Controls how quickly the noise value changes as the turtle progresses. Smaller = smoother, sweeping turns. Larger = more jittery variation.")]
    public float noiseScale = 0.15f; // smaller = smoother changes, larger = more twitchy

    [Tooltip("Offset applied to the Perlin noise input for angle. Changing this shifts the noise pattern without affecting smoothness.")]
    public float noiseOffsetAngle = 10f;
    public int noiseSeed = 12345;    // change this for different “styles”



    // =================================================
    // L-System grammar
    // =================================================
    [Header("Grammar")]
    [TextArea] public List<string> axioms = new() { "F" };

    [System.Serializable]
    public class Rule
    {
        public char symbol;
        [TextArea] public string replacement;
    }

    public List<Rule> rules = new();
    public int iterations = 4;
    public bool pickRandomAxiom = false;

    // =================================================
    // Rendering
    // =================================================
    [Header("Line Settings")]
    public Material lineMaterial;
    public float lineWidth = 0.05f;
    public bool useMeshes = false;
    public GameObject segmentPrefab;

    [Header("Depth Coloring")]
    public Gradient depthGradient;
    public string colorProperty = "_BaseColor"; // URP Lit

    MaterialPropertyBlock mpb;


    LineRenderer line;
    List<Vector3> points = new();

    Dictionary<char, string> ruleMap;

    // =================================================
    // Start
    // =================================================
    void Start()
    {
        SetupLineRenderer();
        mpb = new MaterialPropertyBlock();
        BuildRuleMap();

        string seed = ChooseAxiom();
        string result = Generate(seed);
        Debug.Log(result);

        Interpret(result);

        if (!useMeshes)
            Draw();
    }

    // =================================================
    // Build dictionary
    // =================================================
    void BuildRuleMap()
    {
        ruleMap = new Dictionary<char, string>();

        foreach (var r in rules)
            ruleMap[r.symbol] = r.replacement;
    }

    // =================================================
    // Pick axiom
    // =================================================
    string ChooseAxiom()
    {
        if (axioms.Count == 0) return "";

        if (pickRandomAxiom)
            return axioms[Random.Range(0, axioms.Count)];

        return axioms[0];
    }

    // =================================================
    // Rewrite string
    // =================================================
    string Generate(string current)
    {
        for (int i = 0; i < iterations; i++)
        {
            StringBuilder next = new();

            foreach (char c in current)
            {
                if (ruleMap.ContainsKey(c))
                    next.Append(ruleMap[c]);
                else
                    next.Append(c);
            }

            current = next.ToString();
        }

        return current;
    }

    // =================================================
    // Turtle interpreter
    // =================================================
    void Interpret(string commands)
    {
        // Store depth too so pop restores it correctly
        Stack<(Vector3 pos, Quaternion rot, int depth)> stack = new();

        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        float n = noiseSeed * 0.001f; // base offset so seed matters
        int drawStepIndex = 0;

        points.Clear();
        points.Add(pos);

        int currentDepth = 0;
        int maxDepth = Mathf.Max(1, GetMaxBranchDepth(commands)); // avoid divide-by-zero

        foreach (char c in commands)
        {
            // draw symbols (F,G,X,Y,A,B...)
            if (drawSymbols.Contains(c))
            {
                Vector3 newPos = pos + rot * Vector3.up * step;

                if (useMeshes && segmentPrefab != null)
                {
                    float j = currentDepth / (float)maxDepth; // 0..1
                    Color col = (depthGradient != null) ? depthGradient.Evaluate(j) : Color.white;
                    SpawnSegment(pos, newPos, col);
                    drawStepIndex++;
                }

                points.Add(newPos);
                pos = newPos;
                continue;
            }

            float t = n + (drawStepIndex * noiseScale) * noiseScale;

            // angle noise
            float angleAmt = usePerlinAngles
                ? PerlinSigned(t, noiseOffsetAngle) * maxAngle
                : Random.Range(-maxAngle, maxAngle);

            switch (c)
            {
                case '+':
                    rot *= Quaternion.Euler(0, 0, angleAmt); 
                    break;
                case '-': 
                    rot *= Quaternion.Euler(0, 0, -angleAmt); 
                    break;
                case '^': 
                    rot *= Quaternion.Euler(angleAmt, 0, 0); 
                    break;
                case '&': 
                    rot *= Quaternion.Euler(-angleAmt, 0, 0); 
                    break;

                case '[':
                    stack.Push((pos, rot, currentDepth));
                    currentDepth++;
                    break;

                case ']':
                    currentDepth = Mathf.Max(0, currentDepth - 1);
                    if (stack.Count > 0)
                    {
                        (pos, rot, currentDepth) = stack.Pop();
                    }
                    break;
            }
        }
    }


    // =================================================
    // Rendering
    // =================================================
    void SetupLineRenderer()
    {
        line = gameObject.AddComponent<LineRenderer>();
        line.material = lineMaterial;
        line.widthMultiplier = lineWidth;
        line.useWorldSpace = true;
    }

    void Draw()
    {
        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());
    }

    void SpawnSegment(Vector3 a, Vector3 b, Color col)
    {
        GameObject seg = Instantiate(segmentPrefab, transform);

        seg.transform.position = (a + b) * 0.5f;
        seg.transform.up = (b - a);
        seg.transform.localScale =
            new Vector3(lineWidth, (b - a).magnitude * 0.5f, lineWidth);

        // Persist color into prefab by storing it on a component
        var sc = seg.GetComponent<SegmentColor>();
        if (sc == null) sc = seg.AddComponent<SegmentColor>();

        sc.color = col;
        sc.colorProperty = "_BaseColor"; // URP Lit
        sc.Apply(); // apply now so you see it immediately
    }



    // =================================================
    // Depth coloring
    // =================================================
    int GetMaxBranchDepth(string commands)
    {
        int depth = 0;
        int max = 0;

        foreach (char c in commands)
        {
            if (c == '[')
            {
                depth++;
                if (depth > max) max = depth;
            }
            else if (c == ']')
            {
                depth = Mathf.Max(0, depth - 1);
            }
        }
        return max;
    }

    // =================================================
    // Perlin Random Angles
    // =================================================
    float PerlinSigned(float x, float y)
    {
        return Mathf.PerlinNoise(x, y) * 2f - 1f;
    }

}
