using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// LocalPulsePipeline의 Unity 수명주기 어댑터(얇은 껍데기 — 로직 없음).
    /// FirstPersonController의 발소리 이벤트를 구독해 파이프라인에 넘기고,
    /// 매 프레임 Time.time으로 재판정 틱을 구동하며, 델리버리를 Console 로그로만 출력한다.
    ///
    /// [임시 스모크 리그] 청취 기준점은 Start 시점의 플레이어 위치에 고정된 가상
    /// 청취자(별도 ID)다 — 실제 게임플레이 기능이 아니며, 걸어서 멀어지거나 벽 뒤로
    /// 돌아가면 로그의 Appeared/Updated/Disappeared 변화로 §5.6 차폐가 실물 검증된다.
    /// 시각 연출(파문 링·방향 게이지)은 T8 스코프라 여기 없다.
    /// </summary>
    public sealed class LocalPulsePipelineBehaviour : MonoBehaviour
    {
        /// <summary>역할 배정 시스템 배선 전 로컬 플레이어 임시 ID.</summary>
        private const ulong LocalSourceId = 1;

        /// <summary>GAP-1(본인 제외) 때문에 발생원과 달라야 델리버리가 관측되는 디버그 청취자 ID.</summary>
        private const ulong DebugListenerId = 999;

        [SerializeField] private FirstPersonController _player;

        private LocalPulsePipeline _pipeline;

        private void Awake()
        {
            if (_player == null)
                _player = FindAnyObjectByType<FirstPersonController>();

            if (_player == null)
            {
                Debug.LogError("[LocalPulsePipeline] FirstPersonController를 찾지 못했습니다 — 비활성화합니다.");
                enabled = false;
                return;
            }

            _pipeline = new LocalPulsePipeline(new PhysicsOcclusionProbe(), LocalSourceId);
            _pipeline.DeliveryEmitted += LogDelivery;
        }

        private void Start()
        {
            // 임시 청취점: 스폰 위치에 고정(러너 역할 = §5.7 배율 ×1.0이라 로그 수치가
            // §5.1 원본값 그대로 나와 눈으로 검증하기 쉽다).
            Vector3 anchor = _player.transform.position;
            _pipeline.SetDebugListener(DebugListenerId, anchor, RoleType.Runner);
            Debug.Log($"[Pulse] 디버그 청취점 고정: {anchor} (id={DebugListenerId}, 임시 스모크 리그 — 게임플레이 기능 아님)");
        }

        private void OnEnable()
        {
            if (_player != null)
                _player.FootstepPulseEmitted += OnFootstepPulse;
        }

        private void OnDisable()
        {
            if (_player != null)
                _player.FootstepPulseEmitted -= OnFootstepPulse;
        }

        private void Update()
        {
            _pipeline.Tick(Time.time);
        }

        private void OnFootstepPulse(SoundType type, float radius, float duration, Vector3 position)
        {
            _pipeline.OnFootstepPulse(type, radius, duration, position, Time.time);
        }

        private static void LogDelivery(PulseDelivery delivery)
        {
            if (delivery.Perceived.HasValue)
            {
                PerceivedPulse p = delivery.Perceived.Value;
                Debug.Log($"[Pulse] {delivery.Kind} pulse={delivery.PulseId} radius={p.PerceivedRadius:0.##} duration={p.PerceivedDuration:0.##} octant={p.Direction} ringVisible={p.WorldSpaceRingVisible}");
            }
            else
            {
                Debug.Log($"[Pulse] {delivery.Kind} pulse={delivery.PulseId}");
            }
        }
    }
}
