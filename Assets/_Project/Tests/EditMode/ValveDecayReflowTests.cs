using Marco.Core.Objectives;
using Marco.Core.Role;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §6.1 진행도 감쇠와 §6.1-2 역류를 고정한다.
    ///
    /// <para>
    /// <b>이 파일의 중심은 "둘이 서로 간섭하지 않는다"</b>다. 감쇠는 <i>회전 중이던</i>
    /// 밸브의 진행도에만, 역류는 <i>이미 열린</i> 밸브의 유지 시간에만 적용된다.
    /// 한 타이머로 합치면 "개방 직후 감쇠가 시작되는" 버그가 조용히 생긴다.
    /// </para>
    /// </summary>
    public class ValveDecayReflowTests
    {
        private const ulong RunnerA = 11;
        private const ulong RunnerB = 22;

        /// <summary>§6.1 [v0.4] 밸브 A — 회전 8.0초.</summary>
        private static Valve NewValveA()
        {
            var valve = new Valve(ValveOccupancy.RotateSeconds(ValveId.A));
            valve.Configure(ValveId.A);
            return valve;
        }

        private static void Advance(Valve valve, float seconds, float step = 0.05f)
        {
            int steps = (int)(seconds / step + 0.5f);
            for (int i = 0; i < steps; i++)
                valve.Tick(step);
        }

        // ── §6.1 상수 ────────────────────────────────────────────────────

        [Test]
        public void DecayConstants_MatchDesignDoc()
        {
            Assert.AreEqual(3f, Valve.DecayGraceSeconds, "§6.1 '작업 중단 3초 유예'");
            Assert.AreEqual(0.10f, Valve.DecayPerSecond, "§6.1 '이후 -0.10/sec'");

            // §6.1 "만충(1.0)에서 유예 포함 13초 만에 전손된다" — 유도값이다.
            Assert.AreEqual(13f, Valve.FullDecaySeconds, 0.0001f);
        }

        [Test]
        public void ReflowConstants_MatchDesignDoc()
        {
            Assert.AreEqual(180f, Valve.OpenHoldSeconds, "§6.1-2 개방 유지 180초");
            Assert.AreEqual(30f, Valve.ReflowSeconds, "§6.1-2 역류 진행 30초");
            Assert.AreEqual(10f, Valve.ReflowPulseIntervalSeconds, "§6.1-2 '10초마다 1회'");
            Assert.AreEqual(3, Valve.ReflowPulseCount, "§6.1-2 '총 3회' — 30 ÷ 10에서 유도");
        }

        [Test]
        public void TotalOccupancy_IsUniformEightSeconds()
        {
            // §6.1 [v0.4] "총 점유가 전부 8.0으로 같고 배분만 다르다. 이게 v0.4의 핵심이다".
            Assert.IsTrue(ValveOccupancy.AllTotalsEqual(out float total));
            Assert.AreEqual(8f, total, 0.0001f);

            // 경계: 수중 밸브 두 개의 배분 합
            Assert.AreEqual(8f, 1.5f + 5f + 1.5f, 0.0001f);
            Assert.AreEqual(8f, 0.5f + 7f + 0.5f, 0.0001f);

            foreach (ValveId id in ValveOccupancy.All)
                Assert.AreEqual(8f, ValveOccupancy.TotalSeconds(id), 0.0001f, $"밸브 {id}");
        }

        [Test]
        public void ConcurrencyMultiplier_MatchesDesignDoc()
        {
            // §6.1 "1인 +1/8 per sec / 2인 ×1.6 / 3인 ×1.9(상한, 그 이상 무효)"
            Assert.AreEqual(1f, ValveOccupancy.ConcurrencyMultiplier(1), 0.0001f);
            Assert.AreEqual(1.6f, ValveOccupancy.ConcurrencyMultiplier(2), 0.0001f);
            Assert.AreEqual(1.9f, ValveOccupancy.ConcurrencyMultiplier(3), 0.0001f);
            Assert.AreEqual(1.9f, ValveOccupancy.ConcurrencyMultiplier(4), 0.0001f, "상한이다");

            // §6.1 산출: 8.0초 → 2인 5.0초 / 3인 4.2초
            // (8 ÷ 1.9 = 4.2105… 이며 기획서가 4.2로 반올림해 적은 값이다. 상수는 1.9가 정본이므로
            //  유도값을 그대로 비교하고, 기획서 표기와의 차이는 0.02 이내임을 함께 고정한다.)
            Assert.AreEqual(5f, ValveOccupancy.SecondsToComplete(ValveId.A, 2), 0.0001f);
            Assert.AreEqual(8f / 1.9f, ValveOccupancy.SecondsToComplete(ValveId.A, 3), 0.0001f);
            Assert.AreEqual(4.2f, ValveOccupancy.SecondsToComplete(ValveId.A, 3), 0.02f);
        }

        // ── §6.1 감쇠 경계값 ────────────────────────────────────────────

        [Test]
        public void Decay_AtGraceBoundary_HoldsThenStarts()
        {
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 4f);              // 4/8 = 0.5
            valve.Interrupt(RunnerA);
            float held = valve.Progress01;

            // 경계값: t=2.9 진행도 유지. **단일 틱으로 정확히 잰다** —
            // 0.05초씩 누적하면 부동소수 잔차가 3.0 경계를 앞뒤로 흔든다.
            valve.Tick(2.9f);
            Assert.AreEqual(held, valve.Progress01, 0.001f, "유예 3초 안에는 그대로다.");
            Assert.IsFalse(valve.IsDecaying);

            // 경계값: t=3.0에서 감쇠 시작
            valve.Tick(0.1f);
            Assert.IsTrue(valve.IsDecaying, "§6.1 유예 3초가 지나면 감쇠가 시작된다.");
        }

        [Test]
        public void Decay_FromFull_EmptiesAtThirteenSeconds()
        {
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            // 만충 직전까지 돌린다(1.0에 닿으면 Open이 되므로 0.999에서 멈춘다).
            valve.Tick(7.99f);
            Assert.AreEqual(ValveState.Rotating, valve.State);
            valve.Interrupt(RunnerA);
            Assert.Greater(valve.Progress01, 0.99f);

            // 유예 3초 + 감쇠 10초 = 13초
            valve.Tick(12.8f);
            Assert.Greater(valve.Progress01, 0f, "13초 전에는 아직 남아 있다.");

            valve.Tick(0.3f);
            Assert.AreEqual(0f, valve.Progress01, 0.0001f);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.IsFalse(valve.HasPartialProgress);
        }

        [Test]
        public void Decay_ResumedMidway_ContinuesFromRemaining()
        {
            // §6.1 "감쇠 중 재상호작용 시 그 시점 값에서 이어짐(0부터 아님)"
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 6f);                     // 0.75
            valve.Interrupt(RunnerA);
            Advance(valve, 8f);                     // 유예 3 + 감쇠 5초 → 0.75 - 0.5 = 0.25
            float remaining = valve.Progress01;
            Assert.AreEqual(0.25f, remaining, 0.01f);

            Assert.AreEqual(ValveInteractionRejection.None,
                valve.TryInteract(RunnerB, RoleType.Runner));
            Assert.AreEqual(remaining, valve.Progress01, 0.001f, "0부터가 아니다.");
            Assert.IsFalse(valve.IsDecaying, "회전 중에는 감쇠하지 않는다.");
        }

        [Test]
        public void Decay_DoesNotRunWhileOpen()
        {
            // ★ 감쇠와 역류가 서로 간섭하지 않는다는 것의 직접 증명.
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 8f);
            Assert.AreEqual(ValveState.Open, valve.State);

            // 개방 직후 13초 — 감쇠 규칙이 Open에 새어들면 여기서 진행도가 깎인다.
            Advance(valve, 13f);

            Assert.AreEqual(ValveState.Open, valve.State, "역류 180초 전에는 Open 유지다.");
            Assert.AreEqual(1f, valve.Progress01, 0.0001f, "개방된 밸브는 감쇠하지 않는다(§6.1).");
            Assert.IsFalse(valve.IsDecaying);
        }

        // ── §6.1-2 역류 경계값 ──────────────────────────────────────────

        [Test]
        public void Reflow_BoundariesMatchDesignDoc()
        {
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 8f);
            Assert.AreEqual(ValveState.Open, valve.State);

            // 경계값: t=179.9 Open (단일 틱으로 정확히)
            valve.Tick(179.9f);
            Assert.AreEqual(ValveState.Open, valve.State);

            // 경계값: t=180.0 Reflowing
            valve.Tick(0.15f);
            Assert.AreEqual(ValveState.Reflowing, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.0001f,
                "§6.1-2 '회전 시간은 최초와 동일' — 진행도는 0에서 다시 시작한다.");

            // 경계값: t=209.9 Reflowing (역류 시작 후 29.9초)
            valve.Tick(29.7f);
            Assert.AreEqual(ValveState.Reflowing, valve.State);

            // 경계값: t=210.0 Closed
            valve.Tick(0.3f);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.0001f);
        }

        [Test]
        public void Reflow_EmitsThreeWaterPulses()
        {
            // §6.1-2 "역류 구간 중 10초마다 1회, 총 3회"
            var valve = NewValveA();
            int pulses = 0;
            valve.ReflowPulse += _ => pulses++;

            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 8f);
            Advance(valve, Valve.OpenHoldSeconds + 0.1f);
            Assert.AreEqual(1, pulses, "역류 시작 시점에 1회");

            Advance(valve, 10f);
            Assert.AreEqual(2, pulses);

            Advance(valve, 10f);
            Assert.AreEqual(3, pulses);

            Advance(valve, 15f); // 폐쇄까지 지나가도 더 울리지 않는다
            Assert.AreEqual(Valve.ReflowPulseCount, pulses);
            Assert.AreEqual(3, pulses);
        }

        [Test]
        public void Reflow_RecoveredByFullRotation_ReturnsToOpenAndResetsTimer()
        {
            // §6.1-2 "역류 중 재상호작용해 회전 완료 → Open 복귀 + 타이머 리셋"
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 8f);
            Advance(valve, Valve.OpenHoldSeconds + 0.1f);
            Assert.AreEqual(ValveState.Reflowing, valve.State);

            Assert.AreEqual(ValveInteractionRejection.None,
                valve.TryInteract(RunnerB, RoleType.Runner),
                "§6.1-2 역류 중에도 상호작용이 가능하다.");

            Advance(valve, 8f); // 회전 시간은 최초와 동일
            Assert.AreEqual(ValveState.Open, valve.State);

            // 타이머 리셋 — 다시 180초를 버틴다.
            Advance(valve, 179.9f);
            Assert.AreEqual(ValveState.Open, valve.State, "역류 타이머가 리셋됐다.");
        }

        [Test]
        public void Reflow_DoesNotRunWhileRotating()
        {
            // ★ 반대 방향의 비간섭 — 회전 중인 밸브에 역류 타이머가 돌면 안 된다.
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);

            // 180초를 회전 상태로 버틴다(회전 시간 8초라면 이미 열렸을 테니 홀드를 끊고
            // 감쇠→재개를 반복해 Rotating을 유지하는 대신, 회전 시간이 긴 밸브로 확인한다).
            var slow = new Valve(1000f);
            slow.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(slow, 200f, step: 0.5f);

            Assert.AreEqual(ValveState.Rotating, slow.State,
                "회전 중인 밸브는 역류하지 않는다 — 역류는 Open에만 붙는다(§6.1-2).");
            Assert.AreEqual(0f, slow.OpenHoldRemaining, 0.0001f);
        }

        [Test]
        public void ResetForNewRound_ClearsOpenTimeAndProgress()
        {
            // ★ 라운드가 바뀔 때 Open 진입 시각과 진행도가 리셋되는가(더블체크 2).
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 8f);
            Advance(valve, 100f); // Open 상태로 100초 경과
            Assert.AreEqual(ValveState.Open, valve.State);

            valve.ResetForNewRound();

            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.0001f);
            Assert.AreEqual(0f, valve.OpenHoldRemaining, 0.0001f);
            Assert.IsTrue(valve.IsActive);

            // 지난 라운드의 80초가 남아 있으면 여기서 곧바로 역류한다.
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 8f);
            Advance(valve, 100f);
            Assert.AreEqual(ValveState.Open, valve.State, "새 라운드의 180초를 처음부터 센다.");
        }

        // ── §6.1-0 활성 밸브 ───────────────────────────────────────────

        [Test]
        public void InactiveValve_RejectsInteraction()
        {
            var valve = NewValveA();
            valve.SetActive(false);

            Assert.AreEqual(ValveInteractionRejection.NotActiveThisRound,
                valve.TryInteract(RunnerA, RoleType.Runner));
            Assert.AreEqual(ValveState.Closed, valve.State);
        }

        [Test]
        public void Deactivating_WhileRotating_InterruptsIt()
        {
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            Advance(valve, 2f);

            valve.SetActive(false);

            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0, valve.InteractorCount);
        }

        // ── §6.1 동시 작업 ─────────────────────────────────────────────

        [Test]
        public void TwoInteractors_CompleteInFiveSeconds()
        {
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.TryBeginRotation(RunnerB, RoleType.Runner);
            Assert.AreEqual(2, valve.InteractorCount);

            Advance(valve, 4.9f);
            Assert.AreEqual(ValveState.Rotating, valve.State, "5.0초 전에는 아직이다.");

            Advance(valve, 0.2f);
            Assert.AreEqual(ValveState.Open, valve.State, "§6.1 2인 동시 ×1.6 → 5.0초");
        }

        [Test]
        public void OneOfTwoLeaves_RotationContinuesAtLowerRate()
        {
            var valve = NewValveA();
            valve.TryBeginRotation(RunnerA, RoleType.Runner);
            valve.TryBeginRotation(RunnerB, RoleType.Runner);
            Advance(valve, 1f);

            Assert.IsTrue(valve.Interrupt(RunnerB));

            Assert.AreEqual(ValveState.Rotating, valve.State, "남은 1인이 계속 돈다.");
            Assert.AreEqual(1, valve.InteractorCount);
            Assert.IsFalse(valve.IsDecaying);
        }
    }
}
