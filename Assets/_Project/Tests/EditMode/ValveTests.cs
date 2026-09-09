using NUnit.Framework;
using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Core.Tests
{
    /// <summary>
    /// T3 수용 기준(docs/phase-1-분석.md §3): "3초 홀드 → Open, 중단 시 진행도 0 리셋".
    /// §6.1 상태기계 · §6.4 연결 끊김 처리 · GAP-5(메아리 밸브 조작 불가)를 고정한다.
    /// </summary>
    public class ValveTests
    {
        private const ulong RunnerA = 10;
        private const ulong RunnerB = 20;

        // 1) 초기 상태: Closed, 진행도 0, 상호작용자 없음.
        [Test]
        public void NewValve_StartsClosedWithNoProgress()
        {
            var valve = new Valve();

            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.001f);
            Assert.IsNull(valve.InteractorId);
        }

        // 2) 러너가 회전 시작 → Rotating 전이, 상호작용자 기록.
        [Test]
        public void TryBeginRotation_ByRunner_EntersRotating()
        {
            var valve = new Valve();

            bool started = valve.TryBeginRotation(RunnerA, RoleType.Runner);

            Assert.IsTrue(started);
            Assert.AreEqual(ValveState.Rotating, valve.State);
            Assert.AreEqual(RunnerA, valve.InteractorId);
        }

        // 3) GAP-5: 메아리는 밸브를 돌릴 수 없다.
        [Test]
        public void TryBeginRotation_ByEcho_IsRejected()
        {
            var valve = new Valve();

            bool started = valve.TryBeginRotation(RunnerA, RoleType.Echo);

            Assert.IsFalse(started);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.IsNull(valve.InteractorId);
        }

        // 4) §6.2 4인 기준 3초를 채우면 Open.
        [Test]
        public void Tick_ForFullRotationDuration_Opens()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            valve.Tick(Valve.DefaultRotationSeconds);

            Assert.AreEqual(ValveState.Open, valve.State);
            Assert.AreEqual(1f, valve.Progress01, 0.001f);
            Assert.IsNull(valve.InteractorId);
        }

        // 5) 3초 미만에서는 아직 Rotating이고 진행도가 비례한다.
        [Test]
        public void Tick_PartialDuration_StaysRotatingWithProportionalProgress()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            valve.Tick(1.5f); // 3초 중 절반

            Assert.AreEqual(ValveState.Rotating, valve.State);
            Assert.AreEqual(0.5f, valve.Progress01, 0.001f);
        }

        // 6) §6.1 핵심: 완료 전 중단 시 진행도가 0으로 리셋된다(부분 진행 저장 없음).
        [Test]
        public void Interrupt_BeforeCompletion_ResetsProgressToZero()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(2.9f); // 거의 다 돌렸어도

            bool interrupted = valve.Interrupt(RunnerA);

            Assert.IsTrue(interrupted);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.001f);
            Assert.IsNull(valve.InteractorId);
        }

        // 7) 남이 돌리고 있는 밸브를 제3자가 중단시킬 수 없다.
        [Test]
        public void Interrupt_ByNonInteractor_IsIgnored()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(1f);

            bool interrupted = valve.Interrupt(RunnerB);

            Assert.IsFalse(interrupted);
            Assert.AreEqual(ValveState.Rotating, valve.State);
            Assert.AreEqual(RunnerA, valve.InteractorId);
        }

        // 8) §6.4: 중단 직후 다른 생존 도망자가 즉시 이어서 시작할 수 있다(밸브 잠금 없음).
        [Test]
        public void AfterInterrupt_AnotherRunnerCanStartImmediately()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(1f);
            valve.Interrupt(RunnerA);

            bool started = valve.TryBeginRotation(RunnerB, RoleType.Runner);

            Assert.IsTrue(started);
            Assert.AreEqual(ValveState.Rotating, valve.State);
            Assert.AreEqual(RunnerB, valve.InteractorId);
            Assert.AreEqual(0f, valve.Progress01, 0.001f); // 이어받기가 아니라 처음부터
        }

        // 9) 회전 중인 밸브를 다른 플레이어가 가로챌 수 없다.
        [Test]
        public void TryBeginRotation_WhileAnotherIsRotating_IsRejected()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            bool started = valve.TryBeginRotation(RunnerB, RoleType.Runner);

            Assert.IsFalse(started);
            Assert.AreEqual(RunnerA, valve.InteractorId);
        }

        // 10) 이미 열린 밸브는 다시 돌릴 수 없다.
        [Test]
        public void TryBeginRotation_WhenAlreadyOpen_IsRejected()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(Valve.DefaultRotationSeconds);

            bool started = valve.TryBeginRotation(RunnerB, RoleType.Runner);

            Assert.IsFalse(started);
            Assert.AreEqual(ValveState.Open, valve.State);
        }

        // 11) 열린 뒤에는 Tick이 더 와도 Opened가 다시 발생하지 않는다.
        [Test]
        public void Opened_FiresExactlyOnce()
        {
            var valve = new Valve();
            int openedCount = 0;
            valve.Opened += _ => openedCount++;

            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(Valve.DefaultRotationSeconds);
            valve.Tick(1f); // 추가 Tick
            valve.Tick(1f);

            Assert.AreEqual(1, openedCount);
        }

        // 12) §6.1: 회전 시작 시점에 소음 이벤트가 1회 발생한다(Net 레이어가 여기서 펄스 발행).
        [Test]
        public void RotationStarted_FiresOnSuccessfulBeginOnly()
        {
            var valve = new Valve();
            int startedCount = 0;
            valve.RotationStarted += _ => startedCount++;

            valve.TryBeginRotation(RunnerA, RoleType.Runner);   // 성공 → 1회
            valve.TryBeginRotation(RunnerB, RoleType.Runner);   // 거부 → 발생 안 함
            valve.TryBeginRotation(RunnerB, RoleType.Echo);     // 거부 → 발생 안 함

            Assert.AreEqual(1, startedCount);
        }

        // 13) §6.1: 중단해도 이미 발생한 소음은 취소되지 않는다 —
        //     즉 중단 시 별도 이벤트가 없고, 재시작하면 소음이 새로 한 번 더 발생한다.
        [Test]
        public void RotationStarted_FiresAgainAfterInterruptAndRestart()
        {
            var valve = new Valve();
            int startedCount = 0;
            valve.RotationStarted += _ => startedCount++;

            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Interrupt(RunnerA);
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            Assert.AreEqual(2, startedCount);
        }

        // 14) Closed 상태에서 Tick이 와도 진행도가 생기지 않는다.
        [Test]
        public void Tick_WhileClosed_DoesNothing()
        {
            var valve = new Valve();

            valve.Tick(10f);

            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.001f);
        }

        // 15) 지속시간을 크게 초과하는 Tick이 와도 진행도는 1을 넘지 않는다.
        [Test]
        public void Tick_Overshoot_ClampsProgressToOne()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            valve.Tick(100f);

            Assert.AreEqual(ValveState.Open, valve.State);
            Assert.AreEqual(1f, valve.Progress01, 0.001f);
        }

        // 16) §6.2 6인 구간: 회전 시간 3.75초가 적용되며, 3초로는 아직 안 열린다.
        [Test]
        public void SixPlayerValve_RequiresLongerRotation()
        {
            var valve = new Valve(Valve.SixPlayerRotationSeconds);
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            valve.Tick(3f);
            Assert.AreEqual(ValveState.Rotating, valve.State, "3초로는 6인 밸브가 열리면 안 된다");

            valve.Tick(0.75f);
            Assert.AreEqual(ValveState.Open, valve.State);
        }

        // 17) §6.4 연결 끊김: 상호작용자 이탈은 Interrupt와 동일하게 처리되어
        //     밸브가 즉시 Closed로 돌아간다.
        [Test]
        public void Disconnect_HandledAsInterrupt()
        {
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(2f);

            // Net 레이어가 이탈한 플레이어 ID로 Interrupt를 호출하는 시나리오
            bool handled = valve.Interrupt(RunnerA);

            Assert.IsTrue(handled);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.001f);
        }

        // ── §6.1 밸브별 회전 시간 차등 [기획서 갱신] ──────────────────────

        [Test]
        public void RotationSeconds_MatchDesignDocTable()
        {
            // §6.1 표: A 기계실 3.0초 / B 풀 수중 2.0초 / C 물탱크실(2층) 4.0초
            Assert.AreEqual(3f, Valve.ValveARotationSeconds);
            Assert.AreEqual(2f, Valve.ValveBRotationSeconds);
            Assert.AreEqual(4f, Valve.ValveCRotationSeconds);
        }

        [Test]
        public void ValveA_MatchesLegacyDefault()
        {
            // 밸브 A는 기존 단일 상수와 같은 값이라 이번 변경으로 동작이 바뀌지 않는다.
            Assert.AreEqual(Valve.DefaultRotationSeconds, Valve.ValveARotationSeconds);
        }

        [Test]
        public void ValveB_FitsWithinBreathGaugeBudget()
        {
            // §6.1-1: 진입 1.0 + 회전 2.0 + 부상 1.0 = 4.0초, 숨 게이지 8초의 정확히 50%.
            const float descend = 1f, ascend = 1f, gauge = 8f;
            float total = descend + Valve.ValveBRotationSeconds + ascend;

            Assert.AreEqual(4f, total, 0.001f);
            Assert.AreEqual(gauge / 2f, total, 0.001f, "밸브 B가 한 숨에 끝나지 않으면 §6.1-1 타임라인이 무너진다.");
        }

        [TestCase(3f)]  // A
        [TestCase(2f)]  // B
        [TestCase(4f)]  // C
        public void EachValve_CompletesAtItsOwnRotationTime(float rotationSeconds)
        {
            var valve = new Valve(rotationSeconds);
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            // 자기 회전 시간 직전까지는 열리지 않는다.
            valve.Tick(rotationSeconds - 0.01f);
            Assert.AreEqual(ValveState.Rotating, valve.State);

            valve.Tick(0.01f);
            Assert.AreEqual(ValveState.Open, valve.State);
        }

        [Test]
        public void ValveB_OpensFasterThanA_AndCSlower()
        {
            // 차등화의 방향성 자체를 고정한다 — 값이 뒤집히면 §6.1 리스크 설계가 무너진다.
            Assert.Less(Valve.ValveBRotationSeconds, Valve.ValveARotationSeconds);
            Assert.Greater(Valve.ValveCRotationSeconds, Valve.ValveARotationSeconds);
        }
    }
}
