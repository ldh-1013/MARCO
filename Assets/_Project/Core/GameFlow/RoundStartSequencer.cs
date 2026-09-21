using System;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// <see cref="RoundStartSequencer.Tick"/> 한 틱에 일어난 일. 한 틱에 둘 이상 겹칠 수 있다 —
    /// 맵이 이미 올라와 있으면(리매치) <see cref="StartRound"/>와 <see cref="BriefingStarted"/>가 같은 틱이다.
    /// 호출자는 선언 순서대로 처리한다.
    /// </summary>
    [Flags]
    public enum RoundStartEvents
    {
        None = 0,

        /// <summary>카운트다운 중 조건이 깨졌다(GAP-29) → 로비로. 이 값이 서면 다른 값은 없다.</summary>
        Aborted = 1 << 0,

        /// <summary>카운트다운 완료 — 라운드 번호 · 역할 배정 · 맵 로드 요청. <b>라운드당 1회.</b></summary>
        StartRound = 1 << 1,

        /// <summary>맵 준비 완료 → §12.4 로비 브리핑 30초 시작(활성 밸브 확정). 라운드당 1회.</summary>
        BriefingStarted = 1 << 2,

        /// <summary>브리핑 종료 → 라운드 시작(InGame). 라운드당 1회.</summary>
        BeginRound = 1 << 3
    }

    /// <summary>
    /// RoleAssign 페이즈의 서버 순서기: <b>카운트다운 → 맵 로드 대기 → 브리핑 → 라운드 시작</b>.
    /// FishNet도 UnityEngine도 모른다 — EditMode 테스트 가능(<see cref="ServerLobbyDriver"/>와 같은 구조).
    ///
    /// <para>
    /// <b>[긴급 수정] 로비 드라이버는 카운트다운 단계에서만 틱한다.</b> <see cref="ServerLobbyDriver"/>의 역할은
    /// "전원 준비 → 3초 → <see cref="LobbyTickResult.StartRound"/> 1회"까지다. 그 드라이버는 StartRound 뒤에도
    /// 계속 틱하면 전원 준비 상태를 새 카운트다운으로 받아 3초마다 StartRound를 다시 낸다. 예전 서버 코드는
    /// 맵 대기·브리핑 중(페이즈는 계속 RoleAssign)에도 매 틱 드라이버를 불러 <b>3초 + 1틱마다 라운드 번호 증가 ·
    /// 역할 재배정 · 브리핑 30초 재시작</b>이 반복됐고 라운드가 끝내 시작되지 않았다(실기 로그 확인).
    /// </para>
    ///
    /// <para>
    /// <b>GAP-104</b>: 카운트다운이 끝난 뒤(역할 배정 · 맵 로드 요청 이후)의 이탈·합류는 로비로 되돌리지 않는다 —
    /// §12.3·§12.4·§15.4 어디에도 "브리핑 중 취소" 규칙이 없고, §15.4 RoleAssign의 종료 조건은 "연출 종료"뿐이다.
    /// 이탈·합류는 InGame과 같은 규칙으로 처리된다(합류자는 라운드 시작 후 러너로 채움, 술래 이탈 시 재추첨 없음).
    /// 준비 토글은 원래 Lobby 페이즈에서만 받으므로 "준비 해제"는 이 구간에서 일어날 수 없다.
    /// </para>
    /// </summary>
    public sealed class RoundStartSequencer
    {
        private readonly ServerLobbyDriver _lobby;

        public RoundStartSequencer(ServerLobbyDriver lobby)
        {
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
        }

        /// <summary>카운트다운이 끝났고 맵이 준비되기를 기다리는 중(GAP-30 — 페이즈는 RoleAssign 유지).</summary>
        public bool WaitingForMap { get; private set; }

        /// <summary>§12.4 로비 브리핑 진행 중.</summary>
        public bool Briefing { get; private set; }

        /// <summary>브리핑 잔여 초(서버 권위). 브리핑 중이 아니면 0.</summary>
        public float BriefingRemaining { get; private set; }

        /// <summary>로비 드라이버가 관여하는 단계인가 — 맵 대기·브리핑 중에는 아니다.</summary>
        public bool InCountdown => !WaitingForMap && !Briefing;

        /// <summary>
        /// RoleAssign 페이즈의 서버 틱. <paramref name="playerCount"/>·<paramref name="readyCount"/>는
        /// 카운트다운 단계에서만 쓰인다. <paramref name="mapReady"/>는 이번 틱의 맵 준비 여부.
        /// </summary>
        public RoundStartEvents Tick(int playerCount, int readyCount, bool mapReady, float deltaSeconds)
        {
            RoundStartEvents events = RoundStartEvents.None;

            if (InCountdown)
            {
                switch (_lobby.Tick(playerCount, readyCount, deltaSeconds))
                {
                    case LobbyTickResult.Aborted:
                        return RoundStartEvents.Aborted;

                    case LobbyTickResult.StartRound:
                        WaitingForMap = true;
                        events |= RoundStartEvents.StartRound;
                        break;
                }
            }

            // 맵 로드 대기 중이면 준비되는 틱에 브리핑을 시작한다.
            if (WaitingForMap && mapReady)
            {
                WaitingForMap = false;
                Briefing = true;
                BriefingRemaining = BriefingConfig.Seconds;
                events |= RoundStartEvents.BriefingStarted;
            }

            // §12.4 로비 브리핑 30초 — 끝나면 라운드 시작("라운드 시작 시 사라진다").
            if (Briefing)
            {
                BriefingRemaining -= deltaSeconds;
                if (BriefingRemaining <= 0f)
                {
                    Briefing = false;
                    BriefingRemaining = 0f;
                    events |= RoundStartEvents.BeginRound;
                }
            }

            return events;
        }

        /// <summary>리매치 경계(가결·부결)에서 맵 대기·브리핑 상태를 지운다. 로비 드라이버는 건드리지 않는다.</summary>
        public void Reset()
        {
            WaitingForMap = false;
            Briefing = false;
            BriefingRemaining = 0f;
        }
    }
}
