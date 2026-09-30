using Marco.Core.Net;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 접속 시도 상태(09-30 실기 결함 — 참가가 실패하면 로비 UI가 꺼진 채 빈 월드만 남고 Esc도 먹지 않았다).
    /// 시도 중 → 실패 → 메뉴, 10초 시간 초과, 취소, 재시도를 순수 로직으로 고정한다. <c>ConnectionService</c>가 전송 계층 사건을 넣는다.
    /// </summary>
    public class JoinAttemptTests
    {
        private static readonly JoinAddress Target = new JoinAddress("127.0.0.1", 7999);

        [Test]
        public void TimeoutIsTenSeconds()
        {
            Assert.AreEqual(10f, JoinAttempt.TimeoutSeconds);
        }

        [Test]
        public void Connecting_ShowsTargetElapsedSecondsAndCancelHint()
        {
            var a = new JoinAttempt();
            Assert.IsTrue(a.BeginJoin(Target, now: 100f));

            Assert.AreEqual(JoinAttemptState.Connecting, a.State);
            Assert.AreEqual("127.0.0.1:7999에 접속 중… (3초)", a.ProgressLine(103.7f));
            Assert.AreEqual("Esc — 취소", JoinAttempt.CancelHint);
            Assert.IsFalse(a.ShouldReturnToMenu);
        }

        [Test]
        public void TransportStoppedBeforeConnecting_Fails_WithThreeCauses_ReturnsToMenu()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);
            a.OnTransportStopped(); // Tugboat 자체 포기(포트 무응답 ≈ 5.9초 · 이름 풀이 실패 0.1초)

            Assert.AreEqual(JoinAttemptState.Failed, a.State);
            Assert.IsTrue(a.ShouldReturnToMenu, "시도 중 → 실패 → 메뉴");
            StringAssert.Contains("127.0.0.1:7999", a.FailureMessage);
            StringAssert.Contains("오타", a.FailureMessage);
            StringAssert.Contains("호스트가 아직 방을 만들지 않음", a.FailureMessage);
            StringAssert.Contains("방화벽", a.FailureMessage);

            a.GetMenuNotice(out string text, out bool isError);
            Assert.AreEqual(a.FailureMessage, text);
            Assert.IsTrue(isError, "빨간 안내");
            Assert.AreEqual("127.0.0.1:7999", a.RetryText, "메뉴에서 J를 누르면 직전 주소가 채워진다");
        }

        [Test]
        public void NoConnectionWithinTenSeconds_TimesOut_OnlyOnce()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);

            Assert.IsFalse(a.Tick(109.9f), "10초 전에는 기다린다");
            Assert.AreEqual(JoinAttemptState.Connecting, a.State);

            Assert.IsTrue(a.Tick(110f), "10초 — 실패로 확정하고 호출자가 전송 계층을 내린다");
            Assert.AreEqual(JoinAttemptState.Failed, a.State);
            Assert.IsTrue(a.TimedOut);
            StringAssert.Contains("10초", a.FailureMessage);
            StringAssert.Contains("방화벽", a.FailureMessage);

            string message = a.FailureMessage;
            a.OnTransportStopped(); // 내려서 생긴 Stopped — 이미 확정된 실패라 바뀌지 않는다(기존 재시도 경로와 겹치지 않음)
            Assert.AreEqual(message, a.FailureMessage);
            Assert.IsFalse(a.Tick(200f), "두 번 확정하지 않는다");
        }

        [Test]
        public void Cancel_DuringConnecting_ReturnsToMenu_WithoutError()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);

            Assert.IsTrue(a.Cancel());
            Assert.AreEqual(JoinAttemptState.Cancelled, a.State);
            Assert.IsTrue(a.ShouldReturnToMenu);

            a.OnTransportStopped(); // 취소로 내린 Stopped — 실패로 바뀌지 않는다
            Assert.AreEqual(JoinAttemptState.Cancelled, a.State);

            a.GetMenuNotice(out string text, out bool isError);
            StringAssert.Contains("취소", text);
            Assert.IsFalse(isError, "취소는 오류가 아니다(회색)");
            Assert.AreEqual("127.0.0.1:7999", a.RetryText);
        }

        [Test]
        public void Cancel_WhenNotConnecting_DoesNothing()
        {
            var a = new JoinAttempt();
            Assert.IsFalse(a.Cancel());

            a.BeginJoin(Target, 100f);
            a.OnTransportStarted();
            Assert.IsFalse(a.Cancel(), "이미 연결됐으면 취소가 아니다");
            Assert.AreEqual(JoinAttemptState.Connected, a.State);
        }

        [Test]
        public void Connected_NoTimeout_NoMenu()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);
            a.OnTransportStarted();

            Assert.AreEqual(JoinAttemptState.Connected, a.State);
            Assert.IsFalse(a.Tick(500f), "연결된 뒤에는 시간 초과가 없다");
            Assert.IsFalse(a.ShouldReturnToMenu);
        }

        [Test]
        public void ConnectedThenStopped_IsDisconnect_ReturnsToMenu()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);
            a.OnTransportStarted();
            a.OnTransportStopped(); // 호스트가 나갔다 — 전에는 이 경우도 UI 없이 남았다

            Assert.AreEqual(JoinAttemptState.Failed, a.State);
            Assert.IsTrue(a.ShouldReturnToMenu);
            StringAssert.Contains("끊겼", a.FailureMessage);
        }

        [Test]
        public void Retry_AfterFailure_StartsFresh()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);
            a.Tick(110f); // 시간 초과로 실패

            var next = new JoinAddress("abc.gl.at.ply.gg", 48123);
            Assert.IsTrue(a.BeginJoin(next, now: 300f), "실패 뒤에는 다시 시작할 수 있다");
            Assert.AreEqual(JoinAttemptState.Connecting, a.State);
            Assert.IsNull(a.FailureMessage);
            Assert.IsFalse(a.TimedOut);
            Assert.AreEqual("abc.gl.at.ply.gg:48123에 접속 중… (0초)", a.ProgressLine(300.4f));
            Assert.IsFalse(a.Tick(305f), "타이머도 새로 시작한다");
        }

        [Test]
        public void Retry_AfterCancel_StartsFresh()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);
            a.Cancel();

            Assert.IsTrue(a.BeginJoin(Target, 200f));
            Assert.AreEqual(JoinAttemptState.Connecting, a.State);
        }

        [Test]
        public void BeginWhileConnecting_IsIgnored()
        {
            var a = new JoinAttempt();
            a.BeginJoin(Target, 100f);
            Assert.IsFalse(a.BeginJoin(new JoinAddress("other", 7770), 101f));
            Assert.AreEqual(Target, a.Target);
        }

        [Test]
        public void Host_ProgressAndFailure_NoRetryAddress()
        {
            var a = new JoinAttempt();
            Assert.IsTrue(a.BeginHost(7780, 100f));
            Assert.IsTrue(a.IsHost);
            Assert.AreEqual("방을 여는 중… (1초)", a.ProgressLine(101.2f));

            a.OnTransportStopped(); // 포트 사용 중 등으로 열지 못함
            Assert.AreEqual(JoinAttemptState.Failed, a.State);
            StringAssert.Contains("7780", a.FailureMessage);
            Assert.IsNull(a.RetryText, "호스트 시도는 채울 주소가 없다");
        }
    }
}
