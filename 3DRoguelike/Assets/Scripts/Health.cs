using System;
using UnityEngine;

/// <summary>
/// 공격 판정에서 아군/적을 구분하기 위한 진영.
/// </summary>
public enum Team
{
    Player,
    Enemy
}

/// <summary>
/// 플레이어와 보스가 함께 쓰는 체력 컴포넌트.
///
/// 데미지 계산은 이 스크립트 한 곳에서만 하고,
/// UI / GameManager / 피격 연출(HitFlash)은 아래 이벤트를 구독해서 반응한다.
/// 이렇게 하면 "누가 체력을 깎았는지"와 "깎였을 때 무엇을 보여줄지"가 서로 몰라도 된다.
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField] private Team team = Team.Enemy;
    [SerializeField] private int maxHealth = 100;

    [Tooltip("한 번 맞은 뒤 다음 피격까지 무적 시간(초). 0이면 무적 없음 (플레이어 연속 피격 방지용)")]
    [SerializeField] private float invincibleTimeAfterHit = 0f;

    public Team Team => team;
    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;
    public bool IsInvincible => forcedInvincible || Time.time < invincibleUntil;

    /// <summary>(현재 체력, 최대 체력) — 체력바 갱신용</summary>
    public event Action<int, int> HealthChanged;

    /// <summary>실제로 들어간 데미지 양 — 피격 연출, 피격 횟수 집계용</summary>
    public event Action<int> Damaged;

    /// <summary>체력이 0이 된 순간 한 번만 호출</summary>
    public event Action Died;

    private float invincibleUntil;
    private bool forcedInvincible;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    /// <summary>
    /// 데미지를 준다. 죽었거나 무적이면 무시하고 false를 반환한다.
    /// </summary>
    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead || IsInvincible)
        {
            return false;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        invincibleUntil = Time.time + invincibleTimeAfterHit;

        Damaged?.Invoke(amount);
        HealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth == 0)
        {
            Died?.Invoke();
        }
        return true;
    }

    /// <summary>
    /// 구르기처럼 "일정 구간 동안 무조건 무적"이 필요할 때 켜고 끈다.
    /// </summary>
    public void SetForcedInvincible(bool value)
    {
        forcedInvincible = value;
    }
}
