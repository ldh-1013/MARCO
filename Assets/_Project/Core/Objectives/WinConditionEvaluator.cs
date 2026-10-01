using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.3 승패 판정. 기획서 의사코드를 옮긴 순수 함수다.
    ///
    /// <code>
    /// // [라운드 종료 — 남은 도망자 0명(탈출 또는 포획) 또는 시간 종료]   ← 10-01: 이때만 판정한다
    /// int required = ceil(runnerCount / 2);              // §6.2 탈출 요구 인원
    ///
    /// if (escaped &gt;= required || lastSurvivorEscaped)
    ///     result = RunnersWin;
    /// else
    ///     result = SeekerWin;
    /// </code>
    ///
    /// <para>
    /// <b>10-01 — 라운드가 끝나기 전에는 판정하지 않는다.</b> 예전에는 탈출 수가 요구치에 닿는 <i>순간</i> 도망자 승리였다 —
    /// 도망자 2명(요구 1)이면 1명이 나가자마자 다른 1명이 맵에 있어도 승리 화면이 떴다. 이제 라운드는 ① 남은 도망자가 0명
    /// (전원 탈출 또는 포획) ② 시간 종료(최후 생존자 페이즈 90초 포함)에만 끝나고, 그때 위 식으로 판정한다. 게이트 개방은 탈출을
    /// 가능하게 할 뿐 판정식의 항이 아니다(아래).
    /// </para>
    ///
    /// <para>
    /// <b>v0.4에서 사라진 두 상수.</b>
    /// </para>
    ///
    /// <para>
    /// ① <b><c>TagWinThreshold = 2</c> 삭제.</b> §6.3이 *"태그 2명 도달 시 즉시 종료를
    /// 삭제했다"* 고 명시한다 — 그 규칙은 마지막 생존자에게서 모든 승리 경로를 빼앗아
    /// 도망자 2명이 잡힌 시점에 라운드가 기계적으로 끝나게 만들었다. v0.4에서 도망자가
    /// 1명 남는 것은 <b>술래의 승리가 아니라 §6.5 최후 생존자 페이즈의 시작</b>이다.
    /// 술래는 전원을 잡아야 이긴다.
    /// </para>
    ///
    /// <para>
    /// ② <b><c>EscapeWinThreshold = 2</c> 삭제.</b> §6.2 [v0.4]가 탈출 요구를
    /// <c>⌈도망자 ÷ 2⌉</c>로 일반화했다. 2를 남기면 6인 게임(도망자 5 → 요구 3)에서 틀린다.
    /// 값은 <see cref="ValveRoster.EscapeRequirement"/> 한 곳이 소유한다.
    /// </para>
    ///
    /// <para>
    /// <b>게이트가 판정식에 없는 이유</b>(§6.3 원문): 탈출의 정의 자체가 *"출구 게이트 개방 후
    /// 출구 접촉"* 이므로 게이트가 닫혀 있으면 <c>escaped</c>가 애초에 증가하지 않는다.
    /// 게이트는 판정식의 항이 아니라 <b>탈출의 전제 조건</b>이며, 그 강제는 탈출을 접수하는
    /// 지점(<c>RoundNetworkSync.ServerSubmitEscape</c>)이 한다. 구 <c>totalValves &gt; 0</c>
    /// 방어의 의도("목표가 구성되지 않은 씬을 승리로 읽지 않는다")도 같은 이유로 그쪽으로 옮겨진다.
    /// </para>
    ///
    /// <para>
    /// <b>★ <c>lastSurvivorEscaped</c>는 집계에서 유도할 수 없다.</b> §6.3 전수검증이
    /// <b>52케이스 중 4건</b>이 최종 집계만으로 결정되지 않음을 보였다(도망자 3 E1T2N0 /
    /// 4 E1T3N0 / 5 E1T4N0 / 5 E2T3N0). 같은 숫자에서 <i>마지막 이탈이 탈출인지 태그인지</i>에
    /// 따라 승패가 갈린다. 그래서 이 값은 <b>페이즈 중 탈출이 발생한 순간에 세우는 사건 플래그</b>
    /// 이며 절대 역산하지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b><c>RoundResult</c>에 값을 추가하지 않는다</b>(§6.5-1 "마지막 생존자의 탈출 = 팀 승리").
    /// 최후 생존자 탈출도 <c>RunnersWin</c>이며, 결과 화면 문구만 구분한다.
    /// </para>
    /// </summary>
    public static class WinConditionEvaluator
    {
        /// <summary>
        /// §6.2 [v0.4] 탈출 요구 인원 = <c>⌈도망자 수 ÷ 2⌉</c>.
        /// <see cref="ValveRoster.EscapeRequirement"/>를 그대로 가리킨다 — 두 곳에 두지 않는다.
        /// </summary>
        public static int EscapeRequirement(int runnerCount) =>
            ValveRoster.EscapeRequirement(runnerCount);

        /// <summary>
        /// §6.3 판정. <b>라운드가 끝났을 때만</b>(남은 도망자 0명 또는 시간 종료) 결과를 내고, 그 전에는
        /// <see cref="RoundResult.InProgress"/>다(10-01). 결과의 우선순위는 도망자 승리 → 술래 승리다
        /// (§6.3 "탈출을 먼저 확정 — 도망자에게 유리하게 해석").
        /// </summary>
        /// <param name="runnerCount">이번 라운드 도망자 총수. 탈출 요구의 분모다.</param>
        /// <param name="runnersEscaped">출구로 탈출을 확정한 도망자 수.</param>
        /// <param name="aliveRunners">
        /// <b>살아있는</b> 도망자 수 = 전체 − 태그 아웃 − 탈출 완료.
        /// 이 셈은 <see cref="RunnerCensus"/> 한 곳이 소유하고 나머지는 조회만 한다.
        /// </param>
        /// <param name="lastSurvivorEscaped">
        /// §6.5 최후 생존자가 배수구(또는 열린 출구)로 탈출했는가. <b>사건 플래그</b>다.
        /// </param>
        /// <param name="timeRemainingSeconds">라운드 잔여 시간.</param>
        public static RoundResult Evaluate(
            int runnerCount,
            int runnersEscaped,
            int aliveRunners,
            bool lastSurvivorEscaped,
            float timeRemainingSeconds)
        {
            // 라운드 종료 — §6.3 "모든 도망자가 탈출 또는 태그로 확정 → 즉시 종료, 판정" · "제한시간 종료 → 판정".
            //   **"살아있는 0명"은 "전원 태그"가 아니다** — 탈출자도 빠진다. 1명이라도 맵에 남아 있으면 끝나지 않는다(10-01).
            bool allRunnersOut = runnerCount > 0 && aliveRunners <= 0;
            bool timeUp = timeRemainingSeconds <= 0f;
            if (!allRunnersOut && !timeUp)
                return RoundResult.InProgress;

            // ①·② 도망자 승리 — 요구 인원 탈출, 또는 최후 생존자의 단독 탈출.
            //     §6.3 "마지막 1인의 탈출은 게이트 개방 여부와 무관하게 팀 승리다."
            if (lastSurvivorEscaped)
                return RoundResult.RunnersWin;

            if (runnerCount > 0 && runnersEscaped >= EscapeRequirement(runnerCount))
                return RoundResult.RunnersWin;

            // ③ 술래 승리 — 탈출이 요구치에 못 미친 채 라운드가 끝났다(남은 도망자는 미탈출로 집계).
            return RoundResult.SeekerWin;
        }

        /// <summary>
        /// §6.5-1 최후 생존자 페이즈 진입 조건 — <b>살아있는 도망자가 정확히 1명</b>.
        ///
        /// <para>
        /// 0명은 페이즈가 아니라 <see cref="RoundResult.SeekerWin"/>이다 — 지킬 사람이 없다.
        /// 2명 이상은 아직 일반 라운드다.
        /// </para>
        /// </summary>
        public static bool ShouldEnterLastSurvivorPhase(int aliveRunners) => aliveRunners == 1;
    }

    /// <summary>
    /// §6.3 "살아있는 도망자" 셈의 <b>단일 소유자</b>.
    ///
    /// <para>
    /// 지시서가 *"이 셈을 여러 곳에서 하지 마라. 한 곳이 소유하고 나머지는 조회한다"* 고
    /// 못박았다. 승리 판정·페이즈 진입·배수구 활성·HUD가 전부 이 값을 쓰는데, 각자 세면
    /// "술래는 페이즈에 들어갔는데 판정기는 아직 2명으로 알고 있는" 상태가 생긴다.
    /// </para>
    /// </summary>
    public readonly struct RunnerCensus
    {
        /// <summary>이번 라운드 도망자 총수.</summary>
        public readonly int Total;

        /// <summary>태그당해 메아리가 된 수.</summary>
        public readonly int TaggedOut;

        /// <summary>탈출을 확정한 수.</summary>
        public readonly int Escaped;

        public RunnerCensus(int total, int taggedOut, int escaped)
        {
            Total = total;
            TaggedOut = taggedOut;
            Escaped = escaped;
        }

        /// <summary>§6.3 살아있는 도망자 = 전체 − 태그 아웃 − 탈출 완료. 음수가 되지 않는다.</summary>
        public int Alive => Mathf.Max(0, Total - TaggedOut - Escaped);

        /// <summary>§6.2 [v0.4] 이번 라운드 탈출 요구 인원.</summary>
        public int EscapeRequirement => ValveRoster.EscapeRequirement(Total);

        /// <summary>§6.5-1 페이즈에 들어가야 하는가.</summary>
        public bool ShouldEnterLastSurvivorPhase =>
            WinConditionEvaluator.ShouldEnterLastSurvivorPhase(Alive);

        public override string ToString() =>
            $"도망자 {Total} (생존 {Alive} · 태그 {TaggedOut} · 탈출 {Escaped} / 요구 {EscapeRequirement})";
    }
}
