using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// §3.5 외침(술래)과 숨 참기(도망자)의 입력 계층.
    ///
    /// **한 컴포넌트에 둘을 담은 이유**: §3.5가 하나의 상호작용이다 — 술래가 외치고, 그 1초
    /// 선딜레이 안에 도망자가 숨을 참는다. 역할에 따라 활성 입력만 달라지므로
    /// <c>EchoKnockController</c>처럼 역할 게이트 하나로 갈라 쓴다.
    ///
    /// **서버가 결정하는 것 / 클라이언트가 결정하는 것**:
    /// - 클라이언트: 언제 누르는가 — 그게 전부다
    /// - 서버: 역할 · 쿨다운 45초 · 선딜레이 1초와 이동 취소 · 발동 위치 ·
    ///   공포 반경 22m · 숨 게이지 잔량과 -3 차감 · 비명 발생 여부
    /// 아래 역할·쿨다운 검사는 **표시와 트래픽 절약용 사전 필터**일 뿐이다.
    ///
    /// **키가 임시값인 이유**: §4.3이 "술래 전용: 외침 | **미확정**"이라 확정 키가 없다.
    /// 키 리바인딩 작업에서 정해질 때까지 <see cref="_shoutKey"/>의 임시값을 쓴다(GAP-67).
    /// 숨 참기는 §4.3이 "잠수 키와 동일(Left Ctrl 탭)"로 확정해 뒀다.
    /// </summary>
    public sealed class ShoutInputController : MonoBehaviour
    {
        [Header("§4.3 키")]
        [Tooltip("§4.3 '술래 전용: 외침 | 미확정'. 확정 전까지의 임시 키다(GAP-67).")]
        [SerializeField] private Key _shoutKey = Key.R;

        [Tooltip("§4.3 '숨 참기(비명 억제) | 잠수 키와 동일(Left Ctrl 탭)'.")]
        [SerializeField] private Key _holdBreathKey = Key.LeftCtrl;

        [Header("표시")]
        [SerializeField] private bool _showHud = true;

        private IPulseNetworkBridge _bridge;
        private float _clientShoutCooldownUntil; // 표시·트래픽 절약용 사전 필터(진실은 서버).

        /// <summary>표시용 남은 외침 쿨다운(초). 서버 판정과 어긋날 수 있는 근사값이다.</summary>
        public float ShoutCooldownRemaining => Mathf.Max(0f, _clientShoutCooldownUntil - Time.time);

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // 로컬 pawn은 **매 프레임 다시 읽는다(캐시 금지 — GAP-61).** 모든 pawn이
            // IsLocallyControlled 기본값 true로 자기를 등록하므로, 한 번 잡아 두면 순수
            // 클라이언트에서 원격 pawn을 영구히 물 수 있다.
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null || !player.IsLocallyControlled)
                return;

            if (player.Role == RoleType.Seeker)
            {
                if (keyboard[_shoutKey].wasPressedThisFrame)
                    TryShout();

                return; // 술래는 숨을 참을 일이 없다(§3.5는 도망자 대응이다).
            }

            // §3.5 "숨 참기(선딜레이 1초 안에 입력)" — 메아리는 이미 탈락이라 대상이 아니다.
            if (player.Role == RoleType.Runner && keyboard[_holdBreathKey].wasPressedThisFrame)
                TryHoldBreath();
        }

        private void TryShout()
        {
            if (Time.time < _clientShoutCooldownUntil)
            {
                Debug.Log($"[Shout] 아직 쿨다운 중이다 — {ShoutCooldownRemaining:0.0}초 남음" +
                          $"(§3.5 {SeekerShoutConfig.CooldownSeconds:0}초).");
                return;
            }

            if (!TryGetBridge(out IPulseNetworkBridge bridge))
            {
                Debug.Log("[Shout] 접속 상태가 아니라 외침을 보낼 수 없다(로컬 단독 실행에서는 능력이 비활성이다).");
                return;
            }

            // 사전 필터. 서버는 **선딜레이 완료 시점부터** 45초를 세므로 실제로는 1초 더 길다 —
            // 표시가 0이어도 서버가 거부할 수 있다(진실은 서버).
            _clientShoutCooldownUntil = Time.time + SeekerShoutConfig.CooldownSeconds;

            bridge.SubmitShout();
            Debug.Log($"[Shout] 외침 요청 — {SeekerShoutConfig.WindupSeconds:0.#}초 정지 선딜레이. " +
                      $"움직이면 취소된다(쿨다운 소모 없음, §3.5).");
        }

        private void TryHoldBreath()
        {
            if (!TryGetBridge(out IPulseNetworkBridge bridge))
                return;

            bridge.SubmitHoldBreath();
            Debug.Log($"[Shout] 숨 참기 입력 — 외침 선딜레이 안이고 공포 반경 {SeekerShoutConfig.FearRadiusMeters:0}m " +
                      $"안이면 비명이 억제된다(숨 게이지 -{Core.Breath.BreathConfig.SuppressionCost:0}, §3.5/§5.9-1).");
        }

        /// <summary>
        /// 발소리·음성·노크가 쓰는 브릿지를 그대로 쓴다 — 외침 전용 네트워크 채널을 만들지 않는다.
        /// 이 컴포넌트는 <c>SceneFlow</c>에 있고 브릿지는 <c>PulseSystem</c>에 있어 씬에서 찾는다.
        /// </summary>
        private bool TryGetBridge(out IPulseNetworkBridge bridge)
        {
            if (_bridge == null)
            {
                foreach (MonoBehaviour candidate in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                {
                    if (candidate is IPulseNetworkBridge found)
                    {
                        _bridge = found;
                        break;
                    }
                }
            }

            bridge = _bridge;
            return bridge != null && bridge.NetworkActive;
        }

        private void OnGUI()
        {
            if (!_showHud || Event.current.type != EventType.Repaint)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null || !player.IsLocallyControlled || player.Role != RoleType.Seeker)
                return;

            float remaining = ShoutCooldownRemaining;
            string line = remaining > 0f
                ? $"외침 쿨다운 {remaining:0.0}초 (§3.5 {SeekerShoutConfig.CooldownSeconds:0}초)"
                : $"{_shoutKey} — 외침 (§3.5 1초 정지 후 22m 안 도망자가 비명)";

            GUI.Label(new Rect(16f, Screen.height - 64f, 560f, 24f), line);
        }
    }
}
