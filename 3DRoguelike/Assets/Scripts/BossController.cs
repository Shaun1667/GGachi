using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 까치 보스의 행동(패턴) 스크립트.
///
/// [흐름]
/// StartFight() → FightLoop 코루틴이 "대기 → 패턴 하나 → 대기 → 패턴 하나 ..."를 반복한다.
/// 패턴은 돌진 / 깃털 날리기 / 내려찍기 세 가지이고, 같은 패턴이 연속으로 나오지 않게 고른다.
/// 체력이 phase2HealthRatio 아래로 떨어지면 2페이즈가 되어 대기 시간이 줄고 패턴이 강해진다.
///
/// [공정한 패턴을 위한 원칙]
/// 모든 공격은 시작 전에 예고(바닥 표시 / 준비 동작)를 보여주고,
/// 예고가 나온 뒤에는 목표 위치를 바꾸지 않는다. 그래서 플레이어가 보고 피할 수 있다.
///
/// 보스는 Kinematic Rigidbody라서 물리로 밀리지 않고, 위치는 이 스크립트가 직접 옮긴다.
/// </summary>
[RequireComponent(typeof(Health))]
public class BossController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Health playerHealth;

    [Tooltip("보스가 벗어나지 않을 원형 경기장의 중심")]
    [SerializeField] private Transform arenaCenter;
    [SerializeField] private float arenaRadius = 28f;

    [Tooltip("(선택) 보스 몸통의 Animator (Humanoid). 없으면 무시")]
    [SerializeField] private Animator animator;

    [Tooltip("(선택) 가슴 뼈에 붙은 날개의 Animator (Generic). 몸통과 같은 신호를 받는다")]
    [SerializeField] private Animator wingAnimator;

    [Header("기본")]
    [SerializeField] private float turnSpeed = 140f;
    [SerializeField] private Vector2 idleTimePhase1 = new Vector2(1.2f, 2.0f);
    [SerializeField] private Vector2 idleTimePhase2 = new Vector2(0.6f, 1.1f);
    [SerializeField, Range(0f, 1f)] private float phase2HealthRatio = 0.5f;

    [Tooltip("보스 몸통 판정 반경 (수평 거리). 이 안에 플레이어가 있으면 몸통 박치기 데미지")]
    [SerializeField] private float bodyRadius = 3f;
    [SerializeField] private int contactDamage = 1;

    [Header("패턴 1: 돌진")]
    [SerializeField] private float chargeWindup = 0.9f;
    [SerializeField] private float chargeSpeed = 30f;
    [SerializeField] private float chargeDistance = 40f;
    [SerializeField] private int chargeDamage = 1;
    [SerializeField] private float chargeLineWidth = 5f;

    [Header("패턴 2: 깃털 날리기")]
    [SerializeField] private BossProjectile featherPrefab;
    [SerializeField] private float featherWindup = 0.6f;
    [SerializeField] private int featherCountPhase1 = 7;
    [SerializeField] private int featherCountPhase2 = 11;
    [SerializeField] private float featherSpreadAngle = 70f;
    [SerializeField] private int volleysPhase1 = 1;
    [SerializeField] private int volleysPhase2 = 2;
    [SerializeField] private float volleyInterval = 0.55f;
    [SerializeField] private float featherSpeed = 18f;
    [SerializeField] private float featherSpawnHeight = 4f;

    [Header("패턴 3: 내려찍기")]
    [SerializeField] private float slamRiseTime = 0.6f;
    [SerializeField] private float slamHeight = 12f;
    [SerializeField] private float slamTrackTime = 1.0f;
    [Tooltip("공중에서 추적을 멈추고 낙하 지점을 고정하는 시간 (이 동안 예고 원이 멈춰 있음)")]
    [SerializeField] private float slamLockTime = 0.45f;
    [SerializeField] private float slamFallTime = 0.22f;
    [SerializeField] private float slamTrackSpeed = 14f;
    [SerializeField] private float slamRadius = 8f;
    [SerializeField] private int slamDamage = 2;

    [Header("예고 표시 프리팹")]
    [SerializeField] private GameObject circleTelegraphPrefab;
    [SerializeField] private GameObject lineTelegraphPrefab;

    // 애니메이터 신호 (GGachi.controller: Dive → SlamDive 자세, Land → Idle 호버링)
    private static readonly int DiveId = Animator.StringToHash("Dive");
    private static readonly int LandId = Animator.StringToHash("Land");
    private static readonly int ChargeReadyId = Animator.StringToHash("ChargeReady");
    private static readonly int ChargeId = Animator.StringToHash("Charge");
    private static readonly int DieId = Animator.StringToHash("Die"); // 몸통만: Death (쓰러짐)

    public bool IsPhase2 { get; private set; }

    /// <summary>2페이즈 진입 순간 (UI 배너용)</summary>
    public event Action PhaseChanged;

    /// <summary>내려찍기 착지 순간 (카메라 흔들림용)</summary>
    public event Action Slammed;

    private Health health;
    private Coroutine fightLoop;
    private GameObject activeTelegraph;
    private float groundY;
    private int lastPattern = -1;

    private void Awake()
    {
        health = GetComponent<Health>();
        groundY = transform.position.y;
        health.HealthChanged += OnHealthChanged;
        health.Died += OnDied;

        // 비어 있으면 경기장 경계 계산이 모두 꺼져 돌진·내려찍기가 경기장 밖으로 나간다
        if (arenaCenter == null) Debug.LogWarning("BossController: arenaCenter가 비어 있어 경기장 경계가 적용되지 않습니다.", this);
    }

    private void OnDestroy()
    {
        health.HealthChanged -= OnHealthChanged;
        health.Died -= OnDied;
    }

    public void StartFight()
    {
        if (fightLoop == null && !health.IsDead)
        {
            fightLoop = StartCoroutine(FightLoop());
        }
    }

    public void StopFight()
    {
        if (fightLoop != null)
        {
            StopCoroutine(fightLoop);
            fightLoop = null;
        }
        ClearTelegraph();

        // 내려찍기 도중에 전투가 끝나도 자세가 남지 않게 신호를 지우고 호버링으로 돌려놓는다
        foreach (Animator a in Animators())
        {
            a.ResetTrigger(DiveId);
            a.ResetTrigger(LandId);
            a.ResetTrigger(ChargeReadyId);
            a.ResetTrigger(ChargeId);
            if (!health.IsDead) a.CrossFade("Idle", 0.3f);
        }
    }

    // ───────────────────────── 메인 루프 ─────────────────────────

    private IEnumerator FightLoop()
    {
        while (true)
        {
            Vector2 idle = IsPhase2 ? idleTimePhase2 : idleTimePhase1;
            yield return Idle(UnityEngine.Random.Range(idle.x, idle.y));

            // 같은 패턴 연속 방지: 직전 패턴을 뺀 나머지 둘 중에서 고른다
            int pattern = UnityEngine.Random.Range(0, 2);
            if (pattern >= lastPattern && lastPattern >= 0) pattern++;
            if (lastPattern < 0) pattern = UnityEngine.Random.Range(0, 3);
            lastPattern = pattern;

            switch (pattern)
            {
                case 0: yield return Charge(); break;
                case 1: yield return FeatherVolley(); break;
                default: yield return Slam(); break;
            }
        }
    }

    private IEnumerator Idle(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            FacePlayer(turnSpeed);
            CheckContact(contactDamage, bodyRadius);
            t += Time.deltaTime;
            yield return null;
        }
    }

    // ───────────────────────── 패턴 1: 돌진 ─────────────────────────

    private IEnumerator Charge()
    {
        Vector3 start = transform.position;
        Vector3 dir = FlatDirectionToPlayer();
        // 끝점을 경기장 중심 쪽으로 잘라내면(ClampToArena) 예고선과 다른 방향으로 꺾여 날아간다.
        // 예고선 위에서 경기장 경계와 만나는 지점까지만 가도록 거리를 줄인다.
        float length = DistanceToArenaEdge(start, dir, chargeDistance);
        Vector3 end = start + dir * length;

        // 예고: 돌진 경로를 바닥에 길게 표시 (이후 경로는 바뀌지 않음)
        transform.rotation = Quaternion.LookRotation(dir);
        ShowTelegraph(lineTelegraphPrefab,
            new Vector3((start.x + end.x) * 0.5f, groundY, (start.z + end.z) * 0.5f),
            Quaternion.LookRotation(dir),
            new Vector3(chargeLineWidth, 1f, length));

        SetAnimTrigger(ChargeReadyId); // 땅에 웅크린 출발 자세
        yield return Shake(chargeWindup, 0.15f);
        ClearTelegraph();
        SetAnimTrigger(ChargeId);      // 박차고 날아감

        float speed = IsPhase2 ? chargeSpeed * 1.25f : chargeSpeed;
        while (Vector3.Distance(Flat(transform.position), Flat(end)) > 0.1f)
        {
            Vector3 next = Vector3.MoveTowards(Flat(transform.position), Flat(end), speed * Time.deltaTime);
            transform.position = new Vector3(next.x, groundY, next.z);
            CheckContact(chargeDamage, bodyRadius * 1.1f);
            yield return null;
        }
        SetAnimTrigger(LandId);        // 도착하면 호버링으로 복귀

        yield return Idle(0.4f); // 돌진 후 빈틈 = 플레이어가 때릴 기회
    }

    // ───────────────────────── 패턴 2: 깃털 날리기 ─────────────────────────

    private IEnumerator FeatherVolley()
    {
        // 준비 동작: 플레이어를 바라보며 몸을 부풀렸다가 날린다
        float t = 0f;
        Vector3 baseScale = transform.localScale;
        while (t < featherWindup)
        {
            FacePlayer(turnSpeed * 2f);
            transform.localScale = baseScale * (1f + 0.06f * Mathf.Sin(t / featherWindup * Mathf.PI));
            t += Time.deltaTime;
            yield return null;
        }
        transform.localScale = baseScale;

        int volleys = IsPhase2 ? volleysPhase2 : volleysPhase1;
        int count = IsPhase2 ? featherCountPhase2 : featherCountPhase1;

        for (int v = 0; v < volleys; v++)
        {
            FireFan(count, v % 2 == 1);
            yield return Idle(volleyInterval);
        }
        yield return Idle(0.3f);
    }

    /// <summary>
    /// 플레이어를 향해 부채꼴로 깃털을 날린다.
    /// offsetHalf가 true면 각도를 반 칸씩 엇갈려서 두 번째 연사가 첫 번째의 빈틈을 메운다.
    /// </summary>
    private void FireFan(int count, bool offsetHalf)
    {
        if (featherPrefab == null || playerHealth == null) return;

        Vector3 spawn = transform.position + Vector3.up * featherSpawnHeight + FlatDirectionToPlayer() * bodyRadius;
        Vector3 aim = (playerHealth.transform.position + Vector3.up * 0.8f - spawn).normalized;

        float step = count > 1 ? featherSpreadAngle / (count - 1) : 0f;
        float startAngle = -featherSpreadAngle * 0.5f + (offsetHalf ? step * 0.5f : 0f);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(startAngle + step * i, Vector3.up) * aim;
            BossProjectile feather = Instantiate(featherPrefab, spawn, Quaternion.LookRotation(dir));
            feather.Launch(playerHealth, featherSpeed);
        }
    }

    // ───────────────────────── 패턴 3: 내려찍기 ─────────────────────────

    private IEnumerator Slam()
    {
        float airY = groundY + slamHeight;

        // 1) 상승
        Vector3 from = transform.position;
        for (float t = 0f; t < slamRiseTime; t += Time.deltaTime)
        {
            float k = t / slamRiseTime;
            transform.position = new Vector3(from.x, Mathf.Lerp(groundY, airY, k * (2f - k)), from.z);
            yield return null;
        }

        // 2) 공중에서 플레이어 머리 위를 따라다님 + 바닥에 예고 원 표시
        ShowTelegraph(circleTelegraphPrefab, new Vector3(transform.position.x, groundY, transform.position.z),
            Quaternion.identity, new Vector3(slamRadius * 2f, 1f, slamRadius * 2f));

        for (float t = 0f; t < slamTrackTime; t += Time.deltaTime)
        {
            Vector3 target = ClampToArena(Flat(playerHealth.transform.position));
            Vector3 next = Vector3.MoveTowards(Flat(transform.position), target, slamTrackSpeed * Time.deltaTime);
            transform.position = new Vector3(next.x, airY, next.z);
            MoveTelegraph(new Vector3(next.x, groundY, next.z));
            yield return null;
        }

        // 3) 낙하 지점 고정 (예고 원이 멈추는 시간 = 피할 시간)
        //    이때 몸을 곧게 세우고 발부터 내리꽂는 자세로 바뀐다
        SetAnimTrigger(DiveId);
        yield return Shake(slamLockTime, 0.25f);

        // 4) 낙하
        Vector3 top = transform.position;
        for (float t = 0f; t < slamFallTime; t += Time.deltaTime)
        {
            float k = t / slamFallTime;
            transform.position = new Vector3(top.x, Mathf.Lerp(airY, groundY, k * k), top.z);
            yield return null;
        }
        transform.position = new Vector3(top.x, groundY, top.z);
        ClearTelegraph();

        // 5) 착지 판정: 예고 원 안 + 땅에 붙어 있으면 맞는다 (점프로 높이 떠 있으면 피함)
        if (playerHealth != null)
        {
            Vector3 p = playerHealth.transform.position;
            bool inside = Vector3.Distance(Flat(p), Flat(transform.position)) <= slamRadius;
            bool grounded = p.y <= groundY + 1.0f;
            if (inside && grounded)
            {
                playerHealth.TakeDamage(slamDamage);
            }
        }
        Slammed?.Invoke();
        SetAnimTrigger(LandId); // 착지 후 날개를 펴며 호버링 자세로 복귀

        yield return Idle(IsPhase2 ? 0.5f : 0.8f); // 착지 후 빈틈
    }

    // ───────────────────────── 공통 도우미 ─────────────────────────

    private void OnHealthChanged(int current, int max)
    {
        if (!IsPhase2 && current > 0 && current <= max * phase2HealthRatio)
        {
            IsPhase2 = true;
            PhaseChanged?.Invoke();
        }
    }

    private void OnDied()
    {
        StopFight();
        StartCoroutine(DeathRoutine());
    }

    /// <summary>
    /// 쓰러지는 연출: 몸통은 쓰러지는 동작(Death From Right)을 재생하고, 가슴 뼈에 붙은 날개는 그 자리에서 멈춘 채 몸을 따라간다.
    /// 내려찍기 도중처럼 공중에서 죽으면 바닥으로 내려앉는다.
    /// 컨트롤러에 Die가 없으면(Add Death Animation 메뉴를 안 돌린 경우) 예전처럼 옆으로 기울어진다.
    /// </summary>
    private IEnumerator DeathRoutine()
    {
        bool hasDeathAnim = animator != null && HasParameter(animator, DieId);
        if (hasDeathAnim) animator.SetTrigger(DieId);
        else if (animator != null) animator.speed = 0f;
        if (wingAnimator != null) wingAnimator.speed = 0f;

        Quaternion startRot = transform.rotation;
        Quaternion endRot = hasDeathAnim ? startRot : startRot * Quaternion.Euler(0f, 0f, 80f);
        Vector3 startPos = transform.position;
        Vector3 endPos = new Vector3(startPos.x, groundY, startPos.z);
        float duration = hasDeathAnim ? 0.5f : 1.2f;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            transform.rotation = Quaternion.Slerp(startRot, endRot, k * k);
            transform.position = Vector3.Lerp(startPos, endPos, k);
            yield return null;
        }
        transform.SetPositionAndRotation(endPos, endRot);
    }

    private static bool HasParameter(Animator a, int id)
    {
        foreach (AnimatorControllerParameter p in a.parameters)
        {
            if (p.nameHash == id) return true;
        }
        return false;
    }

    private void SetAnimTrigger(int id)
    {
        foreach (Animator a in Animators()) a.SetTrigger(id);
    }

    /// <summary>켜져 있는 몸통·날개 Animator (둘 다 같은 Idle/SlamDive 구조)</summary>
    private IEnumerable<Animator> Animators()
    {
        if (animator != null && animator.isActiveAndEnabled) yield return animator;
        if (wingAnimator != null && wingAnimator.isActiveAndEnabled) yield return wingAnimator;
    }

    private void FacePlayer(float degreesPerSecond)
    {
        Vector3 dir = FlatDirectionToPlayer();
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * Time.deltaTime);
    }

    /// <summary>플레이어가 몸통 반경 안에 있으면 데미지 (무적 시간 덕분에 매 프레임 맞지 않음)</summary>
    private void CheckContact(int damage, float radius)
    {
        if (playerHealth == null) return;
        Vector3 p = playerHealth.transform.position;
        if (p.y > transform.position.y + 6f) return; // 보스 머리 위로 높이 뜬 경우는 제외
        if (Vector3.Distance(Flat(p), Flat(transform.position)) <= radius)
        {
            playerHealth.TakeDamage(damage);
        }
    }

    /// <summary>준비 동작: 제자리에서 몸을 떨며 기다린다.</summary>
    private IEnumerator Shake(float duration, float amount)
    {
        Vector3 origin = transform.position;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * amount;
            transform.position = origin + new Vector3(r.x, 0f, r.y);
            yield return null;
        }
        transform.position = origin;
    }

    private Vector3 FlatDirectionToPlayer()
    {
        if (playerHealth == null) return transform.forward;
        Vector3 d = Flat(playerHealth.transform.position) - Flat(transform.position);
        return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
    }

    /// <summary>경기장 원 밖으로 나가지 않도록 수평 위치를 잘라낸다 (보스 몸통 반경만큼 여유)</summary>
    private Vector3 ClampToArena(Vector3 p)
    {
        if (arenaCenter == null) return p;
        Vector3 c = Flat(arenaCenter.position);
        Vector3 offset = Flat(p) - c;
        float max = arenaRadius - bodyRadius;
        if (offset.magnitude > max) offset = offset.normalized * max;
        return new Vector3(c.x + offset.x, p.y, c.z + offset.z);
    }

    /// <summary>
    /// start에서 dir(수평 단위 벡터)로 곧게 갈 때 경기장 경계(보스 몸통 반경만큼 여유)에 닿기까지의 거리. 최대 maxDistance.
    /// 원과 반직선의 교점: |o + t·dir| = r 을 t에 대해 푼다 (o = 경기장 중심에서 본 출발점).
    /// </summary>
    private float DistanceToArenaEdge(Vector3 start, Vector3 dir, float maxDistance)
    {
        if (arenaCenter == null) return maxDistance;
        Vector3 o = Flat(start) - Flat(arenaCenter.position);
        float r = arenaRadius - bodyRadius;
        float b = Vector3.Dot(o, dir);
        float disc = b * b - (o.sqrMagnitude - r * r);
        if (disc < 0f) return 0f;  // 경기장 밖에서 바깥쪽을 향함
        return Mathf.Clamp(-b + Mathf.Sqrt(disc), 0f, maxDistance);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private void ShowTelegraph(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        ClearTelegraph();
        if (prefab == null) return;
        activeTelegraph = Instantiate(prefab, position + Vector3.up * 0.05f, rotation);
        activeTelegraph.transform.localScale = scale;
    }

    private void MoveTelegraph(Vector3 position)
    {
        if (activeTelegraph != null) activeTelegraph.transform.position = position + Vector3.up * 0.05f;
    }

    private void ClearTelegraph()
    {
        if (activeTelegraph != null)
        {
            Destroy(activeTelegraph);
            activeTelegraph = null;
        }
    }
}
