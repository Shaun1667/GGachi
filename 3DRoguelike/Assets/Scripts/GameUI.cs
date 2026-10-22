using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 UI 표시만 담당한다 (게임 규칙은 GameManager).
///
/// - 체력바: Health.HealthChanged 이벤트를 구독해서 "바뀔 때만" 목표값을 갱신하고,
///   화면의 막대는 매 프레임 목표값으로 부드럽게 따라간다.
/// - 체력바 채움은 Image.fillAmount 대신 RectTransform의 anchorMax.x를 줄이는 방식이라
///   스프라이트 없이도 동작한다.
/// - 버튼(게임 시작 / 게임 종료 / 돌아가기)은 눌린 사실만 이벤트로 알리고, 무엇을 할지는 GameManager가 정한다.
/// </summary>
public class GameUI : MonoBehaviour
{
    [Header("체력 연결")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private Health bossHealth;

    [Header("HUD")]
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private RectTransform bossBarFill;
    [SerializeField] private RectTransform playerBarFill;
    [SerializeField] private Text bossHpText;
    [SerializeField] private Text playerHpText;
    [SerializeField] private Text timerText;
    [SerializeField] private Text bannerText;

    [Header("가운데 패널 (시작 / 결과)")]
    [SerializeField] private GameObject centerPanel;
    [SerializeField] private Text centerTitle;
    [SerializeField] private Text centerBody;

    [Tooltip("시작 화면에서는 뒤의 경기장이 보이지 않게 가운데 패널 배경을 불투명하게")]
    [SerializeField, Range(0f, 1f)] private float titleBackgroundAlpha = 1f;
    [Tooltip("결과 화면에서는 뒤의 경기장이 비쳐 보이게")]
    [SerializeField, Range(0f, 1f)] private float resultBackgroundAlpha = 0.65f;

    [Header("버튼 (시작 화면 / 일시정지)")]
    [SerializeField] private GameObject titleButtons;
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button pauseQuitButton;

    [SerializeField] private float barFollowSpeed = 3f;
    [SerializeField] private float bannerDuration = 2.5f;

    private float bossTarget = 1f, bossShown = 1f;
    private float playerTarget = 1f, playerShown = 1f;
    private float bannerHideTime;
    private Image centerBackground;

    /// <summary>시작 화면의 [게임 시작]</summary>
    public event Action StartClicked;
    /// <summary>시작 화면 또는 일시정지 화면의 [게임 종료]</summary>
    public event Action QuitClicked;
    /// <summary>일시정지 화면의 [돌아가기]</summary>
    public event Action ResumeClicked;

    private void Awake()
    {
        centerBackground = centerPanel.GetComponent<Image>();
        if (startButton != null) startButton.onClick.AddListener(() => StartClicked?.Invoke());
        if (quitButton != null) quitButton.onClick.AddListener(() => QuitClicked?.Invoke());
        if (pauseQuitButton != null) pauseQuitButton.onClick.AddListener(() => QuitClicked?.Invoke());
        if (resumeButton != null) resumeButton.onClick.AddListener(() => ResumeClicked?.Invoke());
        ShowPause(false);
    }

    private void Start()
    {
        playerHealth.HealthChanged += OnPlayerHealthChanged;
        bossHealth.HealthChanged += OnBossHealthChanged;
        OnPlayerHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
        OnBossHealthChanged(bossHealth.CurrentHealth, bossHealth.MaxHealth);
        bannerText.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.HealthChanged -= OnPlayerHealthChanged;
        if (bossHealth != null) bossHealth.HealthChanged -= OnBossHealthChanged;
    }

    private void Update()
    {
        bossShown = Mathf.MoveTowards(bossShown, bossTarget, barFollowSpeed * Time.unscaledDeltaTime);
        playerShown = Mathf.MoveTowards(playerShown, playerTarget, barFollowSpeed * Time.unscaledDeltaTime);
        SetFill(bossBarFill, bossShown);
        SetFill(playerBarFill, playerShown);

        if (bannerText.gameObject.activeSelf && Time.unscaledTime >= bannerHideTime)
        {
            bannerText.gameObject.SetActive(false);
        }
    }

    public void ShowReady()
    {
        hudRoot.SetActive(false);
        centerPanel.SetActive(true);
        SetCenterBackground(titleBackgroundAlpha);
        SetTitleButtons(true);
        centerTitle.text = "까치 대장의 둥지";
        centerBody.text = "보물을 지키는 까치 대장을 쓰러뜨리세요!\n\n"
                        + "WASD 이동   마우스 시점   좌클릭 공격\n"
                        + "Space 점프   Shift 구르기(무적)   Esc 일시정지";
    }

    /// <summary>등장 연출 동안: 가운데 패널과 HUD를 모두 숨긴다 (레터박스·타이틀은 Timeline이 켠다)</summary>
    public void ShowCinematic()
    {
        centerPanel.SetActive(false);
        SetTitleButtons(false);
        hudRoot.SetActive(false);
    }

    public void ShowHud()
    {
        centerPanel.SetActive(false);
        SetTitleButtons(false);
        hudRoot.SetActive(true);
    }

    /// <summary>일시정지 화면 (돌아가기 / 게임 종료)</summary>
    public void ShowPause(bool show)
    {
        if (pausePanel != null) pausePanel.SetActive(show);
    }

    private void SetTitleButtons(bool show)
    {
        if (titleButtons != null) titleButtons.SetActive(show);
    }

    private void SetCenterBackground(float alpha)
    {
        if (centerBackground != null) centerBackground.color = new Color(0f, 0f, 0f, alpha);
    }

    public void SetTimer(float seconds)
    {
        int m = (int)(seconds / 60f);
        int s = (int)(seconds % 60f);
        timerText.text = $"{m:00}:{s:00}";
    }

    public void ShowBanner(string message)
    {
        bannerText.text = message;
        bannerText.gameObject.SetActive(true);
        bannerHideTime = Time.unscaledTime + bannerDuration;
    }

    public void ShowResult(bool victory, string reason, float seconds, int hitsTaken)
    {
        centerPanel.SetActive(true);
        SetCenterBackground(resultBackgroundAlpha);
        SetTitleButtons(false);
        centerTitle.text = victory ? "승리!" : "패배...";
        centerTitle.color = victory ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.45f, 0.45f);

        int m = (int)(seconds / 60f);
        int s = (int)(seconds % 60f);
        centerBody.text = $"{reason}\n\n"
                        + $"전투 시간  {m:00}:{s:00}\n"
                        + $"맞은 횟수  {hitsTaken}\n\n"
                        + "R 키로 다시 시작";
    }

    private void OnPlayerHealthChanged(int current, int max)
    {
        playerTarget = max > 0 ? (float)current / max : 0f;
        playerHpText.text = $"HP {current} / {max}";
    }

    private void OnBossHealthChanged(int current, int max)
    {
        bossTarget = max > 0 ? (float)current / max : 0f;
        bossHpText.text = $"{current} / {max}";
    }

    private static void SetFill(RectTransform fill, float ratio)
    {
        Vector2 max = fill.anchorMax;
        max.x = Mathf.Clamp01(ratio);
        fill.anchorMax = max;
    }
}
