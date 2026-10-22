using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;
using C = BossIntroCinematic; // 타이밍 상수

/// <summary>
/// 전투 시작 전 보스 등장 연출(Timeline + Cinemachine)을 메뉴 한 번으로 만드는 에디터 도구.
///
/// Tools > Boss Fight > 8. Setup Boss Intro Cinematic
///   - Assets/Timelines/BossIntro.playable : 카메라 컷 / 레터박스 / 보스 이름 타이틀 트랙
///   - 씬의 BossIntro 오브젝트 : PlayableDirector + BossIntroCinematic + Impulse(화면 흔들림) + 연출용 Cinemachine 카메라들
///   - Main Camera에 CinemachineBrain (평소엔 꺼져 있고 연출 동안만 켜짐)
///   - GameUI_Canvas에 레터박스 / 타이틀 UI, GameManager.intro 연결
/// 먼저 7. Build GGachi Stomp로 까치 밟기 동작을 만들어 두어야 한다.
///
/// [컷] (시간은 BossIntroCinematic 상수)
///  1. 와이드: 경기장 안쪽 높은 곳에서 입구로 걸어 들어오는 플레이어를 멀리 내려다봄 (천천히 흘러감)
///  2. 어깨 너머 3인칭: 플레이어를 따라가며 저 멀리 까치를 봄
///  3. 1인칭: 플레이어 머리에 붙어 다가오는 까치를 바라봄
///  4. 바닥 1인칭: 넘어져 누운 눈높이에서 까치를 올려다봄 (화면이 기울어짐)
///  5. 게임 카메라 자리 (BossIntroCinematic이 재생 직전에 실제 게임 카메라 위치로 맞춤)
///
/// 컷 위치는 씬 배치(플레이어 시작 위치·방향, 보스 위치, 머리 뼈)에서 계산하므로 배치를 바꾼 뒤 다시 실행하면 된다.
/// 여러 번 실행해도 같은 결과가 나온다.
/// </summary>
public static class BossIntroSetup
{
    private const string TimelineDir = "Assets/Timelines";
    private const string TimelinePath = TimelineDir + "/BossIntro.playable";
    private const string RootName = "BossIntro";
    private const string LetterboxName = "Cinematic_Letterbox";
    private const string TitleName = "Cinematic_BossTitle";

    private const string BossTitle = "까치 대장";
    private const string BossSubtitle = "보물 둥지의 수호자";
    private const float WalkDistance = 18f;   // BossIntroCinematic.walkDistance와 같게
    // BossIntroCinematic.stompDistance와 같게. 밟기 클립에서 내리찍은 발이 몸 앞 약 2.2m에 오므로,
    // 2.6m 앞에 서면 발이 플레이어 시작 위치 바로 뒤(넘어져 누운 가슴 쪽)에 떨어진다
    private const float StompDistance = 2.6f;

    private const float TitleStart = 11.0f, TitleEnd = 12.3f;
    private const float LetterboxEnd = 12.2f;

    [MenuItem("Tools/Boss Fight/8. Setup Boss Intro Cinematic")]
    public static void Setup()
    {
        Camera mainCam = Camera.main;
        GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
        BossController boss = Object.FindAnyObjectByType<BossController>();
        PlayerMovement player = Object.FindAnyObjectByType<PlayerMovement>();
        GameObject canvasGo = GameObject.Find("GameUI_Canvas");
        if (mainCam == null || gameManager == null || boss == null || player == null || canvasGo == null)
        {
            Debug.LogError("[BossIntroSetup] GGachi_Boss 씬에서 실행하세요 (Main Camera, GameManager, 보스, 플레이어, GameUI_Canvas 필요)");
            return;
        }

        Animator playerAnimator = player.GetComponentInChildren<Animator>(true);
        Transform playerHead = FindBone(player.transform, "mixamorig:Head");
        Transform bossHead = FindBone(boss.transform, "mixamorig:Head");
        Animator[] bossAnimators = boss.GetComponentsInChildren<Animator>(true);
        if (playerAnimator == null || playerHead == null || bossHead == null)
        {
            Debug.LogError("[BossIntroSetup] 플레이어 Animator나 머리 뼈(mixamorig:Head)를 찾지 못했습니다");
            return;
        }
        if (!HasStompState(bossAnimators))
        {
            Debug.LogWarning("[BossIntroSetup] 까치 컨트롤러에 Stomp 상태가 없습니다. 먼저 Tools > Boss Fight > 7. Build GGachi Stomp를 실행하세요");
        }

        DestroyExisting(canvasGo.transform);

        // ── 기준점 (씬 배치에서 읽음) ──
        Vector3 up = Vector3.up;
        Vector3 start = player.transform.position;                           // 전투 시작 위치 = 걸어서 도착하는 곳
        Vector3 a = Flat(player.transform.forward).normalized;               // 플레이어가 바라보는 쪽 (= 보스 쪽)
        Vector3 s = Vector3.Cross(up, a).normalized;                          // 플레이어 기준 오른쪽
        Vector3 entrance = start - a * WalkDistance;
        Vector3 bossPos = boss.transform.position;
        Vector3 toPlayer = Flat(start - bossPos);
        Vector3 bossStomp = bossPos + toPlayer.normalized * Mathf.Max(0f, toPlayer.magnitude - StompDistance);

        // ── 씬 오브젝트 ──
        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Setup Boss Intro");
        PlayableDirector director = root.AddComponent<PlayableDirector>();
        BossIntroCinematic intro = root.AddComponent<BossIntroCinematic>();
        CinemachineImpulseSource impulse = root.AddComponent<CinemachineImpulseSource>();
        impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        impulse.ImpulseDefinition.ImpulseDuration = 0.35f;
        impulse.DefaultVelocity = Vector3.down * 0.6f;

        GameObject cameraRoot = new GameObject("IntroCameras");
        cameraRoot.transform.SetParent(root.transform, false);

        // 1. 와이드: 경기장 안쪽 오른편 높은 곳에서 입구를 내려다봄 → 조금 다가오며 걷는 길 가운데를 봄
        CinemachineCamera wideA = StaticCamera(cameraRoot, "CM_01_Wide_A", mainCam,
            start + a * 12f - s * 20f + up * 16f, entrance + up * 3f, 50f);
        CinemachineCamera wideB = StaticCamera(cameraRoot, "CM_02_Wide_B", mainCam,
            start + a * 8f - s * 17f + up * 13f, (entrance + start) * 0.5f + up * 3f, 50f);

        // 2. 어깨 너머 3인칭: 오른쪽 어깨 뒤에서 따라가며 저 멀리 까치 머리를 봄
        CinemachineCamera shoulder = NewCamera(cameraRoot, "CM_03_OverShoulder", mainCam, 55f);
        shoulder.Follow = player.transform;
        shoulder.LookAt = bossHead;
        CinemachineFollow follow = shoulder.gameObject.AddComponent<CinemachineFollow>();
        follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
        follow.TrackerSettings.PositionDamping = Vector3.zero;
        follow.FollowOffset = -a * 9f + s * 2.2f + up * 6.5f;
        shoulder.gameObject.AddComponent<CinemachineRotationComposer>();
        shoulder.transform.position = start + follow.FollowOffset; // 에디터에서 볼 때의 자리

        // 3. 1인칭: 머리 뼈에 붙어 다가오는 까치 머리를 바라봄
        CinemachineCamera pov = NewCamera(cameraRoot, "CM_04_POV", mainCam, 65f);
        pov.Follow = playerHead;
        pov.LookAt = bossHead;
        pov.gameObject.AddComponent<CinemachineHardLockToTarget>();
        pov.gameObject.AddComponent<CinemachineRotationComposer>();
        pov.gameObject.AddComponent<CinemachineImpulseListener>();
        pov.transform.position = playerHead.position;

        // 4. 바닥 1인칭: 넘어져 누운 눈높이에서 덮친 까치를 올려다봄, 화면이 기울어짐
        Vector3 groundEye = start - a * 1.2f + up * 0.75f;
        CinemachineCamera ground = StaticCamera(cameraRoot, "CM_05_Ground", mainCam, groundEye, bossStomp + up * 5.5f, 70f);
        ground.Lens.Dutch = 12f;
        ground.gameObject.AddComponent<CinemachineImpulseListener>();

        // 5. 게임 카메라 자리
        CinemachineCamera gameplay = StaticCamera(cameraRoot, "CM_06_Gameplay", mainCam,
            mainCam.transform.position, mainCam.transform.position + mainCam.transform.forward, mainCam.fieldOfView);
        gameplay.gameObject.AddComponent<CinemachineImpulseListener>();

        CinemachineBrain brain = mainCam.GetComponent<CinemachineBrain>();
        if (brain == null) brain = Undo.AddComponent<CinemachineBrain>(mainCam.gameObject);
        brain.enabled = false; // 평소엔 CameraController가 카메라를 움직인다

        GameObject letterbox = BuildLetterbox(canvasGo.transform);
        GameObject title = BuildTitle(canvasGo.transform);

        // ── Timeline: 카메라 컷 (겹치는 구간이 블렌드 = 카메라 이동) ──
        var shots = new List<(CinemachineCamera cam, double start, double end)>
        {
            (wideA, 0.0, 3.0),
            (wideB, 0.3, C.ThirdPersonCut),                    // 0.3~3.0 천천히 흘러감, 4.5에 컷
            (shoulder, C.ThirdPersonCut, C.FirstPersonBlendEnd),
            (pov, C.FirstPersonBlendStart, C.FallBlendEnd),     // 6~7 머리 속으로 들어감
            (ground, C.ChargeEnd, C.GameplayBlendEnd),          // 8.6~8.9 바닥으로 쓰러짐
            (gameplay, C.GameplayBlendStart, C.End),            // 11.2~12.4 게임 카메라로
        };

        TimelineAsset timeline = LoadOrCreateTimeline();
        CinemachineTrack cameraTrack = timeline.CreateTrack<CinemachineTrack>("Cameras");
        TimelineClip previous = null;
        foreach (var (vcam, clipStart, clipEnd) in shots)
        {
            TimelineClip clip = cameraTrack.CreateClip<CinemachineShot>();
            clip.start = clipStart;
            clip.duration = clipEnd - clipStart;
            clip.displayName = vcam.name;
            var shotAsset = (CinemachineShot)clip.asset;
            shotAsset.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = vcam.name };
            director.SetReferenceValue(vcam.name, vcam);

            if (previous != null && previous.end > clip.start)
            {
                double overlap = previous.end - clip.start;
                previous.blendOutDuration = overlap;
                clip.blendInDuration = overlap;
            }
            previous = clip;
        }

        ActivationTrack letterboxTrack = timeline.CreateTrack<ActivationTrack>("Letterbox");
        letterboxTrack.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        TimelineClip lb = letterboxTrack.CreateDefaultClip();
        lb.start = 0; lb.duration = LetterboxEnd; lb.displayName = "Letterbox";

        ActivationTrack titleTrack = timeline.CreateTrack<ActivationTrack>("Boss Title");
        titleTrack.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        TimelineClip tc = titleTrack.CreateDefaultClip();
        tc.start = TitleStart; tc.duration = TitleEnd - TitleStart; tc.displayName = "Boss Title";

        timeline.fixedDuration = C.End;
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        EditorUtility.SetDirty(timeline);
        AssetDatabase.SaveAssets();

        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.None;
        director.SetGenericBinding(cameraTrack, brain);
        director.SetGenericBinding(letterboxTrack, letterbox);
        director.SetGenericBinding(titleTrack, title);

        // ── 연결 ──
        CameraShake shake = mainCam.GetComponent<CameraShake>();
        PlayerAnimator playerAnimDriver = player.GetComponent<PlayerAnimator>();
        PlayerAttack playerAttack = player.GetComponent<PlayerAttack>();
        SetField(intro, "director", director);
        SetField(intro, "brain", brain);
        SetField(intro, "gameplayMatchCamera", gameplay);
        SetField(intro, "cameraRoot", cameraRoot);
        SetArray(intro, "disableDuringIntro", shake != null ? new Object[] { shake } : new Object[0]);
        SetField(intro, "impulse", impulse);
        SetField(intro, "player", player.transform);
        SetField(intro, "playerAnimator", playerAnimator);
        SetArray(intro, "playerDriversToDisable", playerAnimDriver != null ? new Object[] { playerAnimDriver } : new Object[0]);
        SetFloat(intro, "walkDistance", WalkDistance);
        SetField(intro, "clawEffect", playerAttack != null ? playerAttack.clawEffect : null);
        SetField(intro, "boss", boss.transform);
        SetArray(intro, "bossAnimators", bossAnimators.Cast<Object>().ToArray());
        SetField(intro, "bossFlash", boss.GetComponent<HitFlash>());
        SetFloat(intro, "stompDistance", StompDistance);
        SetField(gameManager, "intro", intro);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeObject = root;
        Debug.Log($"[BossIntroSetup] 보스 등장 연출 생성 완료: {TimelinePath} (컷 {shots.Count}개, {C.End:0.0}초)");
    }

    // ───────────────────────── 카메라 ─────────────────────────

    private static CinemachineCamera NewCamera(GameObject parent, string name, Camera mainCam, float fov)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        CinemachineCamera vcam = go.AddComponent<CinemachineCamera>();
        vcam.Lens = LensSettings.FromCamera(mainCam);
        vcam.Lens.FieldOfView = fov;
        return vcam;
    }

    /// <summary>제자리에서 한 점을 바라보는 카메라 (움직임은 앞뒤 컷과의 블렌드로 만든다)</summary>
    private static CinemachineCamera StaticCamera(GameObject parent, string name, Camera mainCam, Vector3 position, Vector3 lookAt, float fov)
    {
        CinemachineCamera vcam = NewCamera(parent, name, mainCam, fov);
        vcam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
        return vcam;
    }

    private static bool HasStompState(Animator[] animators)
    {
        foreach (Animator a in animators)
        {
            if (a.runtimeAnimatorController is UnityEditor.Animations.AnimatorController ctrl
                && ctrl.layers[0].stateMachine.states.Any(st => st.state.name == "Stomp")) continue;
            return false;
        }
        return animators.Length > 0;
    }

    // ───────────────────────── UI ─────────────────────────

    /// <summary>화면 위아래 검은 띠 (시네마틱 비율 느낌)</summary>
    private static GameObject BuildLetterbox(Transform canvas)
    {
        RectTransform root = NewRect(LetterboxName, canvas);
        Stretch(root);
        foreach (bool top in new[] { true, false })
        {
            RectTransform bar = NewRect(top ? "Top" : "Bottom", root);
            bar.anchorMin = new Vector2(0f, top ? 1f : 0f);
            bar.anchorMax = new Vector2(1f, top ? 1f : 0f);
            bar.pivot = new Vector2(0.5f, top ? 1f : 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 120f);
            Image img = bar.gameObject.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
        }
        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    /// <summary>보스 이름 타이틀 (화면 아래쪽 1/4)</summary>
    private static GameObject BuildTitle(Transform canvas)
    {
        RectTransform root = NewRect(TitleName, canvas);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.24f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(1200f, 220f);

        Text name = MakeText("Name", root, BossTitle, 110, new Color(1f, 0.85f, 0.3f));
        name.rectTransform.anchoredPosition = new Vector2(0f, 30f);
        Text sub = MakeText("Subtitle", root, BossSubtitle, 40, new Color(1f, 1f, 1f, 0.9f));
        sub.rectTransform.anchoredPosition = new Vector2(0f, -55f);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static Text MakeText(string name, Transform parent, string content, int size, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1200f, size * 1.4f);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        Outline outline = t.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        Shadow shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(3f, -3f);
        return t;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ───────────────────────── 공통 ─────────────────────────

    /// <summary>이전에 만든 연출 오브젝트를 지운다 (다시 실행해도 같은 결과가 나오게)</summary>
    private static void DestroyExisting(Transform canvas)
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        foreach (string n in new[] { LetterboxName, TitleName })
        {
            Transform t = canvas.Find(n);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }
    }

    /// <summary>Timeline 에셋을 불러오고 트랙을 비운다 (없으면 새로 만든다). 에셋을 지우지 않아 GUID가 유지된다</summary>
    private static TimelineAsset LoadOrCreateTimeline()
    {
        if (!AssetDatabase.IsValidFolder(TimelineDir)) AssetDatabase.CreateFolder("Assets", "Timelines");
        TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (timeline == null)
        {
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
        }
        else
        {
            foreach (TrackAsset t in timeline.GetRootTracks().ToList()) timeline.DeleteTrack(t);
        }
        return timeline;
    }

    private static Transform FindBone(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static void SetField(Object target, string name, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(name).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(Object target, string name, float value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(name).floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Object target, string name, Object[] values)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
