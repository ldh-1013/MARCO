using UnityEngine;
using UnityEngine.InputSystem;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// §4.3 E 홀드 입력을 §6.1 밸브 상태기계로 연결하는 씬 글루.
    /// 판정·취소 규칙은 전부 <see cref="ValveInteractionController"/>(순수 로직)에 있고,
    /// 여기서는 입력 폴링 · 최근접 밸브 탐색 · 로그 · 소음 발행만 한다.
    ///
    /// 입력은 <see cref="FirstPersonController"/>와 동일하게 Input System 디바이스를
    /// 직접 폴링한다 — 새 입력 방식을 도입하지 않는다(§12.6 리바인딩 때 액션 에셋으로 전환).
    ///
    /// 정식 홀드 게이지 UI(§4.3 비고)는 별도 UI 스프린트 몫이라, 여기서는 Console
    /// 로그로만 진행률을 확인할 수 있게 한다.
    /// </summary>
    public sealed class ValveInteractor : MonoBehaviour
    {
        [SerializeField] private FirstPersonController _player;

        [Tooltip("§5.1 밸브 회전 소음(12m)을 발행할 파이프라인. 비워두면 소음 없이 동작한다.")]
        [SerializeField] private LocalPulsePipelineBehaviour _pulsePipeline;

        [Tooltip("GAP-10: 기획서에 수치가 없어 둔 값. 플레이테스트 조정 대상.")]
        [SerializeField] private float _interactionRange = ValveInteractionController.DefaultInteractionRange;

        [SerializeField] private Key _interactKey = Key.E;

        [Header("디버그")]
        [Tooltip("홀드 진행률 로그 간격(초). 0이면 진행률 로그를 끈다.")]
        [SerializeField] private float _progressLogInterval = 0.5f;

        private ValveInteractionController _controller;
        private ValveBehaviour[] _valves;
        private float _lastProgressLogTime;
        private ValveInteractionEvent _lastEvent = ValveInteractionEvent.None;

        // 스프린트 10: 네트워크 밸브에 홀드 의사를 보내는 중인 브릿지(로컬 모드면 null).
        private IValveNetworkBridge _engagedBridge;

        public float Progress01 => _controller?.Progress01 ?? 0f;

        private void Awake()
        {
            if (_player == null)
                _player = GetComponentInParent<FirstPersonController>() ?? FindAnyObjectByType<FirstPersonController>();

            if (_pulsePipeline == null)
                _pulsePipeline = FindAnyObjectByType<LocalPulsePipelineBehaviour>();

            _controller = new ValveInteractionController(_interactionRange);
            _valves = FindObjectsByType<ValveBehaviour>();

            if (_player == null)
            {
                Debug.LogError("[Valve] FirstPersonController를 찾지 못했습니다 — 비활성화합니다.");
                enabled = false;
                return;
            }

            if (_valves.Length == 0)
                Debug.LogWarning("[Valve] 씬에 ValveBehaviour가 없습니다 — 상호작용할 밸브가 없습니다.");
        }

        private void Update()
        {
            // 스프린트 10: 네트워크 모드에서 이 컴포넌트는 원격 플레이어 프록시에도 존재한다.
            // 로컬 조종 플레이어만 입력을 폴링·전송해야 원격 프록시가 내 키보드로 밸브를
            // 돌리는 일이 없다. 로컬 단독 실행에서는 IsLocallyControlled가 기본 true라 무해하다
            // (FirstPersonController가 소유권 게이트로 이 값을 관리 — 스프린트 8).
            if (!_player.IsLocallyControlled)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Vector3 playerPosition = _player.transform.position;
            ValveBehaviour nearest = FindNearestValve(playerPosition);
            bool interactHeld = keyboard[_interactKey].isPressed;

            // 스프린트 10: 최근접 밸브가 서버 권위(네트워크)로 관리되면, 로컬에서 Core를
            // 직접 조작하지 않고 서버에 홀드 의사만 보낸다. 상태 확정·개방은 서버가 한다.
            if (nearest != null && nearest.NetworkActive)
            {
                TickNetworked(nearest, playerPosition, interactHeld);
                return;
            }

            // 로컬(비네트워크) 경로 — 스프린트 5 그대로. 방금 네트워크 밸브를 놓았다면 해제 신호를 보낸다.
            ReleaseEngagedBridge();

            var input = new ValveInteractionInput(
                _player.PlayerId,
                _player.Role,
                playerPosition,
                interactHeld,
                nearest != null ? nearest.Valve : null,
                nearest != null ? nearest.transform.position : Vector3.zero);

            ValveInteractionEvent result = _controller.Tick(input, Time.deltaTime);
            ReportEvent(result, nearest);
        }

        /// <summary>
        /// 서버 권위 밸브에 대한 처리: 범위 판정은 클라이언트가 하되(GAP-17), 시작 여부·완료는
        /// 서버가 결정한다. 여기서는 "E 홀드 && 범위 안" 여부만 브릿지에 전달한다.
        /// </summary>
        private void TickNetworked(ValveBehaviour nearest, Vector3 playerPosition, bool interactHeld)
        {
            bool inRange = Vector3.Distance(playerPosition, nearest.transform.position) <= _interactionRange;
            bool held = interactHeld && inRange;

            IValveNetworkBridge bridge = nearest.NetworkBridge;

            // 다른 밸브로 옮겨갔으면 이전 밸브에 해제 신호를 먼저 보낸다.
            if (_engagedBridge != null && !ReferenceEquals(_engagedBridge, bridge))
                ReleaseEngagedBridge();

            bridge.SubmitHoldIntent(_player.PlayerId, _player.Role, held);
            _engagedBridge = held ? bridge : null;
        }

        /// <summary>진행 중이던 네트워크 홀드가 있으면 해제 의사(held=false)를 보낸다.</summary>
        private void ReleaseEngagedBridge()
        {
            if (_engagedBridge == null)
                return;

            _engagedBridge.SubmitHoldIntent(_player.PlayerId, _player.Role, false);
            _engagedBridge = null;
        }

        /// <summary>범위 제한은 컨트롤러가 하므로 여기서는 최근접 하나만 고른다.</summary>
        private ValveBehaviour FindNearestValve(Vector3 playerPosition)
        {
            ValveBehaviour nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < _valves.Length; i++)
            {
                ValveBehaviour valve = _valves[i];
                if (valve == null)
                    continue;

                float sqr = (valve.transform.position - playerPosition).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = valve;
                }
            }

            return nearest;
        }

        private void ReportEvent(ValveInteractionEvent result, ValveBehaviour valve)
        {
            string label = valve != null ? valve.DisplayName : "?";

            // Progressing은 매 프레임 나오므로 간격을 두고, 나머지는 상태가 바뀔 때만 찍는다.
            if (result == ValveInteractionEvent.Progressing)
            {
                if (_progressLogInterval > 0f && Time.time - _lastProgressLogTime >= _progressLogInterval)
                {
                    _lastProgressLogTime = Time.time;
                    Debug.Log($"[Valve] {label} 회전 중 {_controller.Progress01 * 100f:0}%");
                }
                _lastEvent = result;
                return;
            }

            if (result == _lastEvent)
                return;
            _lastEvent = result;

            switch (result)
            {
                case ValveInteractionEvent.Started:
                    _lastProgressLogTime = Time.time;
                    Debug.Log($"[Valve] {label} 회전 시작 (§6.1)");
                    EmitValveSound(valve);
                    break;

                case ValveInteractionEvent.CancelledByRelease:
                    Debug.Log($"[Valve] {label} 취소 — E를 뗌, 진행도 0으로 리셋 (§6.1)");
                    break;

                case ValveInteractionEvent.CancelledByRangeExit:
                    Debug.Log($"[Valve] {label} 취소 — 범위 이탈, 진행도 0으로 리셋 (§6.1)");
                    break;

                case ValveInteractionEvent.Completed:
                    Debug.Log($"[Valve] {label} 개방 완료");
                    break;

                case ValveInteractionEvent.Rejected:
                    Debug.Log($"[Valve] {label} 상호작용 거부 — 역할 {_player.Role} (GAP-5: 메아리는 밸브 조작 불가)");
                    break;
            }
        }

        /// <summary>
        /// §6.1 "밸브 회전 중 소음(12m/3초)이 지속 발생 — 중간에 멈춰도 이미 발생한 소음은
        /// 취소되지 않음". 그래서 시작 시점에 회전 시간 전체를 덮는 펄스를 한 번만 발행하고,
        /// 취소 시에는 아무것도 하지 않는다.
        /// </summary>
        private void EmitValveSound(ValveBehaviour valve)
        {
            if (_pulsePipeline == null || valve == null)
                return;

            _pulsePipeline.EmitPulse(
                SoundType.Valve,
                Valve.SoundRadiusMeters,
                valve.Valve.RotationSeconds,
                valve.transform.position);
        }
    }
}
