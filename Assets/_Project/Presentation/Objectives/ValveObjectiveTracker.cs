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
    /// </summary>
    public sealed class ValveObjectiveTracker : MonoBehaviour
    {
        [Tooltip("§6.2 4인 MVP 기준 밸브 수. 비워두면 씬에서 찾은 개수를 쓴다.")]
        [SerializeField] private int _totalValvesOverride;

        private ValveBehaviour[] _valves;
        private int _lastOpenedCount = -1;

        public int TotalValves => _totalValvesOverride > 0 ? _totalValvesOverride : _valves.Length;
        public int OpenedCount { get; private set; }

        /// <summary>§6.1: 밸브 전부 개방 시 배수로 게이트가 열린다 — 탈출의 전제 조건.</summary>
        public bool IsEscapeGateOpen => TotalValves > 0 && OpenedCount >= TotalValves;

        private void Awake()
        {
            _valves = FindObjectsByType<ValveBehaviour>();
        }

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
