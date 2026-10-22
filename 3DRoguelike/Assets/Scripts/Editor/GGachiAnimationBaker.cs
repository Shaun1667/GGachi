using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 까치 보스 애니메이션을 코드로 만들어 클립으로 굽는 에디터 메뉴.
/// 까치는 몸통(GGachi_Body.fbx, Humanoid)과 날개(GGachi_Wings.fbx, Generic)로 나뉘어 있어서
/// 동작 하나당 클립이 두 개 생긴다.
///
/// Tools > Boss Fight > 4. Build GGachi Hover Idle   : 공중에 떠서 날갯짓 (Idle 상태)
/// Tools > Boss Fight > 5. Build GGachi Slam Dive    : 내려찍기 때 발부터 내리꽂는 자세 (SlamDive 상태)
/// Tools > Boss Fight > 6. Build GGachi Charge       : 돌진 출발 자세 (ChargeReady) + 비행 (Charge, 몸통은 Flying.fbx)
/// Tools > Boss Fight > 7. Build GGachi Stomp        : 등장 연출에서 다리를 들어 발로 찍어 누르는 동작 (Stomp 상태, 반복 없음)
/// Tools > Boss Fight > Add Death Animation          : Mixamo "Death From Right"를 고라니 게임오버 / 까치 쓰러짐(Death 상태)으로
///
/// 만드는 방법:
///  1) 몸통/날개 모델을 임시로 만들고
///  2) 1/30초마다 뼈를 "보스 기준 방향(오른쪽·위·앞)"으로 돌려 자세를 만든 뒤
///  3) 몸통: HumanPoseHandler로 그 자세를 휴머노이드 근육 값으로 읽어 Humanoid 클립에 기록
///           (꼬리는 휴머노이드 뼈가 아니라서 Transform 회전으로 같이 기록)
///     날개: 각 뼈의 localRotation을 Generic 클립에 기록
///
/// 회전 방향 메모 (모델은 +Z를 바라보고 Left 뼈가 -X 쪽):
///  - right 축 + 회전 : 아래를 향한 뼈는 뒤로, 앞을 향한 뼈는 아래로
///  - forward 축      : 왼날개/왼팔은 - 가 위(바깥)로, 오른쪽은 + 가 위(바깥)로
/// </summary>
public static class GGachiAnimationBaker
{
    private const string BodyModelPath = "Assets/Resources/VARCO/GGachi.fbx"; // Mixamo로 다시 리깅한 몸통 (꼬리 뼈 없음)
    private const string WingsModelPath = "Assets/Resources/VARCO/GGachi_Wings.fbx";
    private const string AnimDir = "Assets/Resources/VARCO/Animations";
    public const string BodyControllerPath = AnimDir + "/GGachi.controller";
    public const string WingsControllerPath = AnimDir + "/GGachi_Wings.controller";
    private const string BodyHoverPath = AnimDir + "/GGachi_Hover.anim";
    private const string BodyDivePath = AnimDir + "/GGachi_SlamDive.anim";
    private const string WingsHoverPath = AnimDir + "/GGachi_Wings_Hover.anim";
    private const string WingsDivePath = AnimDir + "/GGachi_Wings_SlamDive.anim";
    private const string BodyChargeReadyPath = AnimDir + "/GGachi_ChargeReady.anim";
    private const string WingsChargeReadyPath = AnimDir + "/GGachi_Wings_ChargeReady.anim";
    private const string WingsFlyingPath = AnimDir + "/GGachi_Wings_Flying.anim";
    private const string BodyStompPath = AnimDir + "/GGachi_Stomp.anim";
    private const string WingsStompPath = AnimDir + "/GGachi_Wings_Stomp.anim";
    // 돌진 비행 몸통 동작: 까치 몸통에 Mixamo로 입힌 Flying (몸을 수평으로 눕혀 나는 자세)
    private const string FlyingModelPath = "Assets/Resources/Animation/Flying.fbx";
    // 쓰러짐: 고라니와 같은 Mixamo 클립 (임포트 설정은 BossFightSetup.ImportGoraniClips가 맡음). 몸통만 쓰고 날개는 멈춰서 몸을 따라감
    private const string DeathModelPath = "Assets/Resources/Animation/Death From Right.fbx";
    private const int Fps = 30;

    // ── 호버링 Idle ──
    private const float HoverClipLength = 4f;     // 날갯짓 2번 (반복 시 이음매가 없도록 주기의 정수배)
    private const float FlapPeriod = 2f;          // 날갯짓 1번에 걸리는 시간
    private const float HoverHeightRatio = 0.13f; // 모델 키 대비 떠 있는 높이
    private const float BobHeightRatio = 0.03f;   // 날갯짓에 따라 오르내리는 폭
    private const float WingUpperAmp = 35f;
    private const float WingForeAmp = 20f;
    private const float WingHandAmp = 15f;
    private const float WingUpBias = 12f;         // 위로는 크게 펴고 아래로는 덜 내려가 날개가 몸을 파고들지 않게

    // ── 하강 찍기 ──
    private const float DiveClipLength = 1f;
    private const float DiveTremblePeriod = 0.25f; // 낙하 바람에 날개 끝·꼬리가 떨리는 주기

    // ── 돌진 ──
    private const float ChargeReadyClipLength = 1f;  // 준비 자세에서 숨 고르듯 들썩이는 주기
    private const float WingsFlyingClipLength = 0.5f; // 비행 중 날개 끝이 바람에 떨리는 주기

    // ── 밟기 (등장 연출) ── 반복하지 않고 마지막 자세(짓누르기)에서 멈춘다
    public const float StompClipLength = 2.0f;
    public const float StompRaiseEnd = 0.5f;   // 오른다리를 높이 드는 데 걸리는 시간
    public const float StompSlamEnd = 0.68f;   // 내리찍어 발이 바닥에 닿는 순간 (BossIntroCinematic이 이때 화면을 흔든다)

    /// <summary>자세를 만들 때 쓰는 뼈 묶음 (몸통 또는 날개 모델 하나)</summary>
    private class Rig
    {
        public Transform root;
        public Vector3 right, up, forward;
        public float height;
        public Vector3 hipsRestWorld;
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

        public Rig(Transform root)
        {
            this.root = root;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                bones[t.name] = t;
                // Mixamo 리그는 "mixamorig:Hips"처럼 접두어가 붙어 있어서 접두어 없는 이름으로도 찾을 수 있게 등록
                int colon = t.name.IndexOf(':');
                if (colon >= 0) bones[t.name.Substring(colon + 1)] = t;
            }
            right = root.right; up = root.up; forward = root.forward;
        }

        public bool Has(string name) => bones.ContainsKey(name);

        public Transform this[string name] =>
            bones.TryGetValue(name, out Transform t) ? t : throw new Exception("[GGachiAnimationBaker] 뼈를 찾지 못했습니다: " + name);

        /// <summary>뼈를 보스 기준 축으로 돌린다. 리그에 없는 뼈(예: 꼬리가 없는 리그의 tail_1)는 건너뛴다</summary>
        public void Rotate(string bone, Vector3 axis, float degrees)
        {
            if (!bones.TryGetValue(bone, out Transform b)) return;
            b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation;
        }

        /// <summary>보스 기준 방향 (x: 오른쪽, y: 위, z: 앞)을 월드 방향으로</summary>
        public Vector3 Dir(float x, float y, float z) => (right * x + up * y + forward * z).normalized;

        /// <summary>뼈(bone)에서 자식 뼈(child)로 향하는 방향이 dir이 되도록 뼈를 돌린다</summary>
        public void Aim(string bone, string child, Vector3 dir)
        {
            if (!bones.TryGetValue(bone, out Transform b) || !bones.TryGetValue(child, out Transform c)) return;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, dir) * b.rotation;
        }

        /// <summary>보스 기준 높이 (루트에서 위로 몇 m)</summary>
        public float Height(string bone) => bones.TryGetValue(bone, out Transform b) ? Vector3.Dot(b.position - root.position, up) : float.MaxValue;
    }

    // ───────────────────────── 메뉴 ─────────────────────────

    [MenuItem("Tools/Boss Fight/4. Build GGachi Hover Idle")]
    public static void BuildHover()
    {
        BakeBody(BodyHoverPath, "GGachi_Hover", HoverClipLength, HoverBody);
        BakeWings(WingsHoverPath, "GGachi_Wings_Hover", HoverClipLength, HoverWings);
        SetupControllers();
        Debug.Log("[GGachiAnimationBaker] 호버링 Idle 생성 완료");
    }

    [MenuItem("Tools/Boss Fight/5. Build GGachi Slam Dive")]
    public static void BuildSlamDive()
    {
        BakeBody(BodyDivePath, "GGachi_SlamDive", DiveClipLength, DiveBody);
        BakeWings(WingsDivePath, "GGachi_Wings_SlamDive", DiveClipLength, DiveWings);
        SetupControllers();
        Debug.Log("[GGachiAnimationBaker] 하강 찍기 생성 완료");
    }

    [MenuItem("Tools/Boss Fight/6. Build GGachi Charge")]
    public static void BuildCharge()
    {
        BakeBody(BodyChargeReadyPath, "GGachi_ChargeReady", ChargeReadyClipLength, ChargeReadyBody, ChargeReadyGroundBones);
        BakeWings(WingsChargeReadyPath, "GGachi_Wings_ChargeReady", ChargeReadyClipLength, ChargeReadyWings);
        BakeWings(WingsFlyingPath, "GGachi_Wings_Flying", WingsFlyingClipLength, FlyingWings);
        ImportFlyingClip();
        SetupControllers();
        Debug.Log("[GGachiAnimationBaker] 돌진 준비·비행 생성 완료");
    }

    [MenuItem("Tools/Boss Fight/7. Build GGachi Stomp")]
    public static void BuildStomp()
    {
        BakeBody(BodyStompPath, "GGachi_Stomp", StompClipLength, StompBody, StompGroundBones, loop: false);
        BakeWings(WingsStompPath, "GGachi_Wings_Stomp", StompClipLength, StompWings, loop: false);
        SetupControllers();
        Debug.Log("[GGachiAnimationBaker] 밟기 생성 완료");
    }

    [MenuItem("Tools/Boss Fight/Add Death Animation (Player + Boss)")]
    public static void AddDeathAnimation()
    {
        BossFightSetup.BuildGoraniController(); // Death From Right 임포트(Humanoid) + 고라니 Death 상태
        SetupControllers();                     // 까치 몸통 Death 상태
        Debug.Log("[GGachiAnimationBaker] 쓰러짐 동작 연결 완료 (고라니 / 까치)");
    }

    // ───────────────────────── 자세: 호버링 ─────────────────────────

    /// <summary>
    /// 몸통: 날갯짓에 맞춰 오르내리고, 두 다리는 곧게 펴서 늘어뜨림, 팔은 내림
    /// </summary>
    private static void HoverBody(Rig r, float time)
    {
        float phase = time / FlapPeriod * Mathf.PI * 2f;
        float slow = phase * 0.5f; // 4초 주기 느린 흔들림

        // 몸: 떠오름 + 날갯짓 반동 (날개를 내려치는 순간 몸이 위로)
        float bob = HoverHeightRatio + BobHeightRatio * -Mathf.Sin(phase);
        r["Hips"].position = r.hipsRestWorld + r.up * (bob * r.height);
        r.Rotate("Spine1", r.right, 3f * Mathf.Sin(phase));

        // 두 다리: 무릎을 펴고 곧게 늘어뜨림, 발끝은 아래로. 엉덩이에서만 엇갈린 박자로 살짝 흔들림
        r.Rotate("LeftUpLeg", r.right, 3f * Mathf.Sin(slow + 0.5f));
        r.Rotate("LeftFoot", r.right, 40f + 4f * Mathf.Sin(slow - 0.9f));
        r.Rotate("RightUpLeg", r.right, 3f * Mathf.Sin(slow + 2.2f));
        r.Rotate("RightFoot", r.right, 40f + 4f * Mathf.Sin(slow + 0.6f));

        // 팔: 몸 옆으로 자연스럽게 내리고 숨 쉬듯 살짝 흔들림
        float sway = 3f * Mathf.Sin(slow);
        r.Rotate("LeftArm", r.forward, 14f);
        r.Rotate("LeftArm", r.right, sway);
        r.Rotate("LeftForeArm", r.right, -12f);
        r.Rotate("RightArm", r.forward, -14f);
        r.Rotate("RightArm", r.right, -sway);
        r.Rotate("RightForeArm", r.right, -12f);
    }

    /// <summary>날개: 크게 퍼덕임. 위팔 → 아래팔 → 손 순서로 조금씩 늦게 따라와 휘는 느낌</summary>
    private static void HoverWings(Rig r, float time)
    {
        float phase = time / FlapPeriod * Mathf.PI * 2f;
        float upper = WingUpBias + WingUpperAmp * Mathf.Sin(phase);
        float fore = WingUpBias * 0.5f + WingForeAmp * Mathf.Sin(phase - 0.6f);
        float hand = WingHandAmp * Mathf.Sin(phase - 1.1f);
        r.Rotate("wing_upper.L", r.forward, -upper);
        r.Rotate("wing_fore.L", r.forward, -fore);
        r.Rotate("wing_hand.L", r.forward, -hand);
        r.Rotate("wing_upper.R", r.forward, upper);
        r.Rotate("wing_fore.R", r.forward, fore);
        r.Rotate("wing_hand.R", r.forward, hand);
    }

    // ───────────────────────── 자세: 하강 찍기 ─────────────────────────

    /// <summary>
    /// 몸통: 몸을 곧게 세우고 발부터 내리꽂는 자세
    ///  - 왼다리는 곧게 뻗어 발톱을 아래로, 오른다리는 무릎을 들어 'ㄱ'자 (허벅지 수평, 정강이 수직)
    ///  - 팔은 어깨에서 옆으로 벌려 내리고 뒤로 살짝 젖힘
    ///  - 머리는 내려찍을 곳을 내려다봄
    /// </summary>
    private static void DiveBody(Rig r, float time)
    {
        float tremble = Mathf.Sin(time / DiveTremblePeriod * Mathf.PI * 2f);

        r.Rotate("Head", r.right, 25f);

        r.Rotate("LeftUpLeg", r.forward, 5f);
        r.Rotate("LeftFoot", r.right, 55f + 2f * tremble);

        r.Rotate("RightUpLeg", r.right, -90f);
        r.Rotate("RightLeg", r.right, 90f);
        r.Rotate("RightFoot", r.right, 40f - 3f * tremble);

        r.Rotate("LeftArm", r.forward, -35f);
        r.Rotate("LeftArm", r.right, 12f);
        r.Rotate("LeftForeArm", r.forward, -10f);
        r.Rotate("RightArm", r.forward, 35f);
        r.Rotate("RightArm", r.right, 12f);
        r.Rotate("RightForeArm", r.forward, 10f);
    }

    /// <summary>날개: 위로 접어 올리고(forward 축) 뒤로 젖힘(up 축), 끝은 낙하 바람에 떨림</summary>
    private static void DiveWings(Rig r, float time)
    {
        float tremble = Mathf.Sin(time / DiveTremblePeriod * Mathf.PI * 2f);
        r.Rotate("wing_upper.L", r.forward, -55f);
        r.Rotate("wing_upper.L", r.up, -30f);
        r.Rotate("wing_fore.L", r.forward, -25f + 4f * tremble);
        r.Rotate("wing_hand.L", r.forward, -15f + 6f * tremble);
        r.Rotate("wing_upper.R", r.forward, 55f);
        r.Rotate("wing_upper.R", r.up, 30f);
        r.Rotate("wing_fore.R", r.forward, 25f - 4f * tremble);
        r.Rotate("wing_hand.R", r.forward, 15f - 6f * tremble);
    }

    // ───────────────────────── 자세: 돌진 ─────────────────────────

    /// <summary>
    /// 몸통: 단거리 출발 자세 ("차렷… 준비!")
    ///  - 상체를 앞으로 거의 수평으로 숙이고 고개는 들어 앞(플레이어)을 노려봄
    ///  - 두 팔은 어깨 아래로 곧게 내려 바닥을 짚음
    ///  - 왼다리는 앞으로 내딛어 무릎을 접고, 오른다리는 뒤로 빼서 무릎이 바닥 가까이
    ///  - 숨 고르듯 살짝 들썩임. 발·손·무릎 중 가장 낮은 곳이 바닥에 닿게 하는 것은 BakeBody(groundBones)가 맡음
    /// </summary>
    private static void ChargeReadyBody(Rig r, float time)
    {
        float breath = Mathf.Sin(time / ChargeReadyClipLength * Mathf.PI * 2f);

        // 골반째 앞으로 기울임 (휴머노이드 허리 근육은 마디당 약 40°까지만 굽어서 허리만으로는 수평이 안 됨)
        r.Rotate("Hips", r.right, 40f);

        // 다리 (엉덩이 기준 아래쪽): 앞다리는 허벅지를 거의 수평으로 들어 무릎을 깊게 접고, 뒷다리는 무릎이 바닥 가까이
        r.Aim("LeftUpLeg", "LeftLeg", r.Dir(0f, -0.3f, 1f));
        r.Aim("LeftLeg", "LeftFoot", r.Dir(0f, -1f, -0.25f));
        r.Aim("LeftFoot", "LeftToeBase", r.Dir(0f, -0.2f, 1f));
        r.Aim("RightUpLeg", "RightLeg", r.Dir(0f, -1f, -0.35f));
        r.Aim("RightLeg", "RightFoot", r.Dir(0f, -0.15f, -1f));
        r.Aim("RightFoot", "RightToeBase", r.Dir(0f, -0.9f, -0.45f));

        // 상체: 허리부터 앞으로 숙여 거의 수평, 고개는 들어 앞을 봄
        r.Aim("Spine", "Spine1", r.Dir(0f, 0.35f, 1f));
        r.Aim("Spine1", "Spine2", r.Dir(0f, 0.25f + 0.03f * breath, 1f));
        r.Aim("Spine2", "Neck", r.Dir(0f, 0.2f, 1f));
        r.Aim("Neck", "Head", r.Dir(0f, 0.6f, 1f));
        r.Rotate("Head", r.right, -35f);

        // 팔: 어깨에서 바닥으로 곧게, 어깨너비로 살짝 벌림
        float sideL = Mathf.Sign(Vector3.Dot(r["LeftArm"].position - r["Spine2"].position, r.right));
        r.Aim("LeftArm", "LeftForeArm", r.Dir(0.12f * sideL, -1f, 0.15f));
        r.Aim("LeftForeArm", "LeftHand", r.Dir(0.05f * sideL, -1f, 0.1f));
        r.Aim("RightArm", "RightForeArm", r.Dir(-0.12f * sideL, -1f, 0.15f));
        r.Aim("RightForeArm", "RightHand", r.Dir(-0.05f * sideL, -1f, 0.1f));

        // 숨 고르듯 살짝 들썩임 (바닥에 닿게 내리는 것은 BakeBody가 근육 한계를 적용한 뒤에 한다)
        r["Hips"].position += r.up * (0.01f * r.height * (1f + breath));
    }

    /// <summary>출발 자세에서 바닥에 닿아야 하는 곳 (발끝·손·뒷무릎)</summary>
    private static readonly string[] ChargeReadyGroundBones = { "LeftToeBase", "RightToeBase", "LeftFoot", "RightFoot", "LeftHand", "RightHand", "RightLeg" };

    /// <summary>날개: 위로 들어 뒤로 젖혀 접어 둠 (박차고 나가기 직전)</summary>
    private static void ChargeReadyWings(Rig r, float time)
    {
        float breath = Mathf.Sin(time / ChargeReadyClipLength * Mathf.PI * 2f);
        r.Rotate("wing_upper.L", r.forward, -35f - 3f * breath);
        r.Rotate("wing_upper.L", r.up, -40f);
        r.Rotate("wing_fore.L", r.forward, -30f);
        r.Rotate("wing_hand.L", r.forward, -20f);
        r.Rotate("wing_upper.R", r.forward, 35f + 3f * breath);
        r.Rotate("wing_upper.R", r.up, 40f);
        r.Rotate("wing_fore.R", r.forward, 30f);
        r.Rotate("wing_hand.R", r.forward, 20f);
    }

    /// <summary>날개: 비행 중 뒤로 쭉 젖혀 펼치고(up 축), 끝은 바람에 떨림</summary>
    private static void FlyingWings(Rig r, float time)
    {
        float tremble = Mathf.Sin(time / WingsFlyingClipLength * Mathf.PI * 2f);
        r.Rotate("wing_upper.L", r.up, -55f);
        r.Rotate("wing_upper.L", r.forward, -8f);
        r.Rotate("wing_fore.L", r.forward, -6f + 4f * tremble);
        r.Rotate("wing_hand.L", r.forward, 6f * tremble);
        r.Rotate("wing_upper.R", r.up, 55f);
        r.Rotate("wing_upper.R", r.forward, 8f);
        r.Rotate("wing_fore.R", r.forward, 6f - 4f * tremble);
        r.Rotate("wing_hand.R", r.forward, -6f * tremble);
    }

    // ───────────────────────── 자세: 밟기 (등장 연출) ─────────────────────────

    /// <summary>
    /// 몸통: 왼다리로 땅을 딛고 서서 오른다리를 높이 들었다가 앞으로 내리찍어 짓누른다
    ///  - 0 ~ StompRaiseEnd      : 오른 허벅지를 앞으로 높이 들고 무릎을 접음, 상체는 살짝 뒤로 젖힘
    ///  - StompRaiseEnd ~ SlamEnd : 앞으로 내리찍음 (허벅지는 앞쪽 아래, 무릎은 펴고 발바닥은 바닥과 수평)
    ///  - 그 뒤                  : 상체를 숙여 체중을 실어 짓누르며 부르르 떪
    ///  - 팔은 옆으로 크게 벌리고, 머리는 발밑(플레이어)을 내려다봄
    /// </summary>
    private static void StompBody(Rig r, float time)
    {
        float raise = Smooth(Mathf.Clamp01(time / StompRaiseEnd));
        float slam = Mathf.Clamp01((time - StompRaiseEnd) / (StompSlamEnd - StompRaiseEnd));
        slam *= slam; // 내리찍을수록 빨라짐
        float hold = Mathf.Clamp01((time - StompSlamEnd) / 0.4f);
        float tremble = time > StompSlamEnd ? Mathf.Sin((time - StompSlamEnd) * 40f) * (1f - 0.5f * hold) : 0f;

        // 오른다리: 들기 → 내리찍기
        float thigh = Mathf.Lerp(-105f * raise, -45f, slam);
        float knee = Mathf.Lerp(110f * raise, 25f, slam);
        r.Rotate("RightUpLeg", r.right, thigh);
        r.Rotate("RightLeg", r.right, knee);
        r.Rotate("RightFoot", r.right, Mathf.Lerp(20f * raise, 10f, slam) + tremble);

        // 디딤발(왼다리): 무릎을 살짝 굽혀 버팀
        r.Rotate("LeftUpLeg", r.right, -10f * raise);
        r.Rotate("LeftLeg", r.right, 15f * raise);

        // 상체: 들 때는 뒤로 젖혔다가 찍은 뒤엔 앞으로 숙여 체중을 싣는다
        float lean = Mathf.Lerp(-10f * raise, 18f, slam) + 4f * hold + 1.5f * tremble;
        r.Rotate("Spine1", r.right, lean);
        r.Rotate("Head", r.right, 30f + 10f * slam);

        // 팔: 옆으로 크게 벌려 위협
        float spread = 50f * raise;
        r.Rotate("LeftArm", r.forward, -spread);
        r.Rotate("LeftForeArm", r.forward, -15f * raise);
        r.Rotate("RightArm", r.forward, spread);
        r.Rotate("RightForeArm", r.forward, 15f * raise);
    }

    /// <summary>밟는 동안 바닥에 붙어 있어야 하는 디딤발</summary>
    private static readonly string[] StompGroundBones = { "LeftFoot", "LeftToeBase" };

    /// <summary>날개: 위로 크게 펼쳐 몸을 키워 보이고, 찍는 순간 앞으로 덮었다가 떤다</summary>
    private static void StompWings(Rig r, float time)
    {
        float raise = Smooth(Mathf.Clamp01(time / StompRaiseEnd));
        float slam = Mathf.Clamp01((time - StompRaiseEnd) / (StompSlamEnd - StompRaiseEnd));
        float tremble = time > StompSlamEnd ? Mathf.Sin((time - StompSlamEnd) * 30f) : 0f;
        float upper = 45f * raise - 15f * slam;
        r.Rotate("wing_upper.L", r.forward, -upper);
        r.Rotate("wing_upper.L", r.up, -15f * raise + 25f * slam);
        r.Rotate("wing_fore.L", r.forward, -20f * raise + 4f * tremble);
        r.Rotate("wing_hand.L", r.forward, -10f * raise + 6f * tremble);
        r.Rotate("wing_upper.R", r.forward, upper);
        r.Rotate("wing_upper.R", r.up, 15f * raise - 25f * slam);
        r.Rotate("wing_fore.R", r.forward, 20f * raise - 4f * tremble);
        r.Rotate("wing_hand.R", r.forward, 10f * raise - 6f * tremble);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Flying 클립 가져오기 설정: Humanoid로 반복 재생.
    /// 몸을 눕힌 방향·높이는 자세에 굽고, 앞으로 날아가는 이동(약 100m)은 버린다 (돌진 이동은 BossController가 맡음)
    /// </summary>
    private static void ImportFlyingClip()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(FlyingModelPath);
        if (imp == null) throw new Exception("[GGachiAnimationBaker] Flying 파일이 없습니다: " + FlyingModelPath);
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        ModelImporterClipAnimation[] clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
        foreach (ModelImporterClipAnimation c in clips)
        {
            c.name = "GGachi_Flying";
            c.loopTime = true;
            c.loopPose = true;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = false;
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    private static AnimationClip LoadModelClip(string modelPath)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        }
        return null;
    }

    // ───────────────────────── 굽기: 몸통 (Humanoid) ─────────────────────────

    private static readonly string[] BodyExtraBones = { "tail_1", "tail_2", "tail_3" };

    /// <param name="groundBones">주면 근육 한계를 적용한 실제 자세에서 이 뼈들 중 가장 낮은 곳이 쉬는 자세의 발바닥 높이에 닿도록 몸 높이를 맞춘다</param>
    /// <param name="loop">false면 반복하지 않고 마지막 자세에서 멈춘다 (밟기처럼 한 번 하고 버티는 동작)</param>
    private static void BakeBody(string path, string clipName, float length, Action<Rig, float> pose, string[] groundBones = null, bool loop = true)
    {
        GameObject model = Spawn(BodyModelPath);
        try
        {
            Avatar avatar = FindAvatar(BodyModelPath);
            var r = new Rig(model.transform) { height = RendererHeight(model.transform) };
            r.hipsRestWorld = r["Hips"].position;
            var handler = new HumanPoseHandler(avatar, model.transform);
            var humanPose = new HumanPose();

            var rest = SnapshotRotations(model.transform);
            Vector3 hipsRestLocal = r["Hips"].localPosition;

            // 손가락 근육은 클립 속성 이름이 달라서 빼고, 몸통·팔·다리·머리 근육만 기록
            var muscleIndices = new List<int>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (!(n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"))) muscleIndices.Add(i);
            }
            var muscleCurves = new AnimationCurve[HumanTrait.MuscleCount];
            foreach (int i in muscleIndices) muscleCurves[i] = new AnimationCurve();
            AnimationCurve[] rootT = NewCurves(3), rootQ = NewCurves(4);
            // 꼬리는 돌리지 않고 쉬는 자세 회전을 그대로 기록해 몸에 고정한다 (꼬리 뼈가 없는 리그는 건너뜀)
            var extras = new List<string>();
            foreach (string b in BodyExtraBones) if (r.Has(b)) extras.Add(b);
            var extraCurves = new Dictionary<string, AnimationCurve[]>();
            foreach (string b in extras) extraCurves[b] = NewCurves(4);
            var lastRot = new Dictionary<string, Quaternion>();

            float restGround = float.MaxValue;
            if (groundBones != null)
                foreach (string b in groundBones) restGround = Mathf.Min(restGround, r.Height(b));

            int frames = Mathf.RoundToInt(length * Fps);
            for (int f = 0; f <= frames; f++)
            {
                float time = f / (float)Fps;
                RestoreRotations(rest);
                r["Hips"].localPosition = hipsRestLocal;
                pose(r, time);

                handler.GetHumanPose(ref humanPose);
                if (groundBones != null) GroundPose(handler, ref humanPose, r, groundBones, restGround);
                foreach (int i in muscleIndices) muscleCurves[i].AddKey(time, humanPose.muscles[i]);
                rootT[0].AddKey(time, humanPose.bodyPosition.x);
                rootT[1].AddKey(time, humanPose.bodyPosition.y);
                rootT[2].AddKey(time, humanPose.bodyPosition.z);
                Quaternion bq = humanPose.bodyRotation;
                rootQ[0].AddKey(time, bq.x); rootQ[1].AddKey(time, bq.y); rootQ[2].AddKey(time, bq.z); rootQ[3].AddKey(time, bq.w);

                foreach (string b in extras) AddRotationKey(extraCurves[b], r[b].localRotation, time, b, lastRot);
            }

            var clip = new AnimationClip { frameRate = Fps };
            foreach (int i in muscleIndices) clip.SetCurve("", typeof(Animator), HumanTrait.MuscleName[i], muscleCurves[i]);
            string[] t = { "RootT.x", "RootT.y", "RootT.z" }, q = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 3; i++) clip.SetCurve("", typeof(Animator), t[i], rootT[i]);
            for (int i = 0; i < 4; i++) clip.SetCurve("", typeof(Animator), q[i], rootQ[i]);
            foreach (string b in extras) SetRotationCurves(clip, AnimationUtility.CalculateTransformPath(r[b], model.transform), extraCurves[b]);

            // 루트 이동은 PlayerMovement처럼 스크립트가 맡으므로 모든 루트 움직임을 자세에 굽는다 (떠 있는 높이 포함)
            AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop;
            s.loopBlendOrientation = true; s.keepOriginalOrientation = true;
            s.loopBlendPositionY = true; s.keepOriginalPositionY = true;
            s.loopBlendPositionXZ = true; s.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            SaveClip(clip, path, clipName);
            handler.Dispose();
        }
        finally
        {
            Object.DestroyImmediate(model);
        }
    }

    /// <summary>
    /// 근육 한계가 적용된 실제 자세를 모델에 입혀 보고, 가장 낮은 뼈가 바닥(restGround)에 닿도록 몸 높이(bodyPosition)를 옮긴다.
    /// bodyPosition 단위가 모델 크기와 달라서 한 번 옮겨 본 비율로 다시 맞춘다
    /// </summary>
    private static void GroundPose(HumanPoseHandler handler, ref HumanPose pose, Rig r, string[] groundBones, float restGround)
    {
        float Low()
        {
            float low = float.MaxValue;
            foreach (string b in groundBones) low = Mathf.Min(low, r.Height(b));
            return low;
        }
        Vector3 bodyUp = Vector3.up;
        handler.SetHumanPose(ref pose);
        float low0 = Low();
        pose.bodyPosition += bodyUp * 0.1f;
        handler.SetHumanPose(ref pose);
        float perUnit = (Low() - low0) / 0.1f;
        if (Mathf.Abs(perUnit) < 1e-5f) { pose.bodyPosition -= bodyUp * 0.1f; return; }
        pose.bodyPosition += bodyUp * ((restGround - low0) / perUnit - 0.1f);
        handler.SetHumanPose(ref pose);
    }

    // ───────────────────────── 굽기: 날개 (Generic) ─────────────────────────

    private static readonly string[] WingBones =
    {
        "wing_upper.L", "wing_fore.L", "wing_hand.L",
        "wing_upper.R", "wing_fore.R", "wing_hand.R"
    };

    private static void BakeWings(string path, string clipName, float length, Action<Rig, float> pose, bool loop = true)
    {
        GameObject model = Spawn(WingsModelPath);
        try
        {
            var r = new Rig(model.transform) { height = RendererHeight(model.transform) };
            var rest = SnapshotRotations(model.transform);
            var curves = new Dictionary<string, AnimationCurve[]>();
            foreach (string b in WingBones) curves[b] = NewCurves(4);
            var lastRot = new Dictionary<string, Quaternion>();

            int frames = Mathf.RoundToInt(length * Fps);
            for (int f = 0; f <= frames; f++)
            {
                float time = f / (float)Fps;
                RestoreRotations(rest);
                pose(r, time);
                foreach (string b in WingBones) AddRotationKey(curves[b], r[b].localRotation, time, b, lastRot);
            }

            var clip = new AnimationClip { frameRate = Fps };
            foreach (string b in WingBones) SetRotationCurves(clip, AnimationUtility.CalculateTransformPath(r[b], model.transform), curves[b]);
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            SaveClip(clip, path, clipName);
        }
        finally
        {
            Object.DestroyImmediate(model);
        }
    }

    // ───────────────────────── 컨트롤러 ─────────────────────────

    /// <summary>
    /// 몸통·날개 컨트롤러를 같은 구조로 만든다 (여러 번 실행해도 같은 결과):
    ///  - Idle(기본) = 호버링, 어느 상태에서든 Dive(trigger) → SlamDive, Land(trigger) → Idle
    ///  - 어느 상태에서든 ChargeReady(trigger) → ChargeReady(출발 자세), Charge(trigger) → Charge(비행), Land(trigger) → Idle
    ///  - 어느 상태에서든 Stomp(trigger) → Stomp(밟기, 등장 연출 전용), Land(trigger) → Idle
    ///  - (몸통만) 어느 상태에서든 Die(trigger) → Death(쓰러짐, 돌아가지 않음)
    /// </summary>
    private static void SetupControllers()
    {
        AnimationClip death = LoadModelClip(DeathModelPath);
        if (death != null && !death.humanMotion) death = null; // 아직 Humanoid로 임포트되지 않았으면 연결하지 않음 (Add Death Animation 메뉴가 임포트)
        SetupController(BodyControllerPath, BodyHoverPath, BodyDivePath,
            AssetDatabase.LoadAssetAtPath<AnimationClip>(BodyChargeReadyPath), LoadModelClip(FlyingModelPath),
            AssetDatabase.LoadAssetAtPath<AnimationClip>(BodyStompPath), death);
        SetupController(WingsControllerPath, WingsHoverPath, WingsDivePath,
            AssetDatabase.LoadAssetAtPath<AnimationClip>(WingsChargeReadyPath), AssetDatabase.LoadAssetAtPath<AnimationClip>(WingsFlyingPath),
            AssetDatabase.LoadAssetAtPath<AnimationClip>(WingsStompPath), null);
    }

    private static void SetupController(string controllerPath, string hoverPath, string divePath, AnimationClip chargeReady, AnimationClip flying, AnimationClip stomp, AnimationClip death)
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

        AnimationClip hover = AssetDatabase.LoadAssetAtPath<AnimationClip>(hoverPath);
        AnimationClip dive = AssetDatabase.LoadAssetAtPath<AnimationClip>(divePath);

        AnimatorState idle = FindState(sm, "Idle") ?? sm.AddState("Idle");
        if (hover != null) idle.motion = hover;
        sm.defaultState = idle;
        if (dive == null)
        {
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return;
        }

        EnsureParameter(ctrl, "Dive", AnimatorControllerParameterType.Trigger);
        EnsureParameter(ctrl, "Land", AnimatorControllerParameterType.Trigger);

        AnimatorState old = FindState(sm, "SlamDive");
        if (old != null) sm.RemoveState(old);
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
        {
            if (t.destinationState == null) sm.RemoveAnyStateTransition(t);
        }

        AnimatorState diveState = sm.AddState("SlamDive");
        diveState.motion = dive;

        AnimatorStateTransition toDive = sm.AddAnyStateTransition(diveState);
        toDive.hasExitTime = false;
        toDive.duration = 0.12f;          // 낙하 지점 고정 순간 빠르게 자세 전환
        toDive.canTransitionToSelf = false;
        toDive.AddCondition(AnimatorConditionMode.If, 0f, "Dive");

        AnimatorStateTransition toIdle = diveState.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.3f;           // 착지 후 날개를 펴며 호버링으로 돌아감
        toIdle.AddCondition(AnimatorConditionMode.If, 0f, "Land");

        // 돌진: 출발 자세 → 비행 → (도착하면 Land) 호버링
        foreach (string name in new[] { "ChargeReady", "Charge" })
        {
            AnimatorState s = FindState(sm, name);
            if (s != null) sm.RemoveState(s);
        }
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
        {
            if (t.destinationState == null) sm.RemoveAnyStateTransition(t);
        }
        if (chargeReady != null && flying != null)
        {
            EnsureParameter(ctrl, "ChargeReady", AnimatorControllerParameterType.Trigger);
            EnsureParameter(ctrl, "Charge", AnimatorControllerParameterType.Trigger);

            AnimatorState readyState = sm.AddState("ChargeReady");
            readyState.motion = chargeReady;
            AnimatorStateTransition toReady = sm.AddAnyStateTransition(readyState);
            toReady.hasExitTime = false;
            toReady.duration = 0.25f;     // 떠 있다가 땅에 내려앉아 웅크림
            toReady.canTransitionToSelf = false;
            toReady.AddCondition(AnimatorConditionMode.If, 0f, "ChargeReady");

            AnimatorState chargeState = sm.AddState("Charge");
            chargeState.motion = flying;
            AnimatorStateTransition toCharge = sm.AddAnyStateTransition(chargeState);
            toCharge.hasExitTime = false;
            toCharge.duration = 0.12f;    // 박차고 나가는 순간 빠르게 몸을 눕힘
            toCharge.canTransitionToSelf = false;
            toCharge.AddCondition(AnimatorConditionMode.If, 0f, "Charge");

            AnimatorStateTransition chargeToIdle = chargeState.AddTransition(idle);
            chargeToIdle.hasExitTime = false;
            chargeToIdle.duration = 0.35f;
            chargeToIdle.AddCondition(AnimatorConditionMode.If, 0f, "Land");
        }

        // 밟기 (등장 연출): 어느 상태에서든 Stomp → 한 번 찍고 마지막 자세 유지 → Land로 호버링 복귀
        AnimatorState oldStomp = FindState(sm, "Stomp");
        if (oldStomp != null) sm.RemoveState(oldStomp);
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
        {
            if (t.destinationState == null) sm.RemoveAnyStateTransition(t);
        }
        if (stomp != null)
        {
            EnsureParameter(ctrl, "Stomp", AnimatorControllerParameterType.Trigger);

            AnimatorState stompState = sm.AddState("Stomp");
            stompState.motion = stomp;
            AnimatorStateTransition toStomp = sm.AddAnyStateTransition(stompState);
            toStomp.hasExitTime = false;
            toStomp.duration = 0.15f;     // 돌진하다 내려앉으며 다리를 듦
            toStomp.canTransitionToSelf = false;
            toStomp.AddCondition(AnimatorConditionMode.If, 0f, "Stomp");

            AnimatorStateTransition stompToIdle = stompState.AddTransition(idle);
            stompToIdle.hasExitTime = false;
            stompToIdle.duration = 0.3f;  // 할퀴기에 맞고 주춤하며 떠오름
            stompToIdle.AddCondition(AnimatorConditionMode.If, 0f, "Land");
        }

        // 쓰러짐: 어느 상태에서든 Die → 한 번 쓰러지고 그대로 (돌아가는 전환 없음)
        AnimatorState oldDeath = FindState(sm, "Death");
        if (oldDeath != null) sm.RemoveState(oldDeath);
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
        {
            if (t.destinationState == null) sm.RemoveAnyStateTransition(t);
        }
        if (death != null)
        {
            EnsureParameter(ctrl, "Die", AnimatorControllerParameterType.Trigger);

            AnimatorState deathState = sm.AddState("Death");
            deathState.motion = death;
            AnimatorStateTransition toDeath = sm.AddAnyStateTransition(deathState);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.2f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState s in sm.states)
        {
            if (s.state.name == name) return s.state;
        }
        return null;
    }

    private static void EnsureParameter(AnimatorController ctrl, string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter p in ctrl.parameters)
        {
            if (p.name == name) return;
        }
        ctrl.AddParameter(name, type);
    }

    // ───────────────────────── 도우미 ─────────────────────────

    private static GameObject Spawn(string modelPath)
    {
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        model.hideFlags = HideFlags.HideAndDontSave;
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return model;
    }

    private static Dictionary<Transform, Quaternion> SnapshotRotations(Transform root)
    {
        var d = new Dictionary<Transform, Quaternion>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>()) d[t] = t.localRotation;
        return d;
    }

    private static void RestoreRotations(Dictionary<Transform, Quaternion> rest)
    {
        foreach (var kv in rest) kv.Key.localRotation = kv.Value;
    }

    private static void AddRotationKey(AnimationCurve[] c, Quaternion q, float time, string key, Dictionary<string, Quaternion> last)
    {
        if (last.TryGetValue(key, out Quaternion prev) && Quaternion.Dot(prev, q) < 0f)
        {
            q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // 보간이 반대로 도는 것 방지
        }
        last[key] = q;
        c[0].AddKey(time, q.x); c[1].AddKey(time, q.y); c[2].AddKey(time, q.z); c[3].AddKey(time, q.w);
    }

    private static void SetRotationCurves(AnimationClip clip, string path, AnimationCurve[] c)
    {
        string[] props = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        for (int i = 0; i < 4; i++) clip.SetCurve(path, typeof(Transform), props[i], c[i]);
    }

    private static void SaveClip(AnimationClip clip, string path, string clipName)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            clip.name = clipName;
            AssetDatabase.CreateAsset(clip, path);
        }
        else
        {
            EditorUtility.CopySerialized(clip, existing); // 같은 에셋(GUID)을 유지한 채 내용만 교체
            existing.name = clipName;
        }
        AssetDatabase.SaveAssets();
    }

    private static AnimationCurve[] NewCurves(int n)
    {
        var c = new AnimationCurve[n];
        for (int i = 0; i < n; i++) c[i] = new AnimationCurve();
        return c;
    }

    private static Avatar FindAvatar(string modelPath)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (o is Avatar a) return a;
        }
        throw new Exception("[GGachiAnimationBaker] 아바타가 없습니다: " + modelPath);
    }

    private static float RendererHeight(Transform root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 1f;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b.size.y;
    }
}
