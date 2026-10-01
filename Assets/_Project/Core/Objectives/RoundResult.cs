namespace Marco.Core.Objectives
{
    /// <summary>§6.3 라운드 결과.</summary>
    public enum RoundResult
    {
        /// <summary>아직 승패가 갈리지 않음.</summary>
        InProgress,

        /// <summary>라운드가 끝났을 때(도망자 전원 탈출 · 포획 또는 시간 종료) 탈출 ≥ ⌈도망자 ÷ 2⌉, 또는 최후 생존자 탈출.</summary>
        RunnersWin,

        /// <summary>라운드가 끝났을 때 탈출이 요구치에 못 미침(전원 포획 포함).</summary>
        SeekerWin
    }
}
