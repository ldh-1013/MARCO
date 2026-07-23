using UnityEngine;
using UnityEngine.InputSystem;
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
    /// 돌아가면 Appeared/Updated/Disappeared 변화로 §5.6 차폐가 실물 검증된다.
    ///
    /// T8부터 델리버리 소비자는 <see cref="PulseVisualRenderer"/>다. Debug.Log는
    /// 디버깅 편의로 남겨두되 기본은 꺼져 있다(_logDeliveries).
    /// </summary>
    public sealed class LocalPulsePipelineBehaviour : MonoBehaviour
    {
        /// <summary>GAP-1(본인 제외) 때문에 발생원과 달라야 델리버리가 관측되는 디버그 청취자 ID.</summary>
        private const ulong DebugListenerId = 999;

        [SerializeField] private FirstPersonController _player;
        [SerializeField] private PulseVisualRenderer _visuals;

        [Header("디버그")]
        [SerializeField] private bool _logDeliveries;

        [Tooltip("§15.5 성능 목표(동시 30개) 체감 확인용. 누르면 청취점 주변에 파문을 한꺼번에 생성한다.")]
        [SerializeField] private Key _stressTestKey = Key.P;
        [SerializeField, Range(1, 100)] private int _stressTestPulseCount = 30;

        [Tooltip("§19 색맹 모드 토글. 인스펙터를 건드리지 않고 Play 중에 색상 전환을 확인한다.")]
        [SerializeField] private Key _colorblindToggleKey = Key.C;

        private LocalPulsePipeline _pipeline;
        private Vector3 _listenerAnchor;

        private void Awake()
        {
            if (_visuals == null)
                _visuals = FindAnyObjectByType<PulseVisualRenderer>();

            // 발생원 ID는 플레이어 바인딩 시점에 실제 값으로 덮어쓴다(BindPlayer).
            // 그 전까지는 로컬 폴백값(1)으로 시작한다.
            _pipeline = new LocalPulsePipeline(new PhysicsOcclusionProbe(), FirstPersonController.LocalFallbackPlayerId);
            _pipeline.DeliveryEmitted += OnDelivery;

            // 플레이어는 네트워크로 스폰될 수 있어 이 시점에 없을 수 있다.
            // 준비되면 알려달라고 등록해 둔다(이미 있으면 즉시 콜백).
            LocalPlayerRegistry.WhenReady(BindPlayer);
        }

        private void OnDestroy() => LocalPlayerRegistry.StopWaiting(BindPlayer);

        private void BindPlayer(FirstPersonController player)
        {
            if (_player == player)
                return;

            if (_player != null)
                _player.FootstepPulseEmitted -= OnFootstepPulse;

            _player = player;
            _player.FootstepPulseEmitted += OnFootstepPulse;

            // 발생원 ID를 실제 플레이어 신원으로 맞춘다(로컬이면 폴백 1, 네트워크면 OwnerId).
            _pipeline.SourcePlayerId = _player.PlayerId;

            // 임시 청취점: 플레이어가 등장한 위치에 고정(러너 역할 = §5.7 배율 ×1.0이라
            // 표시 수치가 §5.1 원본값 그대로 나와 눈으로 검증하기 쉽다).
            _listenerAnchor = _player.transform.position;
            _pipeline.SetDebugListener(DebugListenerId, _listenerAnchor, RoleType.Runner);
            Debug.Log($"[Pulse] 디버그 청취점 고정: {_listenerAnchor} (id={DebugListenerId}, 임시 스모크 리그 — 게임플레이 기능 아님)");
        }

        private void OnDisable()
        {
            if (_player != null)
                _player.FootstepPulseEmitted -= OnFootstepPulse;
        }

        private void Update()
        {
            float now = Time.time;
            _pipeline.Tick(now);

            // 렌더러의 자체 만료 타이머는 델리버리와 무관하게 매 프레임 돌아야 한다 —
            // 자연 만료 시 Disappeared가 오지 않기 때문(T7 설계, 스프린트 3 조사 결론).
            if (_visuals != null)
                _visuals.Tick(now);

            HandleDebugInput(now);
        }

        private void OnFootstepPulse(SoundType type, float radius, float duration, Vector3 position)
        {
            _pipeline.OnFootstepPulse(type, radius, duration, position, Time.time);
        }

        /// <summary>
        /// 발소리 외 소리원(§5.1 밸브 회전 등)이 파문을 발행하는 진입점.
        /// 차폐 판정·재판정 주기는 기존 파이프라인이 그대로 담당한다.
        /// </summary>
        public void EmitPulse(SoundType type, float radius, float duration, Vector3 position)
        {
            _pipeline?.EmitPulse(type, radius, duration, position, Time.time);
        }

        private void OnDelivery(PulseDelivery delivery)
        {
            if (_visuals != null)
                _visuals.Apply(delivery, Time.time);

            if (_logDeliveries)
                LogDelivery(delivery);
        }

        /// <summary>
        /// 수동 검증용 디버그 키. 절차는 docs/수동검증_절차.md 참조.
        /// - 스트레스 키(기본 P): §15.5 성능 목표(동시 30개 @60fps) 체감 확인. 정밀 실측은 T6 스코프
        /// - 색맹 토글 키(기본 C): §19 색맹 팔레트 전환이 실제로 반영되는지 확인
        /// </summary>
        private void HandleDebugInput(float now)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_stressTestKey].wasPressedThisFrame)
            {
                for (int i = 0; i < _stressTestPulseCount; i++)
                {
                    Vector2 offset = Random.insideUnitCircle * 4f;
                    Vector3 position = _listenerAnchor + new Vector3(offset.x, 0f, offset.y);
                    _pipeline.OnFootstepPulse(SoundType.Sprint, 6f, 0.8f, position, now);
                }

                Debug.Log($"[Pulse] 스트레스 테스트: {_stressTestPulseCount}개 생성 " +
                          $"(활성 시각 오브젝트 {(_visuals != null ? _visuals.ActiveVisualCount : 0)}, " +
                          $"프레임 {Time.unscaledDeltaTime * 1000f:0.0}ms)");
            }

            if (keyboard[_colorblindToggleKey].wasPressedThisFrame && _visuals != null)
            {
                bool enabled = _visuals.ToggleColorblindMode();
                Debug.Log($"[Pulse] 색맹 모드 {(enabled ? "켜짐" : "꺼짐")} (§19)");
            }
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
