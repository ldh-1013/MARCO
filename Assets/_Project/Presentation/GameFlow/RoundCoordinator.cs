using UnityEngine;
using Marco.Core.GameFlow;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.Objectives;
using Marco.Presentation.Tagging;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 라운드 진행의 단일 지휘부. 밸브 개방 수(§6.1) · 탈출 수 · 남은 시간(§6.2)을
    /// 모아 §6.3 판정을 **한 곳에서만** 수행하고, 승패가 갈리면 §15.4 상태기계를
    /// `InGame → RoundEnd`로 전이시킨다.
    ///
    /// 판정식은 Core `WinConditionEvaluator`, 상태 전이는 Core `GameFlowManager`가
    /// 소유한다 — 여기서는 입력 수집과 구동만 한다(스프린트 5 배선 패턴 그대로).
    ///
    /// 정식 결과 화면·타이머 HUD(§12)는 별도 UI 스프린트라, 확인은 Console 로그다.
    /// </summary>
    public sealed class RoundCoordinator : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ValveObjectiveTracker _valveTracker;

        [Header("§6.2 제한시간")]
        [Tooltip("4인 MVP 기준 10분(600초). 기획서 §6.2 표에 명시된 값이다.")]
        [SerializeField] private float _roundDurationSeconds = RoundTimer.FourPlayerSeconds;

        [Header("판정 입력 보조")]
        [Tooltip("태그 시스템을 거치지 않고 §6.3 술래 승리 분기를 강제로 확인하고 싶을 때만 사용.")]
        [SerializeField] private bool _forceAllRunnersTagged;

        [Header("디버그")]
        [Tooltip("남은 시간 로그 간격(초). 0이면 끈다.")]
        [SerializeField] private float _timeLogInterval = 30f;

        private readonly RoundTimer _timer = new RoundTimer();
        private readonly RoundOutcomeTracker _outcome = new RoundOutcomeTracker();
        private readonly GameFlowManager _gameFlow = new GameFlowManager();
        private float _lastTimeLog;
        private int _totalRunners;

        // 스프린트 12: 같은 오브젝트의 서버 권위 브릿지(없으면 null — 로컬 전용).
        private IRoundNetworkBridge _bridge;
        private RoundResult _lastServerResult = RoundResult.InProgress;
        private float _lastServerTimeLog;

        public RoundTimer Timer => _timer;
        public RoundOutcomeTracker Outcome => _outcome;
        public GameFlowManager GameFlow => _gameFlow;

        /// <summary>§6.1: 밸브가 전부 열려야 배수로 게이트가 열린다.</summary>
        public bool IsEscapeGateOpen => _valveTracker != null && _valveTracker.IsEscapeGateOpen;

        /// <summary>
        /// 라운드가 서버 권위(네트워크)로 관리되는가. 그러면 로컬 타이머·판정을 멈추고
        /// 서버가 전파한 값(남은 시간·탈출 수·최종 결과)만 반영한다.
        /// </summary>
        public bool IsNetworkActive => _bridge != null && _bridge.NetworkActive;

        private void Awake()
        {
            // 같은 오브젝트에 Net의 RoundNetworkSync가 있으면 Core 인터페이스로만 잡는다.
            _bridge = GetComponent<IRoundNetworkBridge>();

            if (_valveTracker == null)
                _valveTracker = FindAnyObjectByType<ValveObjectiveTracker>();

            // §6.3 allRunnersTagged 판정의 분모. 로컬에서는 씬에 놓인 대역 도망자 수다.
            // 스프린트 11 주의: 네트워크 모드에서 실제 러너 수를 반영하는 것은 라운드 결과
            // 네트워크화(다음 스프린트) 몫이라, 이 분모는 아직 대역 기준이다(GAP-18 기록).
            _totalRunners = FindObjectsByType<TaggableRunner>().Length;
        }

        // 스프린트 11: 태그 확정은 로컬 대역/네트워크 플레이어 모두 TagTargetRegistry를 통해
        // 통지된다. 서버가 확정한 태그만 이 이벤트로 오므로("서버 확정 기준"), 각 피어의
        // RoundCoordinator가 같은 태그 집합을 집계한다. TagDetector가 직접 등록하던 것을 대체.
        private void OnEnable() => TagTargetRegistry.TargetTagged += OnTargetTagged;
        private void OnDisable() => TagTargetRegistry.TargetTagged -= OnTargetTagged;

        private void OnTargetTagged(ITagTarget target)
        {
            if (target == null)
                return;

            // 스프린트 12: 네트워크 활성 시 전원 태그 판정은 서버가 한다(RoundNetworkSync가
            // TagTargetRegistry를 서버 측에서 직접 읽어 판정). 클라이언트가 로컬 집계로
            // 판정하면 서버와 이중 판정이 되므로, 네트워크면 로컬 집계를 하지 않는다.
            if (IsNetworkActive)
                return;

            // 태그된 대상은 태그 시점에 항상 도망자였다(TagDetector·서버가 Runner만 통과시킴).
            // 통지 시점의 target.Role은 이미 Echo이므로, 판정에는 태그 당시 역할(Runner)을 넘긴다.
            TryRegisterTag(RoleType.Seeker, target.PlayerId, RoleType.Runner);
        }

        private void Start()
        {
            // 로컬 단독 실행용 스캐폴딩: §15.4 전이표는 Boot부터 순서대로만 진행할 수
            // 있으므로, InGame까지 밀어 올려 InGame → RoundEnd 전이를 정상 경로로 만든다.
            // 실제 진행은 로비·역할 배정 시스템이 생기면 그쪽이 구동한다.
            _gameFlow.TryTransition(GameFlowState.MainMenu);
            _gameFlow.TryTransition(GameFlowState.Lobby);
            _gameFlow.TryTransition(GameFlowState.RoleAssign);
            _gameFlow.TryTransition(GameFlowState.InGame);

            _timer.Expired += OnTimerExpired;
            _timer.Start(_roundDurationSeconds);

            _lastTimeLog = Time.time;
            Debug.Log($"[Round] 라운드 시작 — 제한시간 {_roundDurationSeconds:0}초 (§6.2), 상태 {_gameFlow.CurrentState}");
        }

        private void OnDestroy() => _timer.Expired -= OnTimerExpired;

        private void Update()
        {
            // 스프린트 12: 네트워크 활성 시 서버가 단독 판정한다 — 로컬 타이머·판정을 돌리지 않고
            // 서버가 전파한 값만 반영한다(밸브·태그와 같은 폴백 원칙, 로컬 실행은 아래 경로 그대로).
            if (IsNetworkActive)
            {
                ReflectServerRound();
                return;
            }

            if (_outcome.IsDecided)
                return;

            _timer.Tick(Time.deltaTime);
            LogRemainingTime();
            EvaluateRound();
        }

        /// <summary>
        /// 네트워크 활성 시: 로컬 판정 대신 서버가 확정한 결과를 반영한다. 남은 시간은 서버
        /// 권위 값을 로그로 찍고, 결과가 InProgress → 승패로 바뀌는 순간 1회만 라운드를 종료한다.
        /// </summary>
        private void ReflectServerRound()
        {
            LogServerRemaining();

            RoundResult serverResult = _bridge.Result;
            if (serverResult == RoundResult.InProgress || _lastServerResult != RoundResult.InProgress)
                return;

            _lastServerResult = serverResult;
            _timer.Stop();
            _gameFlow.TryTransition(GameFlowState.RoundEnd);

            Debug.Log($"[Round] 라운드 종료(서버 확정) — 판정: {serverResult} " +
                      $"(탈출 {_bridge.EscapedCount}명, 남은 시간 {_bridge.RemainingSeconds:0.0}초) " +
                      $"→ 상태 {_gameFlow.CurrentState}");
        }

        /// <summary>
        /// 탈출 지점(<see cref="Objectives.EscapePointTrigger"/>)이 호출한다.
        /// 네트워크 활성 시 서버에 요청만 보내고(서버가 §5.3 재검증·확정·전파),
        /// 로컬 단독 실행이면 기존 경로로 즉시 집계한다. 처리(요청 전송/로컬 집계)됐으면 true.
        /// </summary>
        public bool RequestEscape(ulong playerId, RoleType role)
        {
            if (IsNetworkActive)
            {
                _bridge.SubmitEscapeIntent(playerId, role);
                return true;
            }

            return TryRegisterEscape(playerId, role);
        }

        /// <summary>
        /// [로컬 전용] 실제로 새로 집계됐을 때만 true.
        /// 역할 제약(GAP-11)과 게이트 개방 조건(§6.1)은 <see cref="RoundOutcomeTracker"/>가 강제한다.
        /// </summary>
        public bool TryRegisterEscape(ulong playerId, RoleType role)
        {
            if (!_outcome.TryRegisterEscape(playerId, role, IsEscapeGateOpen))
                return false;

            Debug.Log($"[Round] 탈출 — playerId={playerId} (누적 {_outcome.EscapedCount}명)");
            EvaluateRound();
            return true;
        }

        /// <summary>
        /// 태그 판정(§3.1)이 호출한다. 실제로 새로 태그됐을 때만 true.
        /// 역할 조건은 <see cref="RoundOutcomeTracker"/>가 강제한다 — 판정 규칙을 한 곳에만 둔다.
        /// </summary>
        public bool TryRegisterTag(RoleType taggerRole, ulong targetId, RoleType targetRole)
        {
            if (!_outcome.TryRegisterTag(taggerRole, targetId, targetRole))
                return false;

            Debug.Log($"[Tag] 태그 — targetId={targetId} 메아리로 전환 (§3.1) " +
                      $"[{_outcome.TaggedCount}/{_totalRunners}]");
            EvaluateRound();
            return true;
        }

        private void OnTimerExpired()
        {
            Debug.Log("[Round] 제한시간 종료 (§6.2)");
            EvaluateRound();
        }

        private void EvaluateRound()
        {
            int opened = _valveTracker != null ? _valveTracker.OpenedCount : 0;
            int total = _valveTracker != null ? _valveTracker.TotalValves : 0;
            bool allTagged = _forceAllRunnersTagged || _outcome.AreAllRunnersTagged(_totalRunners);

            if (!_outcome.Evaluate(opened, total, allTagged, _timer.RemainingSeconds))
                return;

            _timer.Stop();
            _gameFlow.TryTransition(GameFlowState.RoundEnd);

            Debug.Log($"[Round] 라운드 종료 — 판정: {_outcome.Result} " +
                      $"(밸브 {opened}/{total}, 탈출 {_outcome.EscapedCount}명, " +
                      $"태그 {_outcome.TaggedCount}/{_totalRunners}, " +
                      $"남은 시간 {_timer.RemainingSeconds:0.0}초) → 상태 {_gameFlow.CurrentState}");
        }

        private void LogRemainingTime()
        {
            if (_timeLogInterval <= 0f || _timer.HasExpired)
                return;

            if (Time.time - _lastTimeLog < _timeLogInterval)
                return;

            _lastTimeLog = Time.time;
            Debug.Log($"[Round] 남은 시간 {_timer.RemainingSeconds:0}초");
        }

        /// <summary>네트워크 경로: 서버 권위 남은 시간을 주기적으로 로그(양쪽 창 동일 값 확인용).</summary>
        private void LogServerRemaining()
        {
            if (_timeLogInterval <= 0f)
                return;

            if (Time.time - _lastServerTimeLog < _timeLogInterval)
                return;

            _lastServerTimeLog = Time.time;
            Debug.Log($"[Round] 남은 시간 {_bridge.RemainingSeconds:0}초 (서버 권위)");
        }
    }
}
