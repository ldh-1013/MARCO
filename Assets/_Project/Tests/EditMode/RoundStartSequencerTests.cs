using System.Collections.Generic;
using NUnit.Framework;
using Marco.Core.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [긴급 수정] 로비 카운트다운 무한 루프 — <see cref="RoundStartSequencer"/>.
    /// 전원 준비 → 3초 → StartRound <b>1회</b> → 맵 준비 → 브리핑 30초(리셋 없음) → 라운드 시작(InGame).
    /// </summary>
    public class RoundStartSequencerTests
    {
        private const int MinPlayers = 2;

        // 1/64초 — 이진수로 정확해서 누적 오차 없이 틱 수를 셀 수 있다(3초 = 192틱, 30초 = 1920틱).
        private const float Dt = 1f / 64f;
        private const float Eps = 1e-4f;

        /// <summary>
        /// 서버(<c>RoundNetworkSync</c>)의 페이즈 분기와 부수 효과를 그대로 옮긴 시험대.
        /// Lobby 틱은 <c>TickLobby</c>, RoleAssign 틱은 <c>TickCountdown</c>과 같은 순서로 처리한다.
        /// </summary>
        private sealed class ServerFlow
        {
            public readonly ServerLobbyDriver Lobby = new ServerLobbyDriver(MinPlayers);
            public readonly RoundStartSequencer Start;

            public GameFlowState Phase = GameFlowState.Lobby;
            public int RoundNumber = -1;                 // OnStartServer와 같다
            public int StartRounds, Briefings, BeginRounds, Aborts;
            public int Players = 2, Ready = 2;
            public float MapLoadSeconds = 5f;            // 요청 후 준비까지(카운트다운 3초보다 길게)
            public bool MapLoaded;
            public float Now;
            public float BriefingStartedAt = -1f, InGameAt = -1f;

            private float _mapRequestedAt = -1f;

            public ServerFlow() { Start = new RoundStartSequencer(Lobby); }

            private bool MapReady =>
                MapLoaded || (_mapRequestedAt >= 0f && Now - _mapRequestedAt >= MapLoadSeconds - Eps);

            public RoundStartEvents Tick(float dt)
            {
                Now += dt;
                switch (Phase)
                {
                    case GameFlowState.Lobby:
                        if (Lobby.Tick(Players, Ready, dt) == LobbyTickResult.CountdownStarted)
                            Phase = GameFlowState.RoleAssign;
                        return RoundStartEvents.None;

                    case GameFlowState.RoleAssign:
                        RoundStartEvents ev = Start.Tick(Players, Ready, MapReady, dt);
                        if ((ev & RoundStartEvents.Aborted) != 0)
                        {
                            Phase = GameFlowState.Lobby;
                            Aborts++;
                            return ev;
                        }
                        if ((ev & RoundStartEvents.StartRound) != 0)
                        {
                            RoundNumber++;
                            StartRounds++;
                            if (_mapRequestedAt < 0f)
                                _mapRequestedAt = Now; // ServerLoadMap — 이미 요청·로드됐으면 무동작
                        }
                        if ((ev & RoundStartEvents.BriefingStarted) != 0)
                        {
                            Briefings++;
                            BriefingStartedAt = Now;
                        }
                        if ((ev & RoundStartEvents.BeginRound) != 0)
                        {
                            BeginRounds++;
                            InGameAt = Now;
                            Phase = GameFlowState.InGame;
                        }
                        return ev;

                    default:
                        return RoundStartEvents.None;
                }
            }

            /// <summary>리매치 가결(<c>TickVote</c> Passed): ServerResetWorld → 카운트다운 직접 진입.</summary>
            public void RematchPassed()
            {
                Start.Reset();
                Lobby.BeginCountdown();
                Phase = GameFlowState.RoleAssign;
            }

            public void RunUntilInGame(int maxTicks = 100000)
            {
                for (int i = 0; i < maxTicks && Phase != GameFlowState.InGame; i++)
                    Tick(Dt);
            }
        }

        // ── 요청 검증 1: 전 구간 ───────────────────────────────────────────

        [Test]
        public void FullSequence_BriefingRunsThirtyToZero_WithoutReset_ThenInGame()
        {
            var f = new ServerFlow();
            float lastBriefing = float.MaxValue;
            int briefingTicks = 0;

            for (int i = 0; i < 100000 && f.Phase != GameFlowState.InGame; i++)
            {
                f.Tick(Dt);
                if (!f.Start.Briefing)
                    continue;

                // 브리핑 타이머는 여러 프레임이 지나도 리셋되지 않는다 — 매 틱 정확히 dt만큼만 준다.
                float now = f.Start.BriefingRemaining;
                if (briefingTicks > 0)
                    Assert.AreEqual(lastBriefing - Dt, now, Eps, $"브리핑 {briefingTicks}틱째 타이머가 리셋·정지됐다");
                Assert.LessOrEqual(now, BriefingConfig.Seconds);
                lastBriefing = now;
                briefingTicks++;
            }

            Assert.AreEqual(GameFlowState.InGame, f.Phase);
            Assert.AreEqual(1, f.StartRounds, "StartRound는 라운드당 1회");
            Assert.AreEqual(1, f.Briefings, "브리핑은 라운드당 1회");
            Assert.AreEqual(1, f.BeginRounds);
            Assert.AreEqual(0, f.RoundNumber, "첫 라운드 = 0번(§2.3 로테이션 기준)");

            // 30초 → 0: 시작 틱에 이미 dt 1회 차감되므로 (시작 틱 ~ 종료 틱) = 30 − dt.
            Assert.AreEqual(BriefingConfig.Seconds - Dt, f.InGameAt - f.BriefingStartedAt, Eps);
            // 전체 = 로비 틱 1 + 카운트다운 3 + 맵 로드 5 + 브리핑 30.
            Assert.AreEqual(Dt + ServerLobbyDriver.CountdownSeconds + f.MapLoadSeconds + BriefingConfig.Seconds - Dt,
                f.InGameAt, Eps);
            Assert.AreEqual(0f, f.Start.BriefingRemaining);
            Assert.IsFalse(f.Start.Briefing);
            Assert.IsFalse(f.Start.WaitingForMap);
        }

        [Test]
        public void Briefing_OneTickLeft_StillRoleAssign_NextTickBeginRound()
        {
            var f = new ServerFlow { MapLoaded = true };
            f.Lobby.BeginCountdown();
            f.Phase = GameFlowState.RoleAssign;

            // 카운트다운 192틱(3초) 끝 틱에 StartRound + 브리핑 시작(맵이 이미 있으므로 같은 틱).
            for (int i = 0; i < 192; i++)
                f.Tick(Dt);
            Assert.AreEqual(1, f.StartRounds);
            Assert.IsTrue(f.Start.Briefing);
            Assert.AreEqual(BriefingConfig.Seconds - Dt, f.Start.BriefingRemaining, Eps);

            for (int i = 0; i < 1918; i++)
                f.Tick(Dt);
            Assert.AreEqual(GameFlowState.RoleAssign, f.Phase, "잔여 1틱 — 아직 브리핑");
            Assert.AreEqual(Dt, f.Start.BriefingRemaining, Eps);

            RoundStartEvents last = f.Tick(Dt);
            Assert.AreEqual(RoundStartEvents.BeginRound, last);
            Assert.AreEqual(GameFlowState.InGame, f.Phase);
        }

        // ── 요청 검증 2: 라운드 번호는 라운드당 정확히 1 ────────────────────

        [Test]
        public void RoundNumber_IncrementsExactlyOncePerRound_AcrossRematches()
        {
            var f = new ServerFlow();
            f.RunUntilInGame();
            Assert.AreEqual(0, f.RoundNumber);

            // 리매치 가결 두 번 — 맵은 이미 올라와 있다(부결이 아니면 내리지 않는다).
            for (int round = 1; round <= 2; round++)
            {
                f.RematchPassed();
                f.RunUntilInGame();
                Assert.AreEqual(GameFlowState.InGame, f.Phase);
                Assert.AreEqual(round, f.RoundNumber, $"{round + 1}번째 라운드");
            }

            Assert.AreEqual(3, f.StartRounds);
            Assert.AreEqual(3, f.Briefings);
            Assert.AreEqual(3, f.BeginRounds);
        }

        [Test]
        public void MapWait_LongerThanCountdown_NoSecondStartRound()
        {
            // 맵 로드가 카운트다운(3초)보다 오래 걸려도 StartRound는 다시 나오지 않는다 —
            // 브리핑 이전 구조에서도 로드 3초 초과면 같은 반복이 났을 경로.
            var f = new ServerFlow { MapLoadSeconds = 60f };
            for (int i = 0; i < 64 * 50; i++) // 50초
                f.Tick(Dt);

            Assert.AreEqual(GameFlowState.RoleAssign, f.Phase);
            Assert.IsTrue(f.Start.WaitingForMap);
            Assert.AreEqual(1, f.StartRounds);
            Assert.AreEqual(0, f.RoundNumber);
            Assert.AreEqual(0, f.Briefings);
            Assert.IsFalse(f.Lobby.CountdownActive, "로비 드라이버는 다시 무장되지 않는다");
        }

        [Test]
        public void BugReproduction_PreFixOrder_RestartsEveryCountdownPlusOneTick()
        {
            // 수정 전 TickCountdown 순서를 그대로 옮긴 재현(제품 코드가 아니다 — 버그 확인용).
            // 맵은 이미 준비(리매치·빠른 로드). 120초 동안 전원 준비 유지.
            var lobby = new ServerLobbyDriver(MinPlayers);
            Assert.AreEqual(LobbyTickResult.CountdownStarted, lobby.Tick(2, 2, Dt)); // TickLobby

            int roundNumber = -1;
            bool waitingForMap = false, briefing = false, inGame = false;
            float briefingRemaining = 0f;
            var startTicks = new List<int>();

            for (int tick = 1; tick <= 64 * 120; tick++)
            {
                LobbyTickResult result = lobby.Tick(2, 2, Dt);          // ← 가드 없음
                if (result == LobbyTickResult.StartRound)
                {
                    roundNumber++;
                    waitingForMap = true;
                    startTicks.Add(tick);
                }
                if (waitingForMap)                                      // MapReadyForRound() == true
                {
                    waitingForMap = false;
                    briefing = true;
                    briefingRemaining = BriefingConfig.Seconds;         // BeginBriefing — 30초로 리셋
                }
                if (briefing)
                {
                    briefingRemaining -= Dt;
                    if (briefingRemaining <= 0f) { inGame = true; break; }
                }
            }

            int countdownTicks = (int)(ServerLobbyDriver.CountdownSeconds / Dt); // 192
            Assert.IsFalse(inGame, "수정 전: 브리핑이 3초마다 30초로 리셋돼 라운드가 끝내 시작되지 않는다");
            Assert.AreEqual(countdownTicks, startTicks[0], "첫 StartRound = 카운트다운 3초");
            for (int i = 1; i < startTicks.Count; i++)
                Assert.AreEqual(countdownTicks + 1, startTicks[i] - startTicks[i - 1],
                    "반복 주기 = 3초 + 1틱(StartRound 다음 틱에 전원 준비를 새 카운트다운으로 받는다)");

            // 120초 = 7680틱 → StartRound 틱 192 + 193k ≤ 7680 → 39회 → 라운드 번호 0..38.
            Assert.AreEqual(39, startTicks.Count);
            Assert.AreEqual(38, roundNumber);

            // 같은 조건에서 수정 후 순서기는 1회.
            var f = new ServerFlow { MapLoaded = true };
            f.Tick(Dt); // TickLobby
            for (int tick = 1; tick <= 64 * 120; tick++)
                f.Tick(Dt);
            Assert.AreEqual(1, f.StartRounds);
            Assert.AreEqual(0, f.RoundNumber);
            Assert.AreEqual(GameFlowState.InGame, f.Phase);
        }

        // ── Aborted(GAP-29) 재검토 · GAP-104 ───────────────────────────────

        [Test]
        public void Countdown_StillAbortsOnLeave_Gap29()
        {
            var f = new ServerFlow();
            f.Tick(Dt);                                   // 로비 → 카운트다운
            for (int i = 0; i < 100; i++)
                f.Tick(Dt);
            Assert.IsTrue(f.Start.InCountdown);

            f.Players = 1; f.Ready = 1;                   // 이탈 → 2인 미만
            RoundStartEvents ev = f.Tick(Dt);

            Assert.AreEqual(RoundStartEvents.Aborted, ev);
            Assert.AreEqual(GameFlowState.Lobby, f.Phase);
            Assert.AreEqual(0, f.StartRounds);
            Assert.AreEqual(-1, f.RoundNumber, "중단 시 되돌릴 배정·번호가 없다");
        }

        [Test]
        public void Countdown_AbortsOnUnreadyJoiner_Gap29()
        {
            var f = new ServerFlow();
            f.Tick(Dt);
            f.Tick(Dt);
            f.Players = 3;                                // 신규 접속자(미준비)
            Assert.AreEqual(RoundStartEvents.Aborted, f.Tick(Dt));
            Assert.AreEqual(GameFlowState.Lobby, f.Phase);
        }

        [Test]
        public void MapWaitAndBriefing_LeaveOrJoin_DoesNotAbort_Gap104()
        {
            var f = new ServerFlow();
            f.Tick(Dt);
            for (int i = 0; i < 192; i++)
                f.Tick(Dt);
            Assert.IsTrue(f.Start.WaitingForMap);

            f.Players = 1; f.Ready = 1;                   // 맵 대기 중 이탈
            for (int i = 0; i < 64 * 6; i++)   // 맵 준비(요청 후 5초)를 넘긴다
                Assert.AreEqual(0, (int)(f.Tick(Dt) & RoundStartEvents.Aborted));
            Assert.IsTrue(f.Start.Briefing, "맵 대기 → 브리핑으로 계속 진행");

            f.Players = 3; f.Ready = 1;                   // 브리핑 중 합류(미준비)
            f.RunUntilInGame();

            Assert.AreEqual(GameFlowState.InGame, f.Phase);
            Assert.AreEqual(0, f.Aborts);
            Assert.AreEqual(1, f.StartRounds);
            Assert.AreEqual(1, f.Briefings);
        }

        // ── 부가 계약 ──────────────────────────────────────────────────────

        [Test]
        public void MapAlreadyLoaded_StartRoundAndBriefingOnSameTick()
        {
            var f = new ServerFlow { MapLoaded = true };
            f.RematchPassed();
            RoundStartEvents ev = RoundStartEvents.None;
            for (int i = 0; i < 192; i++)
                ev = f.Tick(Dt);

            Assert.AreEqual(RoundStartEvents.StartRound | RoundStartEvents.BriefingStarted, ev);
        }

        [Test]
        public void Reset_ClearsMapWaitAndBriefing_KeepsLobbyDriver()
        {
            var lobby = new ServerLobbyDriver(MinPlayers);
            var s = new RoundStartSequencer(lobby);
            lobby.BeginCountdown();
            s.Tick(2, 2, mapReady: true, 3f);             // StartRound + 브리핑
            Assert.IsTrue(s.Briefing);

            s.Reset();

            Assert.IsFalse(s.Briefing);
            Assert.IsFalse(s.WaitingForMap);
            Assert.AreEqual(0f, s.BriefingRemaining);
            Assert.IsTrue(s.InCountdown);
            Assert.IsFalse(lobby.CountdownActive);
        }

        [Test]
        public void Constructor_RejectsNullLobby()
        {
            Assert.Throws<System.ArgumentNullException>(() => new RoundStartSequencer(null));
        }
    }
}
