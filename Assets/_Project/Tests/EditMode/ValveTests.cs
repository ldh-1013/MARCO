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
        public void Interrupt_BeforeCompletion_KeepsProgressForDecay()
        {
            // ★ v0.4에서 의미가 뒤집혔다. v0.3은 "중단 시 진행도 0 리셋"이었지만
            //   §6.1 [v0.4]는 **진행도를 유지하고 3초 유예 뒤 감쇠**시킨다.
            //   그 변경의 이유가 §6.1에 있다 — 리셋 규칙에서는 일단 잡으면 끝까지
            //   돌리는 것이 항상 최적이라 회전 중에 아무 판단도 하지 않는다.
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(2.9f);
            float before = valve.Progress01;

            bool interrupted = valve.Interrupt(RunnerA);

            Assert.IsTrue(interrupted);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(before, valve.Progress01, 0.001f, "중단은 리셋이 아니다(§6.1 [v0.4]).");
            Assert.IsTrue(valve.HasPartialProgress);
            Assert.IsFalse(valve.IsDecaying, "유예 3초 안에는 아직 깎이지 않는다.");
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
        public void AfterInterrupt_AnotherRunnerResumesFromRemainingProgress()
        {
            // ★ v0.4에서 의미가 뒤집혔다. §6.1 "다른 도망자가 즉시 이어받을 수 있다" —
            //   0부터가 아니라 **남은 진행도에서** 이어진다. 이것이 릴레이 플레이의 근거다.
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.Tick(1f);
            valve.Interrupt(RunnerA);
            float handoff = valve.Progress01;

            bool started = valve.TryBeginRotation(RunnerB, RoleType.Runner);

            Assert.IsTrue(started);
            Assert.AreEqual(ValveState.Rotating, valve.State);
            Assert.AreEqual(RunnerB, valve.InteractorId);
            Assert.AreEqual(handoff, valve.Progress01, 0.001f, "이어받기는 그 시점 값에서다.");
            Assert.Greater(handoff, 0f);
        }

        // 9) 회전 중인 밸브를 다른 플레이어가 가로챌 수 없다.
        [Test]
        public void TryBeginRotation_WhileAnotherIsRotating_JoinsAsConcurrentWork()
        {
            // ★ v0.4에서 의미가 뒤집혔다. §6.1이 **동시 작업**을 정의한다 —
            //   2인 ×1.6 / 3인 ×1.9(상한). 가로채기 거부가 아니라 합류다.
            var valve = new Valve();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            bool started = valve.TryBeginRotation(RunnerB, RoleType.Runner);

            Assert.IsTrue(started, "§6.1 동시 작업 — 두 번째 도망자는 합류한다.");
            Assert.AreEqual(2, valve.InteractorCount);
            Assert.IsTrue(valve.IsInteracting(RunnerA));
            Assert.IsTrue(valve.IsInteracting(RunnerB));
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

        // 16) [블록 7 의미 변경] v0.3 "§6.2 6인 구간 회전 3.75초(+25%)"는 v0.4 §6.2가 **폐기**했다
        //     ("6인 구간의 회전시간 +25% 보정을 폐기했다 — 회전이 전 구간 8초로 균등"). 상수(3.75)를
        //     지우고, 같은 모양의 검증을 v0.4 밸브(E 7.0초)로 옮겼다 — 회전 시간 전부를 채워야 열린다.
        [Test]
        public void ValveE_RequiresFullRotation()
        {
            var valve = new Valve(ValveOccupancy.RotateSeconds(ValveId.E));
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            valve.Tick(6.99f);
            Assert.AreEqual(ValveState.Rotating, valve.State, "6.99초로는 E가 열리면 안 된다");

            valve.Tick(0.01f);
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
            float before = valve.Progress01;

            // Net 레이어가 이탈한 플레이어 ID로 Interrupt를 호출하는 시나리오(§13.x).
            bool handled = valve.Interrupt(RunnerA);

            Assert.IsTrue(handled);
            Assert.AreEqual(ValveState.Closed, valve.State);
            // v0.4: 연결 끊김도 **중단**이므로 진행도는 감쇠 대상으로 남는다.
            Assert.AreEqual(before, valve.Progress01, 0.001f);
        }

        // ── §6.1 밸브별 회전 시간 차등 [기획서 갱신] ──────────────────────

        [Test]
        public void RotationSeconds_MatchDesignDocTable()
        {
            // §6.1 [v0.4] 배분: A 회전 8.0 / B 회전 5.0 / C 회전 8.0 / D 8.0 / E 7.0
            // [블록 7] Valve의 중복 상수 5개를 지워 정본(ValveOccupancy) 하나로 검증한다.
            Assert.AreEqual(8f, ValveOccupancy.RotateSeconds(ValveId.A));
            Assert.AreEqual(5f, ValveOccupancy.RotateSeconds(ValveId.B));
            Assert.AreEqual(8f, ValveOccupancy.RotateSeconds(ValveId.C));
            Assert.AreEqual(8f, ValveOccupancy.RotateSeconds(ValveId.D));
            Assert.AreEqual(7f, ValveOccupancy.RotateSeconds(ValveId.E));
        }

        [Test]
        public void ValveA_MatchesLegacyDefault()
        {
            // ★ v0.4에서 의미가 뒤집혔다. v0.3에서는 밸브 A(3.0초)가 폴백
            //   DefaultRotationSeconds(3.0초)와 같은 값이었지만, §6.1 [v0.4]가 A를
            //   8.0초로 올리면서 둘이 갈라졌다. **폴백은 그대로 3.0초로 남긴다** —
            //   기존 씬 프리팹과 v0.3 테스트가 참조하고 있고, v0.4 밸브는
            //   ValveOccupancy.RotateSeconds로 자기 시간을 갖기 때문이다.
            Assert.AreEqual(3f, Valve.DefaultRotationSeconds, "폴백은 v0.3 값을 유지한다.");
            Assert.AreEqual(8f, ValveOccupancy.RotateSeconds(ValveId.A), "§6.1 [v0.4] 밸브 A 회전 8.0초.");
            Assert.AreNotEqual(Valve.DefaultRotationSeconds, ValveOccupancy.RotateSeconds(ValveId.A));
        }

        [Test]
        public void ValveB_FitsWithinBreathGaugeBudget()
        {
            // §6.1 [v0.4] 밸브 B 총 점유 = 진입 1.5 + 회전 5.0 + 부상 1.5 = 8.0초.
            // §5.9-1 숨 게이지 14초(10-02 — 10-01 20초, v0.4 12초)이므로 **한 숨에 끝난다**(8.0 < 14.0).
            float total = ValveOccupancy.TotalSeconds(ValveId.B);

            Assert.AreEqual(1.5f, ValveOccupancy.EntrySeconds(ValveId.B), 0.0001f);
            Assert.AreEqual(5f, ValveOccupancy.RotateSeconds(ValveId.B), 0.0001f);
            Assert.AreEqual(1.5f, ValveOccupancy.SurfaceSeconds(ValveId.B), 0.0001f);
            Assert.AreEqual(8f, total, 0.0001f, "1.5 + 5.0 + 1.5 = 8.0 (§6.1 [v0.4])");

            Assert.Less(total, Breath.BreathConfig.TotalSeconds,
                "수중 밸브 B는 한 숨 안에 끝나야 §6.1-1 타임라인이 성립한다.");

            // v0.4(12초)가 지목한 경계 — 8초를 다 쓰면 잔여 4.0초로 억제(4.5)가 불가능했다. 10-01(20초)은 잔여 12.
            // 10-02 총량 14초 → 잔여 6.0초: 억제는 한 번 가능, B · E를 쉬지 않고 이어서는 못 연다(UnderwaterWorkSessionTests).
            Assert.AreEqual(6f, Breath.BreathConfig.TotalSeconds - total, 0.0001f);
            Assert.GreaterOrEqual(Breath.BreathConfig.TotalSeconds - total, Breath.BreathConfig.SuppressionCost,
                "수중 밸브 직후 비명 억제 1회는 가능하다(6.0 ≥ 4.5).");
            Assert.Less(Breath.BreathConfig.TotalSeconds - total, total, "잔여로 수중 밸브를 한 번 더 할 수는 없다(6.0 < 8.0).");
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
            // ★ v0.4에서 전제가 바뀌었다. v0.3은 "회전 시간 자체를 차등화"했지만
            //   §6.1 [v0.4]는 **총 점유를 8.0초로 균등**하게 맞추고 배분만 다르게 한다 —
            //   비용 격차가 크면 도망자가 항상 싼 밸브만 골라 죽은 밸브가 생기기 때문이다.
            //   그래서 "B가 A보다 빠르다"는 이제 회전 구간에만 해당하고,
            //   총 점유로는 완전히 같다.
            Assert.Less(ValveOccupancy.RotateSeconds(ValveId.B), ValveOccupancy.RotateSeconds(ValveId.A),
                "회전 구간은 B(5.0)가 A(8.0)보다 짧다 — 대신 진입·부상 3.0초가 붙는다.");
            Assert.AreEqual(ValveOccupancy.RotateSeconds(ValveId.C), ValveOccupancy.RotateSeconds(ValveId.A),
                "C는 A와 같은 8.0초다 — 차이는 2층 이동 비용으로 만든다(§6.1).");

            Assert.IsTrue(ValveOccupancy.AllTotalsEqual(out float total),
                "§6.1 [v0.4] 총 점유는 전 밸브 균등해야 한다.");
            Assert.AreEqual(8f, total, 0.0001f);
        }
    }
}
