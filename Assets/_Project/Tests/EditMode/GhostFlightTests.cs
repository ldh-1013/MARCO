using Marco.Core.Locomotion;
using Marco.Core.Role;
using Marco.Core.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 27 후속: §3.2 메아리 자유 비행의 **순수 규칙**을 고정한다.
    ///
    /// 검증 대상은 "어느 방향으로 얼마나 빠르게"까지다. 충돌 해제(<c>CharacterController</c>)와
    /// 실제 <c>Transform</c> 이동은 Presentation·Unity 수명주기라 여기서 다루지 않는다.
    /// </summary>
    public class GhostFlightTests
    {
        private static readonly Vector3 Forward = Vector3.forward;
        private static readonly Vector3 Right = Vector3.right;

        // ── 역할 게이트 (§3.2 메아리 전용) ────────────────────────────────

        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Seeker)]
        public void IsFlying_NonEcho_IsFalse(RoleType role)
        {
            Assert.IsFalse(GhostFlight.IsFlying(role));
        }

        [Test]
        public void IsFlying_Echo_IsTrue()
        {
            Assert.IsTrue(GhostFlight.IsFlying(RoleType.Echo));
        }

        // ── §3.2 이동속도 6.0 m/s ────────────────────────────────────────

        [Test]
        public void Speed_MatchesDesignDoc()
        {
            // §3.2 "이동속도 6.0 m/s"(기획서 갱신 8.0 → 6.0). 지상 이동과 같은 상수를 공유해야 값이 갈라지지 않는다.
            Assert.AreEqual(6.0f, GhostFlight.SpeedMetersPerSecond);
            Assert.AreEqual(LocomotionConfig.EchoSpeed, GhostFlight.SpeedMetersPerSecond);
        }

        [Test]
        public void Velocity_ForwardInput_MovesAtFullSpeed()
        {
            Vector3 v = GhostFlight.Velocity(new Vector2(0f, 1f), 0f, Forward, Right);

            Assert.AreEqual(6.0f, v.magnitude, 0.001f);
            Assert.AreEqual(1f, Vector3.Dot(v.normalized, Forward), 0.001f);
        }

        [Test]
        public void Velocity_DiagonalInput_DoesNotExceedSpeed()
        {
            // §4.2 "대각 입력이 직선보다 빨라지지 않도록" — 비행에도 같은 규칙을 적용한다.
            Vector3 v = GhostFlight.Velocity(new Vector2(1f, 1f), 1f, Forward, Right);

            Assert.AreEqual(6.0f, v.magnitude, 0.001f, "3축을 동시에 눌러도 6.0m/s를 넘으면 안 된다.");
        }

        [Test]
        public void Velocity_NoInput_IsZero()
        {
            Assert.AreEqual(Vector3.zero, GhostFlight.Velocity(Vector2.zero, 0f, Forward, Right));
        }

        // ── 3축 비행 (지상 이동과 다른 지점) ──────────────────────────────

        [Test]
        public void Velocity_VerticalOnly_MovesStraightUpOrDown()
        {
            Vector3 up = GhostFlight.Velocity(Vector2.zero, 1f, Forward, Right);
            Vector3 down = GhostFlight.Velocity(Vector2.zero, -1f, Forward, Right);

            Assert.AreEqual(6.0f, up.y, 0.001f);
            Assert.AreEqual(-6.0f, down.y, 0.001f);
        }

        [Test]
        public void Velocity_FollowsCameraAim_NotWorldAxes()
        {
            // §3.2 "자유 비행형 유령 카메라" — 바라보는 방향으로 날아간다.
            // 아래를 45° 내려다보는 카메라로 전진하면 실제로 아래로 내려가야 한다.
            Vector3 tiltedForward = new Vector3(0f, -1f, 1f).normalized;
            Vector3 v = GhostFlight.Velocity(new Vector2(0f, 1f), 0f, tiltedForward, Right);

            Assert.Less(v.y, 0f, "카메라가 아래를 보는데 고도가 유지되면 자유 비행이 아니다.");
            Assert.AreEqual(6.0f, v.magnitude, 0.001f);
        }

        [Test]
        public void Velocity_UnnormalizedCameraAxes_StillYieldsExactSpeed()
        {
            // 호출자가 정규화하지 않은 축을 넘겨도 속도가 부풀지 않아야 한다.
            Vector3 v = GhostFlight.Velocity(new Vector2(0f, 1f), 0f, Forward * 7f, Right * 3f);

            Assert.AreEqual(6.0f, v.magnitude, 0.001f);
        }

        // ── §5.1 "메아리는 발소리 없음" (GAP-61 2번과 맞물리는 지점) ──────

        [Test]
        public void Simulator_AsEcho_NeverEmitsFootstepPulse()
        {
            // §3.2 비행형이라 발소리가 없다. 역할 전이 후 시뮬레이터가 새로 만들어져야
            // 이 성질이 실제로 적용된다(스프린트 27 후속에서 ApplyRole이 재생성하도록 고쳤다).
            var sim = new LocomotionSimulator(RoleType.Echo);
            var input = new LocomotionInput(new Vector2(0f, 1f), sprintHeld: false,
                diveHeld: false, isOnWaterSurface: false);

            for (int i = 0; i < 120; i++)
                Assert.IsFalse(sim.Tick(input, 0.02f).Pulse.HasValue, $"{i}번째 틱에서 발소리가 났다.");
        }

        [Test]
        public void Simulator_AsRunner_StillEmitsFootstepPulse()
        {
            // 대조군 — 메아리 억제가 "전부 막기"로 잘못 구현되지 않았는지 확인한다.
            var sim = new LocomotionSimulator(RoleType.Runner);
            var input = new LocomotionInput(new Vector2(0f, 1f), sprintHeld: false,
                diveHeld: false, isOnWaterSurface: false);

            bool emitted = false;
            for (int i = 0; i < 120 && !emitted; i++)
                emitted = sim.Tick(input, 0.02f).Pulse.HasValue;

            Assert.IsTrue(emitted);
        }

        [Test]
        public void Simulator_AsEcho_UsesEchoSpeed()
        {
            // §3.2 6.0m/s가 지상 이동 경로에서도 같은 값으로 나오는지(상수 분기 방지).
            var sim = new LocomotionSimulator(RoleType.Echo);
            var input = new LocomotionInput(new Vector2(0f, 1f), sprintHeld: false,
                diveHeld: false, isOnWaterSurface: false);

            LocomotionTick tick = sim.Tick(input, 0.02f);

            Assert.AreEqual(LocomotionConfig.EchoSpeed, tick.LocalVelocity.magnitude, 0.001f);
        }

        // ── §3.4 노크는 게이지를 트리거하지 않는다 (회귀 감시) ────────────

        [Test]
        public void Knock_DoesNotTriggerSeekerGauge()
        {
            // 메아리 관련 변경이 §3.4 제외 규칙을 건드리지 않았는지 함께 고정한다.
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Knock));
        }
    }
}
