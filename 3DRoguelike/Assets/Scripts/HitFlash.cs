using UnityEngine;

/// <summary>
/// 맞았을 때 몸 색을 잠깐 붉게 바꾸는 피격 연출.
///
/// 재질을 직접 바꾸면 재질 에셋이 복제되거나 수정되므로,
/// MaterialPropertyBlock으로 "이 렌더러에만" 색을 덮어쓴다.
/// </summary>
[RequireComponent(typeof(Health))]
public class HitFlash : MonoBehaviour
{
    [SerializeField] private Color flashColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] private float flashTime = 0.12f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Health health;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private float flashUntil;
    private bool flashing;

    private void Awake()
    {
        health = GetComponent<Health>();
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        health.Damaged += OnDamaged;
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
        }
    }

    private void OnDamaged(int amount) => Flash();

    /// <summary>데미지 없이 번쩍임만 보여준다 (등장 연출에서 할퀴기에 맞는 장면 등)</summary>
    public void Flash()
    {
        // 결과 화면 등에서 시간이 멈춰도 원래 색으로 돌아오도록 unscaledTime 사용
        flashUntil = Time.unscaledTime + flashTime;
        flashing = true;

        foreach (Renderer r in renderers)
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, flashColor);
            r.SetPropertyBlock(block);
        }
    }

    private void Update()
    {
        if (flashing && Time.unscaledTime >= flashUntil)
        {
            flashing = false;
            foreach (Renderer r in renderers)
            {
                r.SetPropertyBlock(null); // 덮어쓴 색을 지워 원래 재질 색으로 복구
            }
        }
    }
}
