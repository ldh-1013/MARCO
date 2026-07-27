using NUnit.Framework;
using Marco.Core.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 18: 로비 게이트(<see cref="ServerLobbyDriver"/>)의 계약을 고정한다.
    /// 핵심은 "전원 준비 전엔 라운드 시작 금지"와 §12.3/§15.4의 3초 카운트다운,
    /// 그리고 GAP-29(카운트다운 중 조건 붕괴 시 즉시 중단)다.
    /// </summary>
    public class ServerLobbyDriverTests
    {
        private const int MinPlayers = 2; // §1 최소 인원 = RoleAssigner.MinimumPlayers

        [Test]
        public void CountdownSeconds_IsThree_PerDesignDoc()
        {
            // §12.3 "전원 Ready 시 3초 카운트다운" = §15.4 RoleAssign "3초 연출".
            Assert.AreEqual(3f, ServerLobbyDriver.CountdownSeconds);
        }

        [Test]
        public void BelowMinimumPlayers_NeverStarts_EvenIfAllReady()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            // 1명뿐이고 그 1명이 준비 완료여도 시작하지 않는다(§1 최소 2인).
            Assert.AreEqual(LobbyTickResult.Waiting, d.Tick(playerCount: 1, readyCount: 1, 0.1f));
            Assert.IsFalse(d.CountdownActive);
        }

        [Test]
        public void NotAllReady_Waits()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            Assert.AreEqual(LobbyTickResult.Waiting, d.Tick(2, 1, 0.1f));
            Assert.AreEqual(LobbyTickResult.Waiting, d.Tick(4, 3, 0.1f));
        }

        [Test]
        public void AllReady_StartsCountdown_Once()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            Assert.AreEqual(LobbyTickResult.CountdownStarted, d.Tick(2, 2, 0.1f));
            Assert.IsTrue(d.CountdownActive);
            Assert.AreEqual(ServerLobbyDriver.CountdownSeconds, d.CountdownRemaining);
            // 이후 틱은 진행 상태다(다시 Started를 반환하지 않는다).
            Assert.AreEqual(LobbyTickResult.CountingDown, d.Tick(2, 2, 0.1f));
        }

        [Test]
        public void Countdown_CompletesAfterThreeSeconds_ReturnsStartRoundOnce()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            d.Tick(2, 2, 0f); // 시작

            Assert.AreEqual(LobbyTickResult.CountingDown, d.Tick(2, 2, 1.5f));
            Assert.AreEqual(LobbyTickResult.StartRound, d.Tick(2, 2, 1.6f)); // 누적 3.1초
            Assert.IsFalse(d.CountdownActive, "시작 신호 후 카운트다운은 종료 상태여야 한다");

            // 시작 신호는 1회만 — 다음 틱은 (전원 준비면) 새 카운트다운 시작으로 취급된다.
            Assert.AreEqual(LobbyTickResult.CountdownStarted, d.Tick(2, 2, 0.1f));
        }

        [Test]
        public void UnreadyDuringCountdown_Aborts_Gap29()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            d.Tick(2, 2, 0f);
            Assert.AreEqual(LobbyTickResult.Aborted, d.Tick(2, 1, 0.5f)); // 한 명이 준비 해제
            Assert.IsFalse(d.CountdownActive);
        }

        [Test]
        public void PlayerLeaveDuringCountdown_BelowMinimum_Aborts_Gap29()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            d.Tick(2, 2, 0f);
            Assert.AreEqual(LobbyTickResult.Aborted, d.Tick(1, 1, 0.5f)); // 이탈로 2인 미만
        }

        [Test]
        public void NewPlayerJoinDuringCountdown_NotReady_Aborts_Gap29()
        {
            // 카운트다운 중 새 플레이어 접속(미준비) → 전원 준비가 깨져 중단.
            // 신규 접속자도 준비해야 시작된다는 보수적 규칙.
            var d = new ServerLobbyDriver(MinPlayers);
            d.Tick(2, 2, 0f);
            Assert.AreEqual(LobbyTickResult.Aborted, d.Tick(3, 2, 0.5f));
        }

        [Test]
        public void AbortThenAllReadyAgain_RestartsFullCountdown()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            d.Tick(2, 2, 0f);
            d.Tick(2, 2, 2.9f);            // 거의 다 됨
            d.Tick(2, 1, 0.05f);           // 중단
            d.Tick(2, 2, 0f);              // 재시작
            Assert.AreEqual(ServerLobbyDriver.CountdownSeconds, d.CountdownRemaining,
                "중단 후 재시작은 3초 전체를 다시 센다(부분 이어가기 없음)");
        }

        [Test]
        public void BeginCountdown_EntersCountdownDirectly_ForRematchPath()
        {
            // §15.4 가결 경로: RoundEnd → RoleAssign(Lobby를 거치지 않음) —
            // 전원 준비 확인 없이 직접 카운트다운에 진입한다(준비 상태는 라운드 중 잠겨 있었다).
            var d = new ServerLobbyDriver(MinPlayers);
            d.BeginCountdown();
            Assert.IsTrue(d.CountdownActive);
            Assert.AreEqual(ServerLobbyDriver.CountdownSeconds, d.CountdownRemaining);
            Assert.AreEqual(LobbyTickResult.StartRound, d.Tick(2, 2, 3.1f));
        }

        [Test]
        public void ResetForLobby_ClearsCountdown()
        {
            var d = new ServerLobbyDriver(MinPlayers);
            d.BeginCountdown();
            d.ResetForLobby();
            Assert.IsFalse(d.CountdownActive);
            Assert.AreEqual(0f, d.CountdownRemaining);
        }

        [Test]
        public void SoloOverride_MinimumOne_AllowsSinglePlayerStart()
        {
            // 솔로 테스트용 인스펙터 오버라이드(_minPlayers=1) 경로 — 혼자 준비하면 시작된다.
            var d = new ServerLobbyDriver(1);
            Assert.AreEqual(LobbyTickResult.CountdownStarted, d.Tick(1, 1, 0f));
        }

        [Test]
        public void ZeroPlayers_Waits_NoVacuousStart()
        {
            var d = new ServerLobbyDriver(1);
            Assert.AreEqual(LobbyTickResult.Waiting, d.Tick(0, 0, 0.1f),
                "0명에서 '전원 준비'가 공허하게 참이 되면 안 된다");
        }
    }
}
