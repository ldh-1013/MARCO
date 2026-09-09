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
        [Tooltip("태그 시스템을 거치지 않고 §6.3 술래 승리 분기(태그 2명 도달)를 강제로 확인하고 싶을 때만 사용.")]
        [SerializeField] private bool _forceSeekerTagWin;

        [Header("디버그")]
        [Tooltip("남은 시간 로그 간격(초). 0이면 끈다.")]
        [SerializeField] private float _timeLogInterval = 30f;

        private readonly RoundTimer _timer = new RoundTimer();

        // 스프린트 17: 로컬 재시작 시 새 인스턴스로 교체한다(RoundOutcomeTracker는 판정을 래치하며
        // 되돌리는 전이를 갖지 않는다 — Core 판정 로직을 건드리지 않으려고 교체 방식을 택했다).
        private RoundOutcomeTracker _outcome = new RoundOutcomeTracker();
        private readonly GameFlowManager _gameFlow = new GameFlowManager();
        private float _lastTimeLog;
        private int _totalRunners;

        // 스프린트 12: 같은 오브젝트의 서버 권위 브릿지(없으면 null — 로컬 전용).
        private IRoundNetworkBridge _bridge;
        private float _lastServerTimeLog;

        // 스프린트 18: 마지막으로 미러링한 서버 페이즈(§15.4). Boot = 아직 아무것도 안 받음.
        private GameFlowState _lastMirroredPhase = GameFlowState.Boot;

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

        // ── 표시용 읽기 전용 접근자 (스프린트 16 HUD) ──────────────────────
        // 새 계산이 없다 — 네트워크/로컬 소스 선택 규칙은 이미 이 클래스가 쓰던 것과 동일하며
        // (ValveBehaviour.IsOpen과 같은 패턴), HUD가 그 판단을 중복하지 않도록 여기서 노출한다.

        /// <summary>§6.2 남은 시간. 네트워크면 서버 확정값, 아니면 로컬 타이머 값.</summary>
        public float RemainingSeconds => IsNetworkActive ? _bridge.RemainingSeconds : _timer.RemainingSeconds;

        /// <summary>§6.3 라운드 결과. 네트워크면 서버 확정값, 아니면 로컬 판정 결과.</summary>
        public RoundResult Result => IsNetworkActive ? _bridge.Result : _outcome.Result;

        /// <summary>서버/로컬 어느 경로든 집계된 탈출자 수.</summary>
        public int EscapedCount => IsNetworkActive ? _bridge.EscapedCount : _outcome.EscapedCount;

        // ── 스프린트 18: 로비·리매치 표시용 접근자(LobbyScreen·ResultScreen 소비) ──

        /// <summary>현재 진행 페이즈(§15.4). 네트워크면 서버 확정값, 로컬이면 자체 상태기계.</summary>
        public GameFlowState CurrentPhase => IsNetworkActive ? _bridge.Phase : _gameFlow.CurrentState;

        /// <summary>§12.3 시작 카운트다운 남은 초(RoleAssign 페이즈에서만 의미).</summary>
        public float CountdownRemaining => IsNetworkActive ? _bridge.CountdownRemaining : 0f;

        /// <summary>§12.5 리매치 유효 찬성 수.</summary>
        public int RematchVotesFor => IsNetworkActive ? _bridge.RematchVotesFor : 0;

        /// <summary>§12.5 리매치 가결 필요 표(과반).</summary>
        public int RematchVotesNeeded => IsNetworkActive ? _bridge.RematchVotesNeeded : 0;

        /// <summary>§12.5 리매치 투표 남은 초.</summary>
        public float RematchSecondsRemaining => IsNetworkActive ? _bridge.RematchSecondsRemaining : 0f;

        /// <summary>
        /// 통산 라운드 번호(스프린트 21). 로컬 단독 실행에는 서버 카운터가 없어 0으로 고정한다 —
        /// 소비자(스폰 리셋)는 "값이 바뀌면 새 라운드"로만 쓰므로 로컬에서는 리셋이 일어나지 않는다.
        /// </summary>
        public int RoundNumber => IsNetworkActive ? _bridge.RoundNumber : 0;

        /// <summary>§8 어워드 수상자(플레이어 id). 로컬 단독 실행이면 수상자 없음(-1).</summary>
        public int AwardLoudestScream => IsNetworkActive ? _bridge.AwardLoudestScream : -1;
        public int AwardSilentSurvivor => IsNetworkActive ? _bridge.AwardSilentSurvivor : -1;
        public int AwardBestLiar => IsNetworkActive ? _bridge.AwardBestLiar : -1;

        private void Awake()
        {
            // 같은 오브젝트에 Net의 RoundNetworkSync가 있으면 Core 인터페이스로만 잡는다.
            _bridge = GetComponent<IRoundNetworkBridge>();

            if (_valveTracker == null)
                _valveTracker = FindAnyObjectByType<ValveObjectiveTracker>();

            // **판정에는 더 이상 쓰이지 않는다** — §6.3이 종료 조건을 "태그 2명 도달"로 확정해
            // 분모가 필요 없어졌고(GAP-13/GAP-18 소멸), 이 값은 이제 로그 표시용이다
            // ("태그 1/3"처럼 진행 상황을 읽기 위한 것). 로컬에서는 씬의 대역 도망자 수다.
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

            // 스프린트 18: 진행의 단일 진실 소스가 결과 값에서 서버 페이즈(§15.4)로 바뀌었다.
            // 결과 래치·해제 추론(스프린트 17)은 페이즈 미러링으로 대체 — 로비/카운트다운/부결 복귀까지
            // 모든 진행 분기를 서버가 명시적으로 알려주므로 클라이언트가 추론할 것이 없다.
            GameFlowState serverPhase = _bridge.Phase;
            if (serverPhase == _lastMirroredPhase)
                return;

            MirrorPhase(serverPhase);
            _lastMirroredPhase = serverPhase;

            if (serverPhase == GameFlowState.RoundEnd)
            {
                _timer.Stop();
                Debug.Log($"[Round] 라운드 종료(서버 확정) — 판정: {_bridge.Result} " +
                          $"(탈출 {_bridge.EscapedCount}명, 남은 시간 {_bridge.RemainingSeconds:0.0}초)");
            }
            else if (serverPhase == GameFlowState.InGame)
            {
                Debug.Log("[Round] 라운드 시작(서버 확정) — §15.4 InGame");
            }
            else if (serverPhase == GameFlowState.Lobby)
            {
                Debug.Log("[Round] 로비(서버 확정) — 전원 준비를 기다린다(§12.3)");
            }
        }

        /// <summary>
        /// 로컬 §15.4 상태기계를 서버 페이즈까지 **허용된 전이만 밟아** 이동시킨다(스프린트 18).
        /// 전이표는 순서대로만 진행 가능하므로, 목표까지의 다음 단계를 반복해 시도한다.
        /// (예: 접속 직전 로컬 스캐폴딩이 InGame까지 가 있었다면 InGame→RoundEnd→Lobby로 수렴.)
        /// </summary>
        private void MirrorPhase(GameFlowState target)
        {
            int guard = 0;
            while (_gameFlow.CurrentState != target && guard++ < 8)
            {
                if (!_gameFlow.TryTransition(NextStepToward(_gameFlow.CurrentState, target)))
                    break;
            }

            if (_gameFlow.CurrentState != target)
                Debug.LogWarning($"[Round] 페이즈 미러링 실패 — 로컬 {_gameFlow.CurrentState}, 서버 {target} (§15.4 전이표 확인 필요)");
        }

        /// <summary>§15.4 전이표에서 <paramref name="target"/>을 향한 다음 한 걸음.</summary>
        private static GameFlowState NextStepToward(GameFlowState current, GameFlowState target)
        {
            switch (current)
            {
                case GameFlowState.Boot: return GameFlowState.MainMenu;
                case GameFlowState.MainMenu: return GameFlowState.Lobby;
                case GameFlowState.Lobby: return GameFlowState.RoleAssign;
                case GameFlowState.RoleAssign: return GameFlowState.InGame;
                case GameFlowState.InGame: return GameFlowState.RoundEnd;
                // §15.4 유일한 분기점: 부결 → Lobby, 가결/신규 → RoleAssign.
                case GameFlowState.RoundEnd: return target == GameFlowState.Lobby ? GameFlowState.Lobby : GameFlowState.RoleAssign;
                default: return target;
            }
        }

        /// <summary>
        /// 결과 화면(<c>ResultScreen</c>)이 호출하는 재시작 요청(스프린트 17).
        /// 네트워크면 서버에 요청만 보내고, 로컬 단독 실행이면 이 자리에서 직접 되돌린다.
        /// </summary>
        public void RequestRestart()
        {
            if (IsNetworkActive)
            {
                _bridge.RequestRestart();
                return;
            }

            RestartLocalRound();
        }

        /// <summary>
        /// [로컬 전용] 라운드를 초기 상태로 되돌린다(스프린트 17).
        ///
        /// 네트워크 경로와 같은 범위를 되돌린다 — 라운드 상태(타이머·탈출·판정 래치)와 밸브.
        /// 로컬에는 서버 배정이 없으므로 역할은 인스펙터 값을 그대로 유지하고, 대역 러너
        /// (<c>TaggableRunner</c>)는 스프린트 15에서 비활성화됐으므로 태그 복구 대상이 없다.
        /// </summary>
        private void RestartLocalRound()
        {
            if (!_outcome.IsDecided)
                return; // 진행 중 재시작은 §12.5 흐름이 아니다(네트워크 경로와 동일 규칙).

            _outcome = new RoundOutcomeTracker();

            if (_valveTracker != null)
            {
                ValveBehaviour[] valves = _valveTracker.Valves;
                for (int i = 0; i < valves.Length; i++)
                    valves[i]?.ResetValveForNewRound();
            }

            _gameFlow.TryTransition(GameFlowState.RoleAssign);
            _gameFlow.TryTransition(GameFlowState.InGame);

            _timer.Start(_roundDurationSeconds);
            _lastTimeLog = Time.time;

            Debug.Log($"[Round] 라운드 재시작(로컬) — 제한시간 {_roundDurationSeconds:0}초, 상태 {_gameFlow.CurrentState}");
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
            // §6.3은 "태그 2명 도달"이 종료 조건이라 전체 러너 수(분모)가 필요 없다 —
            // 강제 옵션도 임계값을 그대로 넘기는 것으로 충분하다.
            int tagged = _forceSeekerTagWin
                ? WinConditionEvaluator.TagWinThreshold
                : _outcome.TaggedCount;

            if (!_outcome.Evaluate(opened, total, tagged, _timer.RemainingSeconds))
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
