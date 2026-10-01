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
        /// <summary>
        /// <b>현재 동시에 Open 상태인</b> 밸브 수. §6.1-2 역류로 <b>줄어들 수 있다</b> —
        /// 누적 개방 수가 아니다. 읽는 쪽이 "증가만 한다"고 가정하면 안 된다.
        ///
        /// <para>
        /// §6.5-2 배수구 작업 시간 <c>T = 14 − (동시 개방 밸브 수 × 3)</c>이 이 값을 쓴다.
        /// 블록 4가 페이즈 중에도 조회하므로 언제든 유효해야 한다.
        /// </para>
        /// </summary>
        int OpenedValves { get; }

        /// <summary>
        /// §6.1-0 이번 라운드 <b>활성</b> 밸브 수. 배치 수(5)가 아니라 활성 수(3, 10-01)다 —
        /// HUD가 표시할 슬롯 개수이기도 하다.
        /// </summary>
        int TotalValves { get; }

        /// <summary>
        /// §6.2 게이트가 열리는 데 필요한 <b>동시</b> 개방 수 — 10-01부터 활성 수(3, 인원 무관). 서버가 정한 값을
        /// 읽는다 — 피어마다 따로 계산하지 않는다.
        /// </summary>
        int RequiredOpenValves { get; }

        /// <summary>
        /// §6.1-2 게이트가 열렸는가. <b>latch다</b> — 한 번 열리면 역류로 밸브가 닫혀도
        /// 라운드 종료까지 유지된다. 취소되지 않는다.
        /// </summary>
        bool IsGateOpen { get; }
    }
}
