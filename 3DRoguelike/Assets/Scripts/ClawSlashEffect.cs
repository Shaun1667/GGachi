using UnityEngine;

/// <summary>
/// 할퀴기 이펙트: 세 줄이 시작점에서 끝으로 차례로 그어진 뒤 사라진다.
/// 줄마다 피벗(시작점)을 x축 방향으로 늘려서 그리고, 색 알파를 줄여서 지운다.
/// PlayerAttack이 공격이 맞는 순간 생성한다. 왼손 공격은 루트 x 스케일을 뒤집어 좌우 반대로 긋는다.
/// </summary>
public class ClawSlashEffect : MonoBehaviour
{
    [Tooltip("줄마다 하나씩. 각 피벗의 +x 방향으로 줄이 그어진다")]
    [SerializeField] private Transform[] lines;

    [Tooltip("한 줄이 끝까지 그어지는 시간 (초)")]
    [SerializeField] private float drawTime = 0.07f;
    [Tooltip("다음 줄이 늦게 시작하는 시간 (초)")]
    [SerializeField] private float lineDelay = 0.025f;
    [Tooltip("다 그어진 뒤 그대로 보이는 시간 (초)")]
    [SerializeField] private float holdTime = 0.05f;
    [Tooltip("사라지는 데 걸리는 시간 (초)")]
    [SerializeField] private float fadeTime = 0.2f;
    [Tooltip("줄 색. 1보다 크면 블룸으로 빛난다")]
    [ColorUsage(true, true)]
    [SerializeField] private Color color = new Color(3f, 1.1f, 0.8f, 1f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Renderer[][] renderers;
    private MaterialPropertyBlock block;
    private float time;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        renderers = new Renderer[lines.Length][];
        for (int i = 0; i < lines.Length; i++)
        {
            renderers[i] = lines[i].GetComponentsInChildren<Renderer>();
            Apply(i, 0f, 0f);
        }
    }

    private void Update()
    {
        time += Time.deltaTime;
        for (int i = 0; i < lines.Length; i++)
        {
            float t = time - i * lineDelay;
            float draw = Mathf.Clamp01(t / drawTime);
            float alpha = t < 0f ? 0f : 1f - Mathf.Clamp01((t - drawTime - holdTime) / fadeTime);
            Apply(i, 1f - (1f - draw) * (1f - draw), alpha); // 빠르게 그어지다 끝에서 느려짐
        }

        if (time > (lines.Length - 1) * lineDelay + drawTime + holdTime + fadeTime) Destroy(gameObject);
    }

    private void Apply(int i, float draw, float alpha)
    {
        Vector3 s = lines[i].localScale;
        lines[i].localScale = new Vector3(Mathf.Max(draw, 0.001f), s.y, s.z);
        block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, color.a * alpha));
        foreach (Renderer r in renderers[i]) r.SetPropertyBlock(block);
    }
}
