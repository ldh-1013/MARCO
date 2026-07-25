using UnityEngine;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Player;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// §10.1 배수로 출구의 탈출 지점. 러너가 도달하면 <see cref="RoundCoordinator"/>에
    /// 탈출을 등록한다.
    ///
    /// 역할 제약(GAP-11: 러너만 집계)과 게이트 개방 조건(§6.1: 밸브 전부 개방)은
    /// **여기서 검사하지 않는다** — `RoundOutcomeTracker`가 이미 강제하므로
    /// 중복 검사 없이 한 곳에서만 처리한다(스프린트 5 GAP-5 처리와 같은 원칙).
    ///
    /// 근접 판정은 밸브(스프린트 5)와 동일하게 거리 기반이다. 트리거 콜라이더 대신
    /// 거리를 쓰면 씬 배선이 단순하고 순수 로직 테스트와 결이 맞는다.
    /// </summary>
    public sealed class EscapePointTrigger : MonoBehaviour
    {
        [SerializeField] private FirstPersonController _player;
        [SerializeField] private RoundCoordinator _roundCoordinator;

        [Tooltip("GAP-12: 기획서에 탈출 판정 반경 수치가 없어 둔 값. 플레이테스트 조정 대상.")]
        [SerializeField] private float _escapeRadius = 2f;

        [Header("디버그")]
        [Tooltip("게이트가 닫힌 상태로 도달했을 때 안내 로그를 낼지(1회만).")]
        [SerializeField] private bool _logGateClosedHint = true;

        private bool _gateClosedHintShown;
        private bool _wasInside;

        private void Awake()
        {
            if (_roundCoordinator == null)
                _roundCoordinator = FindAnyObjectByType<RoundCoordinator>();

            if (_roundCoordinator == null)
            {
                Debug.LogError("[Escape] RoundCoordinator를 찾지 못했습니다 — 비활성화합니다.");
                enabled = false;
                return;
            }

            // 플레이어는 네트워크로 스폰될 수 있어 이 시점에 없을 수 있다.
            LocalPlayerRegistry.WhenReady(player => _player = player);
        }

        private void Update()
        {
            // 로컬 플레이어가 아직 스폰되지 않았으면 판정할 대상이 없다.
            if (_player == null)
                return;

            // 스프린트 12: 원격 프록시가 내 입력 없이 탈출 요청을 보내지 않도록 소유권 가드
            // (밸브 스프린트 10·태그 스프린트 11과 같은 원칙). _player는 로컬 플레이어지만 방어적으로 확인한다.
            if (!_player.IsLocallyControlled)
                return;

            bool inside = Vector3.Distance(_player.transform.position, transform.position) <= _escapeRadius;

            // 범위에 "들어온 순간"에만 시도한다 — 서 있는 동안 매 프레임 시도하지 않도록.
            if (inside && !_wasInside)
                TryEscape();

            _wasInside = inside;
        }

        private void TryEscape()
        {
            // 게이트 개방 전엔 요청 자체를 보내지 않는다(클라 사전 필터로 RPC 낭비 방지).
            // 서버는 이와 무관하게 다시 재검증한다(§5.3) — 안전성은 서버가 보장한다.
            if (!_roundCoordinator.IsEscapeGateOpen)
            {
                if (_logGateClosedHint && !_gateClosedHintShown)
                {
                    _gateClosedHintShown = true;
                    Debug.Log("[Escape] 배수로 게이트가 아직 닫혀 있다 — 밸브 3개를 모두 열어야 탈출할 수 있다 (§6.1)");
                }
                return;
            }

            // 네트워크면 서버에 요청만 보내고(서버가 재검증·확정·전파), 로컬이면 즉시 집계한다.
            // 어느 경로인지는 RoundCoordinator가 라우팅한다(§15.2 경계 유지).
            _roundCoordinator.RequestEscape(_player.PlayerId, _player.Role);
        }
    }
}
