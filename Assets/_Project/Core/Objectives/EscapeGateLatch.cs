namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.1-2 배수로 게이트 latch — <b>현재 동시 개방 수</b>가 요구치(§6.2, 2~3)에 닿는 순간 열리고,
    /// 그 뒤 역류로 밸브가 닫혀도 <b>라운드가 끝날 때까지</b> 닫히지 않는다.
    ///
    /// <para>
    /// [블록 7] Presentation(<c>ValveObjectiveTracker</c>) 안에 인라인으로 있던 규칙을 옮겼다 —
    /// 통합 시나리오(블록 7-E)를 EditMode로 고정하려면 규칙이 Core에 있어야 한다. 옮기면서 결함 1건을
    /// 고쳤다: latch가 <b>씬 로드/언로드에서만</b> 풀려, 맵을 유지한 채 시작하는 리매치 라운드가
    /// <b>게이트가 열린 채</b> 시작됐다. 이제 라운드 번호가 바뀌면 풀린다(<see cref="ResetIfNewRound"/>).
    /// </para>
    ///
    /// <para>
    /// 누적 카운터를 쓰지 않는 것이 핵심이다 — "A를 열고 닫고, B를 열고 닫고"로는 절대 열리지 않는다.
    /// </para>
    /// </summary>
    public sealed class EscapeGateLatch
    {
        private int _round = int.MinValue;

        public bool IsOpen { get; private set; }

        /// <summary>이번 호출로 새로 열렸으면 true(1회성 — 로그·연출용).</summary>
        public bool Update(int openedCount, int requiredCount)
        {
            if (IsOpen || requiredCount <= 0 || openedCount < requiredCount)
                return false;

            IsOpen = true;
            return true;
        }

        /// <summary>라운드 번호가 바뀌었으면 latch를 푼다(리매치 포함 — 더블체크 2).</summary>
        public void ResetIfNewRound(int roundNumber)
        {
            if (roundNumber == _round)
                return;

            _round = roundNumber;
            IsOpen = false;
        }

        /// <summary>맵이 바뀌었다(씬 로드/언로드) — 무조건 푼다.</summary>
        public void Reset() => IsOpen = false;
    }
}
