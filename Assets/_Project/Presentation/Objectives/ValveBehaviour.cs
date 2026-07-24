using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Objectives;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// 씬의 밸브 오브젝트 하나에 Core <see cref="Valve"/> 상태기계를 붙이는 얇은 래퍼.
    /// 판정 로직은 전부 Core에 있고 여기서는 인스턴스 소유와 위치 제공만 한다.
    ///
    /// §6.2: 회전 시간은 4인 MVP 기준 3초. 6인 구간(3.75초)은 인스펙터로 바꿀 수 있게
    /// 직렬화해 두되, 인원별 자동 조정은 인원 관리 시스템이 생길 때 배선한다.
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
        [Tooltip("§6.2 회전 시간. 4인 MVP=3초, 6인=3.75초.")]
        [SerializeField] private float _rotationSeconds = Valve.DefaultRotationSeconds;

        [Tooltip("§10.1 구역 이름(로그 식별용).")]
        [SerializeField] private string _displayName = "Valve";

        private Valve _valve;
        private IValveNetworkBridge _bridge;

        public Valve Valve => _valve ??= new Valve(_rotationSeconds);
        public string DisplayName => _displayName;

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
    }
}
