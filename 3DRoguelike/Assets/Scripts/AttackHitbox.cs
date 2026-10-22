using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlayerAttack이 잠깐 생성했다가 지우는 히트박스에 붙는 스크립트.
/// Collider가 Is Trigger로 설정되어 있어야 하고, 맞는 쪽(보스)에 Rigidbody가 있어야
/// 트리거 이벤트가 정상적으로 발생한다 (보스에 Kinematic Rigidbody를 붙여둠).
/// </summary>
[RequireComponent(typeof(Collider))]
public class AttackHitbox : MonoBehaviour
{
    [Tooltip("이 진영의 Health를 가진 대상만 공격 판정")]
    [SerializeField] private Team targetTeam = Team.Enemy;

    [Tooltip("한 번 맞았을 때 들어가는 데미지 (PlayerAttack이 생성 직후 덮어씀)")]
    [SerializeField] private int damage = 10;

    // 한 번의 공격(히트박스 생존 시간) 동안 같은 대상이 중복으로 맞지 않도록 기록
    private readonly HashSet<Health> hitTargets = new HashSet<Health>();

    public void SetDamage(int value)
    {
        damage = value;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 콜라이더가 자식 오브젝트에 있을 수도 있으므로 부모 방향으로 Health를 찾는다
        // (예전 태그 방식은 프로젝트에 "Enemy" 태그가 없으면 CompareTag가 에러를 내서 진영 방식으로 바꿈)
        Health health = other.GetComponentInParent<Health>();
        if (health == null || health.Team != targetTeam)
        {
            return;
        }

        // 이미 이번 공격에서 맞은 대상이면 무시 (한 번 휘두르는 동안 중복 히트 방지)
        if (!hitTargets.Add(health))
        {
            return;
        }

        health.TakeDamage(damage);
    }
}
