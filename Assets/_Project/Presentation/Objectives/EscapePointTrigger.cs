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
        /// <summary>역할 배정 시스템 배선 전 로컬 플레이어 임시 ID(다른 시스템과 동일 값).</summary>
        private const ulong LocalPlayerId = 1;

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

            bool inside = Vector3.Distance(_player.transform.position, transform.position) <= _escapeRadius;

            // 범위에 "들어온 순간"에만 시도한다 — 서 있는 동안 매 프레임 시도하지 않도록.
            if (inside && !_wasInside)
                TryEscape();

            _wasInside = inside;
        }

        private void TryEscape()
        {
            if (_roundCoordinator.TryRegisterEscape(LocalPlayerId, _player.Role))
                return;

            // 등록되지 않은 이유 중 플레이어가 알아야 할 것은 "게이트가 아직 닫힘"뿐이다.
            if (_logGateClosedHint && !_gateClosedHintShown && !_roundCoordinator.IsEscapeGateOpen)
            {
                _gateClosedHintShown = true;
                Debug.Log("[Escape] 배수로 게이트가 아직 닫혀 있다 — 밸브 3개를 모두 열어야 탈출할 수 있다 (§6.1)");
            }
        }
    }
}
