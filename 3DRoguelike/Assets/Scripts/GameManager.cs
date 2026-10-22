using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum GameState
{
    Ready,
    Intro,
    Playing,
    Paused,
    Victory,
    Defeat
}

/// <summary>
/// 게임 전체 흐름(시작 → 등장 연출 → 전투 → 승리/패배 → 재시작)을 관리한다.
///
/// - 등장 연출: intro(BossIntroCinematic)가 연결되어 있으면 시작 직후 재생하고, 끝나면 전투를 시작한다.
/// - 승리: 보스 Health.Died
/// - 패배: 플레이어 Health.Died 또는 경기장 밖으로 추락(fallDeathY 아래)
/// - 게임 중이 아닐 때는 플레이어 조작 컴포넌트를 꺼서 입력을 막는다.
/// - 시작 화면: Enter 또는 [게임 시작] / [게임 종료]. 전투 중 Esc: 일시정지 (시간 정지, [돌아가기] / [게임 종료])
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private Health bossHealth;
    [SerializeField] private BossController boss;
    [SerializeField] private GameUI ui;

    [Tooltip("(선택) 전투 시작 전 보스 등장 연출. 비워두면 바로 전투를 시작한다")]
    [SerializeField] private BossIntroCinematic intro;

    [Tooltip("게임 중에만 켜둘 플레이어 조작 컴포넌트 (이동, 공격, 카메라)")]
    [SerializeField] private Behaviour[] playerControls;

    [Header("규칙")]
    [Tooltip("플레이어가 이 높이 아래로 떨어지면 패배")]
    [SerializeField] private float fallDeathY = -15f;

    public GameState State { get; private set; } = GameState.Ready;

    private float elapsedTime;
    private int hitsTaken;

    private void Start()
    {
        playerHealth.Died += OnPlayerDied;
        playerHealth.Damaged += OnPlayerDamaged;
        bossHealth.Died += OnBossDied;
        boss.PhaseChanged += OnBossPhaseChanged;
        ui.StartClicked += StartGame;
        ui.QuitClicked += QuitGame;
        ui.ResumeClicked += Resume;

        Time.timeScale = 1f; // 일시정지 중에 다시 시작(씬 재로드)해도 시간이 멈춰 있지 않게
        SetControls(false);
        ui.ShowReady();
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= OnPlayerDied;
            playerHealth.Damaged -= OnPlayerDamaged;
        }
        if (bossHealth != null)
        {
            bossHealth.Died -= OnBossDied;
        }
        if (boss != null) boss.PhaseChanged -= OnBossPhaseChanged;
        if (ui != null)
        {
            ui.StartClicked -= StartGame;
            ui.QuitClicked -= QuitGame;
            ui.ResumeClicked -= Resume;
        }
        Time.timeScale = 1f;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;

        switch (State)
        {
            case GameState.Ready:
                // 마우스 클릭은 [게임 시작] / [게임 종료] 버튼이 받으므로 키보드는 Enter만
                if (kb != null && kb.enterKey.wasPressedThisFrame) StartGame();
                break;

            case GameState.Intro:
                break; // 연출 진행은 BossIntroCinematic이 맡는다 (건너뛰기 포함)

            case GameState.Paused:
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Resume();
                break;

            case GameState.Playing:
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    Pause();
                    break;
                }

                elapsedTime += Time.deltaTime;
                ui.SetTimer(elapsedTime);

                if (playerHealth.transform.position.y < fallDeathY)
                {
                    EndGame(false, "경기장 밖으로 떨어졌습니다");
                }
                break;

            default: // 승리 / 패배 화면
                if (kb != null && kb.rKey.wasPressedThisFrame)
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
                break;
        }
    }

    private void StartGame()
    {
        if (State != GameState.Ready) return; // Enter와 버튼이 같은 프레임에 들어와도 한 번만

        if (intro != null)
        {
            State = GameState.Intro;
            ui.ShowCinematic();
            intro.Play(BeginFight);
        }
        else
        {
            BeginFight();
        }
    }

    private void BeginFight()
    {
        State = GameState.Playing;
        SetControls(true);
        boss.StartFight();
        ui.ShowHud();
    }

    /// <summary>
    /// 일시정지: 시간을 멈추고(보스 패턴·투사체·타이머 정지) 조작을 끈다.
    /// 카메라 조작(CameraController)이 꺼지면서 마우스 커서 잠금도 풀려 버튼을 누를 수 있다.
    /// </summary>
    private void Pause()
    {
        State = GameState.Paused;
        Time.timeScale = 0f;
        SetControls(false);
        ui.ShowPause(true);
    }

    private void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        Time.timeScale = 1f;
        ui.ShowPause(false);
        StartCoroutine(EnableControlsNextFrame());
    }

    /// <summary>[돌아가기]를 누른 클릭이 같은 프레임에 공격으로 읽히지 않도록 한 프레임 뒤에 조작을 켠다</summary>
    private IEnumerator EnableControlsNextFrame()
    {
        yield return null;
        if (State == GameState.Playing) SetControls(true);
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void EndGame(bool victory, string reason)
    {
        if (State != GameState.Playing) return; // 이미 끝났으면 중복 처리 안 함

        State = victory ? GameState.Victory : GameState.Defeat;
        SetControls(false);
        if (!victory) boss.StopFight();

        ui.ShowResult(victory, reason, elapsedTime, hitsTaken);
    }

    private void SetControls(bool enabled)
    {
        foreach (Behaviour b in playerControls)
        {
            if (b != null) b.enabled = enabled;
        }
    }

    private void OnPlayerDied() => EndGame(false, "쓰러졌습니다");
    private void OnBossDied() => EndGame(true, "까치 대장을 물리쳤습니다!");
    private void OnPlayerDamaged(int amount) => hitsTaken++;
    private void OnBossPhaseChanged() => ui.ShowBanner("까치 대장이 분노했다! 공격이 빨라집니다");
}
