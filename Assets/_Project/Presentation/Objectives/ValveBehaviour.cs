using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Objectives;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// 씬의 밸브 오브젝트 하나에 Core <see cref="Valve"/> 상태기계를 붙이는 얇은 래퍼.
    /// 판정 로직은 전부 Core에 있고 여기서는 인스턴스 소유와 위치 제공만 한다.
    ///
    /// §6.1: 회전 시간은 **밸브마다 다르다**(A 3.0 / B 2.0 / C 4.0초).
    /// 상수는 <see cref="ValveOccupancy.RotateSeconds"/>에 있고, 어느 밸브인지는
    /// 씬 인스펙터에서 지정한다 — 밸브 종류를 코드가 알 필요가 없기 때문이다.
    /// 6인 구간 보정(3.75초)도 같은 필드로 덮어쓸 수 있다.
    ///
    /// 스프린트 10(서버 권위 동기화): 같은 오브젝트에 <see cref="IValveNetworkBridge"/>
    /// (Net의 <c>ValveNetworkSync</c>)가 붙고 네트워크가 시작되면, 개방 상태의 진실은
    /// 로컬 <see cref="Valve"/>가 아니라 <b>서버가 확정해 전파한 브릿지 값</b>이다.
    /// 그래서 <see cref="IsOpen"/>은 네트워크 활성 시 브릿지를 읽는다 —
    /// 이렇게 하면 <c>ValveObjectiveTracker</c>의 개방 집계가 클라이언트에서도 그대로 맞는다.
    /// 네트워크가 없으면 기존처럼 로컬 <see cref="Valve"/>를 읽어 스프린트 5 워크플로우를 보존한다.
    /// </summary>
    public sealed class ValveBehaviour : MonoBehaviour, IValveHost
    {
        [Tooltip("§10.2 밸브 식별자. 회전 시간·소음 배율·구역 이름을 여기서 유도한다(§6.1).")]
        [SerializeField] private ValveId _valveId = ValveId.A;

        [Tooltip("§10.1 구역 이름(로그 식별용). 비워두면 §6.1 구역 이름을 쓴다.")]
        [SerializeField] private string _displayName = string.Empty;

        private Valve _valve;
        private IValveNetworkBridge _bridge;

        /// <summary>
        /// §10.2 이 밸브의 식별자. <b>회전 시간을 인스펙터에서 따로 받지 않는다</b> —
        /// v0.4에서 총 점유가 8.0초로 균등해지면서 배분이 밸브별로 고정됐고(§6.1),
        /// 인스펙터 값과 표가 갈라지면 균등 제약이 조용히 깨진다.
        /// </summary>
        public ValveId ValveId => _valveId;

        /// <summary>맵 v2 생성기(에디터)가 마커에 붙이면서 §10.2 식별자를 지정한다.</summary>
        public void Configure(ValveId id)
        {
            _valveId = id;
            _valve = null; // 회전 시간이 식별자에서 유도되므로 다시 만든다
        }

        public Valve Valve => _valve ??= CreateValve();

        public string DisplayName =>
            string.IsNullOrEmpty(_displayName) ? ValveOccupancy.ZoneOf(_valveId) : _displayName;

        /// <summary>§6.1 회전 시간은 §6.1 배분표에서 온다.</summary>
        private Valve CreateValve()
        {
            var valve = new Valve(ValveOccupancy.RotateSeconds(_valveId));
            valve.Configure(_valveId);
            return valve;
        }

        /// <summary>
        /// 네트워크가 활성이면 서버 확정 상태를, 아니면 로컬 상태를 읽는다.
        /// 브릿지가 없거나(로컬 전용 씬) 네트워크 미시작이면 로컬 <see cref="Valve"/>가 진실이다.
        /// </summary>
        public bool IsOpen => NetworkActive ? _bridge.State == ValveState.Open : Valve.State == ValveState.Open;

        /// <summary>
        /// 표시용 현재 상태. <see cref="IsOpen"/>과 같은 규칙으로 네트워크/로컬 소스를 고른다.
        /// 새 계산은 없다 — 이미 있는 값을 시각 표시(<c>ValveVisualIndicator</c>)가 읽기만 한다.
        /// </summary>
        public ValveState State => NetworkActive ? _bridge.State : Valve.State;

        /// <summary>표시용 현재 진행도(0~1). 네트워크면 서버 확정값, 아니면 로컬 값.</summary>
        public float Progress01 => NetworkActive ? _bridge.Progress01 : Valve.Progress01;

        /// <summary>
        /// §6.1 감쇠 중인가. <b>HUD가 색을 달리해야 하는 정보다</b>(§12.4) —
        /// 회전 중과 구분되지 않으면 "지금 뺄까 더 돌릴까" 판단이 불가능해진다.
        /// </summary>
        public bool IsDecaying => NetworkActive ? _bridge.IsDecaying : Valve.IsDecaying;

        /// <summary>§6.1-2 역류 잔여(초). 네트워크면 시작 시점 값(클라이언트가 카운트다운).</summary>
        public float ReflowRemaining =>
            NetworkActive ? _bridge.ReflowRemainingAtStart : Valve.ReflowRemaining;

        /// <summary>§6.1-0 이번 라운드 활성인가. 비활성은 잠금 표시되고 상호작용이 거부된다.</summary>
        public bool IsActiveThisRound =>
            NetworkActive ? _bridge.IsActiveThisRound : Valve.IsActive;

        /// <summary>서버 권위 브릿지가 붙어 있고 네트워크가 시작됐는가.</summary>
        public bool NetworkActive => _bridge != null && _bridge.NetworkActive;

        /// <summary>같은 오브젝트의 서버 권위 브릿지(없으면 null — 로컬 전용).</summary>
        public IValveNetworkBridge NetworkBridge => _bridge;

        private void Awake()
        {
            // 프로퍼티 접근 순서와 무관하게 인스턴스를 확정해 둔다.
            _ = Valve;
            // 같은 오브젝트에 Net의 ValveNetworkSync가 있으면 Core 인터페이스로만 잡는다.
            _bridge = GetComponent<IValveNetworkBridge>();
        }

        /// <summary>
        /// 새 라운드를 위해 밸브를 닫힌 초기 상태로 되돌린다(스프린트 17 재시작 골격).
        ///
        /// **Core <see cref="Valve"/>의 판정 로직을 건드리지 않기 위해 인스턴스를 새로 만든다** —
        /// `Valve`는 §6.1 상태기계상 Open에서 Closed로 되돌아가는 전이를 갖지 않으므로(의도된 설계:
        /// 라운드 중 개방은 되돌릴 수 없다), 리셋 전이를 추가하는 대신 라운드 경계에서 새 인스턴스로
        /// 교체한다. 회전 시간 등 설정값은 인스펙터 값에서 그대로 다시 온다.
        ///
        /// 서버 권위 경로에서는 <c>ValveNetworkSync</c>가 이 호출 후 자신의 구동기를 새 인스턴스로
        /// 다시 만들어야 한다(옛 <see cref="Valve"/>를 계속 들고 있으면 리셋이 반영되지 않는다).
        /// </summary>
        public void ResetValveForNewRound()
        {
            // ★ v0.4: 인스턴스를 **교체하지 않는다.** §6.1 [v0.4]가 Valve.ResetForNewRound를
            //   제공하며, 교체하면 서버 구동기가 구독한 Opened/ReflowStarted/ReflowPulse/Closed
            //   이벤트가 끊어져 감쇠·역류 전파가 조용히 사라진다.
            Valve.ResetForNewRound();
        }
    }
}
