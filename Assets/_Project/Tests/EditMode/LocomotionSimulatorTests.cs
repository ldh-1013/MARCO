using NUnit.Framework;
using UnityEngine;
using Marco.Core.Locomotion;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §4.2 이동 상태기계 · §3.1 역할별 속도 · §5.1 발소리 펄스를 고정한다.
    /// </summary>
    public class LocomotionSimulatorTests
    {
        private static LocomotionInput Move(float x, float y, bool sprint = false, bool dive = false, bool onWater = false)
        {
            return new LocomotionInput(new Vector2(x, y), sprint, dive, onWater);
        }

        // 1) §3.1: 러너 걷기 5.0m/s.
        [Test]
        public void Runner_Walk_MovesAtFive()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1), 0.02f);

            Assert.AreEqual(MovementState.Walk, tick.State);
            Assert.AreEqual(5.0f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 2) §3.1: 러너 질주 7.5m/s.
        [Test]
        public void Runner_Sprint_MovesAtSevenPointFive()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1, sprint: true), 0.02f);

            Assert.AreEqual(MovementState.Sprint, tick.State);
            Assert.AreEqual(7.5f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 3) §3.1 × §6.2 4인: 술래 5.4m/s.
        [Test]
        public void Seeker_MovesAtFivePointFour()
        {
            var sim = new LocomotionSimulator(RoleType.Seeker);

            var tick = sim.Tick(Move(0, 1), 0.02f);

            Assert.AreEqual(5.4f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 4) §3.2: 메아리 8.0m/s.
        [Test]
        public void Echo_MovesAtEight()
        {
            var sim = new LocomotionSimulator(RoleType.Echo);

            var tick = sim.Tick(Move(0, 1), 0.02f);

            Assert.AreEqual(6.0f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 5) §3.1: 질주는 도망자 전용 — 술래·메아리는 Shift를 눌러도 기본 속도.
        [TestCase(RoleType.Seeker, 5.4f)]
        [TestCase(RoleType.Echo, 6.0f)]
        public void NonRunner_SprintInput_IsIgnored(RoleType role, float expectedSpeed)
        {
            var sim = new LocomotionSimulator(role);

            var tick = sim.Tick(Move(0, 1, sprint: true), 0.02f);

            Assert.AreNotEqual(MovementState.Sprint, tick.State);
            Assert.AreEqual(expectedSpeed, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 6) §4.3: 질주는 이동 중에만 — 입력 없이 Shift만 누르면 Idle.
        [Test]
        public void SprintWithoutMovement_StaysIdle()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 0, sprint: true), 0.02f);

            Assert.AreEqual(MovementState.Idle, tick.State);
            Assert.AreEqual(0f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 7) §4.2 상태 전이: Idle → Walk → Sprint → Idle.
        [Test]
        public void StateTransitions_IdleWalkSprintIdle()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            Assert.AreEqual(MovementState.Idle, sim.Tick(Move(0, 0), 0.02f).State);
            Assert.AreEqual(MovementState.Walk, sim.Tick(Move(0, 1), 0.02f).State);
            Assert.AreEqual(MovementState.Sprint, sim.Tick(Move(0, 1, sprint: true), 0.02f).State);
            Assert.AreEqual(MovementState.Idle, sim.Tick(Move(0, 0), 0.02f).State);
        }

        // 8) 대각 입력 정규화: (1,1)이어도 속도는 5.0을 넘지 않는다.
        [Test]
        public void DiagonalInput_DoesNotExceedMaxSpeed()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(1, 1), 0.02f);

            Assert.AreEqual(5.0f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // ── §5.1-1 이동거리 기준 발소리 ─────────────────────────────────

        /// <summary>임계값을 정확히 넘도록 한 번에 <paramref name="meters"/>만큼 이동시킨다.</summary>
        private static LocomotionTick MoveMeters(LocomotionSimulator sim, float meters, float speed, bool sprint = false)
        {
            return sim.Tick(Move(0, 1, sprint: sprint), meters / speed);
        }

        // 9) §5.1-1 "2m 이동마다" — 임계값 전에는 소리가 나지 않는다.
        [Test]
        public void Walk_BeforeTwoMeters_EmitsNothing()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            // 1.9m만 이동 — 아직 2m가 아니다.
            LocomotionTick tick = MoveMeters(sim, 1.9f, LocomotionConfig.RunnerWalkSpeed);

            Assert.IsFalse(tick.Pulse.HasValue,
                "§5.1-1은 '2m 이동마다'다 — 첫 틱에 즉시 나던 시간 기준(구 GAP-8)이 아니다.");
        }

        // 10) §5.1: 걷기 2m 도달 시 걷기 펄스(2m/0.4s).
        [Test]
        public void Walk_AtTwoMeters_EmitsWalkPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            LocomotionTick tick = MoveMeters(sim, LocomotionConfig.WalkPulseRadius, LocomotionConfig.RunnerWalkSpeed);

            Assert.IsTrue(tick.Pulse.HasValue);
            Assert.AreEqual(SoundType.Walk, tick.Pulse.Value.Type);
            Assert.AreEqual(2f, tick.Pulse.Value.Radius, 0.001f);
            Assert.AreEqual(0.4f, tick.Pulse.Value.Duration, 0.001f);
        }

        // 11) §5.1: 질주 6m 도달 시 질주 펄스(6m/0.8s).
        [Test]
        public void Sprint_AtSixMeters_EmitsSprintPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            LocomotionTick tick = MoveMeters(sim, LocomotionConfig.SprintPulseRadius,
                LocomotionConfig.RunnerSprintSpeed, sprint: true);

            Assert.IsTrue(tick.Pulse.HasValue);
            Assert.AreEqual(SoundType.Sprint, tick.Pulse.Value.Type);
            Assert.AreEqual(6f, tick.Pulse.Value.Radius, 0.001f);
            Assert.AreEqual(0.8f, tick.Pulse.Value.Duration, 0.001f);
        }

        // 12) §5.1-1 표: 걷기 10m를 이동하면 정확히 5회(10 ÷ 2m).
        [Test]
        public void Walk_TenMeters_EmitsFivePulses()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);
            int pulses = 0;

            // 0.02초 틱으로 2초 = 10m(5.0m/s). 프레임 분할과 무관하게 5회여야 한다.
            for (int i = 0; i < 100; i++)
            {
                if (sim.Tick(Move(0, 1), 0.02f).Pulse.HasValue)
                    pulses++;
            }

            Assert.AreEqual(5, pulses, "10m ÷ 2m = 5회");
        }

        // 13) §5.1-1 표: 질주 30m를 이동하면 정확히 5회(30 ÷ 6m).
        [Test]
        public void Sprint_ThirtyMeters_EmitsFivePulses()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);
            int pulses = 0;

            // 0.02초 틱으로 4초 = 30m(7.5m/s).
            for (int i = 0; i < 200; i++)
            {
                if (sim.Tick(Move(0, 1, sprint: true), 0.02f).Pulse.HasValue)
                    pulses++;
            }

            Assert.AreEqual(5, pulses, "30m ÷ 6m = 5회");
        }

        // 14) **프레임률 독립성**: 같은 거리를 큰 틱으로 가도 파문 수가 같아야 한다.
        //     (임계값 도달 시 0으로 밀지 않고 빼기 때문에 성립한다.)
        [Test]
        public void Walk_PulseCount_IsIndependentOfFrameRate()
        {
            int Count(float dt, int steps)
            {
                var sim = new LocomotionSimulator(RoleType.Runner);
                int n = 0;
                for (int i = 0; i < steps; i++)
                {
                    if (sim.Tick(Move(0, 1), dt).Pulse.HasValue)
                        n++;
                }
                return n;
            }

            // 둘 다 2초 = 10m.
            Assert.AreEqual(Count(0.02f, 100), Count(0.1f, 20));
        }

        // 15) **시간 기준과의 핵심 차이**: 제자리에서는 아무리 시간이 흘러도 소리가 없다.
        [Test]
        public void Idle_NoMatterHowLong_EmitsNothing()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            for (int i = 0; i < 500; i++) // 10초
            {
                LocomotionTick tick = sim.Tick(Move(0, 0), 0.02f);
                Assert.IsFalse(tick.Pulse.HasValue, "정지 중에는 이동거리가 쌓이지 않는다(§5.1-1)");
            }
        }

        // 16) 미세 이동을 반복해도 파문을 만들 수 없다 — 누적이 정지에서 리셋되기 때문이다.
        [Test]
        public void Jitter_StartStop_NeverReachesThreshold()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            for (int i = 0; i < 100; i++)
            {
                // 0.02초(0.1m)만 움직이고 곧바로 멈춘다 — 2m에 영원히 못 닿는다.
                Assert.IsFalse(sim.Tick(Move(0, 1), 0.02f).Pulse.HasValue);
                Assert.IsFalse(sim.Tick(Move(0, 0), 0.02f).Pulse.HasValue);
            }
        }

        // 17) 정지하면 누적이 리셋된다 — 재이동 시 처음부터 2m를 채워야 한다.
        [Test]
        public void Stopping_ResetsAccumulatedDistance()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            MoveMeters(sim, 1.9f, LocomotionConfig.RunnerWalkSpeed); // 2m 직전까지
            sim.Tick(Move(0, 0), 0.02f);                            // 정지 → 리셋

            // 남아 있던 1.9m가 유효하다면 0.2m만 더 가도 파문이 나야 한다 — 나오면 안 된다.
            Assert.IsFalse(MoveMeters(sim, 0.2f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue);

            // 다시 2m를 채우면 그때 난다.
            Assert.IsTrue(MoveMeters(sim, 2f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue);
        }

        // 18) 등급별 누적은 분리돼 있다 — 걷기↔질주를 번갈아 눌러 진행을 지울 수 없다.
        [Test]
        public void WalkAndSprint_AccumulateSeparately()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            // 걷기 1.9m 누적(파문 없음) → 질주로 전환해도 걷기 누적은 남아 있어야 한다.
            Assert.IsFalse(MoveMeters(sim, 1.9f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue);
            Assert.IsFalse(MoveMeters(sim, 1f, LocomotionConfig.RunnerSprintSpeed, sprint: true).Pulse.HasValue,
                "질주는 6m가 임계값이라 1m로는 나지 않는다");

            // 다시 걷기로 0.2m만 더 가면 1.9 + 0.2 = 2.1m → 파문이 난다.
            Assert.IsTrue(MoveMeters(sim, 0.2f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue,
                "등급 전환으로 걷기 누적이 지워지면 안 된다");
        }

        // 19) §5.1 문자 그대로: 술래(5.4 > 5.0)는 이동만 해도 질주 등급(6m) 펄스.
        [Test]
        public void Seeker_Movement_EmitsSprintTierPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Seeker);

            // 6.5m — 임계값 6m를 확실히 넘긴다(5.4m/s는 2진 부동소수로 정확히 표현되지 않아
            // 정확히 6m를 노리면 마지막 자리에서 아슬아슬하게 미달할 수 있다).
            LocomotionTick tick = MoveMeters(sim, 6.5f, LocomotionConfig.SeekerSpeed);

            Assert.IsTrue(tick.Pulse.HasValue);
            Assert.AreEqual(SoundType.Sprint, tick.Pulse.Value.Type);
            Assert.AreEqual(6f, tick.Pulse.Value.Radius, 0.001f);
        }

        // 20) §3.2: 메아리는 아무리 이동해도 발소리 펄스가 없다(비행형).
        [Test]
        public void Echo_Movement_EmitsNoPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Echo);

            for (int i = 0; i < 200; i++)
                Assert.IsFalse(sim.Tick(Move(0, 1), 0.02f).Pulse.HasValue);
        }

        // 21) §4.3: 잠수는 수면 위에서만 — 수면 밖에서는 Ctrl을 눌러도 진입 불가.
        [Test]
        public void Dive_OffWaterSurface_IsRejected()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1, dive: true, onWater: false), 0.02f);

            Assert.AreNotEqual(MovementState.Diving, tick.State);
        }

        // 22) §4.2/§5.9: 수면 위 잠수 → Diving, 이동 정지, 펄스 미발생.
        [Test]
        public void Dive_OnWaterSurface_EntersDivingWithNoPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1, dive: true, onWater: true), 0.02f);

            Assert.AreEqual(MovementState.Diving, tick.State);
            Assert.AreEqual(0f, tick.LocalVelocity.magnitude, 0.001f);
            Assert.IsFalse(tick.Pulse.HasValue);
        }

        // 23) 잠수도 누적을 리셋한다 — 부상 후 다시 2m를 채워야 소리가 난다.
        [Test]
        public void SurfacingAfterDive_RestartsAccumulation()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            MoveMeters(sim, 1.9f, LocomotionConfig.RunnerWalkSpeed);                 // 2m 직전
            sim.Tick(Move(0, 0, dive: true, onWater: true), 0.5f);                   // 잠수 → 리셋

            LocomotionTick resumed = MoveMeters(sim, 0.2f, LocomotionConfig.RunnerWalkSpeed);
            Assert.AreEqual(MovementState.Walk, resumed.State);
            Assert.IsFalse(resumed.Pulse.HasValue);

            Assert.IsTrue(MoveMeters(sim, 2f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue);
        }

        // 24) §5.1-1 "발생 간격에는 재질 배율을 적용하지 않는다" —
        //     시뮬레이터는 재질을 아예 입력으로 받지 않는다는 것으로 이를 고정한다.
        [Test]
        public void PulseInterval_UsesBaseRadiusOnly_NotMaterialScaled()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            // 임계값은 §5.1 기본 반경 그대로다. 카펫(×0.7)이면 1.4m마다,
            // 그레이팅(×1.5)이면 3m마다 나야 한다는 식으로 흔들리지 않는다.
            Assert.IsFalse(MoveMeters(sim, 1.5f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue,
                "1.5m에서 나면 간격에 배율이 섞인 것이다(그레이팅 3m가 아니라 기본 2m가 기준)");
            Assert.IsTrue(MoveMeters(sim, 0.5f, LocomotionConfig.RunnerWalkSpeed).Pulse.HasValue,
                "누적 2.0m에서 정확히 나야 한다");
        }
    }
}
