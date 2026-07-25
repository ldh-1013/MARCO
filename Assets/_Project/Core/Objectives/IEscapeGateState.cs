namespace Marco.Core.Objectives
{
    /// <summary>
    /// 밸브 개방 집계와 §6.1 배수로 게이트 개방 상태를 노출하는 최소 계약(스프린트 12).
    ///
    /// **왜 필요한가**: 라운드 결과를 서버 하나에서 판정하려면(<c>RoundNetworkSync</c>, Net)
    /// 서버가 "게이트가 열렸는지·밸브 몇 개가 열렸는지"를 알아야 한다. 그 집계는
    /// Presentation(<c>ValveObjectiveTracker</c>)에 있는데, §15.2상 Net은 Presentation을
    /// 참조하지 않는다. 그래서 공통 조상 Core에 계약만 두고, 실제 연결은
    /// <see cref="EscapeGateRegistry"/>(지연 바인딩)로 한다 —
    /// 밸브의 <see cref="IValveHost"/>·태그의 <c>ITagTarget</c>과 같은 패턴.
    ///
    /// **서버 권위 보장**: 네트워크 활성 시 <c>ValveObjectiveTracker</c>가 읽는 개방 상태는
    /// 각 밸브의 <c>ValveNetworkSync</c> SyncVar(서버 확정값)이다. 따라서 서버가 이 인터페이스로
    /// 읽는 값도 서버 권위 밸브 상태와 일치한다.
    /// </summary>
    public interface IEscapeGateState
    {
        /// <summary>현재 개방된 밸브 수.</summary>
        int OpenedValves { get; }

        /// <summary>전체 밸브 수(판정식 <c>valvesOpened == totalValves</c>의 분모).</summary>
        int TotalValves { get; }

        /// <summary>§6.1: 밸브 전부 개방 시 배수로 게이트가 열린다 — 탈출의 전제 조건.</summary>
        bool IsGateOpen { get; }
    }
}
