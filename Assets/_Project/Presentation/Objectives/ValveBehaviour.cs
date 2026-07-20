using UnityEngine;
using Marco.Core.Objectives;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// 씬의 밸브 오브젝트 하나에 Core <see cref="Valve"/> 상태기계를 붙이는 얇은 래퍼.
    /// 판정 로직은 전부 Core에 있고 여기서는 인스턴스 소유와 위치 제공만 한다.
    ///
    /// §6.2: 회전 시간은 4인 MVP 기준 3초. 6인 구간(3.75초)은 인스펙터로 바꿀 수 있게
    /// 직렬화해 두되, 인원별 자동 조정은 인원 관리 시스템이 생길 때 배선한다.
    /// </summary>
    public sealed class ValveBehaviour : MonoBehaviour
    {
        [Tooltip("§6.2 회전 시간. 4인 MVP=3초, 6인=3.75초.")]
        [SerializeField] private float _rotationSeconds = Valve.DefaultRotationSeconds;

        [Tooltip("§10.1 구역 이름(로그 식별용).")]
        [SerializeField] private string _displayName = "Valve";

        private Valve _valve;

        public Valve Valve => _valve ??= new Valve(_rotationSeconds);
        public string DisplayName => _displayName;
        public bool IsOpen => Valve.State == ValveState.Open;

        private void Awake()
        {
            // 프로퍼티 접근 순서와 무관하게 인스턴스를 확정해 둔다.
            _ = Valve;
        }
    }
}
