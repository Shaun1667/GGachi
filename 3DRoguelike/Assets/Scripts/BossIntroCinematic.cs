using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// 전투 시작 전 보스 등장 연출 (Timeline + Cinemachine).
///
/// [장면] (시간은 아래 상수, 초)
///  1. 멀고 높은 곳에서 폐허 사이로 플레이어가 천천히 걸어 들어온다 → 어깨 너머 3인칭
///  2. 카메라가 머리 속으로 들어가며 1인칭으로 바뀐다
///  3. 까치가 웅크렸다가 돌진해 와서 플레이어를 넘어뜨린다 (1인칭 카메라가 바닥으로 떨어져 올려다봄)
///  4. 까치가 다리를 들어 발로 찍어 누른다
///  5. 플레이어의 할퀴기 (화면 가득 할퀴기 이펙트 두 번) → 까치가 번쩍이며 주춤하고 원래 자리로 물러난다
///  6. 카메라가 게임 카메라 자리로 이어지고 전투 시작
///
/// [역할 분담]
///  - 카메라 컷: Timeline의 Cinemachine 트랙 (Tools > Boss Fight > 8. Setup Boss Intro Cinematic 메뉴가 아래 상수로 배치)
///  - 배우 움직임(걷기, 돌진·밟기·후퇴, 이펙트, 흔들림): 이 스크립트가 Timeline 시간(director.time)을 읽어 처리
///    → 건너뛰어도 Finish()에서 모든 배우를 전투 시작 상태로 되돌린다.
///
/// [카메라 넘겨주기]
/// Main Camera는 CameraTarget/CameraPivot의 자식이고 평소에는 CameraController가 움직인다.
/// 연출 동안만 CinemachineBrain을 켜서 카메라를 가져오고, 끝나면 Brain을 끄고 로컬 위치·회전·화각을 되돌린다.
/// 마지막 컷(gameplayMatchCamera)은 재생 직전에 게임 카메라와 같은 자리로 옮겨두므로 되돌릴 때 화면이 튀지 않는다.
/// </summary>
public class BossIntroCinematic : MonoBehaviour
{
    // ── 타이밍 (초). BossIntroSetup이 카메라 컷도 같은 값으로 배치한다 ──
    public const float ThirdPersonCut = 4.5f;        // 와이드 → 어깨 너머 3인칭
    public const float FirstPersonBlendStart = 6.0f; // 3인칭 → 1인칭으로 카메라가 머리 속으로
    public const float FirstPersonBlendEnd = 7.0f;
    public const float WalkEnd = 7.0f;               // 걸어 들어와 멈춤 (= 전투 시작 위치)
    public const float HideBodyTime = 6.6f;          // 1인칭이 되기 직전에 몸을 숨김 (카메라가 머리 속을 비추지 않게)
    public const float CrouchTime = 7.3f;            // 까치가 웅크림
    public const float ChargeStart = 7.9f;           // 돌진
    public const float ChargeEnd = 8.6f;             // 도착 → 넘어짐 + 밟기 시작
    public const float FallBlendEnd = 8.9f;          // 1인칭 카메라가 바닥에 떨어짐
    public const float StompImpact = ChargeEnd + 0.68f; // 발이 바닥에 닿는 순간 (GGachiAnimationBaker.StompSlamEnd)
    public const float Claw1 = 10.5f;                // 할퀴기 (오른손 → 왼손)
    public const float Claw2 = 10.75f;
    public const float RecoilStart = 10.9f;          // 주춤하며 물러남
    public const float RecoilEnd = 11.9f;
    public const float GameplayBlendStart = 11.2f;   // 게임 카메라로 이어지기 시작 (이때 몸을 다시 보임)
    public const float GameplayBlendEnd = 12.4f;
    public const float End = 12.6f;

    [Header("Timeline / 카메라")]
    [SerializeField] private PlayableDirector director;

    [Tooltip("Main Camera에 붙은 CinemachineBrain. 연출 동안만 켠다")]
    [SerializeField] private CinemachineBrain brain;

    [Tooltip("마지막 컷. 재생 직전에 게임 카메라와 같은 위치·회전·화각으로 맞춘다")]
    [SerializeField] private CinemachineCamera gameplayMatchCamera;

    [Tooltip("연출용 Cinemachine 카메라들의 부모. 연출 동안만 켠다")]
    [SerializeField] private GameObject cameraRoot;

    [Tooltip("연출 동안 꺼둘 카메라 스크립트 (CameraShake처럼 Main Camera 위치를 직접 바꾸는 것)")]
    [SerializeField] private Behaviour[] disableDuringIntro;

    [Tooltip("넘어짐·밟기·할퀴기 때 화면을 흔드는 Impulse")]
    [SerializeField] private CinemachineImpulseSource impulse;

    [Header("플레이어")]
    [SerializeField] private Transform player;
    [SerializeField] private Animator playerAnimator;

    [Tooltip("연출 동안 꺼둘 플레이어 스크립트 (PlayerAnimator처럼 애니메이터 값을 덮어쓰는 것)")]
    [SerializeField] private Behaviour[] playerDriversToDisable;

    [Tooltip("전투 시작 위치에서 뒤로 이만큼 떨어진 곳에서 걸어 들어온다 (m)")]
    [SerializeField] private float walkDistance = 18f;

    [Tooltip("걷기 애니메이션 재생 속도. 게임 이동 속도(5m/s)일 때 1이므로 천천히 걸을 때는 줄여서 발이 미끄러지지 않게")]
    [SerializeField] private float walkAnimSpeed = 0.55f;

    [Tooltip("할퀴기 이펙트 프리팹 (PlayerAttack과 같은 것)")]
    [SerializeField] private GameObject clawEffect;

    [Header("보스")]
    [SerializeField] private Transform boss;
    [SerializeField] private Animator[] bossAnimators;
    [SerializeField] private HitFlash bossFlash;

    [Tooltip("밟을 때 보스 몸이 플레이어 앞 이만큼 떨어진 곳에 선다 (m). 밟기 클립의 발이 몸 앞 약 2.2m에 내려오므로 2.6이면 누운 가슴 위")]
    [SerializeField] private float stompDistance = 2.6f;

    [Header("건너뛰기")]
    [SerializeField] private bool allowSkip = true;

    public bool IsPlaying { get; private set; }

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");
    private static readonly string[] BossTriggers = { "ChargeReady", "Charge", "Stomp", "Land", "Dive" };

    private Action onFinished;
    private Camera cam;
    private Vector3 camLocalPosition;
    private Quaternion camLocalRotation;
    private float camFieldOfView;
    private int startFrame;
    private float lastTime;

    private Vector3 playerStart, playerEntrance;
    private Quaternion playerRotation;
    private CharacterController playerController;
    private Renderer[] playerRenderers;
    private bool[] playerRendererDefaults; // 원래 꺼져 있던 렌더러는 다시 켜지 않도록
    private Vector3 bossStart, bossStomp;
    private Quaternion bossRotation;

    private void Awake()
    {
        cam = brain.GetComponent<Camera>();
        brain.enabled = false;
        if (cameraRoot != null) cameraRoot.SetActive(false);
        director.playOnAwake = false;
        director.stopped += OnDirectorStopped;
        playerController = player.GetComponent<CharacterController>();
        playerRenderers = player.GetComponentsInChildren<Renderer>(true);
        playerRendererDefaults = new bool[playerRenderers.Length];
        for (int i = 0; i < playerRenderers.Length; i++) playerRendererDefaults[i] = playerRenderers[i].enabled;
    }

    private void OnDestroy()
    {
        if (director != null) director.stopped -= OnDirectorStopped;
    }

    /// <summary>연출을 처음부터 재생한다. 끝나거나 건너뛰면 finished를 한 번 호출한다.</summary>
    public void Play(Action finished)
    {
        onFinished = finished;

        // 연출이 끝나면 되돌아갈 상태 (= 씬에 배치된 전투 시작 상태)를 기억해 둔다
        Transform t = cam.transform;
        camLocalPosition = t.localPosition;
        camLocalRotation = t.localRotation;
        camFieldOfView = cam.fieldOfView;
        playerStart = player.position;
        playerRotation = player.rotation;
        playerEntrance = playerStart - Flat(player.forward).normalized * walkDistance;
        bossStart = boss.position;
        bossRotation = boss.rotation;
        Vector3 toPlayer = Flat(playerStart - bossStart);
        bossStomp = bossStart + toPlayer.normalized * Mathf.Max(0f, toPlayer.magnitude - stompDistance);

        // 마지막 컷을 지금 게임 카메라 자리에 맞춘다 → 연출 끝 화면 = 전투 시작 화면
        gameplayMatchCamera.transform.SetPositionAndRotation(t.position, t.rotation);
        gameplayMatchCamera.Lens.FieldOfView = camFieldOfView;

        SetEnabled(disableDuringIntro, false);
        SetEnabled(playerDriversToDisable, false);
        if (playerController != null) playerController.enabled = false; // 위치를 직접 옮기므로 충돌 처리를 잠시 끔
        player.position = playerEntrance;

        if (cameraRoot != null) cameraRoot.SetActive(true);
        brain.enabled = true;

        IsPlaying = true;
        lastTime = -1f;
        startFrame = Time.frameCount; // 시작 화면을 넘긴 클릭이 같은 프레임에 건너뛰기로 읽히지 않게
        director.time = 0;
        director.Play();
    }

    private void Update()
    {
        if (!IsPlaying) return;

        if (allowSkip && Time.frameCount != startFrame && SkipPressed())
        {
            director.Stop(); // stopped 이벤트 → Finish
            return;
        }

        float time = (float)director.time;
        UpdatePlayer(time);
        UpdateBoss(time);
        FireEvents(lastTime, time);
        lastTime = time;
    }

    // ───────────────────────── 배우 ─────────────────────────

    private void UpdatePlayer(float time)
    {
        float walk = Mathf.Clamp01(time / WalkEnd);
        player.position = Vector3.Lerp(playerEntrance, playerStart, walk);
        player.rotation = playerRotation;

        // 걷다가 마지막 0.4초 동안 천천히 멈춤
        float move = time < WalkEnd ? Mathf.Clamp01((WalkEnd - time) / 0.4f) : 0f;
        playerAnimator.SetFloat(MoveXId, 0f);
        playerAnimator.SetFloat(MoveYId, move);
        playerAnimator.speed = time < WalkEnd ? walkAnimSpeed : 1f;

        // 1인칭 동안은 몸을 숨긴다 (카메라가 머리 속에 있으므로)
        SetPlayerVisible(time < HideBodyTime || time >= GameplayBlendStart);
    }

    private void UpdateBoss(float time)
    {
        Vector3 pos = bossStart;
        Quaternion rot = bossRotation;
        if (time >= ChargeStart && time < ChargeEnd)
        {
            float k = (time - ChargeStart) / (ChargeEnd - ChargeStart);
            pos = Vector3.Lerp(bossStart, bossStomp, k * k); // 점점 빨라지며 덮쳐옴
        }
        else if (time >= ChargeEnd && time < RecoilStart)
        {
            pos = bossStomp;
        }
        else if (time >= RecoilStart && time < RecoilEnd)
        {
            float k = (time - RecoilStart) / (RecoilEnd - RecoilStart);
            pos = Vector3.Lerp(bossStomp, bossStart, 1f - (1f - k) * (1f - k)); // 휙 물러났다가 느려짐
            // 주춤: 상체를 뒤로 젖혔다가 돌아옴
            rot = bossRotation * Quaternion.Euler(-14f * Mathf.Sin(k * Mathf.PI), 0f, 0f);
        }
        boss.SetPositionAndRotation(pos, rot);
    }

    private void FireEvents(float from, float to)
    {
        if (Crossed(from, to, CrouchTime)) BossTrigger("ChargeReady");
        if (Crossed(from, to, ChargeStart)) BossTrigger("Charge");
        if (Crossed(from, to, ChargeEnd)) { BossTrigger("Stomp"); Shake(0.7f); }   // 덮쳐서 넘어짐
        if (Crossed(from, to, StompImpact)) Shake(1.6f);                           // 발로 찍음
        if (Crossed(from, to, Claw1)) Claw(false);
        if (Crossed(from, to, Claw2)) Claw(true);
        if (Crossed(from, to, RecoilStart)) BossTrigger("Land");                  // 주춤하며 떠오름 → 호버링
    }

    private static bool Crossed(float from, float to, float t) => from < t && to >= t;

    private void Claw(bool left)
    {
        if (clawEffect != null)
        {
            // 1인칭 화면 앞에 그대로 생성 (프리팹은 로컬 앞쪽 1.2m에 줄이 그어진다). 왼손은 좌우를 뒤집는다
            Transform c = cam.transform;
            GameObject fx = Instantiate(clawEffect, c.position, c.rotation);
            fx.transform.localScale = new Vector3(left ? -1f : 1f, 1f, 1f);
        }
        if (bossFlash != null) bossFlash.Flash();
        Shake(0.5f);
    }

    private void BossTrigger(string trigger)
    {
        foreach (Animator a in bossAnimators)
        {
            if (a != null) a.SetTrigger(trigger);
        }
    }

    private void Shake(float force)
    {
        if (impulse != null) impulse.GenerateImpulseWithForce(force);
    }

    // ───────────────────────── 종료 / 복구 ─────────────────────────

    private void OnDirectorStopped(PlayableDirector d)
    {
        if (IsPlaying) Finish();
    }

    private void Finish()
    {
        IsPlaying = false;

        // 배우를 전투 시작 상태로 (끝까지 봤든 건너뛰었든 같은 상태)
        player.SetPositionAndRotation(playerStart, playerRotation);
        playerAnimator.speed = 1f;
        playerAnimator.SetFloat(MoveYId, 0f);
        SetPlayerVisible(true);
        if (playerController != null) playerController.enabled = true;
        SetEnabled(playerDriversToDisable, true);

        boss.SetPositionAndRotation(bossStart, bossRotation);
        foreach (Animator a in bossAnimators)
        {
            if (a == null) continue;
            foreach (string trig in BossTriggers) a.ResetTrigger(trig);
            a.Play("Idle", 0, 0f);
        }

        // 카메라를 원래 리그(CameraTarget/CameraPivot)로 돌려준다
        brain.enabled = false;
        if (cameraRoot != null) cameraRoot.SetActive(false);
        cam.transform.localPosition = camLocalPosition;
        cam.transform.localRotation = camLocalRotation;
        cam.fieldOfView = camFieldOfView;
        SetEnabled(disableDuringIntro, true);

        Action callback = onFinished;
        onFinished = null;
        callback?.Invoke();
    }

    // ───────────────────────── 도우미 ─────────────────────────

    private static bool SkipPressed()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
               || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }

    private void SetPlayerVisible(bool visible)
    {
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            Renderer r = playerRenderers[i];
            bool on = visible && playerRendererDefaults[i];
            if (r != null && r.enabled != on) r.enabled = on;
        }
    }

    private static void SetEnabled(Behaviour[] behaviours, bool enabled)
    {
        if (behaviours == null) return;
        foreach (Behaviour b in behaviours)
        {
            if (b != null) b.enabled = enabled;
        }
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
