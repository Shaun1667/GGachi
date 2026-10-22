using UnityEngine;

/// <summary>
/// 고라니(플레이어) 애니메이션 연결.
///
/// 이동·공격 로직은 PlayerMovement / PlayerAttack이 담당하고,
/// 이 스크립트는 그 상태를 읽어서 Animator 파라미터로만 전달한다.
///  - MoveX, MoveY (float): 캐릭터 기준 이동 방향 → 방향별 걷기 블렌드 트리
///      (0, 1) 앞으로 걷기 / (-1, 0) 왼쪽 걷기 / (1, 0) 오른쪽 걷기 / (0, -1) 뒤로 걷기 / (0, 0) 대기
///  - Attack (trigger) + AttackLeft (bool): 공격이 나간 순간 → 오른손/왼손(좌우 반전) 공격 재생 후 이동 블렌드 트리로 복귀
///  - Jump (trigger): 바닥에서 뛰어오른 순간 → 점프 재생 후 이동 블렌드 트리로 복귀
///  - Roll (trigger): 구르기를 시작한 순간 → 구르기 재생 후 이동 블렌드 트리로 복귀
///  - Die (trigger): 체력이 0이 된 순간 → 쓰러지는 동작(Death From Right) 후 그대로 (게임오버)
/// Player 루트에 붙이고, animator에는 고라니 모델(Visual)의 Animator를 연결한다.
/// </summary>
public class PlayerAnimator : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerAttack attack;

    [Tooltip("방향 값이 목표로 따라가는 속도. 클수록 방향 전환이 빠르다")]
    [SerializeField] private float blendSpeed = 8f;

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int AttackLeftId = Animator.StringToHash("AttackLeft");
    private static readonly int JumpId = Animator.StringToHash("Jump");
    private static readonly int RollId = Animator.StringToHash("Roll");
    private static readonly int DieId = Animator.StringToHash("Die");

    private Vector2 blend;
    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (attack != null) attack.Attacked += OnAttacked;
        if (movement != null) movement.Jumped += OnJumped;
        if (movement != null) movement.Rolled += OnRolled;
        if (health != null) health.Died += OnDied;
    }

    private void OnDisable()
    {
        if (attack != null) attack.Attacked -= OnAttacked;
        if (movement != null) movement.Jumped -= OnJumped;
        if (movement != null) movement.Rolled -= OnRolled;
        if (health != null) health.Died -= OnDied;
    }

    private void OnDied()
    {
        if (animator != null) animator.SetTrigger(DieId);
    }

    private void OnJumped()
    {
        if (animator != null) animator.SetTrigger(JumpId);
    }

    private void OnRolled()
    {
        if (animator != null) animator.SetTrigger(RollId);
    }

    private void Update()
    {
        if (animator == null || movement == null) return;

        // 게임이 시작 전이거나 끝나서 이동 컴포넌트가 꺼져 있으면 멈춘 것으로 본다
        Vector2 target = movement.enabled ? movement.MoveInput : Vector2.zero;

        // 갑자기 튀지 않도록 부드럽게 따라감 (A→D로 바로 바꿔도 중간 자세를 거침)
        blend = Vector2.MoveTowards(blend, target, blendSpeed * Time.deltaTime);
        animator.SetFloat(MoveXId, blend.x);
        animator.SetFloat(MoveYId, blend.y);
    }

    private void OnAttacked(bool left)
    {
        if (animator == null) return;
        animator.SetBool(AttackLeftId, left);
        animator.SetTrigger(AttackId);
    }
}
