using UnityEngine;

/// <summary>
/// 보스가 날리는 깃털 투사체.
///
/// 물리 트리거 대신 "목표와의 거리"로 맞았는지 판정한다.
/// 플레이어는 CharacterController라 트리거 조합이 헷갈리기 쉬운데,
/// 거리 판정은 콜라이더 설정과 상관없이 항상 같은 결과가 나와서 디버깅이 쉽다.
/// </summary>
public class BossProjectile : MonoBehaviour
{
    [SerializeField] private float speed = 18f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float lifetime = 4f;

    [Tooltip("목표 중심과 이 거리 안으로 들어오면 명중")]
    [SerializeField] private float hitRadius = 0.9f;

    [Tooltip("목표 Transform 기준으로 몸통 중심이 얼마나 위에 있는지")]
    [SerializeField] private float targetCenterHeight = 0.8f;

    private Health target;
    private float dieTime;

    /// <summary>보스가 생성 직후 호출해서 맞힐 대상과 속도를 넘겨준다.</summary>
    public void Launch(Health targetHealth, float launchSpeed)
    {
        target = targetHealth;
        speed = launchSpeed;
        dieTime = Time.time + lifetime;
    }

    private void Update()
    {
        transform.position += transform.forward * (speed * Time.deltaTime);

        if (target != null && !target.IsDead)
        {
            Vector3 center = target.transform.position + Vector3.up * targetCenterHeight;
            if ((center - transform.position).sqrMagnitude <= hitRadius * hitRadius)
            {
                // 구르기 무적 중이면 TakeDamage가 false를 반환 -> 깃털은 그대로 통과한다
                if (target.TakeDamage(damage))
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        if (Time.time >= dieTime)
        {
            Destroy(gameObject);
        }
    }
}
