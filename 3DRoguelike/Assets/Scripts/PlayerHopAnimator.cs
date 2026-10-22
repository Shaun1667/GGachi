using UnityEngine;

/// <summary>
/// 토끼 모델의 깡충(Hop) 애니메이션을 이동 중에만 재생한다.
/// RabbitHop 클립이 Legacy 클립이라 Animator 대신 Animation 컴포넌트로 재생한다.
/// 토끼 모델(Visual) 오브젝트에 Animation 컴포넌트와 함께 붙인다.
/// </summary>
[RequireComponent(typeof(Animation))]
public class PlayerHopAnimator : MonoBehaviour
{
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private AnimationClip hopClip;

    private Animation anim;

    private void Awake()
    {
        anim = GetComponent<Animation>();
        if (hopClip != null)
        {
            hopClip.wrapMode = WrapMode.Loop;
            if (anim.GetClip(hopClip.name) == null) anim.AddClip(hopClip, hopClip.name);
        }
        anim.playAutomatically = false;
    }

    private void Update()
    {
        if (hopClip == null || movement == null) return;

        bool moving = movement.enabled && movement.IsMoving;
        if (moving && !anim.IsPlaying(hopClip.name))
        {
            anim.Play(hopClip.name);
        }
        else if (!moving && anim.IsPlaying(hopClip.name))
        {
            anim.Stop();
            anim.Sample(); // 정지 시 첫 프레임 자세로 되돌림
        }
    }
}
