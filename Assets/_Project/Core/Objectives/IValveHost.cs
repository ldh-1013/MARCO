namespace Marco.Core.Objectives
{
    /// <summary>
    /// 밸브 오브젝트가 자신의 Core <see cref="Valve"/> 상태기계를 외부(Net 레이어)에
    /// 노출하는 최소 계약.
    ///
    /// **왜 Core에 두는가**: §15.2상 `Net`은 `Presentation`을 참조하지 않는다. 그런데
    /// 서버 권위 동기화(스프린트 10)에서 Net의 `ValveNetworkSync`가 밸브의 권위 상태기계를
    /// 구동하려면 같은 오브젝트의 `ValveBehaviour`(Presentation)가 들고 있는 <see cref="Valve"/>
    /// 인스턴스에 접근해야 한다. 둘이 공통으로 아는 Core에 계약만 두어, Net이 구체 타입
    /// (`ValveBehaviour`)을 전혀 모른 채 `GetComponent&lt;IValveHost&gt;()`로 연결한다 —
    /// <see cref="Marco.Core.Net.ILocalControlGate"/>·<see cref="Marco.Core.Net.IPlayerIdentity"/>와
    /// 완전히 같은 패턴이다.
    ///
    /// <see cref="Valve"/>는 순수 Core 타입이라 Net·Presentation 양쪽이 직접 다룰 수 있다 —
    /// 이 인터페이스는 "누가 그 인스턴스를 소유·제공하는가"만 갈라준다.
    /// </summary>
    public interface IValveHost
    {
        /// <summary>이 오브젝트가 소유한 §6.1 밸브 상태기계 인스턴스.</summary>
        Valve Valve { get; }
    }
}
