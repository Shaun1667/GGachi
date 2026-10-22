using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 시작 화면 버튼과 일시정지 화면을 만드는 에디터 도구.
///
/// Tools > Boss Fight > 9. Setup Title &amp; Pause Menu
///   - 시작 화면(CenterPanel)에 [게임 시작] [게임 종료] 버튼
///   - 일시정지 화면(PausePanel): 어둡게 덮고 [돌아가기] [게임 종료] 버튼 (평소엔 꺼져 있고 Esc로 켜짐)
///   - 버튼 클릭을 받을 EventSystem (새 Input System용 입력 모듈)
///   - GameUI에 연결
/// 시작 화면 배경을 검게 하는 것은 GameUI.titleBackgroundAlpha가 맡는다.
///
/// 2. Setup Current Scene이 UI를 새로 만들 때도 이 도구를 함께 실행한다. 여러 번 실행해도 같은 결과가 나온다.
/// </summary>
public static class BossMenuSetup
{
    private const string TitleButtonsName = "TitleButtons";
    private const string PausePanelName = "PausePanel";

    private static readonly Color ButtonColor = new Color(0.1f, 0.09f, 0.11f, 0.9f);
    private static readonly Color ButtonHover = new Color(0.45f, 0.33f, 0.12f, 0.95f);
    private static readonly Color ButtonPressed = new Color(0.7f, 0.5f, 0.15f, 1f);
    private static readonly Color Gold = new Color(1f, 0.85f, 0.3f);

    [MenuItem("Tools/Boss Fight/9. Setup Title & Pause Menu")]
    public static void SetupMenu()
    {
        GameUI ui = Object.FindAnyObjectByType<GameUI>();
        if (ui == null)
        {
            Debug.LogError("[BossMenuSetup] GameUI가 없습니다. GGachi_Boss 씬에서 실행하세요 (없으면 2. Setup Current Scene 먼저)");
            return;
        }
        Build(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Debug.Log("[BossMenuSetup] 시작 화면 버튼 / 일시정지 화면 생성 완료");
    }

    /// <summary>GameUI가 붙은 캔버스에 시작 화면 버튼·일시정지 화면을 (다시) 만들고 연결한다</summary>
    public static void Build(GameUI ui)
    {
        Transform canvas = ui.transform;
        var so = new SerializedObject(ui);
        Transform centerPanel = ((GameObject)so.FindProperty("centerPanel").objectReferenceValue).transform;

        Destroy(centerPanel.Find(TitleButtonsName));
        Destroy(canvas.Find(PausePanelName));

        // 시작 화면: 설명 글 아래에 나란히
        RectTransform titleButtons = NewRect(TitleButtonsName, centerPanel);
        Place(titleButtons, new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(860f, 100f));
        Button start = MakeButton("StartButton", titleButtons, "게임 시작", new Vector2(-220f, 0f));
        Button quit = MakeButton("QuitButton", titleButtons, "게임 종료", new Vector2(220f, 0f));

        // 일시정지 화면: 모든 UI 위를 어둡게 덮는다
        RectTransform pause = NewRect(PausePanelName, canvas);
        Stretch(pause);
        pause.SetAsLastSibling();
        Image dim = pause.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.7f);
        Text title = MakeText("Title", pause, "일시정지", 84, Gold);
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1000f, 120f));
        Text hint = MakeText("Hint", pause, "Esc로 돌아가기", 30, new Color(1f, 1f, 1f, 0.7f));
        Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1000f, 50f));
        Button resume = MakeButton("ResumeButton", pause, "돌아가기", new Vector2(0f, -30f));
        Button pauseQuit = MakeButton("QuitButton", pause, "게임 종료", new Vector2(0f, -150f));
        pause.gameObject.SetActive(false);

        so.FindProperty("titleButtons").objectReferenceValue = titleButtons.gameObject;
        so.FindProperty("startButton").objectReferenceValue = start;
        so.FindProperty("quitButton").objectReferenceValue = quit;
        so.FindProperty("pausePanel").objectReferenceValue = pause.gameObject;
        so.FindProperty("resumeButton").objectReferenceValue = resume;
        so.FindProperty("pauseQuitButton").objectReferenceValue = pauseQuit;
        so.ApplyModifiedPropertiesWithoutUndo();

        EnsureEventSystem(ui.gameObject.scene);
    }

    /// <summary>버튼 클릭을 받을 EventSystem이 없으면 만든다 (프로젝트는 새 Input System만 사용)</summary>
    public static void EnsureEventSystem(Scene scene)
    {
        if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
        GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
    }

    private static Button MakeButton(string name, Transform parent, string label, Vector2 position)
    {
        RectTransform rt = NewRect(name, parent);
        Place(rt, new Vector2(0.5f, 0.5f), position, new Vector2(380f, 96f));
        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = Color.white; // 실제 색은 Button 색상(ColorBlock)이 곱해진다
        Outline border = rt.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
        border.effectDistance = new Vector2(2f, -2f);

        Button button = rt.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = ButtonPressed;
        colors.selectedColor = ButtonColor; // 누른 뒤 선택 상태로 색이 남지 않게
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.targetGraphic = bg;

        Text text = MakeText("Label", rt, label, 40, Color.white);
        Stretch(text.rectTransform);
        return button;
    }

    private static Text MakeText(string name, Transform parent, string content, int size, Color color)
    {
        Text t = NewRect(name, parent).gameObject.AddComponent<Text>();
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

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    private static void Destroy(Transform t)
    {
        if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
    }
}
