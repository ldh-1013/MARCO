using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.Echo
{
    /// <summary>
    /// §3.2 메아리 노크의 입력 계층(스프린트 27, 기획서 갱신 반영).
    ///
    /// **미니맵은 폐지됐다(§3.2-1)**: "미니맵 시스템은 존재하지 않는다. 기존 '미니맵/탑다운 뷰'는
    /// 전면 삭제하고 소나 시야로 대체한다." §4.3도 <c>Tab(미니맵 토글) + 좌클릭(지점 지정)</c> 행을
    /// 명시적으로 삭제했다. 그래서 이 컴포넌트에서 탑다운 카메라·클릭 좌표 변환·커서 해제가
    /// 전부 사라졌고, 남은 것은 <b>"지금 쓴다"는 키 입력 하나</b>다.
    ///
    /// **발생 위치는 클라이언트가 정하지 않는다(§3.2)**: "발생 위치: 메아리의 현재 위치.
    /// 지점 클릭 방식은 채택하지 않는다." 위치는 서버가 <c>caller.FirstObject</c>에서 얻으므로
    /// (발소리·밸브·음성과 같은 경로, GAP-24) <see cref="IPulseNetworkBridge.SubmitKnock"/>의
    /// 페이로드는 비어 있다.
    ///
    /// **서버가 결정하는 것 / 클라이언트가 결정하는 것**:
    /// - 클라이언트: 언제 누르는가 — 그게 전부다
    /// - 서버: 메아리인가 · 라운드 5회 하드캡 · 전환 후 20초 잠금 · 쿨다운 25초 · 1.5초 지연 ·
    ///   발생 위치 · 파문 반경/지속 · 차폐 · 유인 판정
    /// 아래 역할·쿨다운 검사는 **표시와 트래픽 절약용 사전 필터**일 뿐이며, 통과 여부의
    /// 진실은 <c>ServerKnockDriver</c>가 쥔다(밸브·태그와 같은 원칙).
    ///
    /// **키가 임시값인 이유**: §4.3이 "메아리 전용: 노크 | **미확정**"이라 확정 키가 없다.
    /// 키 리바인딩 작업에서 정해질 때까지 <see cref="_knockKey"/>의 임시값을 쓴다(GAP-65).
    /// </summary>
    public sealed class EchoKnockController : MonoBehaviour
    {
        [Header("§4.3 키")]
        [Tooltip("§4.3 '메아리 전용: 노크 | 미확정'. 확정 전까지의 임시 키다(GAP-65).")]
        [SerializeField] private Key _knockKey = Key.F;

        [Header("표시")]
        [SerializeField] private bool _showHud = true;

        private IPulseNetworkBridge _bridge;
        private float _clientCooldownUntil; // 표시·트래픽 절약용 사전 필터(진실은 서버).

        /// <summary>표시용 남은 쿨다운(초). 서버 판정과 어긋날 수 있는 근사값이다.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _clientCooldownUntil - Time.time);

        private void Update()
        {
            // 메아리가 아니면 능력 자체가 없다(§3.2).
            if (!IsLocalEcho())
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_knockKey].wasPressedThisFrame)
                TryKnock();
        }

        /// <summary>
        /// 로컬 플레이어가 살아 있고 메아리인가(§3.2 노크는 메아리 전용).
        ///
        /// <b>레지스트리를 매 프레임 다시 읽는다(캐시 금지).</b> 모든 pawn은 <c>OnEnable</c> 시점에
        /// <c>IsLocallyControlled</c> 기본값이 true라 **원격 pawn도 자기를 등록**하고, 순수
        /// 클라이언트에서는 원격 pawn이 먼저 도착하는 일이 흔하다. 그 시점에 한 번만 바인딩하면
        /// (<c>WhenReady</c>는 1회성이다) 소유권이 확정돼 <c>Unregister</c> 복구가 일어나도
        /// **이 컴포넌트만 원격 pawn을 계속 물고 있어** 키가 조용히 아무 일도 하지 않는다.
        /// <c>InGameHud</c>·<c>FallRecoveryDriver</c>·<c>PawnPhaseTeleporter</c>·<c>SettingsStore</c>가
        /// 전부 매 프레임 <see cref="LocalPlayerRegistry.Current"/>를 읽는 것과 같은 이유다(GAP-61).
        ///
        /// 소유권 판별 자체는 새로 만들지 않는다 — <c>PlayerOwnershipGate</c>(Net)가
        /// <c>IsOwner</c>를 <see cref="Marco.Core.Net.ILocalControlGate"/>로 밀어 넣은 결과인
        /// <c>IsLocallyControlled</c>를 그대로 쓴다.
        /// </summary>
        private bool IsLocalEcho()
        {
            FirstPersonController player = LocalPlayerRegistry.Current;
            return player != null && player.IsLocallyControlled && player.Role == RoleType.Echo;
        }

        /// <summary>
        /// §3.2 노크 1회를 서버에 요청한다. 위치를 보내지 않는다 —
        /// 서버가 이 플레이어의 현재 위치를 그대로 발생 지점으로 쓴다.
        /// </summary>
        private void TryKnock()
        {
            if (Time.time < _clientCooldownUntil)
            {
                Debug.Log($"[Knock] 아직 쿨다운 중이다 — {CooldownRemaining:0.0}초 남음(§3.2 {KnockConfig.CooldownSeconds:0}초).");
                return;
            }

            if (!TryGetBridge(out IPulseNetworkBridge bridge))
            {
                Debug.Log("[Knock] 접속 상태가 아니라 노크를 보낼 수 없다(로컬 단독 실행에서는 능력이 비활성이다).");
                return;
            }

            // 사전 필터를 먼저 걸어 둔다 — 서버가 거부하면 실제로는 더 길게 남아 있을 수 있다.
            // (라운드 5회 소진·전환 후 20초 잠금은 여기서 흉내 내지 않는다. 클라이언트가 라운드
            //  경계와 태그 시각을 정확히 알지 못해, 흉내 내면 오히려 서버와 어긋난 표시가 된다.)
            _clientCooldownUntil = Time.time + KnockConfig.CooldownSeconds;

            bridge.SubmitKnock();
            Debug.Log($"[Knock] 노크 요청 — 현재 위치에서 {KnockConfig.ActivationDelaySeconds:0.#}초 뒤 발생, " +
                      $"반경 {KnockConfig.RadiusMeters:0.#}m (§3.2). 최종 판정은 서버가 한다.");
        }

        /// <summary>
        /// 발소리·음성이 쓰는 브릿지를 그대로 쓴다 — 노크 전용 네트워크 채널을 만들지 않는다
        /// (26b가 음성에서 내린 판단과 같다). 이 컴포넌트는 <c>SceneFlow</c>에 있고 브릿지는
        /// <c>PulseSystem</c>에 있어 씬에서 찾는다(같은 Lobby 씬).
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
            if (!_showHud || Event.current.type != EventType.Repaint || !IsLocalEcho())
                return;

            float remaining = CooldownRemaining;
            string line = remaining > 0f
                ? $"노크 쿨다운 {remaining:0.0}초 (§3.2 {KnockConfig.CooldownSeconds:0}초)"
                : $"{_knockKey} — 노크 (§3.2 현재 위치에서 1.5초 뒤 발생, 라운드 {KnockConfig.MaxUsesPerRound}회)";

            GUI.Label(new Rect(16f, Screen.height - 40f, 520f, 24f), line);
        }
    }
}
