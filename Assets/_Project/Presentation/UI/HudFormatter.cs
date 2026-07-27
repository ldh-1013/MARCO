using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// 인게임 HUD(§12.4) 표시 문자열을 만드는 순수 함수 모음(스프린트 16).
    ///
    /// HUD 작업의 대부분은 Unity 컴포넌트 배선이라 유닛 테스트가 어렵지만, **무엇을 어떻게
    /// 표기하는가**는 순수 로직이라 여기로 분리해 EditMode 테스트로 고정한다
    /// (§5.1 상수를 <c>ServerPulseDriver</c>로 분리했던 것과 같은 원칙).
    ///
    /// UnityEngine에 의존하지 않는다 — <c>InGameHud</c>가 이 결과를 <c>Text.text</c>에 넣기만 한다.
    /// </summary>
    public static class HudFormatter
    {
        /// <summary>§12.4 밸브 카운트에 쓰는 기호. 기획서 도식의 "⚙ 0/3" 표기를 그대로 따른다.</summary>
        public const string ValveGlyph = "⚙";

        /// <summary>
        /// 남은 시간을 <c>M:SS</c>로 표기한다(§6.2 제한시간 표시).
        /// 음수는 0으로 클램프하고, 초는 <b>올림</b>해 "0:01"이 1초간 보이도록 한다 —
        /// 내림으로 하면 마지막 1초가 "0:00"으로 표시돼 이미 끝난 것처럼 보인다.
        /// </summary>
        public static string FormatRemainingTime(float remainingSeconds)
        {
            if (remainingSeconds < 0f)
                remainingSeconds = 0f;

            int totalSeconds = (int)System.Math.Ceiling(remainingSeconds);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes}:{seconds:00}";
        }

        /// <summary>§12.4 "밸브 카운트 | 좌상단, 소형 | 상시" — 도식의 `⚙ 0/3` 형식.</summary>
        public static string FormatValveCount(int openedCount, int totalValves)
        {
            if (openedCount < 0)
                openedCount = 0;
            if (totalValves < 0)
                totalValves = 0;

            return $"{ValveGlyph} {openedCount}/{totalValves}";
        }

        /// <summary>
        /// 역할 표시 문자열(§3 용어집의 한국어 명칭을 그대로 쓴다).
        /// 기획서 §12.4에 역할 HUD 요소가 명시되지 않아 표기 자체가 GAP-26 결정이다.
        /// </summary>
        public static string FormatRole(RoleType role)
        {
            switch (role)
            {
                case RoleType.Seeker: return "술래";
                case RoleType.Echo: return "메아리";
                default: return "도망자";
            }
        }

        /// <summary>
        /// §6.1 배수로 게이트 상태 안내. 밸브가 전부 열리면 탈출이 열렸음을 알린다 —
        /// 지금까지 Console 로그로만 보이던 이정표(스프린트 5)를 화면으로 옮긴 것이다.
        /// 아직 안 열렸으면 빈 문자열(표시 없음).
        /// </summary>
        public static string FormatGateHint(bool gateOpen)
        {
            return gateOpen ? "배수로 게이트 개방 — 탈출 가능" : string.Empty;
        }

        /// <summary>
        /// 라운드 결과 배너 문구(§6.3 · §12.5 "승패 배너"). 진행 중이면 빈 문자열이라
        /// 아무것도 그리지 않는다. 스프린트 17부터 결과 화면(<c>ResultScreen</c>)도 이 문구를 쓴다.
        /// </summary>
        public static string FormatRoundResult(RoundResult result)
        {
            switch (result)
            {
                case RoundResult.RunnersWin: return "도망자 승리";
                case RoundResult.SeekerWin: return "술래 승리";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 승패 <b>사유</b>를 §6.3 판정식에서 역으로 유도한다(스프린트 17).
        ///
        /// **새 상태를 저장하지 않는다** — 판정식이 이미 사유를 결정론적으로 함의하기 때문이다:
        /// <code>
        /// RunnersWin  → 밸브 전부 + 1인 이상 탈출  (§6.3 첫 분기는 이것뿐)
        /// SeekerWin   → allRunnersTagged || timeRemaining &lt;= 0
        ///               남은 시간이 0이면 시간 초과, 0보다 크면 전원 태그
        /// </code>
        /// 남은 시간이 0보다 큰 채로 SeekerWin이 되는 경로는 전원 태그뿐이고, 서버 구동기는
        /// 판정이 확정되면 타이머를 멈추므로(<c>ServerRoundDriver.Tick</c>) 확정 시점의 값이
        /// 그대로 남아 있다 — 그래서 이 유도가 성립한다.
        /// </summary>
        public static string FormatResultReason(RoundResult result, float remainingSeconds)
        {
            switch (result)
            {
                case RoundResult.RunnersWin:
                    return "밸브를 모두 열고 배수로로 탈출했다";
                case RoundResult.SeekerWin:
                    return remainingSeconds <= 0f
                        ? "제한시간이 끝났다"
                        : "도망자가 전원 붙잡혔다";
                default:
                    return string.Empty;
            }
        }
    }
}
