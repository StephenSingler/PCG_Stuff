using UnityEngine;

[ExecuteAlways]
public class SegmentColor : MonoBehaviour
{
    [Tooltip("Saved color for this segment (serialized into prefab/scene).")]
    public Color color = Color.white;

    [Tooltip("URP Lit uses _BaseColor. If you use a different shader, change this.")]
    public string colorProperty = "_BaseColor";

    static MaterialPropertyBlock _mpb;

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    public void Apply()
    {
        var r = GetComponentInChildren<Renderer>();
        if (!r) return;

        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor(colorProperty, color);
        r.SetPropertyBlock(_mpb);
    }
}
