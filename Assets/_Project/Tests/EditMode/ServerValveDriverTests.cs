using NUnit.Framework;
using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 10: 서버 권위 밸브 구동기의 계약을 고정한다.
    ///
    /// **가장 중요한 것은 "클라이언트가 밸브를 즉시 열 수 없다"**이다(§5.3).
    /// 클라이언트는 홀드 의사만 보내고, 개방은 오직 서버의 <see cref="ServerValveDriver.Tick"/>가
    /// 회전 시간을 모두 진전시켜야만 일어난다. 이 성질이 서버 권위 신뢰 모델의 근간이다.
    ///
    /// 역할 제약(GAP-5)·진행도 리셋(§6.1)은 Core <see cref="Valve"/>가 이미 강제하며
    /// (ValveTests가 담당), 여기서는 구동기가 그것을 올바르게 재사용하는지 + 홀더 추적만 본다.
    /// </summary>
    public class ServerValveDriverTests
    {
        private const ulong Runner = 1;
        private const ulong OtherRunner = 2;

        private static ServerValveDriver NewDriver(float rotationSeconds = Valve.DefaultRotationSeconds)
            => new ServerValveDriver(new Valve(rotationSeconds));

        // 1) 러너의 홀드 요청 → 회전 시작, 홀더 기록.
        [Test]
        public void BeginHold_ByRunner_StartsRotating()
        {
            var driver = NewDriver();

            bool accepted = driver.BeginHold(Runner, RoleType.Runner);

            Assert.IsTrue(accepted);
            Assert.AreEqual(ValveState.Rotating, driver.State);
            Assert.AreEqual(Runner, driver.HolderId);
        }

        // 2) GAP-5: 메아리 요청은 서버가 거부한다(Core Valve 재사용).
        [Test]
        public void BeginHold_ByEcho_IsRejected()
        {
            var driver = NewDriver();

            bool accepted = driver.BeginHold(Runner, RoleType.Echo);

            Assert.IsFalse(accepted);
            Assert.AreEqual(ValveState.Closed, driver.State);
            Assert.IsNull(driver.HolderId);
        }

        // 3) **핵심(서버 권위)**: 클라이언트가 홀드 요청을 아무리 반복해도,
        //    서버가 Tick으로 시간을 진전시키지 않으면 밸브는 절대 열리지 않는다.
        [Test]
        public void RepeatedBeginHold_WithoutServerTick_NeverOpens()
        {
            var driver = NewDriver();

            for (int i = 0; i < 1000; i++)
            {
                driver.BeginHold(Runner, RoleType.Runner);
                // Tick 호출 없음 — 악의적 클라이언트가 요청만 폭주시키는 상황을 모사.
            }

            Assert.AreEqual(ValveState.Rotating, driver.State, "요청만으로는 회전 시작까지만 가능");
            Assert.AreNotEqual(ValveState.Open, driver.State, "서버 Tick 없이는 개방 불가");
            Assert.AreEqual(0f, driver.Progress01, 0.0001f, "진행도는 서버 시간에만 의존");
        }

        // 4) 서버가 회전 시간을 모두 진전시키면 개방되고, 완료 신호는 정확히 1회.
        [Test]
        public void ServerTick_ForFullDuration_OpensExactlyOnce()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);

            int openedSignals = 0;
            // 0.1초씩 넉넉히 진전(3초 + 여유). 완료 이후 Tick은 false여야 한다.
            for (int i = 0; i < 50; i++)
            {
                if (driver.Tick(0.1f))
                    openedSignals++;
            }

            Assert.AreEqual(ValveState.Open, driver.State);
            Assert.AreEqual(1, openedSignals, "개방 완료 신호는 정확히 한 번만 발생해야 한다");
        }

        // 5) 회전 중 다른 플레이어의 요청은 거부되고 홀더는 바뀌지 않는다(단일 홀더).
        [Test]
        public void SecondPlayer_WhileHeld_IsRejected()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);

            bool accepted = driver.BeginHold(OtherRunner, RoleType.Runner);

            Assert.IsFalse(accepted);
            Assert.AreEqual(Runner, driver.HolderId, "홀더는 최초 시작자 그대로");
        }

        // 6) 프레임마다 오는 같은 홀더의 반복 요청은 회전을 리셋하지 않는다.
        [Test]
        public void RepeatedBeginHold_BySameHolder_DoesNotResetProgress()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);
            driver.Tick(1.5f); // 절반 진행
            float mid = driver.Progress01;
            Assert.Greater(mid, 0.4f);

            bool stillHeld = driver.BeginHold(Runner, RoleType.Runner); // 같은 프레임 신호 재수신

            Assert.IsTrue(stillHeld);
            Assert.AreEqual(mid, driver.Progress01, 0.0001f, "재요청이 진행도를 되돌리면 안 된다");
        }

        // 7) 홀더의 해제는 §6.1대로 진행도를 0으로 리셋한다.
        [Test]
        public void EndHold_ByHolder_ResetsProgress()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);
            driver.Tick(2.0f);
            Assert.Greater(driver.Progress01, 0.5f);

            driver.EndHold(Runner);

            Assert.AreEqual(ValveState.Closed, driver.State);
            Assert.AreEqual(0f, driver.Progress01, 0.0001f);
            Assert.IsNull(driver.HolderId);
        }

        // 8) 홀더가 아닌 자의 해제 요청은 무시된다(남의 회전을 못 끊는다).
        [Test]
        public void EndHold_ByNonHolder_IsIgnored()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);
            driver.Tick(1.0f);

            driver.EndHold(OtherRunner);

            Assert.AreEqual(ValveState.Rotating, driver.State, "남이 내 회전을 끊을 수 없다");
            Assert.AreEqual(Runner, driver.HolderId);
        }

        // 9) 취소 후 다시 홀드하면 0부터 시작한다(§6.1 부분 진행 저장 없음).
        [Test]
        public void Rehold_AfterCancel_BeginsFromZero()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);
            driver.Tick(2.0f);
            driver.EndHold(Runner);

            bool accepted = driver.BeginHold(Runner, RoleType.Runner);

            Assert.IsTrue(accepted);
            Assert.Less(driver.Progress01, 0.05f, "이어받기가 아니라 0부터 다시 시작");
        }

        // 10) 이미 열린 밸브는 재홀드를 거부한다.
        [Test]
        public void BeginHold_WhenOpen_IsRejected()
        {
            var driver = NewDriver();
            driver.BeginHold(Runner, RoleType.Runner);
            for (int i = 0; i < 50 && driver.State != ValveState.Open; i++)
                driver.Tick(0.1f);
            Assert.AreEqual(ValveState.Open, driver.State);

            bool accepted = driver.BeginHold(OtherRunner, RoleType.Runner);

            Assert.IsFalse(accepted);
            Assert.AreEqual(ValveState.Open, driver.State);
        }

        // 11) 홀드 전 Tick은 아무 일도 하지 않는다(IsRotating 가드).
        [Test]
        public void Tick_BeforeHold_DoesNothing()
        {
            var driver = NewDriver();

            bool opened = driver.Tick(5f);

            Assert.IsFalse(opened);
            Assert.AreEqual(ValveState.Closed, driver.State);
            Assert.IsFalse(driver.IsRotating);
        }

        // 12) 술래도 밸브를 돌릴 수 있다 — 금지된 것은 메아리뿐(GAP-5).
        [Test]
        public void BeginHold_BySeeker_IsAllowed()
        {
            var driver = NewDriver();

            bool accepted = driver.BeginHold(Runner, RoleType.Seeker);

            Assert.IsTrue(accepted);
            Assert.AreEqual(ValveState.Rotating, driver.State);
        }
    }
}
