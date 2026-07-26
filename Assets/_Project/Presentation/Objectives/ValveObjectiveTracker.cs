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
        [Tooltip("§6.2 4인 MVP 기준 밸브 수. 비워두면 씬에서 찾은 개수를 쓴다.")]
        [SerializeField] private int _totalValvesOverride;

        private ValveBehaviour[] _valves;
        private int _lastOpenedCount = -1;

        public int TotalValves => _totalValvesOverride > 0 ? _totalValvesOverride : _valves.Length;
        public int OpenedCount { get; private set; }

        /// <summary>§6.1: 밸브 전부 개방 시 배수로 게이트가 열린다 — 탈출의 전제 조건.</summary>
        public bool IsEscapeGateOpen => TotalValves > 0 && OpenedCount >= TotalValves;

        /// <summary>
        /// 씬에서 찾은 밸브 목록(읽기 전용 용도). 스프린트 16 HUD가 밸브별 상태·진행률을
        /// 표시하려고 읽는다 — 집계기가 이미 찾아둔 배열을 재사용해 중복 탐색을 피한다.
        /// </summary>
        public ValveBehaviour[] Valves => _valves ?? System.Array.Empty<ValveBehaviour>();

        // ── IEscapeGateState (스프린트 12: 서버 라운드 판정기가 읽는 게이트 상태) ──
        int IEscapeGateState.OpenedValves => OpenedCount;
        int IEscapeGateState.TotalValves => TotalValves;
        bool IEscapeGateState.IsGateOpen => IsEscapeGateOpen;

        private void Awake()
        {
            _valves = FindObjectsByType<ValveBehaviour>();
        }

        // Net(RoundNetworkSync)이 §15.2를 넘어 게이트 상태를 읽도록 Core 레지스트리에 등록한다.
        private void OnEnable() => EscapeGateRegistry.Register(this);
        private void OnDisable() => EscapeGateRegistry.Unregister(this);

        private void Update()
        {
            OpenedCount = CountOpened();
            if (OpenedCount == _lastOpenedCount)
                return;

            _lastOpenedCount = OpenedCount;
            Debug.Log($"[Valve] 개방 {OpenedCount}/{TotalValves}");

            if (IsEscapeGateOpen)
                Debug.Log("[Valve] 밸브 전체 개방 — 배수로 게이트 Open, 탈출 가능 (§6.1)");
        }

        private int CountOpened()
        {
            int opened = 0;
            for (int i = 0; i < _valves.Length; i++)
            {
                if (_valves[i] != null && _valves[i].IsOpen)
                    opened++;
            }
            return opened;
        }
    }
}
