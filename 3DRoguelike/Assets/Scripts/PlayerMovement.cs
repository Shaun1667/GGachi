using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 카메라가 바라보는 방향을 기준으로 이동하는 TPS 스타일 무브먼트.
///
/// - 캐릭터는 항상 "카메라가 보는 방향"을 그대로 바라본다 (faceCameraDirection = true).
/// - W/S는 카메라 정면/후면, A/D는 카메라 기준 좌/우로 이동한다.
/// - 즉 A만 누르면 캐릭터는 정면을 바라본 채로 옆으로 스트레이프(strafe) 이동한다.
/// - Left Shift로 구른다. 구르는 중에는 무적이라 보스 공격을 피하는 데 쓴다.
/// - 공격 동작 중(PlayerAttack.IsAttacking)에는 이동·구르기·점프를 받지 않는다. 구르는 중에는 점프·공격이 안 된다.
///
/// CameraController.cs의 GetFlatForward() / GetFlatRight()를 그대로 사용한다.
/// Player 오브젝트에는 Character Controller 컴포넌트가 필요하다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("씬에 있는 CameraTarget에 붙인 CameraController를 드래그해서 연결")]
    [SerializeField] private CameraController cameraController;

    [Header("이동 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("점프 설정")]
    [Tooltip("점프 시 도달하는 최대 높이 (미터)")]
    [SerializeField] private float jumpHeight = 1.2f;

    [Header("구르기 설정")]
    [Tooltip("구르는 동안의 이동 속도 (m/s)")]
    [SerializeField] private float rollSpeed = 12f;
    [Tooltip("구르기 시간 (초). 구르기 애니메이션 길이와 맞춤 (BossFightSetup의 RollPlaybackSpeed)")]
    [SerializeField] private float rollDuration = 0.8f;
    [SerializeField] private float rollCooldown = 0.9f;

    [Header("회전 설정")]
    [Tooltip("true면 캐릭터가 항상 카메라 방향을 바라본다 (A/D는 옆으로 스트레이프).")]
    [SerializeField] private bool faceCameraDirection = true;

    /// <summary>이번 프레임에 이동 입력이 있었는지</summary>
    public bool IsMoving { get; private set; }

    /// <summary>
    /// 캐릭터 기준 이동 입력 (x: 왼쪽 -1 ~ 오른쪽 +1, y: 뒤 -1 ~ 앞 +1).
    /// 캐릭터가 항상 카메라 방향을 보므로 WASD 입력이 그대로 캐릭터 기준 방향이 된다 → 방향별 걷기 애니메이션에 사용
    /// </summary>
    public Vector2 MoveInput { get; private set; }
    public bool IsRolling => rollTimer > 0f;

    /// <summary>바닥에서 뛰어오른 순간 (점프 애니메이션 재생용)</summary>
    public event System.Action Jumped;

    /// <summary>구르기를 시작한 순간 (구르기 애니메이션 재생용)</summary>
    public event System.Action Rolled;

    private CharacterController controller;
    private Health health;
    private PlayerAttack attack;
    private float verticalVelocity;
    private float rollTimer;
    private float nextRollTime;
    private Vector3 rollDirection;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>(); // 없어도 동작은 하고, 있으면 구르기 무적에 사용
        attack = GetComponent<PlayerAttack>(); // 있으면 공격 동작 중에 조작을 막는다
    }

    private void OnDisable()
    {
        // 구르는 도중 게임이 끝나 컴포넌트가 꺼져도 무적이 남지 않도록 정리
        rollTimer = 0f;
        IsMoving = false;
        MoveInput = Vector2.zero;
        if (health != null) health.SetForcedInvincible(false);
    }

    private void Update()
    {
        // 공격 동작 중에는 이동·구르기·점프 입력을 받지 않는다 (중력은 계속 적용)
        bool locked = attack != null && attack.IsAttacking;

        Vector2 input = locked ? Vector2.zero : ReadMoveInput();
        MoveInput = input.sqrMagnitude > 1f ? input.normalized : input;

        // 1) 카메라 기준 수평 forward/right 벡터를 가져온다 (y성분 제거된 상태)
        Vector3 camForward = cameraController.GetFlatForward();
        Vector3 camRight = cameraController.GetFlatRight();

        // 2) 입력을 카메라 기준 방향으로 변환
        Vector3 moveDir = camForward * input.y + camRight * input.x;
        if (moveDir.sqrMagnitude > 1f)
        {
            moveDir.Normalize();
        }
        IsMoving = moveDir.sqrMagnitude > 0.01f;

        // 3) 캐릭터를 카메라가 보는 방향으로 정렬 (이동 입력과 무관하게 항상 적용 -> 스트레이프 이동)
        if (faceCameraDirection)
        {
            transform.rotation = Quaternion.Euler(0f, cameraController.transform.eulerAngles.y, 0f);
        }

        // 4) 구르기: 입력 방향(없으면 정면)으로 굴러서 이동, 그동안 무적
        if (!locked && !IsRolling && controller.isGrounded && ReadRollPressed() && Time.time >= nextRollTime)
        {
            rollDirection = IsMoving ? moveDir.normalized : camForward;
            rollTimer = rollDuration;
            nextRollTime = Time.time + rollCooldown;
            if (health != null) health.SetForcedInvincible(true);
            Rolled?.Invoke();
        }

        Vector3 horizontal = moveDir * moveSpeed;
        if (rollTimer > 0f)
        {
            horizontal = rollDirection * rollSpeed;
            // 앞구르기 동작이라 구르는 동안은 구르는 방향을 바라본다
            transform.rotation = Quaternion.LookRotation(rollDirection, Vector3.up);
            MoveInput = Vector2.zero;
            rollTimer -= Time.deltaTime;
            if (rollTimer <= 0f && health != null)
            {
                health.SetForcedInvincible(false);
            }
        }

        // 5) 중력 & 점프 처리
        bool isGrounded = controller.isGrounded;

        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f; // 바닥에 붙어있게 하는 최소값
        }

        // 바닥에 닿아있을 때만 스페이스바로 점프 (공중 점프 방지)
        if (!locked && !IsRolling && isGrounded && ReadJumpPressed())
        {
            // v = sqrt(h * -2 * g) : 원하는 최대 높이(jumpHeight)에 도달하는 데 필요한 초기 속도
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            Jumped?.Invoke();
        }

        verticalVelocity += gravity * Time.deltaTime;

        // 6) 최종 이동 적용
        Vector3 velocity = horizontal;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// 새 Input System의 Keyboard를 직접 폴링해서 WASD를 -1~1 범위의 Vector2로 반환한다.
    /// x: A(-1) / D(+1), y: S(-1) / W(+1)
    /// </summary>
    private Vector2 ReadMoveInput()
    {
        Vector2 input = Vector2.zero;
        Keyboard kb = Keyboard.current;
        if (kb == null) return input;

        if (kb.wKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed) input.x -= 1f;

        return input;
    }

    /// <summary>
    /// 스페이스바가 "이번 프레임에 새로 눌렸는지"를 반환한다.
    /// isPressed 대신 wasPressedThisFrame을 써야 키를 계속 누르고 있어도
    /// 매 프레임 점프가 발동하지 않고 한 번만 발동한다.
    /// </summary>
    private bool ReadJumpPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;

        return kb.spaceKey.wasPressedThisFrame;
    }

    private bool ReadRollPressed()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;

        return kb.leftShiftKey.wasPressedThisFrame;
    }
}
