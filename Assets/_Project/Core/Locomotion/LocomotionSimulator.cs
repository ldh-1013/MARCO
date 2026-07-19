using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Core.Locomotion
{
    /// <summary>틱 입력. Presentation 레이어가 Input System에서 읽어 채운다.</summary>
    public readonly struct LocomotionInput
    {
        /// <summary>WASD 축 (-1..1). 정규화 전 원시값.</summary>
        public readonly Vector2 MoveAxis;

        /// <summary>§4.3 질주(Left Shift 홀드).</summary>
        public readonly bool SprintHeld;

        /// <summary>§4.3 잠수(Left Ctrl 홀드).</summary>
        public readonly bool DiveHeld;

        /// <summary>§4.3 "수면 위에서만" — 수면 존 안인지. 존 감지는 호출자 책임.</summary>
        public readonly bool IsOnWaterSurface;

        public LocomotionInput(Vector2 moveAxis, bool sprintHeld, bool diveHeld, bool isOnWaterSurface)
        {
            MoveAxis = moveAxis;
            SprintHeld = sprintHeld;
            DiveHeld = diveHeld;
            IsOnWaterSurface = isOnWaterSurface;
        }
    }

    /// <summary>이번 틱에 방출할 발소리 펄스(§5.1 걷기/질주). 없으면 null.</summary>
    public readonly struct FootstepPulse
    {
        public readonly SoundType Type;      // Walk 또는 Sprint
        public readonly float Radius;
        public readonly float Duration;

        public FootstepPulse(SoundType type, float radius, float duration)
        {
            Type = type;
            Radius = radius;
            Duration = duration;
        }
    }

    /// <summary>틱 결과. Presentation은 LocalVelocity를 CharacterController.Move에 넘기기만 한다.</summary>
    public readonly struct LocomotionTick
    {
        public readonly MovementState State;

        /// <summary>플레이어 로컬 기준 수평 속도(m/s). y는 항상 0 — 중력은 컨트롤러 담당.</summary>
        public readonly Vector3 LocalVelocity;

        public readonly FootstepPulse? Pulse;

        public LocomotionTick(MovementState state, Vector3 localVelocity, FootstepPulse? pulse)
        {
            State = state;
            LocalVelocity = localVelocity;
            Pulse = pulse;
        }
    }

    /// <summary>
    /// §4.2 이동 상태기계 + §5.1 발소리 펄스 타이밍의 순수 로직.
    /// UnityEngine.Time·Input·Physics를 일절 참조하지 않아 EditMode 테스트가 가능하다.
    ///
    /// 구현 결정(기획서 문자 그대로):
    /// - 발소리 등급은 상태가 아니라 실제 속도로 판정한다(§5.1 발생 조건 열).
    ///   따라서 술래(5.4m/s)는 이동만 해도 질주 등급(6m) 펄스가 난다 — §5.1의
    ///   문자적 귀결이며, 러너에게 술래 접근 경보로 작용한다(플레이테스트 검증 항목).
    /// - 메아리는 발소리 펄스를 내지 않는다 — §3.2 "상시 비행형"(지면 접촉 없음).
    /// - 잠수(Diving) 중에는 펄스가 발생하지 않고(§5.9 "잠수 시 파문 미발생")
    ///   수평 이동도 0이다(잠수 = 정지 은신, §5.9 게이지 항목의 "잠수 중" 상태).
    /// - GAP-8: 펄스 발생 주기 = 해당 등급 지속시간(걷기 0.4s, 질주 0.8s).
    ///   이동 중 파문 커버리지가 끊기지 않는 최소 빈도.
    /// - 질주는 이동 중에만(§4.3), 도망자 전용(§3.1).
    /// </summary>
    public sealed class LocomotionSimulator
    {
        private readonly RoleType _role;
        private float _timeSinceLastPulse;
        private bool _hasEmittedFirstPulse;

        public LocomotionSimulator(RoleType role)
        {
            _role = role;
        }

        public MovementState CurrentState { get; private set; } = MovementState.Idle;

        public LocomotionTick Tick(in LocomotionInput input, float deltaSeconds)
        {
            // §4.3: 잠수는 수면 위에서만 진입 가능. 홀드 방식.
            if (input.DiveHeld && input.IsOnWaterSurface)
            {
                CurrentState = MovementState.Diving;
                ResetPulseTimer();
                return new LocomotionTick(MovementState.Diving, Vector3.zero, null);
            }

            Vector2 axis = input.MoveAxis;
            if (axis.sqrMagnitude > 1f)
                axis = axis.normalized; // 대각 입력이 직선보다 빨라지지 않도록

            bool isMoving = axis.sqrMagnitude > 0.0001f;
            if (!isMoving)
            {
                CurrentState = MovementState.Idle;
                ResetPulseTimer();
                return new LocomotionTick(MovementState.Idle, Vector3.zero, null);
            }

            bool sprinting = input.SprintHeld && LocomotionConfig.CanSprint(_role); // §3.1 + §4.3 이동 중에만
            float speed = sprinting ? LocomotionConfig.RunnerSprintSpeed : LocomotionConfig.BaseSpeed(_role);
            CurrentState = sprinting ? MovementState.Sprint : MovementState.Walk;

            Vector3 velocity = new Vector3(axis.x, 0f, axis.y) * speed;

            FootstepPulse? pulse = null;
            if (_role != RoleType.Echo) // §3.2 비행형 — 발소리 없음
            {
                // §5.1 발생 조건: 속도 ≤ 5.0 → 걷기(2m/0.4s), 속도 > 5.0 → 질주(6m/0.8s)
                bool sprintTier = speed > LocomotionConfig.FootstepSpeedThreshold;
                float interval = sprintTier ? LocomotionConfig.SprintPulseDuration : LocomotionConfig.WalkPulseDuration; // GAP-8

                _timeSinceLastPulse += deltaSeconds;
                if (!_hasEmittedFirstPulse || _timeSinceLastPulse >= interval)
                {
                    _hasEmittedFirstPulse = true;
                    _timeSinceLastPulse = 0f;
                    pulse = sprintTier
                        ? new FootstepPulse(SoundType.Sprint, LocomotionConfig.SprintPulseRadius, LocomotionConfig.SprintPulseDuration)
                        : new FootstepPulse(SoundType.Walk, LocomotionConfig.WalkPulseRadius, LocomotionConfig.WalkPulseDuration);
                }
            }

            return new LocomotionTick(CurrentState, velocity, pulse);
        }

        private void ResetPulseTimer()
        {
            _timeSinceLastPulse = 0f;
            _hasEmittedFirstPulse = false;
        }
    }
}
