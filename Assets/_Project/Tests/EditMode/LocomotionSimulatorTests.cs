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

            Assert.AreEqual(8.0f, tick.LocalVelocity.magnitude, 0.001f);
        }

        // 5) §3.1: 질주는 도망자 전용 — 술래·메아리는 Shift를 눌러도 기본 속도.
        [TestCase(RoleType.Seeker, 5.4f)]
        [TestCase(RoleType.Echo, 8.0f)]
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

        // 9) §5.1: 걷기 첫 틱에 걷기 펄스(2m/0.4s) 방출.
        [Test]
        public void Walk_FirstTick_EmitsWalkPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1), 0.02f);

            Assert.IsTrue(tick.Pulse.HasValue);
            Assert.AreEqual(SoundType.Walk, tick.Pulse.Value.Type);
            Assert.AreEqual(2f, tick.Pulse.Value.Radius, 0.001f);
            Assert.AreEqual(0.4f, tick.Pulse.Value.Duration, 0.001f);
        }

        // 10) §5.1: 질주 펄스는 6m/0.8s.
        [Test]
        public void Sprint_FirstTick_EmitsSprintPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1, sprint: true), 0.02f);

            Assert.IsTrue(tick.Pulse.HasValue);
            Assert.AreEqual(SoundType.Sprint, tick.Pulse.Value.Type);
            Assert.AreEqual(6f, tick.Pulse.Value.Radius, 0.001f);
            Assert.AreEqual(0.8f, tick.Pulse.Value.Duration, 0.001f);
        }

        // 11) GAP-8: 걷는 동안 0.4초 주기로만 펄스가 나온다.
        [Test]
        public void Walk_PulseCadence_FollowsDuration()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);
            int pulses = 0;

            // 1.0초를 0.02초 틱 50회로 진행: 최초 1회 + 0.4s + 0.8s = 3회
            for (int i = 0; i < 50; i++)
            {
                if (sim.Tick(Move(0, 1), 0.02f).Pulse.HasValue)
                    pulses++;
            }

            Assert.AreEqual(3, pulses);
        }

        // 12) 정지 시 펄스 없음 + 타이머 리셋: 다시 걸으면 즉시 첫 펄스.
        [Test]
        public void StoppingResetsPulseTimer()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            sim.Tick(Move(0, 1), 0.02f);              // 첫 펄스
            var idle = sim.Tick(Move(0, 0), 0.02f);   // 정지
            Assert.IsFalse(idle.Pulse.HasValue);

            var resumed = sim.Tick(Move(0, 1), 0.02f); // 재이동 → 즉시 펄스
            Assert.IsTrue(resumed.Pulse.HasValue);
        }

        // 13) §5.1 문자 그대로: 술래(5.4 > 5.0)는 이동만 해도 질주 등급(6m) 펄스.
        [Test]
        public void Seeker_Movement_EmitsSprintTierPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Seeker);

            var tick = sim.Tick(Move(0, 1), 0.02f);

            Assert.IsTrue(tick.Pulse.HasValue);
            Assert.AreEqual(SoundType.Sprint, tick.Pulse.Value.Type);
            Assert.AreEqual(6f, tick.Pulse.Value.Radius, 0.001f);
        }

        // 14) §3.2: 메아리는 이동해도 발소리 펄스가 없다(비행형).
        [Test]
        public void Echo_Movement_EmitsNoPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Echo);

            var tick = sim.Tick(Move(0, 1), 0.02f);

            Assert.IsFalse(tick.Pulse.HasValue);
        }

        // 15) §4.3: 잠수는 수면 위에서만 — 수면 밖에서는 Ctrl을 눌러도 진입 불가.
        [Test]
        public void Dive_OffWaterSurface_IsRejected()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1, dive: true, onWater: false), 0.02f);

            Assert.AreNotEqual(MovementState.Diving, tick.State);
        }

        // 16) §4.2/§5.9: 수면 위 잠수 → Diving, 이동 정지, 펄스 미발생.
        [Test]
        public void Dive_OnWaterSurface_EntersDivingWithNoPulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            var tick = sim.Tick(Move(0, 1, dive: true, onWater: true), 0.02f);

            Assert.AreEqual(MovementState.Diving, tick.State);
            Assert.AreEqual(0f, tick.LocalVelocity.magnitude, 0.001f);
            Assert.IsFalse(tick.Pulse.HasValue);
        }

        // 17) 잠수 해제 후 걸으면 즉시 첫 펄스(타이머가 리셋돼 있음).
        [Test]
        public void SurfacingAfterDive_ResumesWithImmediatePulse()
        {
            var sim = new LocomotionSimulator(RoleType.Runner);

            sim.Tick(Move(0, 1), 0.02f);                              // 걷기 + 첫 펄스
            sim.Tick(Move(0, 0, dive: true, onWater: true), 0.5f);    // 잠수
            var resumed = sim.Tick(Move(0, 1), 0.02f);                // 부상 후 걷기

            Assert.AreEqual(MovementState.Walk, resumed.State);
            Assert.IsTrue(resumed.Pulse.HasValue);
        }
    }
}
