using System.Collections.Generic;
using InsaneOne.DevTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 움직이지 않는 BossArena의 메시들을 메시 하나 + 재질 하나(텍스처 아틀라스)로 굽는다 (MeshBaker 사용).
/// 원본 오브젝트는 남기고 렌더러만 꺼서 MeshCollider·조명은 그대로 쓴다.
/// 여러 번 실행해도 같은 결과: 예전 결과(BossArena_Baked)를 지우고 꺼 둔 원본 렌더러를 다시 켠 뒤 굽는다.
/// </summary>
public static class BossArenaBaker
{
    private const string ArenaName = "BossArena";
    private const string BakedName = "BossArena_Baked";
    private const string BakeFolder = "Baked/BossArena";
    // 바닥(4096)과 소품 7개(2048)를 줄이지 않고 담는 크기
    private const int AtlasSize = 8192;

    [MenuItem("Tools/Boss Fight/Bake Boss Arena")]
    public static void Bake()
    {
        GameObject arena = GameObject.Find(ArenaName);
        if (arena == null)
        {
            Debug.LogError("[BossArenaBaker] 씬에 BossArena가 없습니다.");
            return;
        }

        // 예전 결과 정리 → 원본 렌더러 다시 켜기
        Transform old = arena.transform.Find(BakedName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var renderers = new List<MeshRenderer>();
        foreach (MeshRenderer r in arena.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled)
            {
                Undo.RecordObject(r, "Bake Boss Arena");
                r.enabled = true;
            }
            renderers.Add(r);
        }

        var baked = new GameObject(BakedName);
        Undo.RegisterCreatedObjectUndo(baked, "Bake Boss Arena");
        baked.transform.SetParent(arena.transform, false);
        MeshFilter target = baked.AddComponent<MeshFilter>();
        baked.AddComponent<MeshRenderer>();

        MeshBakerCore.Bake(renderers, new MeshBakeSettings
        {
            atlasSize = AtlasSize,
            bakeNormals = true,
            bakeSpecular = false, // 경기장 재질에는 금속/스페큘러 맵이 없음
            bakeAo = false,
            saveMesh = true,
            generateLightmapUVs = false, // 실시간 조명만 사용
            disableOriginalRenderers = true,
            folder = BakeFolder,
            postfix = "",
            target = target
        });

        EditorSceneManager.MarkSceneDirty(arena.scene);
        Debug.Log($"[BossArenaBaker] {renderers.Count}개 메시를 {BakedName} 하나로 구웠습니다 (Assets/{BakeFolder}).");
    }
}
