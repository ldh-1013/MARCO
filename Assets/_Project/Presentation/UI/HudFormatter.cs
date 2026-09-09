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

        // ── 스프린트 18: 로비(§12.3) · 리매치 투표(§12.5) ─────────────────

        /// <summary>§12.3 하단 준비 표시 문구 그대로 — "준비완료 (2/4)".</summary>
        public static string FormatReadyCount(int readyCount, int totalPlayers)
        {
            if (readyCount < 0) readyCount = 0;
            if (totalPlayers < 0) totalPlayers = 0;
            return $"준비완료 ({readyCount}/{totalPlayers})";
        }

        /// <summary>
        /// 로비 플레이어 한 줄. §12.3 아바타 그리드의 텍스트 대응 —
        /// 채워진 원(●)=준비, 빈 원(○)=대기, 자기 자신은 "(나)" 표기.
        /// </summary>
        public static string FormatPlayerRow(ulong playerId, bool isReady, bool isSelf)
        {
            string mark = isReady ? "●" : "○";
            string state = isReady ? "준비" : "대기";
            string self = isSelf ? " (나)" : string.Empty;
            return $"{mark} P{playerId} — {state}{self}";
        }

        /// <summary>§12.3 "3초 카운트다운" 표시. 타이머와 같은 이유로 올림(마지막 1초가 0으로 보이지 않게).</summary>
        public static string FormatLobbyCountdown(float remainingSeconds)
        {
            if (remainingSeconds < 0f)
                remainingSeconds = 0f;
            return $"시작까지 {(int)System.Math.Ceiling(remainingSeconds)}초";
        }

        /// <summary>§12.5 리매치 투표 상태 — "리매치? (2/3 찬성) · 12초" 형식(도식 문구 준용).</summary>
        public static string FormatRematchVote(int votesFor, int votesNeeded, float remainingSeconds)
        {
            if (votesFor < 0) votesFor = 0;
            if (votesNeeded < 0) votesNeeded = 0;
            if (remainingSeconds < 0f) remainingSeconds = 0f;
            return $"리매치? ({votesFor}/{votesNeeded} 찬성) · {(int)System.Math.Ceiling(remainingSeconds)}초";
        }

        /// <summary>
        /// §12.3 상단 "방코드" 줄. GAP-28: MVP는 Tugboat LAN 직결이라 4자리 코드 발급 체계가 없다 —
        /// 접속 주소가 방코드의 역할을 대신하며, Steam 단계에서 §12.2 코드 매칭으로 대체된다.
        /// </summary>
        public static string FormatRoomCode(string address)
        {
            return $"방코드: {(string.IsNullOrWhiteSpace(address) ? "-" : address)}";
        }

        /// <summary>
        /// 승패 <b>사유</b>를 §6.3 판정식에서 역으로 유도한다(스프린트 17).
        ///
        /// **새 상태를 저장하지 않는다** — 판정식이 이미 사유를 결정론적으로 함의하기 때문이다:
        /// <code>
        /// RunnersWin  → 밸브 전부 + 탈출 2명 이상  (§6.3 첫 분기는 이것뿐)
        /// SeekerWin   → 태그 2명 도달 || timeRemaining &lt;= 0
        ///               남은 시간이 0이면 시간 초과, 0보다 크면 태그 2명 도달
        /// </code>
        /// 남은 시간이 0보다 큰 채로 SeekerWin이 되는 경로는 태그 2명 도달뿐이고, 서버 구동기는
        /// 판정이 확정되면 타이머를 멈추므로(<c>ServerRoundDriver.Tick</c>) 확정 시점의 값이
        /// 그대로 남아 있다 — 그래서 이 유도가 성립한다.
        /// </summary>
        /// <summary>
        /// §12.5 어워드 카드 한 장. 수상자가 없으면(-1) 그 사실을 밝힌다 — 빈칸으로 두면
        /// "집계가 고장났다"와 "조건을 만족한 사람이 없다"를 구분할 수 없다.
        ///
        /// 플레이어 이름 체계가 아직 없어 id로 표시한다(§12.3 도식의 닉네임은 미구현 — GAP-41).
        /// </summary>
        public static string FormatAward(string awardName, int winnerPlayerId)
        {
            string winner = winnerPlayerId < 0 ? "수상자 없음" : $"플레이어 {winnerPlayerId}";
            return $"{awardName}\n{winner}";
        }

        public static string FormatResultReason(RoundResult result, float remainingSeconds)
        {
            switch (result)
            {
                case RoundResult.RunnersWin:
                    return $"밸브를 모두 열고 {WinConditionEvaluator.EscapeWinThreshold}명이 배수로로 탈출했다";
                case RoundResult.SeekerWin:
                    return remainingSeconds <= 0f
                        ? "제한시간이 끝났다"
                        : $"도망자 {WinConditionEvaluator.TagWinThreshold}명이 붙잡혔다";
                default:
                    return string.Empty;
            }
        }
    }
}
