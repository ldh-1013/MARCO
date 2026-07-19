namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.3 승패 판정. 기획서의 의사코드를 그대로 옮긴 순수 함수다.
    ///
    /// <code>
    /// if (valvesOpened == totalValves &amp;&amp; runnersEscaped >= 1)
    ///     result = Result.RunnersWin;
    /// else if (allRunnersTagged || timeRemaining &lt;= 0)
    ///     result = Result.SeekerWin;
    /// </code>
    ///
    /// 판정 순서가 중요하다. 도망자 승리 조건을 먼저 확인하므로,
    /// 탈출과 시간 초과가 같은 프레임에 겹쳐도 탈출이 우선한다 —
    /// 탈출은 이미 달성된 결과라 나중에 취소될 수 없기 때문이다.
    ///
    /// 참고: §6.1상 탈출은 밸브가 전부 열려 배수로 게이트가 개방된 뒤에만 가능하므로,
    /// 실전에서 runnersEscaped >= 1이면 valvesOpened == totalValves가 이미 성립한다.
    /// 그래도 두 조건을 모두 확인하는 것은 기획서 의사코드에 충실하기 위함이다.
    /// </summary>
    public static class WinConditionEvaluator
    {
        public static RoundResult Evaluate(
            int valvesOpened,
            int totalValves,
            int runnersEscaped,
            bool allRunnersTagged,
            float timeRemainingSeconds)
        {
            // 기획서는 ==를 쓰지만, 초과 상태가 생기더라도 승리를 놓치지 않도록 >=로 둔다.
            if (valvesOpened >= totalValves && runnersEscaped >= 1)
                return RoundResult.RunnersWin;

            if (allRunnersTagged || timeRemainingSeconds <= 0f)
                return RoundResult.SeekerWin;

            return RoundResult.InProgress;
        }
    }
}
