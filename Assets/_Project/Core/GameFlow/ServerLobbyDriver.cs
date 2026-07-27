namespace Marco.Core.GameFlow
{
    /// <summary><see cref="ServerLobbyDriver.Tick"/>의 프레임별 판정 결과.</summary>
    public enum LobbyTickResult
    {
        /// <summary>아직 시작 조건 미충족(인원 부족 또는 전원 준비 아님).</summary>
        Waiting,

        /// <summary>이번 틱에 전원 준비가 성립해 §12.3 카운트다운이 시작됨 → 페이즈 RoleAssign 전이.</summary>
        CountdownStarted,

        /// <summary>카운트다운 진행 중.</summary>
        CountingDown,

        /// <summary>카운트다운 중 조건이 깨져(이탈/준비 해제) 로비로 되돌아감(GAP-29).</summary>
        Aborted,

        /// <summary>카운트다운 완료 — 라운드를 시작하라(1회만 반환).</summary>
        StartRound
    }

    /// <summary>
    /// 서버 권위 로비 게이트(스프린트 18). "전원 준비 전엔 라운드 시작 금지"를 서버 한 곳에서
    /// 판정한다. FishNet도 UnityEngine도 모른다 — EditMode 테스트 가능
    /// (<c>ServerValveDriver</c>·<c>ServerRoundDriver</c>와 같은 구조).
    ///
    /// **§12.3 / §15.4 사양 그대로**:
    /// - "전원 Ready 시 3초 카운트다운 후 역할 추첨 연출로 전환"(§12.3 준비 버튼 행)
    /// - Lobby[전원 Ready] → RoleAssign[3초 연출, 역할 배정] → InGame (§15.4 표)
    /// 카운트다운 3초가 곧 RoleAssign 페이즈의 길이다 — 별도 상수를 만들지 않고 표를 그대로 옮겼다.
    ///
    /// **GAP-29**: 카운트다운 중 준비 해제·이탈 시의 처리가 기획서에 없다 →
    /// 조건이 깨지는 즉시 카운트다운을 중단하고 로비로 되돌린다(보수적 해석 — 전원 합의가
    /// 유지되는 동안만 진행). 리매치 가결 경로는 <see cref="BeginCountdown"/>으로 직접
    /// 카운트다운에 진입한다(§15.4 RoundEnd → RoleAssign, Lobby를 다시 거치지 않음).
    /// </summary>
    public sealed class ServerLobbyDriver
    {
        /// <summary>§12.3 "3초 카운트다운" = §15.4 RoleAssign "3초 연출".</summary>
        public const float CountdownSeconds = 3f;

        private readonly int _minPlayers;

        public ServerLobbyDriver(int minPlayers)
        {
            _minPlayers = minPlayers < 1 ? 1 : minPlayers;
        }

        public bool CountdownActive { get; private set; }
        public float CountdownRemaining { get; private set; }

        /// <summary>
        /// 매 서버 틱 호출. <paramref name="playerCount"/>·<paramref name="readyCount"/>는
        /// 호출 시점의 실측 스냅샷이어야 한다(플레이어 이탈이 자동 반영되도록 목록을 저장하지 않는다).
        /// </summary>
        public LobbyTickResult Tick(int playerCount, int readyCount, float deltaSeconds)
        {
            bool allReady = playerCount >= _minPlayers && readyCount >= playerCount;

            if (!CountdownActive)
            {
                if (!allReady)
                    return LobbyTickResult.Waiting;

                BeginCountdown();
                return LobbyTickResult.CountdownStarted;
            }

            // GAP-29: 카운트다운 중 조건이 깨지면 즉시 중단.
            if (!allReady)
            {
                CountdownActive = false;
                CountdownRemaining = 0f;
                return LobbyTickResult.Aborted;
            }

            CountdownRemaining -= deltaSeconds;
            if (CountdownRemaining > 0f)
                return LobbyTickResult.CountingDown;

            CountdownActive = false;
            CountdownRemaining = 0f;
            return LobbyTickResult.StartRound;
        }

        /// <summary>
        /// 카운트다운을 직접 시작한다. 리매치 가결 경로(§15.4 RoundEnd → RoleAssign — Lobby를
        /// 거치지 않는 즉시 재시작)에서 쓴다. 이후 진행·중단 판정은 <see cref="Tick"/>과 동일하다.
        /// </summary>
        public void BeginCountdown()
        {
            CountdownActive = true;
            CountdownRemaining = CountdownSeconds;
        }

        /// <summary>로비로 되돌아갈 때(부결·중단) 카운트다운 상태를 지운다.</summary>
        public void ResetForLobby()
        {
            CountdownActive = false;
            CountdownRemaining = 0f;
        }
    }
}
