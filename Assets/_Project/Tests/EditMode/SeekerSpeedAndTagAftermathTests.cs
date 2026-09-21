using Marco.Core.GameFlow;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Core.Tagging;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 블록 3 — §6.2 인원별 술래 이속, §6.2-1 종반 압박, §3.1-1 태그 처리,
    /// §6.5-1 최후 생존자 페이즈 진입 훅을 고정한다.
    /// </summary>
    public class SeekerSpeedAndTagAftermathTests
    {
        // ── §6.2 [v0.4] 인원별 술래 이속 ────────────────────────────────

        [Test]
        public void SeekerBaseSpeed_IsFive()
        {
            Assert.AreEqual(5f, LocomotionConfig.SeekerBaseSpeed, "§3.1 술래 기본 5.0 m/s");
        }

        // §6.2 표: 총원 2→+5% / 3→+5% / 4→+4% / 5→+6% / 6→+8%
        [TestCase(2, 0.05f, 5.25f)]
        [TestCase(3, 0.05f, 5.25f)]
        [TestCase(4, 0.04f, 5.20f)]
        [TestCase(5, 0.06f, 5.30f)]
        [TestCase(6, 0.08f, 5.40f)]
        public void SeekerSpeed_IsBaseTimesCorrection(int players, float correction, float speed)
        {
            Assert.AreEqual(correction, LocomotionConfig.SeekerSpeedCorrection(players), 0.0001f);
            Assert.AreEqual(speed, LocomotionConfig.SeekerSpeedFor(players), 0.0001f,
                "5.30 같은 결과값을 상수로 박지 않고 5.0 × 보정으로 유도한다.");
        }

        [Test]
        public void SeekerSpeedCorrection_NeverBelowFloor()
        {
            // §6.2 "⚠ 하한 = +1%. 보정은 절대 0%가 될 수 없다."
            Assert.AreEqual(0.01f, LocomotionConfig.SeekerSpeedCorrectionFloor, 0.0001f);

            for (int players = 0; players <= 12; players++)
            {
                Assert.GreaterOrEqual(LocomotionConfig.SeekerSpeedCorrection(players),
                    LocomotionConfig.SeekerSpeedCorrectionFloor, $"총원 {players}");
            }
        }

        [Test]
        public void FootstepTier_BoundaryAtFive()
        {
            // §5.1은 발소리 등급을 **실제 속도**로 판정한다 — 걷기 ≤ 5.0 / 질주 > 5.0.
            // 경계값: 5.00 → 걷기 등급 / 5.01 → 질주 등급.
            // 이것이 하한 +1%의 존재 이유다 — 보정이 0%면 술래(5.00)가 걷기 등급(2m)으로
            // 떨어져 도망자가 접근을 들을 수 없게 된다.
            float threshold = LocomotionConfig.FootstepSpeedThreshold;

            Assert.AreEqual(5f, threshold);
            Assert.IsFalse(5.00f > threshold, "5.00은 걷기 등급(2m)이다.");
            Assert.IsTrue(5.01f > threshold, "5.01은 질주 등급(6m)이다.");

            // 하한을 적용한 최저 술래 속도도 질주 등급이어야 한다.
            float slowest = LocomotionConfig.SeekerBaseSpeed * (1f + LocomotionConfig.SeekerSpeedCorrectionFloor);
            Assert.Greater(slowest, threshold, "하한 +1%면 5.05 m/s — 여전히 질주 등급이다.");
        }

        [Test]
        public void Seeker_AtFloorCorrection_EmitsSprintTierFootsteps()
        {
            // 실제 시뮬레이터로 확인한다 — 등급 판정은 LocomotionSimulator가 소유한다.
            var sim = new LocomotionSimulator(RoleType.Seeker) { TotalPlayers = 4 }; // +4% = 5.20
            var input = new LocomotionInput(new Vector2(0f, 1f),
                sprintHeld: false, diveHeld: false, isOnWaterSurface: false);

            SoundType? tier = null;
            for (int i = 0; i < 200 && tier == null; i++)
            {
                LocomotionTick tick = sim.Tick(input, 0.02f);
                if (tick.Pulse.HasValue)
                    tier = tick.Pulse.Value.Type;
            }

            Assert.AreEqual(SoundType.Sprint, tier, "술래는 이동만 해도 질주 등급(6m)이다.");
        }

        // ── §6.2-1 종반 압박 ───────────────────────────────────────────

        [Test]
        public void EndgameBonus_IsMultiplicativeOnPerCountSpeed()
        {
            // §6.2-1 "5인 기준 5.30 → 5.57". 5.57을 상수로 박지 않고 (5.0 × 1.06) × 1.05로 유도한다.
            float normal = LocomotionConfig.SeekerSpeedFor(5);
            float endgame = LocomotionConfig.SeekerSpeedFor(5, endgamePressure: true);

            Assert.AreEqual(5.30f, normal, 0.0001f);
            Assert.AreEqual(5.30f * 1.05f, endgame, 0.0001f);
            Assert.AreEqual(5.57f, endgame, 0.01f, "기획서 표기 5.57(= 5.565 반올림)");
        }

        [Test]
        public void EndgameThresholds_MatchDesignDoc()
        {
            Assert.AreEqual(120f, LocomotionConfig.EndgameDroneSeconds, "§6.2-1 잔여 2분 드론");
            Assert.AreEqual(60f, LocomotionConfig.EndgamePressureSeconds, "§6.2-1 잔여 1분 +5%");
            Assert.AreEqual(30f, LocomotionConfig.EndgameExitPulseSeconds, "§6.2-1 잔여 30초 출구 파문");
            Assert.AreEqual(0.05f, LocomotionConfig.EndgameSpeedBonus, 0.0001f);
        }

        [Test]
        public void Simulator_AppliesEndgamePressureToSeekerOnly()
        {
            var seeker = new LocomotionSimulator(RoleType.Seeker) { TotalPlayers = 5, EndgamePressure = true };
            var runner = new LocomotionSimulator(RoleType.Runner) { TotalPlayers = 5, EndgamePressure = true };
            var input = new LocomotionInput(new Vector2(0f, 1f), false, false, false);

            Assert.AreEqual(5.30f * 1.05f, seeker.Tick(input, 0.02f).LocalVelocity.magnitude, 0.001f);
            Assert.AreEqual(LocomotionConfig.RunnerWalkSpeed,
                runner.Tick(input, 0.02f).LocalVelocity.magnitude, 0.001f,
                "종반 +5%는 술래 전용이다(§6.2-1).");
        }

        [Test]
        public void SeekerStealthSpeed_IsFixedThreePointFive()
        {
            Assert.AreEqual(3.5f, LocomotionConfig.SeekerStealthSpeed, "§3.1 잠행 3.5 m/s");
        }

        // ── §3.1-1 태그 처리 ───────────────────────────────────────────

        [Test]
        public void TagAftermath_MatchesDesignDoc()
        {
            Assert.AreEqual(1.0f, TagAftermath.SeekerStunSeconds, "§3.1-1 술래 1.0초 경직");
            Assert.AreEqual(3.0f, TagAftermath.RunnerBlackoutSeconds, "§3.1-1 도망자 3.0초 암전");
            Assert.AreEqual(SoundType.Shout, TagAftermath.PulseType,
                "§3.1-1 태그 파문은 기존 Shout 등급 — 새 SoundType 금지(§3.3)");
        }

        [Test]
        public void TagPulse_ReusesShoutSpec_TwentyTwoMeters()
        {
            // §3.1-1 "고함급 파문 1회(발생 22m / 지속 2.5초)" — §5.1 Shout 표 그대로다.
            Assert.IsTrue(Marco.Core.Sound.ServerPulseDriver.TryGetAppliedSpec(
                TagAftermath.PulseType, FootstepMaterialRules.Default, out float radius, out float duration));
            Assert.AreEqual(22f, radius, 0.0001f);
            Assert.AreEqual(2.5f, duration, 0.0001f);
        }

        [Test]
        public void SeekerStun_BoundaryAtOneSecond()
        {
            const float taggedAt = 10f;

            Assert.IsTrue(TagAftermath.IsSeekerStunned(taggedAt, 10.99f), "1.0초 전에는 경직이다.");
            Assert.IsFalse(TagAftermath.IsSeekerStunned(taggedAt, 11.0f), "1.0초가 되면 풀린다.");
            Assert.IsFalse(TagAftermath.IsSeekerStunned(0f, 11.0f), "태그한 적 없으면 경직이 아니다.");
        }

        [Test]
        public void RunnerBlackout_BoundaryAtThreeSeconds()
        {
            const float taggedAt = 5f;

            Assert.IsTrue(TagAftermath.IsRunnerBlackedOut(taggedAt, 7.99f));
            Assert.IsFalse(TagAftermath.IsRunnerBlackedOut(taggedAt, 8.0f));
            Assert.AreEqual(1f, TagAftermath.BlackoutRemaining(taggedAt, 7f), 0.0001f);
        }

        // ── §6.5-1 최후 생존자 페이즈 훅 ───────────────────────────────

        [Test]
        public void Driver_EntersPhaseOnce_WhenOneRunnerLeft()
        {
            var d = new ServerRoundDriver(600f);
            RunnerCensus census = d.Census(totalRunners: 3, taggedRunners: 2);

            Assert.IsTrue(census.ShouldEnterLastSurvivorPhase);
            Assert.IsTrue(d.TryEnterLastSurvivorPhase());
            Assert.IsFalse(d.TryEnterLastSurvivorPhase(), "1회성이다.");
            Assert.IsTrue(d.LastSurvivorPhase);
        }

        [Test]
        public void DrainEscape_WinsWithoutGate_AndOnlyDuringPhase()
        {
            var d = new ServerRoundDriver(600f);

            // 페이즈 밖에서는 배수구 탈출이 거부된다 — §6.5 전용 오브젝트다.
            Assert.IsFalse(d.TryRegisterDrainEscape(7, RoleType.Runner));

            d.TryEnterLastSurvivorPhase();
            Assert.IsTrue(d.TryRegisterDrainEscape(7, RoleType.Runner),
                "§6.5-2 배수구 자체가 탈출구다 — 게이트 개방을 요구하지 않는다.");
            Assert.IsTrue(d.LastSurvivorEscaped);

            // 4인 게임(도망자 3)에서 태그 2 + 배수구 탈출 1 → 순서 의존 케이스, 마지막이 탈출.
            Assert.IsTrue(d.Evaluate(d.Census(totalRunners: 3, taggedRunners: 2)));
            Assert.AreEqual(RoundResult.RunnersWin, d.Result, "§6.5-1 마지막 생존자의 탈출 = 팀 승리");
        }

        [Test]
        public void GateEscapeDuringPhase_AlsoSetsLastSurvivorFlag()
        {
            // §6.3 "게이트가 이미 열려 있으면 배수구를 열 필요 없이 정문·배수로로 나가면 된다."
            var d = new ServerRoundDriver(600f);
            d.TryEnterLastSurvivorPhase();

            Assert.IsTrue(d.TryRegisterEscape(9, RoleType.Runner, gateOpen: true));
            Assert.IsTrue(d.LastSurvivorEscaped, "페이즈 중 출구 탈출도 사건 플래그를 세운다.");
        }

        [TestCase(600f, 90f, 90f)]  // 라운드 잔여가 길면 페이즈 90초
        [TestCase(89f, 90f, 89f)]   // §6.5-1 경계: 라운드 잔여 89초에 진입 → 유효 89초
        [TestCase(30f, 90f, 30f)]
        public void EffectiveRemaining_IsMinOfRoundAndPhase(float round, float phase, float expected)
        {
            // §6.5-1 "라운드 잔여가 더 짧으면 그쪽 우선" — 페이즈 타이머가 라운드를 연장하지 않는다.
            var d = new ServerRoundDriver(round);
            d.TryEnterLastSurvivorPhase();

            Assert.AreEqual(expected, d.EffectiveRemainingSeconds(phase), 0.0001f);
        }

        [Test]
        public void EffectiveRemaining_OutsidePhase_IsRoundRemaining()
        {
            var d = new ServerRoundDriver(400f);
            Assert.AreEqual(400f, d.EffectiveRemainingSeconds(90f), 0.0001f);
        }
    }
}
