using UnityEngine;

/// <summary>
/// 보스가 내려찍을 때 카메라를 잠깐 흔든다.
/// 카메라의 로컬 위치만 흔들었다가 원래 자리로 돌려놓으므로 CameraController의 회전 계산과 충돌하지 않는다.
/// Main Camera(= CameraPivot의 자식)에 붙인다.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [SerializeField] private BossController boss;
    [SerializeField] private float duration = 0.35f;
    [SerializeField] private float amplitude = 0.35f;

    private Vector3 restLocalPosition;
    private float shakeUntil;

    private void Start()
    {
        restLocalPosition = transform.localPosition;
        if (boss != null) boss.Slammed += Shake;
    }

    private void OnDestroy()
    {
        if (boss != null) boss.Slammed -= Shake;
    }

    public void Shake()
    {
        shakeUntil = Time.time + duration;
    }

    private void LateUpdate()
    {
        if (Time.time < shakeUntil)
        {
            float k = (shakeUntil - Time.time) / duration; // 시간이 지날수록 약해짐
            transform.localPosition = restLocalPosition + Random.insideUnitSphere * (amplitude * k);
        }
        else
        {
            transform.localPosition = restLocalPosition;
        }
    }
}
