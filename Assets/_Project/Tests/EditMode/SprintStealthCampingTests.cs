using Marco.Core.Awards;
using Marco.Core.Locomotion;
using Marco.Core.Role;
using Marco.Core.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [블록 5] §3.1 질주 스태미나 · 술래 잠행 · §3.6 캠핑 방지. 수치는 전부 기획서 표에서 옮겼다.
    /// </summary>
    public class SprintStealthCampingTests
    {
        private const float Eps = 1e-4f;

        private static LocomotionInput Move(bool sprint = false, bool stealth = false, float speedMultiplier = 1f,
            bool dive = false, bool inWater = false) =>
            new LocomotionInput(new Vector2(0f, 1f), sprint, dive, inWater, canSubmerge: true,
                speedMultiplier: speedMultiplier, stealthHeld: stealth);

        private static readonly LocomotionInput Idle =
            new LocomotionInput(Vector2.zero, sprintHeld: true, diveHeld: false, isOnWaterSurface: false);

        // ── 5-A 질주 스태미나 ─────────────────────────────────────────────

        [Test]
        public void SprintConstants_MatchDesignDoc()
        {
            Assert.AreEqual(5f, SprintConfig.StaminaSeconds, Eps);        // §3.1 "5초"
            Assert.AreEqual(3f, SprintConfig.ExhaustPenaltySeconds, Eps); // §3.1 "3초간"
            Assert.AreEqual(0.8f, SprintConfig.ExhaustSpeedMultiplier, Eps); // §3.1 "-20%"
        }

        [Test]
        public void SingleSprintRange_Is37Point5()
        {
            // §10.2 "스프린트 1회 사거리 = 7.5 m/s × 5초 = 37.5m" — 유도값.
            Assert.AreEqual(37.5f, SprintConfig.SingleSprintRangeMeters, Eps);
        }

        // §10.2 [블록 1-D 갱신] C# 배치 검증 툴 실측 경로 — "A↔C · A↔D · A↔E는 1회로 도달 불가능".
        [TestCase("A-C", 60.1f)]
        [TestCase("A-D", 59.3f)]
        [TestCase("A-E", 45.6f)]
        public void MachineRoomPairs_UnreachableInOneSprint(string pair, float pathMeters)
        {
            Assert.Greater(pathMeters, SprintConfig.SingleSprintRangeMeters, pair);
        }

        [Test]
        public void SimulatedSprint_Covers37Point5_ThenPenaltyAt4MetersPerSecond()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);
            float covered = 0f;
            for (int i = 0; i < 10; i++) // 0.5초 × 10 = 5초 (2진 정확값)
            {
                LocomotionTick t = sim.Tick(Move(sprint: true), 0.5f);
                Assert.AreEqual(MovementState.Sprint, t.State, $"tick {i}");
                covered += t.LocalVelocity.magnitude * 0.5f;
            }

            Assert.AreEqual(37.5f, covered, 1e-3f);
            Assert.IsTrue(sim.Stamina.IsExhausted, "5초 경계에서 소진");

            // §10.2-1 "소진 페널티 3초(12m)" = 5.0 × 0.8 = 4.0 m/s. Shift를 계속 눌러도 질주하지 않는다.
            LocomotionTick after = sim.Tick(Move(sprint: true), 0.5f);
            Assert.AreEqual(MovementState.Walk, after.State);
            Assert.AreEqual(4f, after.LocalVelocity.magnitude, 1e-3f);
        }

        [Test]
        public void Stamina_JustBeforeFive_NotExhausted()
        {
            var s = new SprintStamina();
            Assert.IsFalse(s.Tick(true, 4.99f));
            Assert.IsTrue(s.CanSprint);
            Assert.IsTrue(s.Tick(true, 0.01f), "5.00초에 소진");
        }

        [Test]
        public void Penalty_LastsThreeSeconds_Boundary()
        {
            var s = new SprintStamina();
            s.Tick(true, 5f);
            s.Tick(false, 2.99f);
            Assert.IsTrue(s.IsExhausted, "2.99초 — 아직 감속");
            Assert.AreEqual(0.8f, s.SpeedMultiplier, Eps);

            s.Tick(false, 0.01f);
            Assert.IsFalse(s.IsExhausted, "3.00초 — 페널티 종료");
            Assert.AreEqual(1f, s.SpeedMultiplier, Eps);
            Assert.IsFalse(s.CanSprint, "GAP-92 잠정: 페널티 동안 회복하지 않았으므로 아직 0");
        }

        [Test]
        public void Recovery_Provisional_OneToOne()
        {
            // GAP-92 잠정값 — 회복 규칙이 기획서에 없다. 1초 질주 = 1초 휴식.
            var s = new SprintStamina();
            s.Tick(true, 2f);
            s.Tick(false, 1f);
            Assert.AreEqual(4f, s.Remaining, Eps);
            s.Tick(false, 10f);
            Assert.AreEqual(SprintConfig.StaminaSeconds, s.Remaining, Eps, "상한 5초");
        }

        [Test]
        public void Stamina_NotConsumedWhenStandingWithShift()
        {
            // §4.3 "질주 — 이동 중에만 작동".
            var sim = new LocomotionSimulator(RoleType.Runner);
            sim.Tick(Idle, 3f);
            Assert.AreEqual(SprintConfig.StaminaSeconds, sim.Stamina.Remaining, Eps);
        }

        [TestCase(RoleType.Seeker)]
        [TestCase(RoleType.Echo)]
        public void NonRunners_NeverConsumeStamina(RoleType role)
        {
            var sim = new LocomotionSimulator(role);
            sim.Tick(Move(sprint: true), 6f);
            Assert.IsFalse(sim.Stamina.IsExhausted);
            Assert.AreEqual(SprintConfig.StaminaSeconds, sim.Stamina.Remaining, Eps);
        }

        [Test]
        public void ChokeAndExhaust_DoNotStack_SlowerOneOnly()
        {
            // GAP-93 결정: 0.8 × 0.8 = 0.64가 아니라 0.8 (§9.4 이중 보정 금지).
            Assert.AreEqual(0.8f, SprintConfig.CombineSpeedMultipliers(0.8f, 0.8f), Eps);
            Assert.AreEqual(0.8f, SprintConfig.CombineSpeedMultipliers(1f, 0.8f), Eps);
            Assert.AreEqual(1f, SprintConfig.CombineSpeedMultipliers(1f, 1f), Eps);

            var sim = new LocomotionSimulator(RoleType.Runner);
            sim.Tick(Move(sprint: true), 5f); // 소진
            LocomotionTick both = sim.Tick(Move(speedMultiplier: 0.8f), 0.1f); // 질식도 동시에
            Assert.AreEqual(4f, both.LocalVelocity.magnitude, 1e-3f);
        }

        // ── 5-B 술래 잠행 ─────────────────────────────────────────────────

        [TestCase(2)]
        [TestCase(5)]
        [TestCase(6)]
        public void Stealth_Is3Point5_FixedAcrossPlayerCounts(int players)
        {
            // GAP-84: 인원 보정·종반 보정을 받지 않는 고정값.
            var sim = new LocomotionSimulator(RoleType.Seeker) { TotalPlayers = players, EndgamePressure = true };
            LocomotionTick t = sim.Tick(Move(stealth: true), 0.1f);
            Assert.AreEqual(3.5f, t.LocalVelocity.magnitude, 1e-3f);
            Assert.IsTrue(sim.IsStealthing);
        }

        [Test]
        public void Stealth_FootstepsDropToWalkTier_WithoutExtraBranch()
        {
            // §3.1 "잠행 시 2m" — 속도 판정(§5.1 ≤ 5.0 = 걷기)이 자동으로 고른다.
            var sim = new LocomotionSimulator(RoleType.Seeker) { TotalPlayers = 5 };
            FootstepPulse? pulse = null;
            for (int i = 0; i < 10 && !pulse.HasValue; i++)
                pulse = sim.Tick(Move(stealth: true), 0.2f).Pulse;

            Assert.IsTrue(pulse.HasValue);
            Assert.AreEqual(SoundType.Walk, pulse.Value.Type);
            Assert.AreEqual(2f, pulse.Value.Radius, Eps);

            // 잠행을 풀면 5.30 > 5.0 → 질주급 6m(§3.1 "상시 질주급").
            var chase = new LocomotionSimulator(RoleType.Seeker) { TotalPlayers = 5 };
            FootstepPulse? chasePulse = null;
            for (int i = 0; i < 20 && !chasePulse.HasValue; i++)
                chasePulse = chase.Tick(Move(), 0.2f).Pulse;
            Assert.AreEqual(SoundType.Sprint, chasePulse.Value.Type);
        }

        [Test]
        public void Stealth_IgnoredForRunner()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);
            Assert.AreEqual(5f, sim.Tick(Move(stealth: true), 0.1f).LocalVelocity.magnitude, 1e-3f);
            Assert.IsFalse(sim.IsStealthing);
        }

        [Test]
        public void Seeker_InWater_WithCtrl_StealthsInsteadOfDiving()
        {
            // 같은 키(GAP-94)라도 술래는 잠수하지 않는다 — §6.5-3.
            var sim = new LocomotionSimulator(RoleType.Seeker) { TotalPlayers = 5 };
            LocomotionTick t = sim.Tick(Move(stealth: true, dive: true, inWater: true), 0.1f);
            Assert.AreNotEqual(MovementState.Diving, t.State);
            Assert.AreEqual(3.5f, t.LocalVelocity.magnitude, 1e-3f);
        }

        // ── 5-C 캠핑 방지 ─────────────────────────────────────────────────

        [Test]
        public void CampingConstants_MatchDesignDoc()
        {
            Assert.AreEqual(20f, CampingConfig.WindowSeconds, Eps);
            Assert.AreEqual(3f, CampingConfig.TriggerDistanceMeters, Eps);
            Assert.AreEqual(3f, CampingConfig.FirstRadiusMeters, Eps);
            Assert.AreEqual(0.6f, CampingConfig.DurationSeconds, Eps);
            Assert.AreEqual(15f, CampingConfig.IntervalSeconds, Eps);
            Assert.AreEqual(2f, CampingConfig.StepMeters, Eps);
            Assert.AreEqual(9f, CampingConfig.MaxRadiusMeters, Eps);
            Assert.AreEqual(5f, CampingConfig.ResetDistanceMeters, Eps);
        }

        [TestCase(0, 3f)]
        [TestCase(1, 5f)]
        [TestCase(2, 7f)]
        [TestCase(3, 9f)]
        [TestCase(4, 9f)]
        [TestCase(10, 9f)]
        public void BreathRadius_Amplifies_CappedAt9(int index, float radius)
        {
            Assert.AreEqual(radius, CampingConfig.RadiusForPulse(index), Eps);
        }

        [TestCase(RoleType.Runner, true)]
        [TestCase(RoleType.Seeker, true)] // ★ §3.6 "적용: 도망자 + 술래" — §6.5-3의 전제
        [TestCase(RoleType.Echo, false)]
        public void Camping_AppliesToRunnerAndSeeker(RoleType role, bool expected)
        {
            Assert.AreEqual(expected, CampingConfig.AppliesTo(role));
        }

        /// <summary>정지 상태로 <paramref name="seconds"/>초 진행하고, 마지막 틱의 반경을 돌려준다.</summary>
        private static float Stand(CampingMonitor m, float seconds, bool suspended = false)
        {
            float last = 0f;
            for (float t = 0f; t < seconds - 1e-4f; t += 0.5f)
                last = m.Tick(0.5f, 0f, suspended);
            return last;
        }

        [Test]
        public void Stationary_Timeline_20_35_50_65()
        {
            // §3.6 억제 근거 표: 20초 3m → 15초마다 +2m → 65초~ 9m.
            var m = new CampingMonitor();
            Assert.AreEqual(0f, Stand(m, 19.5f), "19.5초 — 아직");
            Assert.AreEqual(3f, m.Tick(0.5f, 0f, false), Eps, "20초 — 1단계 3m");
            Assert.AreEqual(0f, Stand(m, 14.5f));
            Assert.AreEqual(5f, m.Tick(0.5f, 0f, false), Eps, "35초 — 5m");
            Assert.AreEqual(7f, Stand(m, 15f), Eps, "50초 — 7m");
            Assert.AreEqual(9f, Stand(m, 15f), Eps, "65초 — 9m(최대)");
            Assert.AreEqual(9f, Stand(m, 15f), Eps, "80초 — 9m 유지");
        }

        [Test]
        public void Trigger_Boundary_Under3MetersOnly()
        {
            // "3m 미만" — 2.99m는 발동, 3.00m는 발동하지 않는다.
            var under = new CampingMonitor();
            under.Tick(0.5f, 2.99f, false);
            Assert.AreEqual(3f, Stand(under, 19.5f), Eps);

            var exact = new CampingMonitor();
            exact.Tick(0.5f, 3f, false);
            Assert.AreEqual(0f, Stand(exact, 19.5f));
            Assert.IsFalse(exact.IsActive);
        }

        [Test]
        public void SlidingWindow_OldMovementExpires()
        {
            // 처음 1초에 3m 이동 후 정지 — 20초 시점엔 창 안에 3m가 남아 미발동, 20.5초에 앞 표본이 빠져 발동.
            var m = new CampingMonitor();
            m.Tick(0.5f, 1.5f, false);
            m.Tick(0.5f, 1.5f, false);
            Assert.AreEqual(0f, Stand(m, 19f), "20.0초 — 창에 3.0m");
            Assert.AreEqual(3f, m.Tick(0.5f, 0f, false), Eps, "20.5초 — 창 1.5m → 발동");
        }

        [Test]
        public void Reset_At5Meters_Boundary()
        {
            var m = new CampingMonitor();
            Stand(m, 20f);
            Assert.IsTrue(m.IsActive);

            m.Tick(0.5f, 4.99f, false);
            Assert.IsTrue(m.IsActive, "4.99m — 유지");
            m.Tick(0.5f, 0.01f, false);
            Assert.IsFalse(m.IsActive, "5.00m — 즉시 초기화");

            Assert.AreEqual(0f, Stand(m, 19.5f), "초기화 후 20초를 처음부터");
            Assert.AreEqual(3f, m.Tick(0.5f, 0f, false), Eps);
        }

        [Test]
        public void Suspension_PausesNotResets()
        {
            // §3.6 "타이머가 정지(일시중단) — 초기화가 아니라 정지다".
            var m = new CampingMonitor();
            Stand(m, 10f);
            Assert.AreEqual(0f, Stand(m, 30f, suspended: true), "밸브·잠수·메뉴 중 — 파문 없음");
            Assert.AreEqual(0f, Stand(m, 9.5f), "합계 19.5초");
            Assert.AreEqual(3f, m.Tick(0.5f, 0f, false), Eps, "중단 전 10초가 이어져 20초에 발동");
        }

        [Test]
        public void Suspension_WhileActive_PausesAmplification()
        {
            var m = new CampingMonitor();
            Stand(m, 20f);
            Stand(m, 10f);
            Assert.AreEqual(0f, Stand(m, 60f, suspended: true));
            Assert.AreEqual(5f, Stand(m, 5f), Eps, "중단 전 10초 + 5초 = 15초 → 2번째 호흡음");
        }

        [Test]
        public void Suspension_IgnoresMovementDuringIt()
        {
            // 중단 중 이동(텔레포트성 보정 등)은 세지 않는다 — 재개 후 창이 그대로.
            var m = new CampingMonitor();
            Stand(m, 10f);
            m.Tick(0.5f, 50f, true);
            Assert.AreEqual(0f, Stand(m, 9.5f));
            Assert.AreEqual(3f, m.Tick(0.5f, 0f, false), Eps);
        }

        // ── Breath enum 파급(더블체크 9) ─────────────────────────────────

        [Test]
        public void Breath_PulseSpec_FromTable()
        {
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Breath, out float r, out float d));
            Assert.AreEqual(3f, r, Eps);
            Assert.AreEqual(0.6f, d, Eps);
        }

        [Test]
        public void Breath_IsServerOnly()
        {
            Assert.IsTrue(ServerPulseDriver.IsServerOnly(SoundType.Breath));
            Assert.IsTrue(ServerPulseDriver.IsServerOnly(SoundType.Scream), "§8.1 비명상 오염 차단");
            Assert.IsTrue(ServerPulseDriver.IsServerOnly(SoundType.Knock), "§3.2 노크 제약 우회 차단");
            // [블록 7 의미 변경] 밸브 회전음도 서버 전용이 됐다 — 서버가 홀드를 수락할 때 밸브별로 낸다.
            Assert.IsTrue(ServerPulseDriver.IsServerOnly(SoundType.Valve), "§6.1 밸브 회전음 — 서버 발생");

            // 정당한 클라이언트 경로: 발소리 · 음성 3등급.
            foreach (SoundType t in new[] { SoundType.Walk, SoundType.Sprint, SoundType.Whisper, SoundType.Talk,
                                             SoundType.Shout })
                Assert.IsFalse(ServerPulseDriver.IsServerOnly(t), t.ToString());
        }

        [Test]
        public void Breath_DoesNotTriggerDirectionGauge()
        {
            // §5.1 표 Breath 행 방향 게이지 ✗.
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Breath));
        }

        [Test]
        public void Breath_NotAFootstep_NoMaterialMultiplier()
        {
            Assert.IsFalse(FootstepMaterialRules.AppliesTo(SoundType.Breath));
        }

        [Test]
        public void Breath_CountsTowardSilentSurvivorNoise()
        {
            // §8.2 제외 목록은 밸브뿐 — 호흡음은 문자 그대로 포함(GAP-96).
            Assert.IsTrue(AwardTally.CountsTowardNoise(SoundType.Breath));
        }

        [Test]
        public void Breath_AmplifiedRadius_IsRegistered()
        {
            var driver = new ServerPulseDriver();
            int id = driver.AddPulse(1, SoundType.Breath, Vector3.zero, 0f, radiusOverride: 9f);
            Assert.GreaterOrEqual(id, 0);
            Assert.AreEqual(1, driver.ActivePulseCount);
        }

        [Test]
        public void Breath_IsLastEnumValue_ExistingValuesUnchanged()
        {
            // 맨 끝에 추가 — 네트워크로 오가는 기존 종류의 정수값이 밀리지 않는다.
            Assert.AreEqual(0, (int)SoundType.Whisper);
            Assert.AreEqual(7, (int)SoundType.Knock);
            Assert.AreEqual(8, (int)SoundType.Breath);
        }
    }
}
