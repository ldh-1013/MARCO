using UnityEngine;
using Marco.Core.Objectives;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// 씬의 밸브 개방 수를 집계하고, §6.1 "밸브 3개 모두 Open → 배수로 게이트 Open"
    /// 상태를 노출한다.
    ///
    /// **§6.3 승패 판정은 여기서 하지 않는다.** 스프린트 6부터는
    /// `RoundCoordinator`가 탈출·시간까지 모아 단일 지점에서 판정한다 —
    /// 판정 권한이 두 곳에 흩어져 서로 다른 결론을 로그로 찍는 것을 막기 위함.
    ///
    /// 스프린트 12(라운드 서버 권위화): 서버 판정기(<c>RoundNetworkSync</c>, Net)가 밸브 게이트
    /// 상태를 읽어야 하므로 <see cref="IEscapeGateState"/>를 구현해 <see cref="EscapeGateRegistry"/>에
    /// 등록한다 — Net이 Presentation을 참조하지 않고도(§15.2) 서버 권위 밸브 집계를 읽는다.
    /// 네트워크 활성 시 <c>ValveBehaviour.IsOpen</c>이 이미 서버 확정 SyncVar를 읽으므로,
    /// 여기 집계값도 서버 권위와 일치한다.
    /// </summary>
    public sealed class ValveObjectiveTracker : MonoBehaviour, IEscapeGateState
    {
        [Tooltip("이번 라운드 활성 밸브 수를 강제한다. 0이면 씬의 활성 밸브를 센다(§6.1-0).")]
        [SerializeField] private int _totalValvesOverride;

        [Tooltip("총원(§6.2 요구 개방 수 유도용). 0이면 씬의 역할 수를 못 알므로 5인 기준을 쓴다.")]
        [SerializeField] private int _totalPlayersOverride;

        private int _lastOpenedCount = -1;

        /// <summary>
        /// §6.1-0 이번 라운드 <b>활성</b> 밸브 수. 배치 수(5)가 아니다 —
        /// HUD 슬롯 개수이자 게이트 판정의 모집단이다.
        /// </summary>
        public int TotalValves => _totalValvesOverride > 0
            ? _totalValvesOverride
            : ObjectiveView.ActiveValves(ObjectiveRegistry.Current, CountActive());

        /// <summary>
        /// <b>현재 동시에 Open인</b> 밸브 수. §6.1-2 역류로 <b>줄어든다</b> —
        /// 누적 카운터가 아니다.
        /// </summary>
        public int OpenedCount { get; private set; }

        /// <summary>
        /// §6.2 게이트가 열리는 데 필요한 동시 개방 수(2 또는 3).
        /// <b>3을 하드코딩하지 않는다</b> — 총원에서 유도한다.
        /// </summary>
        public int RequiredOpenCount
        {
            get
            {
                int required = ObjectiveView.RequiredOpen(ObjectiveRegistry.Current, _totalPlayersOverride, out bool usedFallback);
                if (usedFallback && IsNetworkMode() && !_fallbackErrorLogged)
                {
                    // 5인 폴백은 오프라인 스모크 리그 전용이다(09-29) — 네트워크에서 쓰면 HUD · 게이트가 서버와 어긋난다.
                    _fallbackErrorLogged = true;
                    Debug.LogError($"[Valve] 네트워크 모드인데 서버 목표 수치가 공개되지 않아 {ObjectiveView.OfflineFallbackPlayers}인 폴백" +
                                   $"(요구 {required})을 쓴다 — RoundNetworkSync가 스폰됐는지 확인하라.");
                }

                return required;
            }
        }

        private bool _fallbackErrorLogged;

        /// <summary>접속이 시작됐는가(호스트 · 클라이언트). 오프라인 스모크 리그는 false.</summary>
        private static bool IsNetworkMode()
        {
            Marco.Core.Net.IConnectionService connection = Marco.Core.Net.ConnectionServiceRegistry.Current;
            return connection != null && connection.HasStarted;
        }

        /// <summary>§6.2 권장 구성(5인). 총원을 모를 때의 폴백이다.</summary>
        private const int DefaultTotalPlayers = 5;

        /// <summary>
        /// §6.1-2 게이트 latch. <b>한 번 true가 되면 되돌아가지 않는다</b> —
        /// *"출구 바로 앞에서 문이 닫히는 연출은 극적이지만 억울함이 재미를 넘어선다"*.
        /// 라운드 초기화(<see cref="Rescan"/>)에서만 풀린다.
        /// </summary>
        public bool IsEscapeGateOpen => ObjectiveView.GateOpen(ObjectiveRegistry.Current, _latch.IsOpen);

        /// <summary>§6.1-2 latch 규칙(Core). 라운드 번호가 바뀌면 풀린다 — 리매치 결함 수정(블록 7).</summary>
        private readonly EscapeGateLatch _latch = new EscapeGateLatch();

        private Marco.Presentation.GameFlow.RoundCoordinator _round;

        /// <summary>
        /// 씬에서 찾은 밸브 목록(읽기 전용 용도). 스프린트 16 HUD가 밸브별 상태·진행률을
        /// 표시하려고 읽는다 — 집계기가 이미 찾아둔 배열을 재사용해 중복 탐색을 피한다.
        /// </summary>
        public ValveBehaviour[] Valves => ValveRegistry.Snapshot;

        // ── IEscapeGateState (스프린트 12: 서버 라운드 판정기가 읽는 게이트 상태) ──
        int IEscapeGateState.OpenedValves => OpenedCount;
        int IEscapeGateState.TotalValves => TotalValves;
        int IEscapeGateState.RequiredOpenValves => RequiredOpenCount;
        bool IEscapeGateState.IsGateOpen => IsEscapeGateOpen;

        private void Awake()
        {
            Rescan();
        }

        /// <summary>
        /// 씬의 밸브를 다시 찾는다(스프린트 18b — 맵 애디티브 로드 대응).
        ///
        /// **왜 필요한가**: §15.4상 맵은 InGame 진입 시점에 로드되므로(§15.1 "Game(맵별 어디티브 로드)"),
        /// 이 집계기가 시스템 씬에서 <see cref="Awake"/>를 돌 때는 밸브가 **아직 존재하지 않는다**.
        /// 맵이 로드·언로드될 때마다 <c>SceneFlowController</c>가 이 메서드를 호출해 목록을 갱신한다.
        /// 맵이 없으면 빈 배열이 되어 <see cref="TotalValves"/>=0 → <see cref="IsEscapeGateOpen"/>=false다
        /// (로비에서 탈출·게이트 개방이 성립하지 않는 올바른 상태).
        /// </summary>
        public void Rescan()
        {
            OpenedCount = 0;
            _lastOpenedCount = -1; // 다음 Update에서 로그를 한 번 다시 찍게 한다

            // §6.1-2 latch는 라운드 경계에서만 풀린다. 여기가 그 지점이다 —
            // 풀지 않으면 다음 라운드가 게이트 열린 상태로 시작한다(더블체크 2).
            _latch.Reset();
        }

        // Net(RoundNetworkSync)이 §15.2를 넘어 게이트 상태를 읽도록 Core 레지스트리에 등록한다.
        private void OnEnable()
        {
            EscapeGateRegistry.Register(this);

            // 스프린트 18b: 맵(Game 씬)이 애디티브로 오갈 때마다 밸브 목록을 다시 맞춘다.
            // **스스로** 구독하는 이유: Net이 이 Presentation 타입을 호출하면 §15.2 경계가 깨진다.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            EscapeGateRegistry.Unregister(this);
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Rescan();
        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => Rescan();

        private void Update()
        {
            OpenedCount = CountOpened();

            // §6.1-2 latch: **현재 동시 개방 수**가 요구치에 닿는 순간 한 번 열고, 그 뒤로는
            // 역류로 개방 수가 줄어도 닫지 않는다. 누적 카운터를 쓰지 않는 것이 핵심이다 —
            // "A를 열고 닫고, B를 열고 닫고" 로는 절대 열리지 않아야 한다.
            // [블록 7] 리매치는 맵을 다시 로드하지 않는다 — 라운드 번호로 latch를 푼다(더블체크 2).
            if (_round == null)
                _round = FindAnyObjectByType<Marco.Presentation.GameFlow.RoundCoordinator>();
            _latch.ResetIfNewRound(_round != null ? _round.RoundNumber : 0);

            if (_latch.Update(OpenedCount, RequiredOpenCount))
            {
                Debug.Log($"[Valve] 동시 개방 {OpenedCount}/{RequiredOpenCount} 달성 — " +
                          "탈출구 게이트 Open(latch, §6.1-2). 역류로 밸브가 닫혀도 유지된다");
            }

            if (OpenedCount == _lastOpenedCount)
                return;

            _lastOpenedCount = OpenedCount;
            Debug.Log($"[Valve] 동시 개방 {OpenedCount}/{RequiredOpenCount} (활성 {TotalValves})" +
                      (IsEscapeGateOpen ? " · 게이트 Open 유지" : string.Empty));
        }

        /// <summary>§6.1-0 이번 라운드 활성 밸브 수. 비활성은 상호작용 불가라 모집단에서 뺀다.</summary>
        private static int CountActive()
        {
            ValveBehaviour[] valves = ValveRegistry.Snapshot;
            int active = 0;
            for (int i = 0; i < valves.Length; i++)
            {
                if (valves[i] != null && valves[i].IsActiveThisRound)
                    active++;
            }

            return active;
        }

        /// <summary>
        /// <b>현재 Open 상태인</b> 밸브 수. 상태를 직접 읽으므로 역류로 Open이 풀리면
        /// 다음 프레임에 자동으로 줄어든다 — 증가만 하는 카운터가 아니다.
        /// </summary>
        private static int CountOpened()
        {
            ValveBehaviour[] valves = ValveRegistry.Snapshot;
            int opened = 0;
            for (int i = 0; i < valves.Length; i++)
            {
                if (valves[i] != null && valves[i].IsOpen)
                    opened++;
            }
            return opened;
        }
    }
}
