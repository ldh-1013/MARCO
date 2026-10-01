using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.Awards;
using Marco.Core.GameFlow;
using Marco.Core.Net;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 라운드 전체(타이머·탈출·최종 판정)를 서버 권위로 동기화한다(스프린트 12).
    ///
    /// **스프린트 18 — 페이즈 상태기계로 확장**: 기존 "서버 시작 즉시 라운드 시작"을 §15.4
    /// 진행 흐름으로 대체했다. 서버가 <see cref="SyncVar{T}"/>로 전파하는 페이즈가 모든 진행의
    /// 단일 진실 소스다:
    /// <code>
    /// Lobby ──(전원 Ready, ServerLobbyDriver)──▶ RoleAssign ──(3초, §12.3)──▶ InGame
    ///   ▲                                            ▲                          │
    ///   │ 부결(§15.4)                                 │ 가결(즉시 재시작)          │ §6.3 판정
    ///   └────────────── RoundEnd(리매치 투표 15초·과반, RematchVoteDriver) ◀─────┘
    /// </code>
    /// - 로비 게이트·카운트다운: <see cref="ServerLobbyDriver"/>(Core, 순수 — §12.3/§15.4)
    /// - 리매치 투표: <see cref="RematchVoteDriver"/>(Core, 순수 — §12.5, GAP-27 해소)
    /// - 라운드 판정: <see cref="ServerRoundDriver"/>(스프린트 12 그대로 — 판정 로직 무변경)
    ///
    /// **판정 입력의 출처**(스프린트 12 · 09-29 개정): 밸브 게이트 = 서버의 <see cref="RoundObjective"/>(SyncVar로 공개),
    /// 전원 태그 = <see cref="TagTargetRegistry"/>, 탈출 = <see cref="ServerSubmitEscape"/>,
    /// 타이머 = 이 컴포넌트. 준비 인원 = <c>ReadyNetworkSync.Spawned</c>(스프린트 18).
    ///
    /// **어셈블리 경계**: Net은 Presentation을 참조하지 않는다 — 소비자(<c>RoundCoordinator</c>·
    /// <c>LobbyScreen</c>)는 <see cref="IRoundNetworkBridge"/>(Core)로만 읽는다.
    ///
    /// **NRE 가드**(스프린트 10 교훈): <c>IsSpawned</c>/<c>IsServerStarted</c>는
    /// <see cref="NetworkBehaviour.NetworkObject"/> null 확인 뒤에만 호출한다.
    /// </summary>
    [DefaultExecutionOrder(ServerTickOrder.UnderwaterWork)] // §5.9-1 0초 경계 — 숨 게이지 다음에 배수구 세션이 돈다
    public sealed class RoundNetworkSync : NetworkBehaviour, IRoundNetworkBridge
    {
        [Tooltip("§6.2 제한시간. 4인 MVP=600초. 로컬 RoundTimer.FourPlayerSeconds와 같은 값을 유지할 것.")]
        [SerializeField] private float _roundDurationSeconds = 600f;

        [Tooltip("라운드 시작에 필요한 최소 인원(§1 최소 2). 혼자 밸브·파문을 점검하는 솔로 테스트 때만 1로 낮춘다.")]
        [SerializeField] private int _minPlayers = RoleAssigner.MinimumPlayers;

        /// <summary>
        /// 서버가 현재 확정한 진행 페이즈(서버 프로세스 전용 정적 노출 — 스프린트 18).
        /// <c>ValveNetworkSync</c>·<c>TagNetworkSync</c>·<c>ReadyNetworkSync</c>의 ServerRpc가
        /// "지금 이 입력이 유효한 페이즈인가"를 검사하는 데 쓴다(로비 중 밸브 조작 차단 등).
        /// 클라이언트 표시는 이 정적이 아니라 SyncVar(<see cref="Phase"/>)를 읽는다.
        /// </summary>
        internal static GameFlowState ServerPhase { get; private set; } = GameFlowState.Boot;

        /// <summary>서버에서 동작 중인 인스턴스(§8 어워드 집계 유입점 — <c>PulseNetworkSync</c>가 쓴다).</summary>
        private static RoundNetworkSync ServerInstance { get; set; }

        /// <summary>
        /// 한 프레임에 이 거리를 넘게 움직였으면 이동이 아니라 순간이동(스폰·리스폰)으로 본다.
        /// 최고 속도는 메아리 8.0m/s(<c>LocomotionConfig.EchoSpeed</c> — 러너 질주 7.5m/s보다 빠르다)라,
        /// 프레임당 2m는 정상 이동으로 도달할 수 없는 값이다.
        /// </summary>
        private const float MaxDistancePerFrame = 2f;

        // 서버가 확정해 전 클라이언트에 전파하는 권위 상태.
        private readonly SyncVar<GameFlowState> _phase = new(); // 기본값 Boot(0) — 스폰 전 표시는 소비자가 NetworkActive로 거른다
        private readonly SyncVar<float> _remaining = new();
        private readonly SyncVar<int> _escaped = new();
        private readonly SyncVar<RoundResult> _result = new();
        private readonly SyncVar<float> _countdown = new();      // §12.3 3초 카운트다운(RoleAssign)

        /// <summary>
        /// §12.4 로비 브리핑 잔여(초). <b>시작 시 1회만 보내고 클라이언트가 센다</b>(§14.3) —
        /// 값은 브리핑 시작 시점의 30이고, 클라이언트는 수신 시각부터 뺀다.
        /// </summary>
        private readonly SyncVar<float> _briefingStartValue = new();
        private float _briefingReceivedAt;
        private readonly SyncVar<int> _votesFor = new();         // §12.5 리매치 찬성 수
        private readonly SyncVar<int> _votesNeeded = new();      // §12.5 과반 기준
        private readonly SyncVar<float> _voteRemaining = new();  // §12.5 15초 창

        /// <summary>
        /// 통산 라운드 번호(0-기반). §2.3 술래 로테이션의 순번 기준이며, 세트/판수 표시에도 쓴다.
        /// 첫 라운드가 0이 되도록 -1에서 시작한다.
        /// </summary>
        private readonly SyncVar<int> _roundNumber = new();

        // §8 어워드 3종 수상자(플레이어 id, -1이면 수상자 없음). 서버가 집계해 결과만 전파한다.
        private readonly SyncVar<int> _awardScream = new();
        private readonly SyncVar<int> _awardSilent = new();
        private readonly SyncVar<int> _awardLiar = new();

        /// <summary>§8 "세션 로그" 기준 누적 집계(서버 전용).</summary>
        private readonly AwardTally _awards = new AwardTally();

        /// <summary>이동거리 집계용 직전 위치(서버 전용, 플레이어 id → 위치).</summary>
        private readonly Dictionary<int, Vector3> _lastPositions = new Dictionary<int, Vector3>();

        /// <summary>
        /// §6.2-1 종반 압박(잔여 1분 술래 +5%)이 적용됐는가. <b>1회 적용 후 라운드 끝까지
        /// 유지된다</b> — §6.2-1이 *"껐다 켜지 않는다 — 경계에서 속도가 오락가락하면
        /// 추격 감각이 무너진다"* 고 못박았다. 전 피어가 술래 속도를 같이 알아야 하므로
        /// SyncVar다.
        /// </summary>
        private readonly SyncVar<bool> _endgamePressure = new();

        /// <summary>§6.2 이번 라운드 총원(술래 이속 보정 입력). 순수 클라이언트용 전파.</summary>
        private readonly SyncVar<int> _totalPlayers = new();

        /// <summary>§6.5-1 최후 생존자 페이즈 중인가. 순수 클라이언트용 전파.</summary>
        private readonly SyncVar<bool> _lastSurvivorPhase = new();

        /// <summary>
        /// §6.5-1 페이즈 진입 순간의 **라운드 잔여**(초). <b>1회만 전송한다</b> — 클라이언트는
        /// <c>페이즈 잔여 = 90 − (진입 시 라운드 잔여 − 현재 라운드 잔여)</c>로 스스로 센다.
        /// 매 프레임 따로 보내지 않는다(§14.3).
        /// </summary>
        private readonly SyncVar<float> _phaseEntryRoundRemaining = new();

        /// <summary>§6.5-2 활성 배수구(0 없음 / 1 메인풀 / 2 유아풀). 양 진영 공개.</summary>
        private readonly SyncVar<int> _activeDrain = new();

        /// <summary>§6.5-2 배수구 진행도.</summary>
        private readonly SyncVar<float> _drainProgress = new();

        /// <summary>§6.5-2 배수구 감쇠 중(§12.4 HUD 색 구분).</summary>
        private readonly SyncVar<bool> _drainDecaying = new();

        // 이번 판 목표 수치(09-29) — 서버가 라운드 시작 때 확정하고 게이트는 서버만 판정한다. 클라이언트는 이 값을 읽는다
        // (ObjectiveRegistry → ValveObjectiveTracker · HUD · 브리핑 · 출구 사전 필터). 피어마다 따로 계산하지 않는다.
        private readonly SyncVar<int> _activeValves = new();
        private readonly SyncVar<int> _requiredOpen = new();
        private readonly SyncVar<bool> _gateOpen = new();
        private readonly RoundObjective _objective = new RoundObjective();
        private readonly List<Vector3> _gatePulses = new List<Vector3>();

        /// <summary>§6.5-2 이번 페이즈의 배수구(서버 전용). 없으면 null.</summary>
        private DrainHatch _drain;

        /// <summary>§6.1 [v0.4] / §6.5-3 배수구 수중 작업 강제 파문 타이머(서버 전용).</summary>
        private readonly UnderwaterWorkPulse _drainPulse = new UnderwaterWorkPulse();

        /// <summary>
        /// §6.5-2 배수구 작업 <b>의사</b>(E를 누르고 있다고 주장한 플레이어). 서버 전용.
        /// 거절하고 끝내지 않고 <b>주장을 보관한 채 매 틱 자격을 재평가한다</b> —
        /// 위치 동기화 지연으로 경계에서 한 번 거부되면 클라이언트는 값이 바뀔 때만 보내므로
        /// E를 다시 누를 때까지 영영 붙지 않는다. 잠수 주장(<c>_diveIntent</c>)과 같은 패턴이다.
        /// </summary>
        private readonly HashSet<ulong> _drainIntent = new HashSet<ulong>();

        /// <summary>
        /// [커밋 전 수정 4-1] 배수구 작업 세션 — 수중 밸브 B·E와 <b>같은</b> <see cref="UnderwaterWorkSession"/>
        /// (진입 1 → 작업 T → 부상 1, 전 구간 잠수). 부상을 마쳐야 통과가 열린다. 서버 전용.
        /// </summary>
        private readonly Dictionary<ulong, UnderwaterWorkSession> _drainSessions = new Dictionary<ulong, UnderwaterWorkSession>();
        private readonly List<ulong> _drainScratch = new List<ulong>();

        /// <summary>§6.2-1 잔여 30초 출구 파문의 다음 발생 시각(서버 전용).</summary>
        private float _nextExitPulseAt;

        private ServerRoundDriver _driver;      // InGame 동안만 존재(서버 전용)
        private ServerLobbyDriver _lobby;       // 서버 전용
        private RoundStartSequencer _start;     // RoleAssign: 카운트다운 → 맵 대기(18b) → 브리핑 → 라운드(서버 전용)
        private bool _valveSpawnWaitLogged;     // 맵 씬은 올라왔는데 밸브 스폰 대기 — 진단 로그 1회(서버 전용)
        private RematchVoteDriver _vote;        // RoundEnd 동안만 존재(서버 전용)

        /// <summary>
        /// 이번 판의 술래 순번(§2.3 로테이션). 라운드 배정 시점에 **한 번만** 확정하고 그 뒤로는
        /// 재계산하지 않는다. -1은 미확정.
        ///
        /// <b>왜 고정해야 하는가</b>: <see cref="SeekerRotation.SeekerOrderIndex"/>는
        /// <c>roundNumber % playerCount</c>라 인원수에 의존한다. 라운드 중 한 명만 합류해도 값이
        /// 달라져, 진행 중이던 술래가 러너로 강등되고 다른 러너가 술래가 됐다. 더 나쁜 경우로는
        /// 새 순번이 이미 태그당한 플레이어를 가리켜 <b>술래가 0명</b>이 되기도 했다.
        /// </summary>
        private int _fixedSeekerOrder = -1;

        // 재사용 버퍼(매 프레임 할당 방지 — T8 성능 조사의 무할당 원칙).
        private readonly List<RoleNetworkSync> _assignBuffer = new List<RoleNetworkSync>();
        private readonly List<ulong> _liveIdBuffer = new List<ulong>();

        // ── IRoundNetworkBridge ───────────────────────────────────────────

        /// <summary>NetworkObject가 스폰된 뒤에만 true(연결 전 NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        public float RemainingSeconds => _remaining.Value;
        public int EscapedCount => _escaped.Value;
        public RoundResult Result => _result.Value;
        public GameFlowState Phase => _phase.Value;
        public float CountdownRemaining => _countdown.Value;

        public float BriefingSecondsRemaining =>
            _briefingStartValue.Value > 0f && _phase.Value == GameFlowState.RoleAssign
                ? Mathf.Max(0f, _briefingStartValue.Value - (Time.time - _briefingReceivedAt))
                : 0f;
        public int RematchVotesFor => _votesFor.Value;
        public int RematchVotesNeeded => _votesNeeded.Value;
        public float RematchSecondsRemaining => _voteRemaining.Value;
        public int RoundNumber => _roundNumber.Value;
        public int AwardLoudestScream => _awardScream.Value;
        public int AwardSilentSurvivor => _awardSilent.Value;
        public int AwardBestLiar => _awardLiar.Value;

        public bool LastSurvivorPhaseActive => _lastSurvivorPhase.Value;

        public float LastSurvivorSecondsRemaining
        {
            get
            {
                if (!_lastSurvivorPhase.Value)
                    return 0f;

                // 클라이언트 쪽 카운트다운 — 진입 시 1회 받은 값에서 스스로 센다.
                float elapsed = _phaseEntryRoundRemaining.Value - _remaining.Value;
                float phase = Mathf.Max(0f, DrainConfig.PhaseSeconds - elapsed);
                return Mathf.Min(_remaining.Value, phase);
            }
        }

        public int ActiveDrain => _activeDrain.Value;
        public float DrainProgress01 => _drainProgress.Value;
        public bool DrainDecaying => _drainDecaying.Value;

        public void SubmitDrainHold(bool held)
        {
            if (!NetworkActive)
                return;

            ServerSubmitDrainHold(held);
        }

        /// <summary>
        /// 탈출 의사만 보낸다 — <paramref name="playerId"/>·<paramref name="role"/>은 **전송하지 않고**
        /// 서버가 호출자에서 직접 읽는다(<see cref="ServerSubmitEscape"/>). 인자는
        /// <see cref="IRoundNetworkBridge"/> 계약을 로컬 단독 실행 경로와 공유하기 위해 남아 있다.
        /// </summary>
        public void SubmitEscapeIntent(ulong playerId, RoleType role)
        {
            if (!NetworkActive)
                return;

            ServerSubmitEscape();
        }

        public void RequestRestart()
        {
            if (!NetworkActive)
                return;

            ServerRequestRestart();
        }

        // ── 서버: 시작 = 로비 대기 (스프린트 18 — 즉시 시작 폐지) ──────────

        public override void OnStartServer()
        {
            base.OnStartServer();

            _lobby = new ServerLobbyDriver(_minPlayers);
            _start = new RoundStartSequencer(_lobby);
            _driver = null; // 라운드는 로비 게이트를 통과해야만 생성된다.
            _vote = null;

            _remaining.Value = _roundDurationSeconds; // HUD가 로비에서도 제한시간을 표시할 수 있게
            _escaped.Value = 0;
            _result.Value = RoundResult.InProgress;
            _countdown.Value = 0f;

            // 첫 라운드가 0번이 되도록 -1에서 시작한다(§2.3 로테이션 순번 기준).
            _roundNumber.Value = -1;

            // §8 어워드는 "세션 로그" 기준이라 세션 시작에 한 번만 비운다(라운드마다 비우지 않는다).
            ServerInstance = this;
            _awards.Reset();
            _lastPositions.Clear();
            _awardScream.Value = AwardTally.NoWinner;
            _awardSilent.Value = AwardTally.NoWinner;
            _awardLiar.Value = AwardTally.NoWinner;

            SetPhase(GameFlowState.Lobby);
            Debug.Log($"[RoundNet:Server] 로비 대기 시작 — 최소 {_minPlayers}명 전원 준비 시 " +
                      $"{ServerLobbyDriver.CountdownSeconds:0}초 카운트다운(§12.3/§15.4). 즉시 시작은 폐지됐다(스프린트 18).");
        }

        private void Update()
        {
            if (!NetworkActive || !IsServerStarted)
                return;

            float dt = Time.deltaTime;
            switch (_phase.Value)
            {
                case GameFlowState.Lobby: TickLobby(dt); break;
                case GameFlowState.RoleAssign: TickCountdown(dt); break;
                case GameFlowState.InGame: TickRound(dt); break;
                case GameFlowState.RoundEnd: TickVote(dt); break;
            }
        }

        /// <summary>§12.4 브리핑 시작 값을 받은 시각 — 클라이언트 카운트다운의 기준점.</summary>
        private void OnBriefingStartChanged(float prev, float next, bool asServer)
        {
            if (next > 0f)
                _briefingReceivedAt = Time.time;
        }

        // ── 서버: 페이즈별 틱 ────────────────────────────────────────────

        private void TickLobby(float dt)
        {
            CountReadyPlayers(out int players, out int ready);

            if (_lobby.Tick(players, ready, dt) == LobbyTickResult.CountdownStarted)
            {
                _countdown.Value = _lobby.CountdownRemaining;
                SetPhase(GameFlowState.RoleAssign);
                Debug.Log($"[RoundNet:Server] 전원 준비({ready}/{players}) — {ServerLobbyDriver.CountdownSeconds:0}초 카운트다운 시작(§12.3)");
            }
        }

        /// <summary>
        /// RoleAssign 페이즈 서버 틱. 순서 판정은 <see cref="RoundStartSequencer"/>가 하고 여기서는 부수 효과만 낸다.
        /// <b>[긴급 수정]</b> 로비 드라이버는 카운트다운 단계에서만 틱한다 — 맵 대기·브리핑 중에 틱하면
        /// 3초마다 StartRound가 다시 나와 라운드 번호·역할·브리핑이 무한 반복됐다(가드는 시퀀서 안, GAP-104).
        /// </summary>
        private void TickCountdown(float dt)
        {
            CountReadyPlayers(out int players, out int ready);
            RoundStartEvents events = _start.Tick(players, ready, MapReadyForRound(), dt);
            _countdown.Value = _lobby.CountdownRemaining;

            if ((events & RoundStartEvents.Aborted) != 0)
            {
                // GAP-29: 카운트다운(3초) 중 이탈·신규 접속자 미준비 시 즉시 로비로. 준비 토글은 Lobby
                // 페이즈에서만 받으므로 RoleAssign에서 "준비 해제"는 일어나지 않는다. 카운트다운이 끝난 뒤
                // (맵 대기·브리핑)에는 중단하지 않는다(GAP-104).
                SetPhase(GameFlowState.Lobby);
                _briefingStartValue.Value = 0f;
                Debug.Log("[RoundNet:Server] 카운트다운 중단 — 준비 해제/이탈, 로비로 복귀(GAP-29)");
                return;
            }

            if ((events & RoundStartEvents.StartRound) != 0)
            {
                // §2.3 술래 로테이션: 배정 **전에** 라운드 번호를 확정한다 — 이 번호가 이번 판의
                // 술래 순번을 정하고, 라운드 중 늦게 들어온 플레이어의 재배정에도 같은 값이 쓰인다.
                _roundNumber.Value++;

                // §15.4 RoleAssign: "3초 연출, 역할 배정" — 배정을 카운트다운 완료 시점에
                // 1회 수행한다(중단 시 되돌릴 배정이 없도록 끝에서 확정).
                // 이 호출만이 술래 순번을 정한다(_fixedSeekerOrder 확정).
                EnsureRolesAssigned(roundStart: true);

                // 스프린트 18b: §15.4 "InGame: **맵 로드**" — 맵이 올라온 뒤에 라운드를 시작한다.
                // 맵 없이 시작하면 밸브가 0개라 §6.1 배수로 게이트가 영구히 닫혀 라운드가 성립하지 않는다.
                // 로드는 비동기라 이 틱의 맵 준비 판정(위 Tick 인자)을 바꾸지 않는다 — 이미 올라와 있으면 무동작.
                SceneFlowController.Instance?.ServerLoadMap();
            }

            // 맵이 준비된 틱에 브리핑 시작(페이즈는 RoleAssign 유지 — GAP-30).
            if ((events & RoundStartEvents.BriefingStarted) != 0)
                BeginBriefing();

            // §12.4 로비 브리핑 30초 — 끝나면 라운드 시작("라운드 시작 시 사라진다").
            if ((events & RoundStartEvents.BeginRound) != 0)
            {
                _briefingStartValue.Value = 0f;
                BeginRound();
            }
        }

        /// <summary>
        /// §12.4 로비 브리핑 시작. <b>활성 밸브 조합을 여기서 확정한다</b> — 평면도가 "이번 라운드의
        /// 활성 밸브 위치"를 보여줘야 하므로 라운드 시작 전에 골라져 있어야 한다(§6.1-0 "로비 브리핑
        /// 30초에 위치 공개"). 밸브 초기화(<see cref="ServerResetWorld"/>)는 이보다 앞선 리매치/로비
        /// 전이에서 이미 끝났다 — 여기서 고른 조합이 지워지지 않는다.
        /// </summary>
        private void BeginBriefing()
        {
            ServerSelectActiveValves();
            _briefingStartValue.Value = BriefingConfig.Seconds; // 서버 타이머는 RoundStartSequencer가 센다
            _countdown.Value = 0f;
            Debug.Log($"[RoundNet:Server] §12.4 로비 브리핑 {BriefingConfig.Seconds:0}초 — 평면도 + 이번 라운드 활성 밸브 공개. " +
                      "끝나면 라운드 시작");
        }

        /// <summary>
        /// 라운드를 시작해도 되는 맵 상태인가(스프린트 18b).
        ///
        /// 씬 흐름 컨트롤러가 없는 구성(맵과 시스템이 한 씬에 있는 스프린트 18 이전 배치)에서는
        /// 항상 true다 — 씬 분리 전/후 어느 배치에서도 동작하게 하려는 것이다(마이그레이션 안전장치).
        ///
        /// <para>
        /// <b>[긴급 수정] 씬 로드 완료만으로는 부족하다 — 맵의 밸브가 스폰돼 있어야 한다.</b> Unity가 씬을
        /// "로드됨"으로 보고하는 틱에는 FishNet이 그 씬의 NetworkObject를 아직 스폰하지 않았다
        /// (<see cref="ValveNetworkSync.Spawned"/>는 <c>OnStartNetwork</c>에서 채워진다). 그 틱에 브리핑을
        /// 시작하면 <see cref="ServerSelectActiveValves"/>가 "밸브 0개"로 건너뛰어 <b>밸브 5개가 전부 활성</b>인 채
        /// 라운드가 돈다(§6.2 "활성 = 요구 + 1" 붕괴 — 실기 로그 첫 브리핑에서 확인). 무한 루프 동안에는 두 번째
        /// 반복부터 밸브가 잡혀 가려져 있었다. 씬 오브젝트는 한 번에 스폰되므로 밸브 1개 이상 = 스폰 완료.
        /// </para>
        /// </summary>
        private bool MapReadyForRound()
        {
            SceneFlowController flow = SceneFlowController.Instance;
            if (flow == null)
                return true;
            if (!flow.MapLoaded)
                return false;
            if (ValveNetworkSync.Spawned.Count > 0)
            {
                _valveSpawnWaitLogged = false;
                return true;
            }

            // 예전에는 여기서 "밸브 0개" 경고와 함께 라운드가 시작됐다. 이제는 기다리므로, 스폰이 끝내 안 오면
            // 조용히 멈추지 않게 한 번 남긴다(정상 로드에서는 1~2틱 뒤 바로 브리핑 로그가 따라온다).
            if (!_valveSpawnWaitLogged)
            {
                _valveSpawnWaitLogged = true;
                Debug.Log("[RoundNet:Server] 맵 씬 로드됨 — 밸브 NetworkObject 스폰 대기(FishNet). " +
                          "이 줄 뒤로 브리핑 로그가 오지 않으면 맵 씬의 밸브 네트워크 배선을 확인하라(§6.1-0).");
            }
            return false;
        }

        private void TickRound(float dt)
        {
            if (_driver == null)
                return;

            // 라운드 중 접속한 플레이어도 배정을 받는다(스프린트 13 동작 보존) —
            // 단 **신규 접속자만** 러너로 채우고 기존 배정은 건드리지 않는다.
            EnsureRolesAssigned();

            // §8 어워드: 이동거리는 라운드 진행 중에만 센다(로비 이동은 집계 대상이 아니다).
            AccumulateDistances();

            if (_driver.IsDecided)
                return;

            _driver.Tick(dt);
            if (_remaining.Value != _driver.RemainingSeconds)
                _remaining.Value = _driver.RemainingSeconds;

            ServerTickGate();

            // §6.2-1 종반 압박 — 이 호출부가 없으면 술래 이속 +5%도 출구 파문도 영영 안 걸린다.
            TickEndgamePressure(_driver.RemainingSeconds);

            TickDrain(dt);

            PublishRoundState();

            EvaluateAndPush();
        }

        private void TickVote(float dt)
        {
            if (_vote == null)
                return;

            BuildLivePlayerIds();
            RematchVoteResult result = _vote.Tick(_liveIdBuffer, dt);

            _votesFor.Value = _vote.CountVotes(_liveIdBuffer);
            _votesNeeded.Value = RematchVoteDriver.RequiredVotes(_liveIdBuffer.Count);
            _voteRemaining.Value = _vote.SecondsRemaining;

            switch (result)
            {
                case RematchVoteResult.Passed:
                    // §15.4 가결: RoundEnd → RoleAssign(즉시 재시작, Lobby를 거치지 않음).
                    ServerResetWorld();
                    _lobby.BeginCountdown();
                    _countdown.Value = _lobby.CountdownRemaining;
                    SetPhase(GameFlowState.RoleAssign);
                    Debug.Log($"[RoundNet:Server] 리매치 가결({_votesFor.Value}표) — 즉시 재시작 카운트다운(§12.5/§15.4)");
                    break;

                case RematchVoteResult.Failed:
                    // §15.4 부결: RoundEnd → Lobby. 전원 준비 해제 — 다음 라운드는 새 합의로.
                    ServerResetWorld();
                    ClearAllReady();
                    _lobby.ResetForLobby();

                    // 스프린트 18b: 로비로 돌아가면 맵을 내린다(§15.4상 맵은 InGame의 것이다).
                    // 다음 라운드에서 새로 로드되므로 밸브·탈출 지점도 깨끗한 상태로 다시 온다.
                    // (맵 대기 상태는 위 ServerResetWorld의 시퀀서 초기화가 지운다.)
                    SceneFlowController.Instance?.ServerUnloadMap();

                    SetPhase(GameFlowState.Lobby);
                    Debug.Log("[RoundNet:Server] 리매치 부결(15초 만료) — 로비로 복귀, 전원 준비 해제 + 맵 언로드(§12.5/§15.4)");
                    break;
            }
        }

        /// <summary>라운드를 실제로 시작한다(로비 게이트 통과 후에만 도달). 서버 전용.</summary>
        private void BeginRound()
        {
            _driver = new ServerRoundDriver(_roundDurationSeconds);
            _remaining.Value = _driver.RemainingSeconds;
            _escaped.Value = 0;
            _result.Value = RoundResult.InProgress;
            _countdown.Value = 0f;

            // 활성 조합은 브리핑 시작 시 이미 골랐다(BeginBriefing). 여기서 다시 고르면 평면도와
            // 실제 활성 밸브가 달라진다.

            SetPhase(GameFlowState.InGame);
            Debug.Log($"[RoundNet:Server] 라운드 시작 — 서버 권위 타이머 {_roundDurationSeconds:0}초 (§6.2)");
        }

        /// <summary>
        /// §6.1-0 이번 라운드 활성 밸브를 서버가 고른다 — 10-01 규칙: 수영장 B · E 고정 + A · C · D 중 1개 = 3개.
        /// <b>이 호출부가 없으면 밸브 5개가 전부 활성이고 필요 개방 수도 정해지지 않는다</b>
        /// (과거 <c>ServerKnockDriver.Reset()</c> 호출부 부재와 같은 유형).
        ///
        /// <para>
        /// <b>시드</b>는 라운드 번호와 총원을 섞어 만든다 — <c>UnityEngine.Random</c>은 Core
        /// 계층 위반이고(§15.2), 시드를 고정할 수 있어야 테스트가 조합을 재현한다.
        /// 라운드마다 달라지므로 §6.1-0의 "매 라운드 조합이 달라진다"도 성립한다.
        /// </para>
        ///
        /// <para>
        /// <b>결과만 네트워크로 나간다</b> — 클라이언트가 같은 시드로 다시 굴리게 만들지 않는다
        /// (그러면 서버 권위가 아니라 합의가 된다). 각 밸브의 <c>_active</c> SyncVar가 전파한다.
        /// </para>
        /// </summary>
        private void ServerSelectActiveValves()
        {
            List<ValveNetworkSync> valves = ValveNetworkSync.Spawned;
            if (valves.Count == 0)
            {
                Debug.LogWarning("[RoundNet:Server] 밸브가 0개 — 활성 선택을 건너뜁니다. " +
                                 "맵이 로드되지 않았거나 밸브 배선이 빠졌습니다(§6.1-0).");
                return;
            }

            CountReadyPlayers(out int totalPlayers, out int _);
            if (totalPlayers <= 0)
                totalPlayers = valves.Count;

            // 10-01 규칙(활성 3 · 수영장 B · E 고정 · 필요 개방 = 활성 수)을 여기서 단언한다 — 어긋나면 조용히 밸런스가
            // 무너지는 대신 이 지점에서 터진다.
            ValveRoster.AssertInvariant();

            // 라운드 번호가 시드에 들어가므로 매 라운드(재경기 포함) 다시 고른다.
            int seed = unchecked(_roundNumber.Value * 73856093 + totalPlayers * 19349663 + 1);
            List<ValveId> active = ValveRoster.SelectActive(seed);

            for (int i = 0; i < valves.Count; i++)
            {
                ValveNetworkSync valve = valves[i];
                if (valve == null)
                    continue;

                bool isActive = valve.ValveId.HasValue && active.Contains(valve.ValveId.Value);
                valve.ServerSetActive(isActive);
            }

            // 필요 개방 수 = 활성 밸브 수(10-01, 인원 무관) — 여기서 한 번 확정한다. 라운드 중 인원이 바뀌어도 이 판에서는 변하지 않는다.
            _objective.BeginRound(totalPlayers, active.Count);
            ServerPublishObjective();

            Debug.Log($"[RoundNet:Server] §6.1-0 활성 밸브 {active.Count}개 " +
                      $"({string.Join(", ", active)}) / 요구 개방 {_objective.RequiredOpen}개 " +
                      $"· 총원 {totalPlayers} · seed={seed}");
        }

        /// <summary>서버 목표 수치를 SyncVar와 레지스트리(호스트)에 공개한다. 서버 전용.</summary>
        private void ServerPublishObjective()
        {
            if (_activeValves.Value != _objective.ActiveValves)
                _activeValves.Value = _objective.ActiveValves;
            if (_requiredOpen.Value != _objective.RequiredOpen)
                _requiredOpen.Value = _objective.RequiredOpen;
            if (_gateOpen.Value != _objective.GateOpen)
                _gateOpen.Value = _objective.GateOpen;

            ObjectiveRegistry.Publish(_objective.Publication);
        }

        /// <summary>서버 게이트 판정 — 지금 동시에 열린 밸브 수로 래치를 갱신한다(역류로 줄어도 닫히지 않는다). 서버 전용.</summary>
        private void ServerTickGate()
        {
            int opened = CountOpenValves();
            bool openedNow = _objective.Tick(opened);

            // §10.5 게이트가 열리는 순간 양쪽 출구에서 고함급 파문 1회 — 출구 좌표는 씬의 탈출 지점에서 읽는다.
            GateAnnouncement.CollectPulses(openedNow, EscapePointRegistry.Positions, _gatePulses);
            for (int i = 0; i < _gatePulses.Count; i++)
                PulseNetworkSync.ServerEmitWorldPulse(GateAnnouncement.PulseType, _gatePulses[i]);

            if (!openedNow)
                return;

            ServerPublishObjective();
            Debug.Log($"[RoundNet:Server] §6.1-2 게이트 개방 — 동시 개방 {opened}/{_objective.RequiredOpen} (래치, 역류로 닫혀도 유지) · " +
                      $"§10.5 출구 개방음 {_gatePulses.Count}곳({GateAnnouncement.PulseType})");
        }

        /// <summary>순수 클라이언트: 목표 수치 SyncVar가 바뀌면 레지스트리를 다시 채운다(늦은 접속자도 같은 값).</summary>
        private void OnClientObjective()
        {
            ObjectiveRegistry.Publish(new ObjectivePublication(true, _activeValves.Value, _requiredOpen.Value, _gateOpen.Value));
        }

        private void OnObjectiveInt(int prev, int next, bool asServer) => OnClientObjective();

        private void OnObjectiveBool(bool prev, bool next, bool asServer) => OnClientObjective();

        /// <summary>
        /// §6.2-1 종반 압박이 걸렸는가(전 피어 공유). 이동 시뮬레이터가 술래 속도를
        /// 고를 때 읽는다.
        /// </summary>
        public static bool EndgamePressureActive { get; private set; }

        /// <summary>
        /// §6.2-1 종반 압박을 매 프레임 판정한다. 서버 전용, <see cref="TickRound"/>에서 호출.
        ///
        /// <para>
        /// 세 단계 중 <b>수치 변화는 1분의 +5%뿐</b>이다 — 2분 드론은 연출이고(블록 7),
        /// 30초 출구 파문은 정보 제공이다(위치는 이미 공개된 출구라 정보 손실이 없다).
        /// </para>
        /// </summary>
        private void TickEndgamePressure(float remaining)
        {
            // §6.2-1 1분 — 술래 이속 +5% 추가. **1회 적용 후 유지**한다.
            if (!_endgamePressure.Value && remaining <= LocomotionConfig.EndgamePressureSeconds)
            {
                _endgamePressure.Value = true;
                EndgamePressureActive = true;

                CountReadyPlayers(out int totalPlayers, out int _);
                Debug.Log($"[RoundNet:Server] §6.2-1 종반 압박 — 술래 이속 " +
                          $"{LocomotionConfig.SeekerSpeedFor(totalPlayers):0.00} → " +
                          $"{LocomotionConfig.SeekerSpeedFor(totalPlayers, endgamePressure: true):0.00} m/s " +
                          $"(1회 적용, 라운드 끝까지 유지)");
            }

            // §6.2-1 30초 — 양쪽 출구에서 고함급(22m) 파문 주기 발생.
            //   "종반에 도망자가 숨어서 시간을 버리는 것이 최적해가 되면 마지막 30초가
            //    무사건 구간이 된다" — 마지막 도박을 유도한다.
            if (remaining > LocomotionConfig.EndgameExitPulseSeconds)
                return;

            if (Time.time < _nextExitPulseAt)
                return;

            _nextExitPulseAt = Time.time + ExitPulseIntervalSeconds;
            EmitExitPulses();
        }

        /// <summary>
        /// §6.2-1 출구 파문 주기(초). <b>기획서에 "주기 발생"만 적혀 있고 간격이 없다</b> —
        /// 잠정값 5초다(30초 구간에 6회). GAP-86.
        /// </summary>
        private const float ExitPulseIntervalSeconds = 5f;

        /// <summary>
        /// §6.2-1 양쪽 출구에서 고함급 파문. §10.5 출구 좌표를 씬의 탈출 지점에서 읽는다 —
        /// 좌표를 코드에 박으면 §10.1과 갈라진다.
        /// </summary>
        private void EmitExitPulses()
        {
            List<Vector3> exits = EscapePointRegistry.Positions;
            if (exits.Count == 0)
                return;

            for (int i = 0; i < exits.Count; i++)
            {
                PulseNetworkSync.ServerEmitWorldPulse(
                    GateAnnouncement.PulseType, exits[i]);
            }

            Debug.Log($"[RoundNet:Server] §6.2-1 출구 파문 {exits.Count}곳 " +
                      $"(고함급 22m, 잔여 {_remaining.Value:0}초)");
        }

        /// <summary>
        /// §6.2 / §6.2-1 / §6.5-1 라운드 전역 상태를 Core 레지스트리에 발행한다 —
        /// Presentation(이동 시뮬레이터·HUD)이 §15.2를 넘지 않고 읽는 경로다.
        ///
        /// <para>
        /// <b>호스트(서버 겸 클라이언트)에서만 실제로 채워진다.</b> 순수 클라이언트는
        /// 이 메서드가 돌지 않으므로 <see cref="OnClientRoundState"/>가 SyncVar 변경에서
        /// 같은 레지스트리를 채운다 — 두 경로가 같은 값을 쓴다.
        /// </para>
        /// </summary>
        private void PublishRoundState()
        {
            CountReadyPlayers(out int totalPlayers, out int _);
            if (_totalPlayers.Value != totalPlayers)
                _totalPlayers.Value = totalPlayers;

            bool phase = _driver != null && _driver.LastSurvivorPhase;
            if (_lastSurvivorPhase.Value != phase)
                _lastSurvivorPhase.Value = phase;

            Core.Net.RoundStateRegistry.PublishFromServer(
                totalPlayers, _endgamePressure.Value, phase);
        }

        /// <summary>
        /// 순수 클라이언트 쪽 레지스트리 갱신. 세 SyncVar 중 무엇이 바뀌든 전부 다시 쓴다 —
        /// 부분 갱신을 하면 늦게 접속한 클라이언트가 섞인 상태를 볼 수 있다.
        /// </summary>
        private void OnClientRoundState()
        {
            Core.Net.RoundStateRegistry.PublishFromServer(
                _totalPlayers.Value, _endgamePressure.Value, _lastSurvivorPhase.Value);
            EndgamePressureActive = _endgamePressure.Value;
        }

        private void SetPhase(GameFlowState phase)
        {
            _phase.Value = phase;
            ServerPhase = phase;
        }

        // ── 서버: 탈출 (스프린트 12 그대로 + 페이즈 가드) ──────────────────

        /// <summary>
        /// 탈출은 특정 플레이어가 소유하지 않는 라운드 오브젝트에서 처리되므로 소유권 검사를 끈다.
        /// <paramref name="caller"/>는 FishNet이 주입하는 탈출 요청자의 커넥션 —
        /// **플레이어 ID와 역할을 서버가 여기서 읽는다**(GAP-20 역할 부분 해소).
        ///
        /// 이전에는 클라이언트가 <c>role</c>을 주장했고 <see cref="ServerRoundDriver.TryRegisterEscape"/>가
        /// 그 값으로 GAP-11("러너만 탈출 집계")을 판정했다. 술래나 메아리가 <c>role=Runner</c>를
        /// 보내면 게이트만 열려 있으면 라운드를 RunnersWin으로 끝낼 수 있었다.
        /// 위치 재검증(거리)은 여전히 이월이다 — §14.4-3 "안티치트 과투자 금지".
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitEscape(NetworkConnection caller = null)
        {
            if (_phase.Value != GameFlowState.InGame || _driver == null || _driver.IsDecided)
                return;

            // 호출자의 플레이어 오브젝트를 못 찾으면 신원을 확정할 수 없다 — 폐기(NRE 가드 겸용).
            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
            {
                Debug.LogWarning("[RoundNet:Server] 탈출 폐기 — 호출자의 플레이어 오브젝트/역할을 찾을 수 없습니다.");
                return;
            }

            // 출구 트리거는 범위 안에서 0.5초마다 재시도한다(09-29) — 이미 확정된 러너의 재요청은 조용히 무시한다.
            if (_driver.HasEscaped(playerId))
                return;

            bool gateOpen = CurrentGateOpen();
            if (!_driver.TryRegisterEscape(playerId, role, gateOpen))
            {
                Debug.Log($"[RoundNet:Server] 탈출 거부 — playerId={playerId} 서버 재검증 실패 " +
                          $"(gateOpen={gateOpen}, role={role}) §5.3");
                return;
            }

            OnServerEscapeConfirmed(playerId, "출구 트리거");
        }

        /// <summary>
        /// 탈출이 확정된 뒤의 공통 처리 — 집계 SyncVar · pawn 월드 제외 · 판정. 출구 트리거(<see cref="ServerSubmitEscape"/>)와
        /// §6.3 탈출 우선(<see cref="ServerEscapeTakesPriorityOverTag"/>)이 같은 경로를 쓴다. 서버 전용.
        /// </summary>
        private void OnServerEscapeConfirmed(ulong playerId, string source)
        {
            _escaped.Value = _driver.EscapedCount;
            ServerMarkPawnEscaped(playerId);
            Debug.Log($"[RoundNet:Server] 탈출 확정 — playerId={playerId} (누적 {_driver.EscapedCount}명, {source})");
            EvaluateAndPush();
        }

        /// <summary>
        /// §6.3 동일 프레임 탈출 우선(09-30) — <c>TagNetworkSync</c>가 태그를 확정하기 직전에 묻는다. 게이트가 열려 있고 대상이 출구
        /// 판정 반경 안(서버 위치)이면 탈출로 확정하고 true(호출자는 태그를 거부한다). 규칙은 Core
        /// (<see cref="ServerRoundDriver.ResolveTagRequest"/>)가 소유한다. 서버 전용.
        /// </summary>
        internal static bool ServerEscapeTakesPriorityOverTag(ulong targetId, RoleType targetRole, Vector3 targetFeet)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null || ServerPhase != GameFlowState.InGame || instance._driver == null)
                return false;

            ServerRoundDriver.TagResolution resolution = instance._driver.ResolveTagRequest(
                targetId, targetRole, targetFeet, instance.CurrentGateOpen(), EscapePointRegistry.Positions);
            if (resolution != ServerRoundDriver.TagResolution.EscapeInstead)
                return false;

            instance.OnServerEscapeConfirmed(targetId, "태그 요청 중 출구 반경 안 — §6.3 탈출 우선");
            return true;
        }

        /// <summary>
        /// 탈출을 그 pawn의 SyncVar 하나로 공개한다(09-29) — 모든 피어가 월드에서 뺀다(<c>PawnRoleSync</c> → <c>IEscapeState</c>).
        /// </summary>
        private static void ServerMarkPawnEscaped(ulong playerId)
        {
            List<TagNetworkSync> pawns = TagNetworkSync.Spawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] != null && pawns[i].PlayerId == playerId)
                {
                    pawns[i].ServerMarkEscaped();
                    return;
                }
            }

            Debug.LogWarning($"[RoundNet:Server] 탈출 pawn을 찾지 못했다 — playerId={playerId}. 월드 제외가 적용되지 않는다.");
        }

        /// <summary>서버 판정을 1회 수행하고, 새로 결정되면 결과 전파 + RoundEnd 페이즈 진입. 서버 전용.</summary>
        private void EvaluateAndPush()
        {
            int opened = CountOpenValves();
            int required = _objective.RequiredOpen;
            int tagged = ServerRoundDriver.TaggedCount(TagTargetRegistry.Targets);

            // §6.3 [v0.4] 판정 입력이 "밸브 개방 수"에서 **도망자 인구**로 바뀌었다 —
            // 게이트는 판정식의 항이 아니라 탈출의 전제 조건이고, 그 강제는
            // ServerSubmitEscape → TryRegisterEscape(gateOpen)가 한다.
            RunnerCensus census = _driver.Census(CountRunners(), tagged);

            // 판정과 페이즈 진입의 순서는 Core(ServerRoundDriver.Step)가 소유한다 — 판정 먼저, 미정일 때만 §6.5-1 진입
            //   (탈출로 승리가 확정되는 같은 처리에서 '최후 생존자 페이즈 진입'이 찍히던 순서 결함, 09-30).
            ServerRoundDriver.RoundStep step = _driver.Step(census);
            if (step.EnteredLastSurvivorPhase)
                OnLastSurvivorPhaseEntered(census);

            if (!step.Decided)
                return;

            _result.Value = _driver.Result;

            // §8 "결과 화면 전환 즉시 어워드 3종 판정" — RoundEnd 진입과 같은 시점에 확정한다.
            PublishAwards();

            // 스프린트 18: 판정 확정 = RoundEnd 진입 + §12.5 리매치 투표 창(15초) 개시.
            _vote = new RematchVoteDriver();
            BuildLivePlayerIds();
            _votesFor.Value = 0;
            _votesNeeded.Value = RematchVoteDriver.RequiredVotes(_liveIdBuffer.Count);
            _voteRemaining.Value = RematchVoteDriver.VoteWindowSeconds;
            SetPhase(GameFlowState.RoundEnd);

            Debug.Log($"[RoundNet:Server] 라운드 종료 판정 = {_driver.Result} — 전 피어 전파 " +
                      $"(동시 개방 {opened}/{required}, {census}, " +
                      $"최후 생존자 페이즈={_driver.LastSurvivorPhase}, 단독 탈출={_driver.LastSurvivorEscaped}, " +
                      $"남은 {_driver.RemainingSeconds:0.0}초) → 리매치 투표 {RematchVoteDriver.VoteWindowSeconds:0}초");
        }

        /// <summary>
        /// §6.3 이번 라운드 <b>도망자 총수</b>. 태그로 메아리가 된 플레이어도 원래 도망자였으므로
        /// 함께 센다 — <c>RunnerCensus.Total</c>의 분모다.
        ///
        /// <para>
        /// 술래는 §2.3 로테이션으로 정확히 1명이므로 "전체 − 술래"로 유도한다. 역할 배정 전
        /// (Lobby·RoleAssign)에는 0이 나올 수 있고, 그때는 판정이 어차피 InProgress다.
        /// </para>
        /// </summary>
        private static int CountRunners()
        {
            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            int seekers = 0;
            int assigned = 0;

            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0)
                    continue;

                assigned++;

                // 태그된 플레이어는 CurrentRole이 Echo이므로 술래로 세어지지 않는다.
                if (p.CurrentRole == RoleType.Seeker)
                    seekers++;
            }

            return Mathf.Max(0, assigned - seekers);
        }

        /// <summary>
        /// §6.5-1 최후 생존자 페이즈 진입 시점. <b>블록 4가 배수구 활성과 90초 타이머를
        /// 여기에 붙인다.</b> 지금은 진입 알림 파문과 로그만 남긴다.
        ///
        /// <para>
        /// 진입 알림은 §6.5-1대로 <b>기존 Shout 등급</b>을 재사용한다 — 새 SoundType을
        /// 만들지 않는다(§3.3). §8.1 최다 비명상은 <c>Scream</c>만 집계하므로 오염되지 않는다.
        /// </para>
        /// </summary>
        private void OnLastSurvivorPhaseEntered(in RunnerCensus census)
        {
            _lastSurvivorPhase.Value = true;
            _phaseEntryRoundRemaining.Value = _driver.RemainingSeconds;

            bool gateOpen = _objective.GateOpen;

            // §6.5-2 "게이트 개방 상태 — 활성화하지 않는다(기존 출구를 쓰면 된다)".
            if (!DrainSelection.ShouldActivate(gateOpen))
            {
                _activeDrain.Value = 0;
                Debug.Log($"[RoundNet:Server] §6.5-1 최후 생존자 페이즈 진입 — {census}. " +
                          "게이트가 이미 열려 배수구를 활성화하지 않는다(§6.5-2). 기존 출구로 나가면 팀 승리");
                return;
            }

            // §6.5-2 무작위 1개. 시드를 주입해 테스트에서 고정 가능하게 — UnityEngine.Random 금지.
            int seed = unchecked(_roundNumber.Value * 83492791 + census.TaggedOut * 2654435 + 7);
            DrainId chosen = DrainSelection.Choose(seed);

            // §6.5-2 T = 14 − (동시 개방 밸브 수 × 3). **페이즈 진입 시점에 1회 확정한다 — GAP-87.**
            //   서버가 게이트 판정에 쓰는 것과 같은 셈(현재 Open 상태 밸브 수)을 쓴다.
            int openValves = CountOpenValves();
            float workSeconds = DrainConfig.WorkSeconds(openValves);

            _drain = new DrainHatch(chosen, workSeconds);
            _drainPulse.Reset();
            _drainIntent.Clear();
            _drainSessions.Clear();
            _activeDrain.Value = (int)chosen;
            _drainProgress.Value = 0f;
            _drainDecaying.Value = false;

            // §6.5-1 진입 알림 — 활성 배수구에서 고함급(22m) 파문 1회, 양 진영 인지.
            //   **새 SoundType을 만들지 않는다** — 기존 Shout 재사용(§6.5-1 명시).
            if (DrainRegistry.TryGetPosition(chosen, out Vector3 drainPos))
                PulseNetworkSync.ServerEmitWorldPulse(Core.Sound.SoundType.Shout, drainPos);
            else
                Debug.LogWarning($"[RoundNet:Server] 배수구 {chosen} 위치를 찾지 못해 진입 알림 파문을 생략합니다 " +
                                 "(맵 v2 배수구 마커에 DrainPoint가 없음).");

            Debug.Log($"[RoundNet:Server] §6.5-1 최후 생존자 페이즈 진입 — {census}. " +
                      $"활성 배수구 {chosen} · 동시 개방 {openValves}개 → T={workSeconds:0}초 " +
                      $"(총 점유 {DrainConfig.TotalOccupancySeconds(openValves):0}초, " +
                      $"잠수 {DrainConfig.RequiredDives(openValves, Core.Breath.BreathConfig.TotalSeconds)}회) · " +
                      $"제한 {_driver.EffectiveRemainingSeconds(_driver.PhaseRemainingSeconds):0}초");
        }

        /// <summary>
        /// §6.5-2 배수구 작업 의사. <b>페이로드는 bool 하나</b> — 역할·위치·거리·활성 여부를
        /// 전부 서버가 재검증한다(GAP-24).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitDrainHold(bool held, NetworkConnection caller = null)
        {
            if (_phase.Value != GameFlowState.InGame || _driver == null || _driver.IsDecided || _drain == null)
                return;

            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
                return;

            // 탈출자는 월드에서 빠졌다(09-29).
            if (!RoleNetworkSync.CallerInWorld(caller))
                return;

            if (!held)
            {
                _drainIntent.Remove(playerId);
                _drain.StopWork(playerId);
                return;
            }

            // 자격(역할·거리·잠수)은 여기서 판정하지 않는다 — TickDrain이 매 틱 재평가한다.
            if (_drainIntent.Add(playerId))
                Debug.Log($"[RoundNet:Server] 배수구 작업 의사 접수 — playerId={playerId} (role={role}). " +
                          "자격은 매 틱 서버가 재평가");
        }

        /// <summary>
        /// 보관된 작업 의사마다 자격을 재평가해 작업을 붙이거나 뗀다(<see cref="DrainHatch.CanWork"/>).
        /// <see cref="DrainHatch.TryWork"/>는 이미 참여 중이면 아무것도 바꾸지 않는다(멱등).
        /// </summary>
        private void ApplyDrainIntents(float dt)
        {
            bool hasPos = DrainRegistry.TryGetPosition(_drain.Id, out Vector3 drainPos);

            // ① 시작 — 누르고 있고, 자격이 되면(도망자 · 범위 · 이 자리에서 실제로 잠길 수 있음) 진입을 연다.
            if (!_drain.IsInTransit)
            {
                foreach (ulong playerId in _drainIntent)
                {
                    if (_drainSessions.ContainsKey(playerId))
                        continue;

                    if (IsEligibleForDrain(playerId, hasPos, drainPos, out _))
                    {
                        _drainSessions[playerId] = UnderwaterWorkSession.ForDrain();
                        Debug.Log($"[RoundNet:Server] 배수구 진입 — playerId={playerId} (§6.5-2 진입 " +
                                  $"{DrainConfig.EntrySeconds:0}초 → 작업 {_drain.WorkSeconds:0}초 → 부상 {DrainConfig.SurfaceSeconds:0}초, 전 구간 잠수)");
                    }
                }
            }

            if (_drainSessions.Count == 0)
                return;

            // ② 진행 — 밸브 B·E와 같은 세션 규칙.
            _drainScratch.Clear();
            _drainScratch.AddRange(_drainSessions.Keys);
            for (int i = 0; i < _drainScratch.Count; i++)
            {
                ulong playerId = _drainScratch[i];
                UnderwaterWorkSession session = _drainSessions[playerId];

                bool eligible = IsEligibleForDrain(playerId, hasPos, drainPos, out RoleType role);
                bool holding = _drainIntent.Contains(playerId) && eligible;
                bool canSubmerge = PulseNetworkSync.ServerCanSubmerge(playerId);

                UnderwaterWorkTick step = session.Tick(dt, holding, _drain.IsCompleted, canSubmerge);

                if (step.BeginRotation && _drain.TryWork(playerId, role) != ValveInteractionRejection.None)
                    session.ForceSurface();
                if (step.StopRotation)
                    _drain.StopWork(playerId);

                if (session.Phase != UnderwaterWorkPhase.Done)
                    continue;

                _drainSessions.Remove(playerId);

                if (session.EndedByForce)
                {
                    // §5.9-1 강제 부상 — 통과는 열리지 않는다(완료는 유지). 다시 누르게 한다.
                    _drainIntent.Remove(playerId);
                    Debug.Log($"[RoundNet:Server] 배수구 강제 부상 — playerId={playerId} 숨 0. " +
                              (_drain.IsCompleted ? "작업은 끝나 있음 — 다음 잠수에서 진입·부상만 하면 된다" : "작업 미완 — 감쇠"));
                }
                else if (_drain.IsCompleted && _drain.BeginTransit(playerId))
                {
                    Debug.Log($"[RoundNet:Server] §6.5-2 부상 완료 → 통과 {DrainConfig.TransitSeconds:0.0}초 (그 동안 태그 가능)");
                }
            }
        }

        /// <summary>
        /// 배수구 작업 자격(§6.5-2 · GAP-88 해소) — 도망자 · 수평 2.5m · <b>이 자리에서 실제로 잠길 수 있음</b>
        /// (<see cref="UnderwaterWorkSession.CanWork"/>, 밸브 B·E와 같은 식).
        /// </summary>
        private static bool IsEligibleForDrain(ulong playerId, bool hasPos, Vector3 drainPos, out RoleType role)
        {
            RoleNetworkSync player = FindPlayer(playerId);
            role = player != null ? player.EffectiveRole : RoleType.Echo;
            if (player == null || !hasPos)
                return false;

            Vector3 feet = player.transform.position;
            bool inRange = InteractionRules.InRange(feet, drainPos, underwaterTarget: true);
            return UnderwaterWorkSession.CanWork(role, inRange, Core.Water.WaterVolumeRegistry.Sample(feet), feet.y,
                PulseNetworkSync.ServerCanSubmerge(playerId));
        }

        /// <summary>이 플레이어가 배수구 작업 구간(진입·작업·부상) 안인가 — 잠수 의도로 합쳐진다. 서버 전용.</summary>
        internal static bool ServerIsInDrainWork(ulong playerId)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null || ServerPhase != GameFlowState.InGame)
                return false;

            return instance._drainSessions.TryGetValue(playerId, out UnderwaterWorkSession s) && s.KeepsSubmerged;
        }

        /// <summary>
        /// 이 플레이어의 배수구 작업이 이번 틱에 부상을 마치는가 — §5.9-1 0초 경계(숨 게이지가 질식 판정 전에 묻는다).
        /// 숨은 세션보다 먼저 돈다(<see cref="ServerTickOrder"/>) — 여기서 보는 세션 상태는 이번 틱 진전 전이다.
        /// </summary>
        internal static bool ServerSurfaceCompletesWithin(ulong playerId, float deltaSeconds)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null || ServerPhase != GameFlowState.InGame)
                return false;

            return instance._drainSessions.TryGetValue(playerId, out UnderwaterWorkSession s) &&
                   s.SurfaceCompletesWithin(deltaSeconds);
        }

        private static RoleNetworkSync FindPlayer(ulong playerId)
        {
            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p != null && p.OrderKey >= 0 && (ulong)p.OrderKey == playerId)
                    return p;
            }

            return null;
        }

        /// <summary>
        /// §6.5-2 배수구를 진전시킨다. 서버 전용, <see cref="TickRound"/>에서 호출한다 —
        /// <b>이 호출부가 없으면 배수구 진행도·감쇠·통과가 영영 돌지 않는다</b>(더블체크 1).
        /// </summary>
        private void TickDrain(float dt)
        {
            if (_drain == null)
                return;

            ApplyDrainIntents(dt);

            // §6.1 [v0.4] / §6.5-3 수중 작업 중 2.5초 주기 강제 파문(Valve 등급 12m) —
            //   "침묵 탈출은 불가능하다". 새 SoundType 없음.
            int pulses = _drainPulse.Tick(_drain.IsWorking, dt);
            if (pulses > 0 && DrainRegistry.TryGetPosition(_drain.Id, out Vector3 drainPos))
            {
                for (int i = 0; i < pulses; i++)
                    PulseNetworkSync.ServerEmitWorldPulse(Core.Sound.SoundType.Valve, drainPos,
                        UnderwaterWorkPulse.IntervalSeconds); // GAP-103 — 밸브 수중 파문과 같은 규칙
            }

            DrainTickResult tick = _drain.Tick(dt);

            _drainProgress.Value = _drain.Progress01;
            if (_drainDecaying.Value != _drain.IsDecaying)
                _drainDecaying.Value = _drain.IsDecaying;

            if (tick.Completed)
                Debug.Log("[RoundNet:Server] §6.5-2 배수구 작업 완료 — 부상을 마치면 통과가 열린다");

            if (!tick.EscapedPlayer.HasValue)
                return;

            ulong escaper = tick.EscapedPlayer.Value;

            // 통과 중에 태그됐다면 이미 메아리다 — 역할을 서버에서 다시 읽는다.
            RoleType current = CurrentRoleOf(escaper);
            if (_driver.TryRegisterDrainEscape(escaper, current))
            {
                _escaped.Value = _driver.EscapedCount;
                Debug.Log($"[RoundNet:Server] §6.5-1 최후 생존자 배수구 탈출 — playerId={escaper} → 팀 승리");
                EvaluateAndPush();
            }
        }

        /// <summary>서버가 알고 있는 플레이어의 현재 역할. 태그되면 Echo다.</summary>
        private static RoleType CurrentRoleOf(ulong playerId)
        {
            RoleNetworkSync p = FindPlayer(playerId);
            return p != null ? p.EffectiveRole : RoleType.Echo;
        }

        /// <summary>서버 게이트 래치(09-29 — 서버만 판정한다).</summary>
        private bool CurrentGateOpen() => _objective.GateOpen;

        private static int CountOpenValves()
        {
            int opened = 0;
            List<ValveNetworkSync> valves = ValveNetworkSync.Spawned;
            for (int i = 0; i < valves.Count; i++)
            {
                if (valves[i] != null && valves[i].State == ValveState.Open)
                    opened++;
            }

            return opened;
        }

        // ── 서버: 리매치 투표 (스프린트 18 — GAP-27 해소) ─────────────────

        /// <summary>
        /// 리매치 **찬성 투표**(§12.5). 스프린트 17의 "요청 즉시 재시작"을 대체한다.
        /// 씬 오브젝트라 소유권 검사를 끄고, 투표자 신원은 클라 주장값이 아니라
        /// FishNet이 주입한 <paramref name="caller"/>에서 얻는다(GAP-24와 같은 원칙).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestRestart(NetworkConnection caller = null)
        {
            if (_phase.Value != GameFlowState.RoundEnd || _vote == null)
            {
                Debug.Log("[RoundNet:Server] 리매치 투표 무시 — 결과 화면(RoundEnd)이 아니다.");
                return;
            }

            if (caller == null)
                return;

            if (_vote.TryVote((ulong)caller.ClientId))
            {
                BuildLivePlayerIds();
                _votesFor.Value = _vote.CountVotes(_liveIdBuffer);
                Debug.Log($"[RoundNet:Server] 리매치 찬성 — clientId={caller.ClientId} " +
                          $"({_votesFor.Value}/{RematchVoteDriver.RequiredVotes(_liveIdBuffer.Count)}표, §12.5 과반)");
            }
        }

        /// <summary>
        /// 월드를 새 라운드 직전 상태로 되돌린다(라운드 시작은 하지 않는다 — 시작은
        /// <see cref="BeginRound"/>만 수행). 서버 전용.
        ///
        /// **순서가 중요하다**(스프린트 17): ① 태그를 먼저 풀어야 ② 역할 재배정이 메아리 고착에
        /// 걸리지 않는다(<c>RoleNetworkSync.IsTaggedOut</c>이 태그 SyncVar를 본다). 밸브는 순서 무관.
        /// </summary>
        private void ServerResetWorld()
        {
            // §12.4 진행 중이던 브리핑·맵 대기도 끝낸다(부결·리매치 경계).
            _start?.Reset();
            _briefingStartValue.Value = 0f;

            // ① 태그 해제(먼저) — 역할 재배정의 전제.
            List<TagNetworkSync> tags = TagNetworkSync.Spawned;
            for (int i = 0; i < tags.Count; i++)
                tags[i]?.ServerResetForNewRound();

            // ② 역할 배정 해제 → 다음 RoleAssign 페이즈에서 §6.2 표대로 재배정된다.
            List<RoleNetworkSync> roles = RoleNetworkSync.Spawned;
            for (int i = 0; i < roles.Count; i++)
                roles[i]?.ServerClearAssignmentForNewRound();

            // 술래 순번도 미확정으로 되돌린다 — 다음 라운드 배정이 새 라운드 번호로 다시 정한다.
            _fixedSeekerOrder = -1;

            // ③ 밸브 초기화(닫힘·진행도 0·역류 타이머 0·전부 활성).
            //    활성 조합은 다음 BeginRound에서 §6.1-0대로 다시 고른다.
            List<ValveNetworkSync> valves = ValveNetworkSync.Spawned;
            for (int i = 0; i < valves.Count; i++)
                valves[i]?.ServerResetForNewRound();

            // ④ 라운드 상태 초기화(표시값 포함). 결과가 InProgress로 돌아가며 결과 화면이 닫힌다.
            //    §6.2-1 종반 압박과 §6.5-1 페이즈도 **라운드 경계에서** 해제한다 —
            //    남아 있으면 새 라운드가 시작부터 술래 +5% 상태가 된다(더블체크 2).
            _endgamePressure.Value = false;
            _lastSurvivorPhase.Value = false;
            _phaseEntryRoundRemaining.Value = 0f;
            _activeDrain.Value = 0;
            _drainProgress.Value = 0f;
            _drainDecaying.Value = false;
            _drain = null;
            _drainPulse.Reset();
            _drainIntent.Clear();
            _drainSessions.Clear();
            _nextExitPulseAt = 0f;
            EndgamePressureActive = false;
            Core.Net.RoundStateRegistry.PublishFromServer(_totalPlayers.Value, false, false);
            _driver = null;
            _vote = null;
            _objective.Reset();
            ServerPublishObjective();
            _remaining.Value = _roundDurationSeconds;
            _escaped.Value = 0;
            _result.Value = RoundResult.InProgress;

            Debug.Log($"[RoundNet:Server] 월드 초기화 — 밸브 {valves.Count}개·태그 {tags.Count}명·역할 {roles.Count}명 (§12.5)");
        }

        /// <summary>부결로 로비 복귀 시 전원 준비 해제(§12.3 준비는 라운드 단위 합의다). 서버 전용.</summary>
        private void ClearAllReady()
        {
            List<ReadyNetworkSync> ready = ReadyNetworkSync.Spawned;
            for (int i = 0; i < ready.Count; i++)
                ready[i]?.ServerClearReady();
        }

        // ── 서버: 인원 집계 ──────────────────────────────────────────────

        /// <summary>로비 게이트 입력 — 소유권 확정된 접속자 수와 그중 준비 완료 수(실측).</summary>
        private void CountReadyPlayers(out int players, out int ready)
        {
            players = 0;
            ready = 0;

            List<ReadyNetworkSync> spawned = ReadyNetworkSync.Spawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                ReadyNetworkSync p = spawned[i];
                if (p == null || !p.NetworkActive)
                    continue;

                players++;
                if (p.IsReady)
                    ready++;
            }
        }

        /// <summary>리매치 투표의 유효 인원(현재 접속자) ID 목록을 재사용 버퍼에 채운다.</summary>
        private void BuildLivePlayerIds()
        {
            _liveIdBuffer.Clear();
            List<RoleNetworkSync> spawned = RoleNetworkSync.Spawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                RoleNetworkSync p = spawned[i];
                if (p != null && p.OrderKey >= 0)
                    _liveIdBuffer.Add((ulong)p.OrderKey);
            }
        }

        // ── 서버: 역할 배정 (스프린트 13 — 호출 시점만 로비 게이트 뒤로 이동) ──

        /// <summary>
        /// §6.2 역할 배정. 서버 전용. 호출 목적이 둘로 나뉜다:
        ///
        /// <list type="bullet">
        /// <item><paramref name="roundStart"/> = true — 라운드 배정(카운트다운 완료 1회).
        /// 이번 판의 술래 순번(<see cref="_fixedSeekerOrder"/>)을 **여기서만** 확정하고 전원에게
        /// §6.2 표대로 배정한다.</item>
        /// <item><paramref name="roundStart"/> = false — 라운드 중 합류 처리(InGame 매 프레임).
        /// **미배정자만 러너로 채우고 기존 배정은 절대 건드리지 않는다.**</item>
        /// </list>
        ///
        /// 스프린트 18: 호출 지점이 "매 프레임"에서 **RoleAssign 완료 시점 + InGame 중**으로
        /// 옮겨졌다(§15.4 "RoleAssign: 역할 배정" — 로비에서는 배정하지 않는다. GAP-23의
        /// "미배정 발생 시 즉시"는 로비 게이트가 생기며 폐기).
        ///
        /// <b>두 경로를 나눈 이유</b>: 이전에는 미배정자가 하나라도 생기면 **전원 재배정**이
        /// 돌았고, 술래 순번이 <c>roundNumber % playerCount</c>라 인원이 바뀌면 값이 달라졌다.
        /// 그래서 라운드 중 한 명만 합류해도 술래가 교체되거나(증상 1), 새 순번이 태그당한
        /// 플레이어를 가리켜 <c>IsTaggedOut</c> 스킵에 걸리면 술래가 0명이 됐다(증상 2).
        /// 배정 규칙 자체(§6.2 표·OwnerId 오름차순 정렬·메아리 제외)는 무변경이다.
        /// </summary>
        private void EnsureRolesAssigned(bool roundStart = false)
        {
            _assignBuffer.Clear();
            List<RoleNetworkSync> spawned = RoleNetworkSync.Spawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                RoleNetworkSync p = spawned[i];
                if (p != null && p.OrderKey >= 0) // 소유권이 확정된 플레이어만
                    _assignBuffer.Add(p);
            }

            if (!RoleAssigner.CanAssign(_assignBuffer.Count))
                return; // §1 최소 2인 미만 — 배정하지 않고 로컬 기본값(러너)을 유지한다(GAP-21).

            bool anyUnassigned = false;
            for (int i = 0; i < _assignBuffer.Count; i++)
            {
                if (!_assignBuffer[i].HasAssignment)
                {
                    anyUnassigned = true;
                    break;
                }
            }

            if (!anyUnassigned && !roundStart)
                return; // 전원 배정 완료 — 매 프레임 정렬 비용을 피한다.

            _assignBuffer.Sort(CompareByOrderKey);

            int playerCount = _assignBuffer.Count;

            if (!roundStart)
            {
                AssignLateJoinersAsRunners(playerCount);
                return;
            }

            // §2.3 술래 로테이션(스프린트 21): 통산 라운드 번호로 술래 순번을 옮긴다.
            // 정렬 규칙(OwnerId 오름차순)은 스프린트 13 그대로이고, 그 안에서 **몇 번째가 술래인가**만
            // 라운드마다 달라진다. 이 값은 이번 판 내내 고정된다.
            _fixedSeekerOrder = SeekerRotation.SeekerOrderIndex(_roundNumber.Value, playerCount);

            for (int i = 0; i < playerCount; i++)
            {
                RoleNetworkSync p = _assignBuffer[i];
                if (p.IsTaggedOut)
                    continue; // 메아리는 배정 대상 제외(태그 결과 보존)

                p.ServerAssign(RoleAssigner.RoleForOrder(i, playerCount, _fixedSeekerOrder));
            }

            Debug.Log($"[RoleNet:Server] 역할 배정 완료 — 인원 {playerCount}명 " +
                      $"(술래 {RoleAssigner.SeekersFor(playerCount)} / 러너 {RoleAssigner.RunnersFor(playerCount)}, §6.2 표). " +
                      $"§2.3 로테이션: {SeekerRotation.SetNumber(_roundNumber.Value)}세트 " +
                      $"{SeekerRotation.RoundInSet(_roundNumber.Value)}/{SeekerRotation.RoundsPerSet}판 — " +
                      $"술래는 정렬 {_fixedSeekerOrder}번(OwnerId {_assignBuffer[_fixedSeekerOrder].OrderKey}), 이번 판 고정.");
        }

        /// <summary>
        /// 라운드 중 합류한 플레이어를 러너로 채운다(서버 전용, 이미 정렬된 <see cref="_assignBuffer"/> 기준).
        ///
        /// **기존 배정은 읽지도 쓰지도 않는다** — 술래는 라운드 시작 시점에 확정됐고(§6.2 "술래 1인 고정"),
        /// 늦게 들어온 사람이 그 자리를 뺏을 이유가 없다. 술래가 도중에 이탈해도 재추첨하지 않는다:
        /// 진행 중인 라운드에서 역할을 바꾸는 것이 술래 공석보다 더 큰 혼란이고, §2.3 로테이션은
        /// 다음 판에 정상 동작한다.
        /// </summary>
        private void AssignLateJoinersAsRunners(int playerCount)
        {
            for (int i = 0; i < playerCount; i++)
            {
                RoleNetworkSync p = _assignBuffer[i];
                if (p.HasAssignment || p.IsTaggedOut)
                    continue;

                p.ServerAssign(RoleType.Runner);
                Debug.Log($"[RoleNet:Server] 라운드 중 합류 — ownerId={p.OrderKey} 러너로 배정. " +
                          $"이번 판 술래(정렬 {_fixedSeekerOrder}번)는 그대로 유지된다(§6.2).");
            }
        }

        private static int CompareByOrderKey(RoleNetworkSync a, RoleNetworkSync b) =>
            a.OrderKey.CompareTo(b.OrderKey);

        // ── §8 어워드 집계 (서버 전용) ────────────────────────────────────

        /// <summary>
        /// 파문 1건을 세션 집계에 기록한다. <c>PulseNetworkSync</c>가 발생원을 확정한
        /// 직후 호출한다 — 클라이언트 보고가 아니라 **서버가 받은 사실**만 센다.
        ///
        /// <paramref name="radiusMeters"/>는 §8.2대로 **재질 배율까지 적용된 실제 등록 반경**이며,
        /// <c>ServerPulseDriver.TryGetAppliedSpec</c>이 낸 값을 그대로 넘긴다.
        /// </summary>
        internal static void ServerRecordPulse(int playerId, SoundType type, float radiusMeters, float durationSeconds)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null)
                return;

            instance._awards.RecordPulse(playerId, type, radiusMeters, durationSeconds);
        }

        /// <summary>
        /// §8.2 "생존 요건 — 탈출 또는 미탈출만 수상 대상. 태그당한 메아리는 제외".
        /// 태그가 서버에서 확정된 뒤 호출한다.
        /// </summary>
        internal static void ServerMarkTaggedOut(int playerId)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null)
                return;

            instance._awards.MarkTaggedOut(playerId);
        }

        /// <summary>
        /// §8 "메아리 노크 성공 유인 횟수"를 1회 기록한다(스프린트 27). <c>PulseNetworkSync</c>가
        /// <c>ServerKnockDriver</c>의 판정을 받아 호출한다 — 서버가 술래 위치로 확인한 사실만 센다.
        ///
        /// 이 호출이 생기면서 **최고의 거짓말상이 처음으로 수상자를 낼 수 있게 됐다**(GAP-37 잔여분).
        /// </summary>
        internal static void ServerRecordKnockLure(int playerId)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null)
                return;

            instance._awards.RecordKnockLure(playerId);
        }

        /// <summary>
        /// 라운드 중 플레이어 이동 거리를 누적한다(§8 "이동거리 대비 파문 발생 0회"의 이동거리).
        /// 서버가 자기 쪽 위치 변화를 재므로 클라이언트가 값을 부풀릴 수 없다.
        /// </summary>
        private void AccumulateDistances()
        {
            List<RoleNetworkSync> spawned = RoleNetworkSync.Spawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                RoleNetworkSync p = spawned[i];
                if (p == null || p.OrderKey < 0)
                    continue;

                Vector3 current = p.transform.position;
                if (_lastPositions.TryGetValue(p.OrderKey, out Vector3 previous))
                {
                    float moved = Vector3.Distance(previous, current);

                    // 스폰·리스폰 순간이동을 이동거리로 세지 않는다(한 프레임에 걸을 수 없는 거리).
                    //
                    // 메아리(태그 아웃)는 이동거리에서 제외한다(GAP-56): §3.2상 발소리를 내지 않아
                    // §8 무성 생존상의 "파문 발생 0회"를 **구조적으로** 충족하는데, 이동속도까지
                    // 가장 빨라(8.0m/s) 그대로 두면 먼저 태그당한 플레이어가 상을 독식한다.
                    // 태그 전까지 쌓인 거리는 그대로 남는다 — 러너로 실제 움직인 몫이기 때문이다.
                    if (moved <= MaxDistancePerFrame && !p.IsTaggedOut)
                        _awards.AddDistance(p.OrderKey, moved);

                    // §8.2 "태그당한 메아리는 제외" — 이동거리만 빼는 것으로는 부족하다.
                    // 태그 전까지 쌓인 거리와 낮은 소음량으로 여전히 수상 후보가 되기 때문이다.
                    if (p.IsTaggedOut)
                        _awards.MarkTaggedOut(p.OrderKey);
                }
                else
                {
                    _awards.Track(p.OrderKey); // 움직이지 않아도 후보로는 잡히게
                }

                _lastPositions[p.OrderKey] = current;
            }
        }

        /// <summary>§8 판정 결과를 SyncVar로 전파한다(서버 전용).</summary>
        private void PublishAwards()
        {
            AwardResults results = _awards.Evaluate();
            _awardScream.Value = results.LoudestScream;
            _awardSilent.Value = results.SilentSurvivor;
            _awardLiar.Value = results.BestLiar;

            Debug.Log($"[Awards:Server] §8 어워드 판정 — 최다 비명상={Describe(results.LoudestScream)}, " +
                      $"무성 생존상={Describe(results.SilentSurvivor)}, 최고의 거짓말상={Describe(results.BestLiar)}. " +
                      "비명은 §5.2 음성 파이프라인, 노크는 §3.2 메아리 능력이 있어야 집계된다(GAP-37).");
        }

        private static string Describe(int playerId) =>
            playerId == AwardTally.NoWinner ? "수상자 없음" : $"플레이어 {playerId}";

        // ── 전 피어: 확정 결과 수신 로깅(양쪽 창에서 반영 확인용) ───────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _briefingStartValue.OnChange += OnBriefingStartChanged;
            _result.OnChange += OnResultChanged;
            _phase.OnChange += OnPhaseChanged;
            _endgamePressure.OnChange += OnRoundStateBool;
            _lastSurvivorPhase.OnChange += OnRoundStateBool;
            _totalPlayers.OnChange += OnRoundStateInt;
            _activeValves.OnChange += OnObjectiveInt;
            _requiredOpen.OnChange += OnObjectiveInt;
            _gateOpen.OnChange += OnObjectiveBool;

            // 늦게 접속한 클라이언트도 현재값을 바로 받게 한다(§13.3).
            OnClientRoundState();
            OnClientObjective();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _briefingStartValue.OnChange -= OnBriefingStartChanged;
            _result.OnChange -= OnResultChanged;
            _phase.OnChange -= OnPhaseChanged;
            _endgamePressure.OnChange -= OnRoundStateBool;
            _lastSurvivorPhase.OnChange -= OnRoundStateBool;
            _totalPlayers.OnChange -= OnRoundStateInt;
            _activeValves.OnChange -= OnObjectiveInt;
            _requiredOpen.OnChange -= OnObjectiveInt;
            _gateOpen.OnChange -= OnObjectiveBool;
            ObjectiveRegistry.Clear();

            // 세션이 끝나면 종반 압박이 다음 판에 새지 않게 비운다(더블체크 2).
            Core.Net.RoundStateRegistry.ResetForNewSession();
            EndgamePressureActive = false;
        }

        private void OnRoundStateBool(bool prev, bool next, bool asServer) => OnClientRoundState();

        private void OnRoundStateInt(int prev, int next, bool asServer) => OnClientRoundState();

        private void OnResultChanged(RoundResult prev, RoundResult next, bool asServer)
        {
            if (asServer)
                return;

            if (next != RoundResult.InProgress)
                Debug.Log($"[RoundNet:Client] 라운드 결과 수신 = {next} — 서버 확정, 화면 반영");
        }

        private void OnPhaseChanged(GameFlowState prev, GameFlowState next, bool asServer)
        {
            if (asServer)
                return;

            Debug.Log($"[RoundNet:Client] 페이즈 수신 {prev} → {next} (§15.4)");
        }

        /// <summary>도메인 리로드 꺼짐 대비 static 잔여 상태 정리.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => ServerPhase = GameFlowState.Boot;
    }
}
