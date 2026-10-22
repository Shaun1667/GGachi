using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 보스전에 필요한 프리팹과 씬 구성을 메뉴 한 번으로 만드는 에디터 도구.
///
/// Tools > Boss Fight > 1. Build Prefabs        : 플레이어 / 보스 / 깃털 / 예고 표시 / 공격 히트박스 프리팹 생성
/// Tools > Boss Fight > 2. Setup Current Scene  : 열린 씬(GGachi_Boss)에 프리팹 배치, 카메라 리그, 충돌체, GameManager, UI 구성
///
/// 두 메뉴 모두 여러 번 실행해도 같은 결과가 나오도록, 이전에 만든 것을 지우고 다시 만든다.
/// </summary>
public static class BossFightSetup
{
    private const string PrefabDir = "Assets/Prefabs/BossFight";
    private const string MaterialDir = "Assets/Materials/BossFight";
    private const string GoraniPath = "Assets/Resources/VARCO/Gorani/Gorani.fbx";
    private const string GoraniMaterialPath = "Assets/Resources/VARCO/Gorani/Gorani_Mat.mat";
    private const string AnimationDir = "Assets/Resources/Animation";
    private const string GoraniControllerPath = AnimationDir + "/Gorani.controller";
    private const string GGachiBodyPath = "Assets/Resources/VARCO/GGachi.fbx";        // Humanoid (Mixamo 리그)
    private const string GGachiWingsPath = "Assets/Resources/VARCO/GGachi_Wings.fbx"; // Generic, 가슴 뼈에 붙음

    private const float BossModelScale = 8f;

    /// <summary>플레이어(고라니) 전체 크기 배율. 키 약 1.75m × 2.75 ≈ 4.8m (경기장 크기에 맞춰 키움)</summary>
    private const float PlayerScale = 2.7533f;

    // ───────────────────────── 1. 프리팹 ─────────────────────────

    [MenuItem("Tools/Boss Fight/1. Build Prefabs")]
    public static void BuildPrefabs()
    {
        EnsureFolder("Assets/Prefabs", "BossFight");
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets/Materials", "BossFight");

        Material telegraphMat = TransparentUnlit("Telegraph_Mat", new Color(1f, 0.15f, 0.1f, 0.35f));
        Material featherMat = OpaqueUnlit("Feather_Mat", new Color(0.35f, 0.9f, 1f, 1f));

        // 예고 원: 지름 1짜리 납작한 원기둥. 보스가 루트 스케일을 (지름, 1, 지름)으로 맞춘다
        GameObject circle = new GameObject("Telegraph_Circle");
        AddVisual(circle.transform, PrimitiveType.Cylinder, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.01f, 1f), telegraphMat);
        GameObject circlePrefab = SavePrefab(circle);

        // 예고 선: 1x1 납작한 판. 보스가 루트 스케일을 (폭, 1, 길이)로 맞춘다
        GameObject line = new GameObject("Telegraph_Line");
        AddVisual(line.transform, PrimitiveType.Cube, Vector3.zero, Quaternion.identity, new Vector3(1f, 0.02f, 1f), telegraphMat);
        GameObject linePrefab = SavePrefab(line);

        // 깃털 투사체: Varco3D 깃털 모델 (모델이 없으면 진행 방향(z)으로 길쭉한 캡슐)
        GameObject feather = new GameObject("Feather_Projectile");
        feather.AddComponent<BossProjectile>();
        if (!AddFeatherModel(feather.transform))
            AddVisual(feather.transform, PrimitiveType.Capsule, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.45f, 0.9f, 0.2f), featherMat);
        GameObject featherPrefab = SavePrefab(feather);

        // 플레이어 공격 히트박스: 보이지 않는 트리거 박스 (보이는 효과는 할퀴기 이펙트가 담당)
        GameObject slash = new GameObject("Player_SlashHitbox");
        BoxCollider box = slash.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 0f, 1.2f);
        box.size = new Vector3(3.2f, 2.6f, 3.6f);
        slash.AddComponent<AttackHitbox>();
        GameObject slashPrefab = SavePrefab(slash);

        BuildPlayerPrefab(slashPrefab, BuildClawEffect());
        BuildBossPrefab(featherPrefab, circlePrefab, linePrefab);

        AssetDatabase.SaveAssets();
        Debug.Log("[BossFightSetup] 프리팹 생성 완료: " + PrefabDir);
    }

    private static void BuildPlayerPrefab(GameObject slashPrefab, GameObject clawPrefab)
    {
        GameObject player = new GameObject("Player");

        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 1.75f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        Health health = player.AddComponent<Health>();
        SetFields(health, ("team", (int)Team.Player), ("maxHealth", 5), ("invincibleTimeAfterHit", 1.0f));

        PlayerMovement movement = player.AddComponent<PlayerMovement>();
        SetFields(movement, ("moveSpeed", 9f), ("gravity", -20f), ("jumpHeight", 1.6f));

        GameObject attackPoint = new GameObject("AttackPoint");
        attackPoint.transform.SetParent(player.transform, false);
        attackPoint.transform.localPosition = new Vector3(0f, 1.1f, 0.6f);

        PlayerAttack attack = player.AddComponent<PlayerAttack>();
        attack.attack1 = slashPrefab;
        attack.clawEffect = clawPrefab;
        attack.attackPoint = attackPoint.transform;
        attack.damage = 10;
        attack.attackCooldown = 0.4f;

        // 고라니 모델 (Humanoid 리그, 키 약 1.75m). 발바닥이 루트 높이 0에 오도록 올린다
        GameObject goraniAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GoraniPath);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(goraniAsset);
        visual.name = "Visual";
        visual.transform.SetParent(player.transform, false);
        Material goraniMat = AssetDatabase.LoadAssetAtPath<Material>(GoraniMaterialPath);
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = goraniMat;
        Bounds b = RendererBounds(visual);
        visual.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

        // Humanoid 아바타 + Idle/Walk/Attack 컨트롤러 (Mixamo 클립)
        Animator animator = visual.GetComponent<Animator>();
        if (animator == null) animator = visual.AddComponent<Animator>();
        animator.avatar = FindAvatar(GoraniPath);
        animator.applyRootMotion = false; // 이동은 PlayerMovement가 담당, 애니메이션은 제자리에서만 재생
        animator.runtimeAnimatorController = BuildGoraniController();

        PlayerAnimator playerAnimator = player.AddComponent<PlayerAnimator>();
        SetFields(playerAnimator, ("animator", animator), ("movement", movement), ("attack", attack));

        player.AddComponent<HitFlash>(); // 렌더러 목록을 Awake에서 모으므로 모델을 붙인 뒤 추가

        // 루트 스케일로 키우면 CharacterController·공격 위치·히트박스(PlayerAttack이 스케일 반영)가 함께 커진다
        player.transform.localScale = Vector3.one * PlayerScale;

        SavePrefab(player);
    }

    // ───────────────────────── 깃털 투사체 모델 ─────────────────────────

    // 곧고 좌우 대칭인 깃털을 대각선으로 놓고 뽑은 모델 (똑바로 세워 뽑으면 끝과 깃대가 뭉개짐)
    private const string FeatherTextureDir = "Assets/Resources/VARCO/Feather/Feather_Diag0";
    private const string FeatherModelPath = FeatherTextureDir + "/Feather_Diag0.fbx";
    private const string FeatherMaterialPath = FeatherTextureDir + "/Feather_Mat.mat";
    private const float FeatherLength = 2.2f; // 투사체 길이 (m). 예전 캡슐(1.8m)보다 조금 길게

    /// <summary>
    /// 이미 만든 깃털 투사체 프리팹의 겉모습만 Varco3D 깃털 모델로 바꾼다 (판정·속도는 그대로). 여러 번 실행해도 같은 결과.
    /// </summary>
    [MenuItem("Tools/Boss Fight/Use Feather Model")]
    private static void UseFeatherModelMenu()
    {
        string path = PrefabDir + "/Feather_Projectile.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        Transform old = root.transform.Find("Visual");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        bool ok = AddFeatherModel(root.transform);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log(ok ? "[BossFightSetup] 깃털 투사체에 Varco3D 깃털 모델 적용 완료" : "[BossFightSetup] 깃털 모델이 없습니다: " + FeatherModelPath);
    }

    /// <summary>
    /// 깃털 모델을 "Visual"로 붙인다. 모델의 긴 축을 진행 방향(+z)으로, 얇은 축을 위(+y)로 눕히고,
    /// 깃대(흰 줄기) 쪽이 뒤로 가도록 깃털 끝을 앞으로 돌린다. 모델이 없으면 false
    /// </summary>
    private static bool AddFeatherModel(Transform parent)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(FeatherModelPath);
        if (asset == null) return false;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        model.transform.SetParent(visual.transform, false);

        Material mat = FeatherMaterial();
        var points = new List<Vector3>();
        foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>())
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; // 작은 투사체 여러 개라 그림자는 생략
            MeshFilter mf = r.GetComponent<MeshFilter>();
            Matrix4x4 toVisual = visual.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            foreach (Vector3 v in mf.sharedMesh.vertices) points.Add(toVisual.MultiplyPoint3x4(v));
        }
        foreach (Collider c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        // 깃털은 판처럼 얇으므로 가장 얇은 축 = 판의 법선. 판 안에서 정점이 가장 길게 퍼진 방향 = 깃털 길이 방향
        // (Varco3D 모델은 깃털이 대각선으로 놓여 있을 수 있어 축 방향 대신 정점 분포로 찾는다)
        Bounds b = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 p in points) b.Encapsulate(p);
        Vector3 upDir = Vector3.zero; upDir[MinAxis(b.size)] = 1f;
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in points) center += p;
        center /= points.Count;
        Vector3 longDir = PrincipalDirection(points, center, upDir);
        Vector3 sideDir = Vector3.Cross(upDir, longDir);

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (Vector3 p in points)
        {
            float t = Vector3.Dot(p - center, longDir);
            lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
        }
        float len = hi - lo;

        // 끝에서 12~25% 구간의 폭을 비교: 깃대 쪽은 깃가지가 막 시작해 좁고, 깃털 끝 쪽은 넓다
        float widthLo = 0f, widthHi = 0f;
        foreach (Vector3 p in points)
        {
            float t = (Vector3.Dot(p - center, longDir) - lo) / len;
            float w = Mathf.Abs(Vector3.Dot(p - center, sideDir));
            if (t > 0.12f && t < 0.25f) widthLo = Mathf.Max(widthLo, w);
            else if (t > 0.75f && t < 0.88f) widthHi = Mathf.Max(widthHi, w);
        }
        Vector3 tipDir = widthHi >= widthLo ? longDir : -longDir;
        Vector3 mid = center + longDir * ((lo + hi) * 0.5f); // 길이 방향 가운데

        Quaternion toForward = Quaternion.Inverse(Quaternion.LookRotation(tipDir, upDir));
        float scale = FeatherLength / len;
        model.transform.localPosition = toForward * (-mid * scale);
        model.transform.localRotation = toForward * model.transform.localRotation;
        model.transform.localScale = model.transform.localScale * scale;
        return true;
    }

    /// <summary>normal에 수직인 평면에서 점들이 가장 길게 퍼진 방향 (2차원 공분산의 주축)</summary>
    private static Vector3 PrincipalDirection(List<Vector3> points, Vector3 center, Vector3 normal)
    {
        Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 c = Vector3.Cross(normal, a);
        float xx = 0f, xy = 0f, yy = 0f;
        foreach (Vector3 p in points)
        {
            float x = Vector3.Dot(p - center, a), y = Vector3.Dot(p - center, c);
            xx += x * x; xy += x * y; yy += y * y;
        }
        float angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
        return (a * Mathf.Cos(angle) + c * Mathf.Sin(angle)).normalized;
    }

    private static int MinAxis(Vector3 v) => v.x <= v.y && v.x <= v.z ? 0 : (v.y <= v.z ? 1 : 2);

    /// <summary>깃털 텍스처(색·노멀)를 쓰는 URP Lit 재질. 모델에서 뽑아 둔 텍스처를 쓴다</summary>
    private static Material FeatherMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(FeatherMaterialPath);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, FeatherMaterialPath);
        }
        Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(FeatherTextureDir + "/base_color.png");
        string normalPath = FeatherTextureDir + "/normal.png";
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
        {
            ni.textureType = TextureImporterType.NormalMap;
            ni.SaveAndReimport();
        }
        Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        m.SetTexture("_BaseMap", baseTex);
        m.SetColor("_BaseColor", Color.white);
        if (normalTex != null)
        {
            m.SetTexture("_BumpMap", normalTex);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Smoothness", 0.55f); // 깃털 광택
        m.SetFloat("_Cull", (float)CullMode.Off); // 얇은 판이라 뒷면도 보이게
        EditorUtility.SetDirty(m);
        return m;
    }

    // ───────────────────────── 할퀴기 이펙트 ─────────────────────────

    // Slash Effects FREE(Matthew Guz)의 가는 베기 줄 텍스처. 패키지 재질은 기본 렌더 파이프라인용이라 URP 재질을 새로 만든다
    private const string ClawTexturePath = "Assets/AssetStore/Matthew Guz/Slash Effects FREE/Textures/5-Slash S.png";
    private const float ClawLength = 3.4f;   // 가운데 줄 길이 (플레이어 스케일 전, m). 뒤에서 봐도 몸 밖으로 삐져나오게
    private const float ClawWidth = 1.6f;    // 텍스처 한 장의 세로 폭 (보이는 줄 굵기는 그중 가운데 일부)
    private const float ClawSpacing = 0.36f; // 줄 사이 간격
    private const float ClawForward = 1.2f;  // attackPoint보다 앞으로 (몸에 가려지지 않게)
    private const float ClawUp = 0.35f;      // attackPoint(가슴)보다 위로, 뒤에서 볼 때 어깨 위로 보이게

    /// <summary>
    /// 할퀴기 이펙트를 다시 만들고 플레이어(프리팹·씬)에 연결한다. 예전 반투명 원판 베기 효과는 지운다.
    /// 여러 번 실행해도 같은 결과.
    /// </summary>
    [MenuItem("Tools/Boss Fight/Build Claw Slash Effect")]
    private static void BuildClawEffectMenu()
    {
        GameObject claw = BuildClawEffect();

        string playerPath = PrefabDir + "/Player.prefab";
        GameObject player = PrefabUtility.LoadPrefabContents(playerPath);
        player.GetComponent<PlayerAttack>().clawEffect = claw;
        PrefabUtility.SaveAsPrefabAsset(player, playerPath);
        PrefabUtility.UnloadPrefabContents(player);
        foreach (PlayerAttack a in Object.FindObjectsByType<PlayerAttack>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(a, "Assign Claw Effect");
            a.clawEffect = claw;
            EditorUtility.SetDirty(a);
        }

        string hitboxPath = PrefabDir + "/Player_SlashHitbox.prefab";
        GameObject hitbox = PrefabUtility.LoadPrefabContents(hitboxPath);
        Transform oldVisual = hitbox.transform.Find("Visual");
        if (oldVisual != null) Object.DestroyImmediate(oldVisual.gameObject);
        PrefabUtility.SaveAsPrefabAsset(hitbox, hitboxPath);
        PrefabUtility.UnloadPrefabContents(hitbox);

        AssetDatabase.SaveAssets();
        Debug.Log("[BossFightSetup] 할퀴기 이펙트 생성·연결 완료");
    }

    /// <summary>
    /// 세 줄 할퀴기: 오른손 기준으로 오른쪽 위에서 왼쪽 아래로 나란히 그어진다 (오른손 공격이 오른쪽→왼쪽으로 휘두름).
    /// 줄마다 피벗을 시작점에 두고 +x가 긋는 방향이 되게 돌려, ClawSlashEffect가 x 스케일로 그린다.
    /// </summary>
    public static GameObject BuildClawEffect()
    {
        EnsureFolder("Assets/Prefabs", "BossFight");
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets/Materials", "BossFight");
        Material mat = AdditiveTextured("ClawSlash_Mat", AssetDatabase.LoadAssetAtPath<Texture2D>(ClawTexturePath));

        GameObject root = new GameObject("Player_ClawSlash");
        ClawSlashEffect effect = root.AddComponent<ClawSlashEffect>();
        Vector3 dir = new Vector3(-1f, -0.45f, 0f).normalized;
        Vector3 across = new Vector3(-dir.y, dir.x, 0f);
        var pivots = new Object[3];
        for (int i = 0; i < 3; i++)
        {
            float length = ClawLength * (i == 1 ? 1f : 0.85f); // 가운데 줄이 가장 길다
            Vector3 center = new Vector3(0f, ClawUp, ClawForward) + across * ((i - 1) * ClawSpacing);

            GameObject pivot = new GameObject("Line" + (i + 1));
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = center - dir * (length * 0.5f);
            pivot.transform.localRotation = Quaternion.FromToRotation(Vector3.right, dir);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Visual";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(pivot.transform, false);
            quad.transform.localPosition = new Vector3(length * 0.5f, 0f, 0f);
            quad.transform.localScale = new Vector3(-length, ClawWidth, 1f); // 텍스처의 뾰족한 머리가 긋는 끝(+x)에 오게 뒤집음
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            pivots[i] = pivot.transform;
        }
        SetArray(effect, "lines", pivots);
        return SavePrefab(root);
    }

    /// <summary>
    /// 고라니 Animator 컨트롤러.
    ///  - Locomotion (2D 방향 블렌드 트리, MoveX/MoveY): 대기 / 앞 걷기 / 뒤로 걷기 / 왼쪽 걷기 / 오른쪽 걷기
    ///  - 어느 상태에서든 Attack(trigger) → AttackLeft(bool)에 따라 오른손/왼손 공격 → 끝나면 Locomotion으로 복귀
    ///  - 어느 상태에서든 Jump(trigger) → 점프 → 끝나면 Locomotion으로 복귀
    ///  - 어느 상태에서든 Roll(trigger) → 구르기 → 끝나면 Locomotion으로 복귀
    /// 이미 있으면 같은 에셋(GUID 유지) 안의 상태만 다시 만든다.
    /// </summary>
    [MenuItem("Tools/Boss Fight/Build Gorani Animator")]
    private static void BuildGoraniControllerMenu() => BuildGoraniController();

    public static AnimatorController BuildGoraniController()
    {
        ImportGoraniClips();

        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(GoraniControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(GoraniControllerPath);

        // 초기화 (예전 블렌드 트리 서브에셋까지 지워서 컨트롤러 파일에 찌꺼기가 쌓이지 않게)
        foreach (AnimatorControllerParameter p in ctrl.parameters) ctrl.RemoveParameter(p);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        foreach (ChildAnimatorState s in sm.states) sm.RemoveState(s.state);
        foreach (AnimatorStateTransition t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(GoraniControllerPath))
        {
            if (o is BlendTree) Object.DestroyImmediate(o, true);
        }

        ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("MoveY", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("AttackLeft", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Roll", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        // 이동: 입력 방향(MoveX, MoveY)에 따라 다섯 동작을 섞는다
        AnimatorState locomotion = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.SimpleDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";
        tree.AddChild(LoadClip(AnimationDir + "/Idle.fbx"), Vector2.zero);
        tree.AddChild(LoadClip(AnimationDir + "/Walk.fbx"), new Vector2(0f, 1f));
        tree.AddChild(LoadClip(AnimationDir + "/Walking Backward.fbx"), new Vector2(0f, -1f));
        tree.AddChild(LoadClip(AnimationDir + "/Left Strafe Walking.fbx"), new Vector2(-1f, 0f));
        tree.AddChild(LoadClip(AnimationDir + "/Right Strafe Walking.fbx"), new Vector2(1f, 0f));

        // 이동 속도(초당 9m)가 걷기 동작보다 빨라서 걷기 계열은 모두 2배속
        ChildMotion[] children = tree.children;
        for (int i = 1; i < children.Length; i++) children[i].timeScale = WalkPlaybackSpeed;
        tree.children = children;

        sm.defaultState = locomotion;

        // 점프: 뛰어오른 순간 재생, 착지할 즈음 이동으로 복귀
        AnimatorState jumpState = sm.AddState("Jump");
        jumpState.motion = LoadClip(AnimationDir + "/Jump.fbx");
        jumpState.speed = JumpPlaybackSpeed;
        AnimatorStateTransition toJump = sm.AddAnyStateTransition(jumpState);
        toJump.hasExitTime = false;
        toJump.duration = 0.05f;
        toJump.canTransitionToSelf = false;
        toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        AnimatorStateTransition jumpBack = jumpState.AddTransition(locomotion);
        jumpBack.hasExitTime = true;
        jumpBack.exitTime = 0.9f;
        jumpBack.duration = 0.1f;

        // 구르기: 클립 앞의 준비 동작(0~0.36초)은 건너뛰고 구르기~일어나기(0.36~2.0초)만 구르기 시간에 맞춰 재생
        AnimatorState rollState = sm.AddState("Roll");
        rollState.motion = LoadClip(AnimationDir + "/Stand To Roll.fbx");
        rollState.speed = RollPlaybackSpeed;
        AnimatorStateTransition toRoll = sm.AddAnyStateTransition(rollState);
        toRoll.hasExitTime = false;
        toRoll.duration = 0.05f;
        toRoll.offset = RollClipStart;
        toRoll.canTransitionToSelf = false;
        toRoll.AddCondition(AnimatorConditionMode.If, 0f, "Roll");
        AnimatorStateTransition rollBack = rollState.AddTransition(locomotion);
        rollBack.hasExitTime = true;
        rollBack.exitTime = RollClipEnd;
        rollBack.duration = 0.1f;

        // 공격: 오른손(원본) / 왼손(같은 클립을 좌우 반전). AttackLeft 값으로 어느 손인지 고른다
        AnimationClip attackClip = LoadClip(AnimationDir + "/Attack.fbx");
        foreach (bool left in new[] { false, true })
        {
            AnimatorState attackState = sm.AddState(left ? "AttackLeft" : "Attack");
            attackState.motion = attackClip;
            attackState.mirror = left;
            attackState.speed = AttackPlaybackSpeed;

            AnimatorStateTransition toAttack = sm.AddAnyStateTransition(attackState);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.05f;
            toAttack.canTransitionToSelf = true; // 연타하면 공격 동작을 처음부터 다시
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            toAttack.AddCondition(left ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "AttackLeft");

            AnimatorStateTransition back = attackState.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.15f;
        }

        // 게임오버: 쓰러지는 동작을 한 번 재생하고 쓰러진 자세로 멈춘다 (돌아가는 전환 없음)
        AnimatorState deathState = sm.AddState("Death");
        deathState.motion = LoadClip(AnimationDir + "/Death From Right.fbx");
        AnimatorStateTransition toDeath = sm.AddAnyStateTransition(deathState);
        toDeath.hasExitTime = false;
        toDeath.duration = 0.1f;
        toDeath.canTransitionToSelf = false;
        toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    private const float WalkPlaybackSpeed = 2f;

    // 2.63초짜리 공격 클립을 0.88초로. 손이 가장 앞으로 나오는 1.05초 지점이 0.35초에 온다 (PlayerAttack.hitDelay)
    private const float AttackPlaybackSpeed = 3f;

    // 1초짜리 점프 클립을 체공 시간(점프 높이 1.6m, 중력 -20 → 0.8초)에 맞춤
    private const float JumpPlaybackSpeed = 1.25f;

    // 2.37초짜리 구르기 클립 중 0.36~2.0초 구간(정규화 0.15~0.85)을 구르기 시간 0.8초(PlayerMovement.rollDuration)에 맞춤
    private const float RollClipStart = 0.15f;
    private const float RollClipEnd = 0.85f;
    private const float RollPlaybackSpeed = 2.07f;

    /// <summary>
    /// Mixamo 애니메이션 FBX를 Humanoid로 임포트하고 클립 설정(반복, 제자리)을 맞춘다.
    /// 애니메이션 FBX는 고라니와 뼈 계층이 달라(Armature 상위 오브젝트 없음) 각 파일에서 아바타를 만든다.
    /// </summary>
    private static void ImportGoraniClips()
    {
        var clips = new (string file, string clipName, bool loop)[]
        {
            ("Idle", "Gorani_Idle", true),
            ("Walk", "Gorani_Walk", true),
            ("Walking Backward", "Gorani_WalkBackward", true),
            ("Left Strafe Walking", "Gorani_StrafeLeft", true),
            ("Right Strafe Walking", "Gorani_StrafeRight", true),
            ("Attack", "Gorani_Attack", false),
            ("Jump", "Gorani_Jump", false),
            ("Stand To Roll", "Gorani_Roll", false),
            ("Death From Right", "Death_FromRight", false), // 고라니 게임오버 + 까치 쓰러짐 (GGachiAnimationBaker도 사용)
        };
        foreach (var (file, clipName, loop) in clips)
        {
            // 점프 높이는 PlayerMovement(CharacterController)가 정하므로, 점프 클립의 몸 오르내림은 버린다
            bool keepHeightInPose = file != "Jump";
            // 쓰러지며 옆으로 밀려나는 이동은 루트 모션을 쓰지 않으므로 자세에 굽는다 (안 그러면 제자리에서 쓰러짐)
            bool keepTravelInPose = file == "Death From Right";
            string path = $"{AnimationDir}/{file}.fbx";
            ModelImporter imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null)
            {
                Debug.LogError("[BossFightSetup] 애니메이션 파일이 없습니다: " + path);
                continue;
            }
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.clipAnimations = new ModelImporterClipAnimation[0];
                imp.SaveAndReimport();
            }
            ModelImporterClipAnimation[] settings = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            for (int i = 0; i < settings.Length; i++)
            {
                settings[i].name = clipName;
                settings[i].loopTime = loop;
                settings[i].loopPose = loop;
                settings[i].lockRootRotation = true;      // 방향은 PlayerMovement가 정함
                settings[i].keepOriginalOrientation = true;
                settings[i].lockRootHeightY = keepHeightInPose; // 발이 바닥에서 뜨지 않게
                settings[i].keepOriginalPositionY = true;
                settings[i].lockRootPositionXZ = keepTravelInPose;
                if (keepTravelInPose) settings[i].keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = settings;
            imp.SaveAndReimport();
        }
    }

    private static AnimationClip LoadClip(string modelPath)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        }
        Debug.LogError("[BossFightSetup] 애니메이션 클립을 찾지 못했습니다: " + modelPath);
        return null;
    }

    private static void BuildBossPrefab(GameObject featherPrefab, GameObject circlePrefab, GameObject linePrefab)
    {
        GameObject boss = new GameObject("Boss_GGachi");

        var (model, animator, wingAnimator) = AttachGGachiModel(boss);

        Bounds b = RendererBounds(model);
        CapsuleCollider capsule = boss.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, b.size.y * 0.5f, 0f);
        capsule.radius = Mathf.Min(b.extents.x, b.extents.z) * 0.9f + 0.5f;
        capsule.height = b.size.y;

        Rigidbody rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true; // 물리에 밀리지 않고 스크립트가 직접 움직인다
        rb.useGravity = false;

        Health health = boss.AddComponent<Health>();
        SetFields(health, ("team", (int)Team.Enemy), ("maxHealth", 300), ("invincibleTimeAfterHit", 0f));

        BossController controller = boss.AddComponent<BossController>();
        SetFields(controller,
            ("animator", animator),
            ("wingAnimator", wingAnimator),
            ("bodyRadius", capsule.radius),
            ("featherPrefab", featherPrefab.GetComponent<BossProjectile>()),
            ("circleTelegraphPrefab", circlePrefab),
            ("lineTelegraphPrefab", linePrefab));

        boss.AddComponent<HitFlash>();

        SavePrefab(boss);
    }

    /// <summary>
    /// 까치 모델을 보스 루트 아래에 붙인다 (이전 모델은 지움).
    ///  - 몸통 GGachi_Body (Humanoid): 보스 몸통 애니메이션 담당
    ///  - 날개 GGachi_Wings (Generic): 몸통의 가슴 뼈(Spine1) 자식으로 붙여 몸통을 따라 움직이고, 날갯짓은 자기 Animator로 재생
    /// 두 FBX는 Blender에서 같은 좌표로 내보냈으므로, 같은 위치·크기로 놓은 뒤 월드 위치를 유지한 채 가슴 뼈로 옮기면 딱 맞는다.
    /// </summary>
    public static (GameObject model, Animator body, Animator wings) AttachGGachiModel(GameObject boss)
    {
        foreach (string old in new[] { "GGachi", "GGachi_Body" })
        {
            Transform t = boss.transform.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GGachiBodyPath));
        body.name = "GGachi_Body";
        body.transform.SetParent(boss.transform, false);
        body.transform.localScale = Vector3.one * BossModelScale;

        Animator bodyAnim = body.GetComponent<Animator>();
        if (bodyAnim == null) bodyAnim = body.AddComponent<Animator>();
        bodyAnim.avatar = FindAvatar(GGachiBodyPath);
        bodyAnim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GGachiAnimationBaker.BodyControllerPath);
        bodyAnim.applyRootMotion = false;

        GameObject wings = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GGachiWingsPath));
        wings.name = "GGachi_Wings";
        wings.transform.SetParent(boss.transform, false);
        wings.transform.localScale = Vector3.one * BossModelScale;
        // 날개 뿌리는 어깨 높이라 윗가슴 뼈(Mixamo Spine2)에 붙인다. 없는 리그면 가슴 뼈
        Transform chest = bodyAnim.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = bodyAnim.GetBoneTransform(HumanBodyBones.Chest);
        wings.transform.SetParent(chest, true); // 월드 위치 유지 → 가슴 뼈를 따라감

        Animator wingAnim = wings.GetComponent<Animator>();
        if (wingAnim == null) wingAnim = wings.AddComponent<Animator>();
        wingAnim.avatar = null;
        wingAnim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GGachiAnimationBaker.WingsControllerPath);
        wingAnim.applyRootMotion = false;

        return (body, bodyAnim, wingAnim);
    }

    /// <summary>이미 있는 보스 프리팹의 모델만 몸통+날개로 바꾼다 (씬에 놓인 보스 위치 등은 그대로)</summary>
    [MenuItem("Tools/Boss Fight/Swap Boss Model (Body + Wings)")]
    public static void SwapBossModelInPrefab()
    {
        string path = PrefabDir + "/Boss_GGachi.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var (_, body, wings) = AttachGGachiModel(root);
            SetFields(root.GetComponent<BossController>(), ("animator", body), ("wingAnimator", wings));
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log("[BossFightSetup] 보스 모델을 몸통+날개로 교체했습니다.");
    }

    // ───────────────────────── 2. 씬 구성 ─────────────────────────

    [MenuItem("Tools/Boss Fight/2. Setup Current Scene")]
    public static void SetupScene()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Player.prefab");
        GameObject bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Boss_GGachi.prefab");
        if (playerPrefab == null || bossPrefab == null)
        {
            BuildPrefabs();
            playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Player.prefab");
            bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Boss_GGachi.prefab");
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject arena = GameObject.Find("BossArena");
        Transform floor = arena != null ? arena.transform.Find("Arena_Floor") : null;
        if (floor == null)
        {
            Debug.LogError("[BossFightSetup] BossArena/Arena_Floor를 찾지 못했습니다. GGachi_Boss 씬을 열고 실행하세요.");
            return;
        }

        // 바닥 중심 / 디딤면 높이 / 반지름
        Bounds floorBounds = RendererBounds(floor.gameObject);
        float surfaceY = WalkSurfaceY(floor, floorBounds);
        Vector3 center = new Vector3(floorBounds.center.x, surfaceY, floorBounds.center.z);
        float floorRadius = floorBounds.extents.x;

        // 경기장 중심 표시
        Transform arenaCenter = arena.transform.Find("ArenaCenter");
        if (arenaCenter == null)
        {
            arenaCenter = new GameObject("ArenaCenter").transform;
            arenaCenter.SetParent(arena.transform, false);
        }
        arenaCenter.position = center;

        // 바닥·기둥에 충돌체 (없을 때만 추가)
        foreach (MeshFilter mf in arena.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
            {
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        // 이전 실행 결과와 예전 보스 모델 정리
        DestroyRoots("Player", "Boss_GGachi", "CameraTarget", "GameManager", "GameUI_Canvas", "GGachi", "TmpCap");

        // 보스: 경기장 중심보다 조금 뒤(보물 더미 쪽), 플레이어를 바라봄(+z)
        GameObject boss = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, scene);
        boss.transform.position = center + new Vector3(0f, 0f, -floorRadius * 0.2f);
        boss.transform.rotation = Quaternion.identity;

        // 플레이어: 경기장 앞쪽, 보스를 바라봄(-z)
        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        player.transform.position = center + new Vector3(0f, 0.1f, floorRadius * 0.5f);
        player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        // 카메라 리그: CameraTarget(좌우) > CameraPivot(상하) > Main Camera
        GameObject target = new GameObject("CameraTarget");
        SceneManager.MoveGameObjectToScene(target, scene);
        target.transform.SetPositionAndRotation(player.transform.position, Quaternion.Euler(0f, 180f, 0f));
        GameObject pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(target.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 1.8f * PlayerScale, 0f); // 캐릭터 머리 높이
        pivot.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);

        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.transform.SetParent(pivot.transform, false);
        cam.transform.localPosition = new Vector3(0f, 0.5f * PlayerScale, -5.5f * PlayerScale); // 캐릭터 크기에 비례한 거리
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 2000f;
        CameraShake shake = cam.GetComponent<CameraShake>();
        if (shake == null) shake = cam.gameObject.AddComponent<CameraShake>();

        CameraController camController = target.AddComponent<CameraController>();
        SetFields(camController, ("target", player.transform), ("pivot", pivot.transform), ("minPitch", -20f), ("maxPitch", 60f));

        // 참조 연결
        Health playerHealth = player.GetComponent<Health>();
        Health bossHealth = boss.GetComponent<Health>();
        BossController bossController = boss.GetComponent<BossController>();

        SetFields(player.GetComponent<PlayerMovement>(), ("cameraController", camController));
        SetFields(bossController, ("playerHealth", playerHealth), ("arenaCenter", arenaCenter), ("arenaRadius", floorRadius - 3f));
        SetFields(shake, ("boss", bossController));

        // UI
        GameUI ui = BuildUI(scene, playerHealth, bossHealth);

        // GameManager
        GameObject gmObject = new GameObject("GameManager");
        SceneManager.MoveGameObjectToScene(gmObject, scene);
        GameManager gm = gmObject.AddComponent<GameManager>();
        SetFields(gm,
            ("playerHealth", playerHealth),
            ("bossHealth", bossHealth),
            ("boss", bossController),
            ("ui", ui),
            ("fallDeathY", surfaceY - 15f));
        SetArray(gm, "playerControls", new Object[]
        {
            player.GetComponent<PlayerMovement>(),
            player.GetComponent<PlayerAttack>(),
            camController
        });

        AddSceneToBuildSettings(scene.path);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[BossFightSetup] 씬 구성 완료. 씬을 저장(Ctrl+S)한 뒤 Play로 확인하세요.");
    }

    // ───────────────────────── UI ─────────────────────────

    private static GameUI BuildUI(Scene scene, Health playerHealth, Health bossHealth)
    {
        GameObject canvasGo = new GameObject("GameUI_Canvas");
        SceneManager.MoveGameObjectToScene(canvasGo, scene);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // HUD
        RectTransform hud = Stretch(NewRect("HUD", canvasGo.transform));

        RectTransform bossBar = NewRect("BossBar", hud);
        Place(bossBar, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(900f, 30f));
        Text bossName = MakeText("BossName", bossBar, "까치 대장", 34, TextAnchor.LowerCenter, Color.white);
        Place(bossName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 26f), new Vector2(900f, 50f));
        RectTransform bossFill = MakeBar(bossBar, new Color(0.85f, 0.15f, 0.2f));
        Text bossHp = MakeText("BossHpText", bossBar, "300 / 300", 22, TextAnchor.MiddleCenter, Color.white);
        Stretch(bossHp.rectTransform);

        RectTransform playerBar = NewRect("PlayerBar", hud);
        Place(playerBar, new Vector2(0f, 0f), new Vector2(280f, 70f), new Vector2(440f, 26f));
        RectTransform playerFill = MakeBar(playerBar, new Color(0.35f, 0.85f, 0.4f));
        Text playerHp = MakeText("PlayerHpText", playerBar, "HP 5 / 5", 26, TextAnchor.LowerLeft, Color.white);
        Place(playerHp.rectTransform, new Vector2(0f, 1f), new Vector2(220f, 22f), new Vector2(440f, 40f));

        Text timer = MakeText("Timer", hud, "00:00", 44, TextAnchor.MiddleRight, Color.white);
        Place(timer.rectTransform, new Vector2(1f, 1f), new Vector2(-140f, -60f), new Vector2(240f, 60f));

        Text banner = MakeText("Banner", hud, "", 36, TextAnchor.MiddleCenter, new Color(1f, 0.7f, 0.3f));
        Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1400f, 60f));

        Text hint = MakeText("ControlsHint", hud, "WASD 이동 · 마우스 시점 · 좌클릭 공격 · Space 점프 · Shift 구르기", 22,
            TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.7f));
        Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-460f, 50f), new Vector2(860f, 40f));

        // 가운데 패널 (시작 / 결과)
        RectTransform panel = Stretch(NewRect("CenterPanel", canvasGo.transform));
        Image dim = panel.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        Text title = MakeText("Title", panel, "", 84, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f));
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1400f, 120f));
        Text body = MakeText("Body", panel, "", 34, TextAnchor.UpperCenter, Color.white);
        Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(1400f, 420f));

        GameUI ui = canvasGo.AddComponent<GameUI>();
        SetFields(ui,
            ("playerHealth", playerHealth),
            ("bossHealth", bossHealth),
            ("hudRoot", hud.gameObject),
            ("bossBarFill", bossFill),
            ("playerBarFill", playerFill),
            ("bossHpText", bossHp),
            ("playerHpText", playerHp),
            ("timerText", timer),
            ("bannerText", banner),
            ("centerPanel", panel.gameObject),
            ("centerTitle", title),
            ("centerBody", body));
        BossMenuSetup.Build(ui); // 시작 화면 버튼 / 일시정지 화면 / EventSystem
        return ui;
    }

    private static RectTransform MakeBar(RectTransform parent, Color fillColor)
    {
        Image bg = NewRect("Background", parent).gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        Stretch(bg.rectTransform);

        Image fill = NewRect("Fill", parent).gameObject.AddComponent<Image>();
        fill.color = fillColor;
        Stretch(fill.rectTransform); // GameUI가 anchorMax.x를 줄여서 체력 비율을 표시
        return fill.rectTransform;
    }

    private static Text MakeText(string name, Transform parent, string content, int size, TextAnchor anchor, Color color)
    {
        Text t = NewRect(name, parent).gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        Outline outline = t.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        return t;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    // ───────────────────────── 공통 도우미 ─────────────────────────

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    private static GameObject SavePrefab(GameObject root)
    {
        string path = $"{PrefabDir}/{root.name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path); // 같은 경로면 GUID를 유지한 채 덮어씀
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void AddVisual(Transform parent, PrimitiveType type, Vector3 localPos, Quaternion localRot, Vector3 localScale, Material mat)
    {
        GameObject v = GameObject.CreatePrimitive(type);
        v.name = "Visual";
        Object.DestroyImmediate(v.GetComponent<Collider>()); // 판정은 스크립트/루트 콜라이더가 담당
        v.transform.SetParent(parent, false);
        v.transform.localPosition = localPos;
        v.transform.localRotation = localRot;
        v.transform.localScale = localScale;
        MeshRenderer mr = v.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Material TransparentUnlit(string name, Color color)
    {
        Material m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>더해서 빛나는 반투명 재질 (앞뒤 모두 보임). 색과 알파는 MaterialPropertyBlock(_BaseColor)으로 바꾼다</summary>
    private static Material AdditiveTextured(string name, Texture texture)
    {
        Material m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetTexture("_BaseMap", texture);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material OpaqueUnlit(string name, Color color)
    {
        Material m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LoadOrCreateMaterial(string name, string shaderName)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shaderName));
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    private static Bounds RendererBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>바닥 안쪽(반지름 35% 이내)에서 가장 높은 정점 = 사람이 서는 디딤면 높이</summary>
    private static float WalkSurfaceY(Transform floor, Bounds floorBounds)
    {
        MeshFilter mf = floor.GetComponentInChildren<MeshFilter>();
        float top = floorBounds.center.y;
        if (mf == null || mf.sharedMesh == null) return top;
        top = float.MinValue;
        Vector3 c = floorBounds.center;
        float r = floorBounds.extents.x * 0.35f;
        foreach (Vector3 v in mf.sharedMesh.vertices)
        {
            Vector3 w = mf.transform.TransformPoint(v);
            if (new Vector2(w.x - c.x, w.z - c.z).magnitude < r) top = Mathf.Max(top, w.y);
        }
        return top;
    }

    private static Avatar FindAvatar(string modelPath)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (o is Avatar a) return a;
        }
        return null;
    }

    private static void DestroyRoots(params string[] names)
    {
        Scene scene = SceneManager.GetActiveScene();
        var set = new HashSet<string>(names);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!set.Contains(root.name)) continue;
            // 카메라 리그를 지울 때 Main Camera까지 지워지지 않도록 먼저 꺼낸다
            Camera cam = root.GetComponentInChildren<Camera>();
            if (cam != null && cam.CompareTag("MainCamera")) cam.transform.SetParent(null, true);
            Object.DestroyImmediate(root);
        }
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int index = scenes.FindIndex(s => s.path == scenePath);
        if (index == 0) return;
        if (index > 0) scenes.RemoveAt(index);
        scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true)); // 실행 빌드의 첫 씬으로
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>private [SerializeField] 필드를 이름으로 채운다.</summary>
    private static void SetFields(Object target, params (string name, object value)[] fields)
    {
        SerializedObject so = new SerializedObject(target);
        foreach (var (name, value) in fields)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null)
            {
                Debug.LogError($"[BossFightSetup] {target.GetType().Name}에 '{name}' 필드가 없습니다.");
                continue;
            }
            switch (value)
            {
                case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case bool bo: p.boolValue = bo; break;
                case Object o: p.objectReferenceValue = o; break;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Object target, string name, Object[] values)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(name);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
