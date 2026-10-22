using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 블러드본 같은 "어두운 고딕 밤" 분위기를 씬에 입히는 에디터 메뉴.
///
/// Tools > Boss Fight > 3. Apply Dark Gothic Look
///  - 포스트 프로세싱 볼륨: 청록빛 그림자 / 낮은 채도 / 높은 대비 / 블룸 / 비네트 / 필름 그레인
///  - 거리 안개(청록), 차가운 달빛, 보물 더미의 따뜻한 등불빛, 어두운 하늘
/// 여러 번 실행해도 같은 결과가 나오도록 기존 볼륨/조명을 찾아 값을 덮어쓴다.
/// 수치를 바꾸고 싶으면 생성된 프로파일(Assets/Settings/DarkGothic_Profile.asset)을 인스펙터에서 조절하면 된다.
/// </summary>
public static class DarkGothicLook
{
    private const string ProfilePath = "Assets/Settings/DarkGothic_Profile.asset";

    // 안개·조명 색 (참고 이미지의 청록 밤빛)
    // (1차 적용 후 "너무 어둡다"는 피드백으로 안개·주변광을 밝게 올림)
    private static readonly Color FogColor = new Color(0.27f, 0.37f, 0.39f);
    private static readonly Color MoonColor = new Color(0.70f, 0.83f, 0.90f);
    private static readonly Color AmbientSky = new Color(0.30f, 0.40f, 0.43f);
    private static readonly Color AmbientEquator = new Color(0.20f, 0.27f, 0.29f);
    private static readonly Color AmbientGround = new Color(0.11f, 0.14f, 0.15f);
    private const float FogDensity = 0.032f;
    private const float MoonIntensity = 1.35f;
    private const float SkyExposure = 0.9f;
    private static readonly Color LanternColor = new Color(1f, 0.62f, 0.3f);

    [MenuItem("Tools/Boss Fight/3. Apply Dark Gothic Look")]
    public static void Apply()
    {
        VolumeProfile profile = BuildProfile();
        SetupVolume(profile);
        SetupCamera();
        SetupFogAndAmbient();
        SetupMoonLight();
        SetupSky();
        SetupLanterns();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[DarkGothicLook] 적용 완료. 씬을 저장(Ctrl+S)하세요.");
    }

    // ───────────────────────── 포스트 프로세싱 ─────────────────────────

    private static VolumeProfile BuildProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // 영화 같은 톤: 밝은 부분이 하얗게 날아가지 않고 부드럽게 눌린다
        Tonemapping tone = Get<Tonemapping>(profile);
        // (ACES는 이 씬처럼 원래 어두운 장면을 너무 눌러서 거의 검게 만들어 Neutral 사용)
        tone.mode.Override(TonemappingMode.Neutral);

        // 대비는 높게, 채도는 낮게, 청록 필터
        ColorAdjustments color = Get<ColorAdjustments>(profile);
        color.postExposure.Override(0.55f);
        color.contrast.Override(18f);
        color.saturation.Override(-30f);
        color.colorFilter.Override(new Color(0.88f, 0.97f, 0.98f));

        // 그림자는 청록, 밝은 부분은 살짝 따뜻하게 (참고 이미지의 등불/달빛 대비)
        // 0.5 회색이 "변화 없음"이라 그 근처에서 살짝만 기울인다
        SplitToning split = Get<SplitToning>(profile);
        split.shadows.Override(new Color(0.40f, 0.56f, 0.56f));
        split.highlights.Override(new Color(0.58f, 0.54f, 0.47f));
        split.balance.Override(-10f);

        // 어두운 영역을 살짝만 가라앉혀 깊이감
        LiftGammaGain lgg = Get<LiftGammaGain>(profile);
        lgg.lift.Override(new Vector4(0.98f, 1.0f, 1.02f, 0f));
        lgg.gamma.Override(new Vector4(0.98f, 1.0f, 1.01f, 0f));
        lgg.gain.Override(new Vector4(1.0f, 1.0f, 1.0f, 0.02f));

        // 달·등불·크리스탈처럼 밝은 것만 번지게
        Bloom bloom = Get<Bloom>(profile);
        bloom.threshold.Override(0.85f);
        bloom.intensity.Override(1.2f);
        bloom.scatter.Override(0.75f);
        bloom.tint.Override(new Color(0.85f, 0.95f, 1f));
        bloom.highQualityFiltering.Override(true);

        // 화면 가장자리를 어둡게 → 시선이 가운데(보스)로
        Vignette vignette = Get<Vignette>(profile);
        vignette.color.Override(new Color(0.02f, 0.04f, 0.05f));
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.45f);

        // 오래된 필름 느낌의 입자
        FilmGrain grain = Get<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Medium3);
        grain.intensity.Override(0.25f);
        grain.response.Override(0.8f);

        // 렌즈 가장자리의 아주 약한 색 번짐
        ChromaticAberration ca = Get<ChromaticAberration>(profile);
        ca.intensity.Override(0.12f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile); // 프로파일 에셋 안에 같이 저장
        }
        component.active = true;
        return component;
    }

    private static void SetupVolume(VolumeProfile profile)
    {
        GameObject go = GameObject.Find("DarkGothic_Volume");
        if (go == null)
        {
            go = new GameObject("DarkGothic_Volume");
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
        }
        Volume volume = go.GetComponent<Volume>();
        if (volume == null) volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.weight = 1f;
        volume.sharedProfile = profile;
    }

    private static void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true; // 꺼져 있으면 볼륨이 화면에 반영되지 않는다
        data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        cam.allowHDR = true;
        EditorUtility.SetDirty(cam);
        EditorUtility.SetDirty(data);
    }

    // ───────────────────────── 안개 · 조명 · 하늘 ─────────────────────────

    private static void SetupFogAndAmbient()
    {
        // 지수 안개: 가까운 보스는 보이고, 멀리 있는 폐허는 안개 속으로 사라진다
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = FogDensity;

        // 하늘 반사광 대신 어두운 청록 3색 주변광 (그림자 속도 완전히 검지 않게)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
        RenderSettings.reflectionIntensity = 0.4f;
    }

    private static void SetupMoonLight()
    {
        foreach (Light light in Object.FindObjectsByType<Light>())
        {
            if (light.type != LightType.Directional) continue;
            light.color = MoonColor;
            light.intensity = MoonIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            // 높은 곳에서 비스듬히 내려오는 달빛
            light.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
            EditorUtility.SetDirty(light);
            RenderSettings.sun = light;
        }
    }

    private static void SetupSky()
    {
        Material sky = RenderSettings.skybox;
        if (sky == null) return;
        if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", SkyExposure);
        if (sky.HasProperty("_Tint")) sky.SetColor("_Tint", new Color(0.48f, 0.56f, 0.58f));
        EditorUtility.SetDirty(sky);
    }

    /// <summary>보물 더미 위에 따뜻한 등불빛 → 청록 화면 속 유일한 따뜻한 포인트</summary>
    private static void SetupLanterns()
    {
        GameObject hoard = GameObject.Find("BossArena/Hoard_Treasure");
        if (hoard == null) return;

        Renderer[] rs = hoard.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);

        GameObject go = GameObject.Find("BossArena/Hoard_Light");
        if (go == null)
        {
            go = new GameObject("Hoard_Light");
            go.transform.SetParent(GameObject.Find("BossArena").transform, false);
        }
        go.transform.position = new Vector3(b.center.x, b.max.y + 2f, b.center.z + b.extents.z * 0.6f);
        Light l = go.GetComponent<Light>();
        if (l == null) l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = LanternColor;
        l.range = Mathf.Max(b.size.x, b.size.z) * 1.4f;
        l.intensity = 6f;
        l.shadows = LightShadows.None;
        EditorUtility.SetDirty(l);
    }
}
