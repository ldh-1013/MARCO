using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.Awards;
using Marco.Core.GameFlow;
using Marco.Core.Net;
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
    /// **판정 입력의 출처**(스프린트 12): 밸브 게이트 = <see cref="EscapeGateRegistry"/>,
    /// 전원 태그 = <see cref="TagTargetRegistry"/>, 탈출 = <see cref="ServerSubmitEscape"/>,
    /// 타이머 = 이 컴포넌트. 준비 인원 = <c>ReadyNetworkSync.Spawned</c>(스프린트 18).
    ///
    /// **어셈블리 경계**: Net은 Presentation을 참조하지 않는다 — 소비자(<c>RoundCoordinator</c>·
    /// <c>LobbyScreen</c>)는 <see cref="IRoundNetworkBridge"/>(Core)로만 읽는다.
    ///
    /// **NRE 가드**(스프린트 10 교훈): <c>IsSpawned</c>/<c>IsServerStarted</c>는
    /// <see cref="NetworkBehaviour.NetworkObject"/> null 확인 뒤에만 호출한다.
    /// </summary>
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

        private ServerRoundDriver _driver;      // InGame 동안만 존재(서버 전용)
        private ServerLobbyDriver _lobby;       // 서버 전용
        private RematchVoteDriver _vote;        // RoundEnd 동안만 존재(서버 전용)
        private bool _waitingForMap;            // 스프린트 18b: 맵 로드 완료를 기다리는 중(서버 전용)

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
        public int RematchVotesFor => _votesFor.Value;
        public int RematchVotesNeeded => _votesNeeded.Value;
        public float RematchSecondsRemaining => _voteRemaining.Value;
        public int RoundNumber => _roundNumber.Value;
        public int AwardLoudestScream => _awardScream.Value;
        public int AwardSilentSurvivor => _awardSilent.Value;
        public int AwardBestLiar => _awardLiar.Value;

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

        private void TickCountdown(float dt)
        {
            CountReadyPlayers(out int players, out int ready);
            LobbyTickResult result = _lobby.Tick(players, ready, dt);
            _countdown.Value = _lobby.CountdownRemaining;

            switch (result)
            {
                case LobbyTickResult.Aborted:
                    // GAP-29: 준비 해제·이탈 시 즉시 로비로. (리매치 경로에서는 준비가 라운드 내내
                    // 잠겨 있으므로, 여기 도달하는 건 이탈 또는 신규 접속자 미준비뿐이다.)
                    SetPhase(GameFlowState.Lobby);
                    Debug.Log("[RoundNet:Server] 카운트다운 중단 — 준비 해제/이탈, 로비로 복귀(GAP-29)");
                    break;

                case LobbyTickResult.StartRound:
                    // §2.3 술래 로테이션: 배정 **전에** 라운드 번호를 확정한다 — 이 번호가 이번 판의
                    // 술래 순번을 정하고, 라운드 중 늦게 들어온 플레이어의 재배정에도 같은 값이 쓰인다.
                    _roundNumber.Value++;

                    // §15.4 RoleAssign: "3초 연출, 역할 배정" — 배정을 카운트다운 완료 시점에
                    // 1회 수행한다(중단 시 되돌릴 배정이 없도록 끝에서 확정).
                    // 이 호출만이 술래 순번을 정한다(_fixedSeekerOrder 확정).
                    EnsureRolesAssigned(roundStart: true);

                    // 스프린트 18b: §15.4 "InGame: **맵 로드**" — 맵이 올라온 뒤에 라운드를 시작한다.
                    // 맵 없이 시작하면 밸브가 0개라 §6.1 배수로 게이트가 영구히 닫혀 라운드가 성립하지 않는다.
                    _waitingForMap = true;
                    SceneFlowController.Instance?.ServerLoadMap();
                    break;
            }

            // 맵 로드 대기 중이면 완료되는 프레임에 라운드를 시작한다(페이즈는 RoleAssign 유지 — GAP-30).
            if (_waitingForMap && MapReadyForRound())
            {
                _waitingForMap = false;
                BeginRound();
            }
        }

        /// <summary>
        /// 라운드를 시작해도 되는 맵 상태인가(스프린트 18b).
        ///
        /// 씬 흐름 컨트롤러가 없는 구성(맵과 시스템이 한 씬에 있는 스프린트 18 이전 배치)에서는
        /// 항상 true다 — 씬 분리 전/후 어느 배치에서도 동작하게 하려는 것이다(마이그레이션 안전장치).
        /// </summary>
        private static bool MapReadyForRound()
        {
            SceneFlowController flow = SceneFlowController.Instance;
            return flow == null || flow.MapLoaded;
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
                    _waitingForMap = false;
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

            SetPhase(GameFlowState.InGame);
            Debug.Log($"[RoundNet:Server] 라운드 시작 — 서버 권위 타이머 {_roundDurationSeconds:0}초 (§6.2)");
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

            bool gateOpen = CurrentGateOpen();
            if (!_driver.TryRegisterEscape(playerId, role, gateOpen))
            {
                Debug.Log($"[RoundNet:Server] 탈출 거부 — playerId={playerId} 서버 재검증 실패 " +
                          $"(gateOpen={gateOpen}, role={role}) §5.3");
                return;
            }

            _escaped.Value = _driver.EscapedCount;
            Debug.Log($"[RoundNet:Server] 탈출 확정 — playerId={playerId} (누적 {_driver.EscapedCount}명)");
            EvaluateAndPush();
        }

        /// <summary>서버 판정을 1회 수행하고, 새로 결정되면 결과 전파 + RoundEnd 페이즈 진입. 서버 전용.</summary>
        private void EvaluateAndPush()
        {
            IEscapeGateState gate = EscapeGateRegistry.Current;
            int opened = gate != null ? gate.OpenedValves : 0;
            int total = gate != null ? gate.TotalValves : 0;
            bool allTagged = ServerRoundDriver.AllRunnersTagged(TagTargetRegistry.Targets);

            if (!_driver.Evaluate(opened, total, allTagged))
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
                      $"(밸브 {opened}/{total}, 탈출 {_driver.EscapedCount}, 전원태그={allTagged}, " +
                      $"남은 {_driver.RemainingSeconds:0.0}초) → 리매치 투표 {RematchVoteDriver.VoteWindowSeconds:0}초");
        }

        private bool CurrentGateOpen()
        {
            IEscapeGateState gate = EscapeGateRegistry.Current;
            return gate != null && gate.IsGateOpen;
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

            // ③ 밸브 초기화(닫힘·진행도 0).
            List<ValveNetworkSync> valves = ValveNetworkSync.Spawned;
            for (int i = 0; i < valves.Count; i++)
                valves[i]?.ServerResetForNewRound();

            // ④ 라운드 상태 초기화(표시값 포함). 결과가 InProgress로 돌아가며 결과 화면이 닫힌다.
            _driver = null;
            _vote = null;
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
        /// 파문 1건을 세션 집계에 기록한다. <c>PulseNetworkSync</c>의 ServerRpc가 발생원을 확정한
        /// 직후 호출한다 — 클라이언트 보고가 아니라 **서버가 받은 사실**만 센다.
        /// </summary>
        internal static void ServerRecordPulse(int playerId, SoundType type)
        {
            RoundNetworkSync instance = ServerInstance;
            if (instance == null)
                return;

            instance._awards.RecordPulse(playerId, type);
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
            _result.OnChange += OnResultChanged;
            _phase.OnChange += OnPhaseChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _result.OnChange -= OnResultChanged;
            _phase.OnChange -= OnPhaseChanged;
        }

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
