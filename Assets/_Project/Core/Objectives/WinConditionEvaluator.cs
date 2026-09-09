namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.3 승패 판정. 기획서의 의사코드를 그대로 옮긴 순수 함수다.
    ///
    /// <code>
    /// // §6.3 [라운드 종료 시점에 1회만 평가]
    /// if (valvesOpened == totalValves &amp;&amp; escaped > (tagged + notEscaped))
    ///     result = Result.RunnersWin;
    /// else
    ///     result = Result.SeekerWin;
    /// </code>
    ///
    /// **왜 코드는 <c>escaped &gt; (tagged + notEscaped)</c>가 아니라 "탈출 ≥ 2"인가**:
    /// §6.3이 직접 계산한 **3 Runner 전수검증(10/10)** 이 두 식의 일치를 증명한다.
    /// 도망자 3명 고정(§1)에서 세 상태의 합은 항상 3이므로
    /// <c>escaped &gt; 3 - escaped</c> ⟺ <c>2·escaped &gt; 3</c> ⟺ <c>escaped ≥ 2</c>다.
    ///
    /// 이렇게 옮긴 실질적 이유는 <b>"미탈출"이 라운드 도중에는 존재하지 않는 값</b>이기
    /// 때문이다 — §6.3의 정의상 미탈출은 "시간 종료 시점에 살아 있으나 탈출하지 못함"이라
    /// 종료 전에는 확정할 수 없다. 판정은 매 프레임 호출되므로, 도중에도 계산 가능한
    /// 등가식으로 옮겨야 "탈출 2명이 나온 순간 즉시 도망자 승리"가 성립한다.
    ///
    /// **도망자 3명 전제**: §1이 "현재 버전의 모든 밸런스 수치와 승리조건은 도망자 3명
    /// 기준으로 잠근다"고 못박았고, "도망자가 4~5명이 되면 승리 목표 인원을 별도로
    /// 재계산해야 한다"고 경고한다. 그래서 두 임계값을 상수로 드러내 둔다 —
    /// 인원이 늘면 <b>여기만 고치면 되고, 고쳐야 한다는 사실이 눈에 보인다.</b>
    ///
    /// **판정 순서(§6.3 "동일 프레임 처리 우선순위" 1 → 2 → 3)**: 탈출 → 태그 → 시간 종료.
    /// 도망자 승리 조건을 먼저 확인하므로 탈출과 태그·시간 초과가 같은 프레임에 겹쳐도
    /// 탈출이 우선한다 — §6.3이 "탈출을 먼저 확정(도망자에게 유리하게 해석)"이라고 명시한다.
    /// </summary>
    public static class WinConditionEvaluator
    {
        /// <summary>
        /// §6.3·§8 도망자 승리에 필요한 탈출 인원. 도망자 3명 기준에서
        /// <c>escaped &gt; (tagged + notEscaped)</c>와 등가다(§6.3 전수검증 10/10).
        /// §12.3 HUD의 "탈출 ●● / 태그 ●○" 두 칸이 이 값을 시각화한 것이다.
        /// </summary>
        public const int EscapeWinThreshold = 2;

        /// <summary>
        /// §6.3 "태그 2명 도달 시 즉시 술래 승리 확정, 라운드 종료".
        ///
        /// **술래는 도망자 3명을 전부 태그할 필요가 없다.** 2명이면 남은 도망자는 1명뿐이라
        /// 탈출이 최대 1 &lt; <see cref="EscapeWinThreshold"/>가 되어 도망자 승리가 산술적으로
        /// 불가능해진다 — 그 시점에 결과가 이미 확정됐으므로 라운드를 더 끌지 않는다.
        /// </summary>
        public const int TagWinThreshold = 2;

        /// <param name="taggedRunners">
        /// 태그당해 메아리가 된 도망자 수(§6.3 "태그"). 예전 <c>allRunnersTagged</c> 불리언을
        /// 대체한다 — §6.3이 "전원 태그"가 아니라 <b>2명 도달</b>을 종료 조건으로 정했다.
        /// </param>
        public static RoundResult Evaluate(
            int valvesOpened,
            int totalValves,
            int runnersEscaped,
            int taggedRunners,
            float timeRemainingSeconds)
        {
            // §6.3 우선순위 1 — 탈출. 기획서는 ==를 쓰지만, 초과 상태가 생기더라도 승리를
            // 놓치지 않도록 >=로 둔다.
            //
            // totalValves <= 0 방어(이월 C1): 밸브 목표가 구성되지 않은 씬에서는 `0 >= 0`이
            // 참이 되어 밸브 조건이 **공허하게 성립**한다. 목표가 없는 라운드를 "목표를 전부
            // 달성했다"로 읽는 것은 §6.1("밸브 3개 개방 후 출구 접촉")의 뜻과 정반대다.
            // 미구성은 승리가 아니라 **판정 불가**로 다룬다.
            if (totalValves > 0 && valvesOpened >= totalValves && runnersEscaped >= EscapeWinThreshold)
                return RoundResult.RunnersWin;

            // §6.3 우선순위 2·3 — 태그 2명 도달, 그리고 시간 종료.
            if (taggedRunners >= TagWinThreshold || timeRemainingSeconds <= 0f)
                return RoundResult.SeekerWin;

            return RoundResult.InProgress;
        }
    }
}
