namespace Marco.Core.Objectives
{
    /// <summary>§6.3 라운드 결과.</summary>
    public enum RoundResult
    {
        /// <summary>아직 승패가 갈리지 않음.</summary>
        InProgress,

        /// <summary>밸브 전부 개방 + 1인 이상 탈출.</summary>
        RunnersWin,

        /// <summary>도망자 전원 태그 또는 제한시간 종료.</summary>
        SeekerWin
    }
}
