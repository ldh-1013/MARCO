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

        [Tooltip("스프린트 18b: 교차 씬 지연 바인딩이 실제로 성공했는지 실기에서 눈으로 확인하기 위한 진단 로그(각 1회).")]
        [SerializeField] private bool _logCoordinatorBinding = true;

        private bool _gateClosedHintShown;
        private bool _wasInside;
        private bool _bindLogged;
        private bool _bindFailureLogged;

        private void Awake()
        {
            // 스프린트 18b: 맵이 애디티브로 로드되면 이 트리거(맵 소속)와 RoundCoordinator(시스템 씬)가
            // **다른 씬**에 있게 되어 인스펙터 교차 씬 참조가 불가능하다. 그래서 인스펙터 참조가
            // 비어 있으면 런타임에 찾고(아래 EnsureCoordinator), 못 찾아도 비활성화하지 않는다 —
            // 로드 순서에 따라 잠시 뒤에 나타날 수 있기 때문이다.
            EnsureCoordinator();

            // 플레이어는 네트워크로 스폰될 수 있어 이 시점에 없을 수 있다.
            LocalPlayerRegistry.WhenReady(player => _player = player);
        }

        /// <summary>
        /// 라운드 지휘부를 확보한다(씬 간 지연 바인딩). 이미 있으면 즉시 반환.
        /// 맵과 시스템이 다른 씬에 있어도 동작하게 하는 유일한 연결점이다.
        /// </summary>
        private bool EnsureCoordinator()
        {
            if (_roundCoordinator != null)
                return true;

            _roundCoordinator = FindAnyObjectByType<RoundCoordinator>();

            // 진단(스프린트 18b): 마이그레이션 시 Unity가 교차 씬 참조를 지원하지 않아 인스펙터의
            // _roundCoordinator를 null로 정리한다("Cross scene references are not supported" 경고).
            // 그 자리를 이 지연 탐색이 메우므로, 실제로 메워졌는지 실기에서 확인할 수 있어야 한다.
            if (!_logCoordinatorBinding)
                return _roundCoordinator != null;

            if (_roundCoordinator != null)
            {
                if (!_bindLogged)
                {
                    _bindLogged = true;
                    Debug.Log($"[Escape:Diag] RoundCoordinator 지연 바인딩 성공 — " +
                              $"이 트리거='{gameObject.name}'({gameObject.scene.name} 씬), " +
                              $"지휘부='{_roundCoordinator.gameObject.name}'({_roundCoordinator.gameObject.scene.name} 씬). " +
                              "두 씬 이름이 다르면 교차 씬 연결이 정상 동작한다는 뜻이다(§15.1).");
                }
            }
            else if (!_bindFailureLogged)
            {
                // 맵이 시스템 씬보다 먼저 로드되면 잠시 못 찾을 수 있다 — 그래서 1회만 알리고
                // 매 프레임 재시도를 계속한다(Update).
                _bindFailureLogged = true;
                Debug.Log($"[Escape:Diag] RoundCoordinator를 아직 찾지 못했다('{gameObject.scene.name}' 씬 기준) — " +
                          "시스템 씬이 로드되면 자동으로 연결된다. 탈출 시도 시점까지 " +
                          "성공 로그가 없으면 시스템 씬 누락을 의심할 것.");
            }

            return _roundCoordinator != null;
        }

        private void Update()
        {
            // 로컬 플레이어가 아직 스폰되지 않았으면 판정할 대상이 없다.
            if (_player == null)
                return;

            // 시스템 씬이 아직 준비되지 않았을 수 있다(맵이 먼저 로드된 경우).
            if (!EnsureCoordinator())
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
