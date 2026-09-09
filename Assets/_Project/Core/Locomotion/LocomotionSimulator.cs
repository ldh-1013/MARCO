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

        /// <summary>
        /// §5.9-1 "강제 부상" — 숨이 남아 있어 잠수를 유지할 수 있는가.
        /// <c>BreathGauge.CanSubmerge</c>를 그대로 넘긴다. false면 Ctrl을 눌러도 잠수하지 않는다.
        /// </summary>
        public readonly bool CanSubmerge;

        /// <summary>
        /// §5.9-1 질식 페널티 "이동속도 -20% 3초"의 배율(<c>BreathGauge.SpeedMultiplier</c>).
        /// 평소 1.0. 역할별 기본 속도에 곱해진다.
        /// </summary>
        public readonly float SpeedMultiplier;

        public LocomotionInput(Vector2 moveAxis, bool sprintHeld, bool diveHeld, bool isOnWaterSurface,
            bool canSubmerge = true, float speedMultiplier = 1f)
        {
            MoveAxis = moveAxis;
            SprintHeld = sprintHeld;
            DiveHeld = diveHeld;
            IsOnWaterSurface = isOnWaterSurface;
            CanSubmerge = canSubmerge;
            SpeedMultiplier = speedMultiplier > 0f ? speedMultiplier : 0f;
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
    /// - §5.1-1(갱신): 발소리는 **이동거리 기준**으로 난다 — "자신의 발생 반경과 같은 거리를
    ///   이동할 때마다 1회"(걷기 2m마다, 질주 6m마다). 새 상수를 만들지 않고
    ///   <see cref="LocomotionConfig.WalkPulseRadius"/>·<see cref="LocomotionConfig.SprintPulseRadius"/>를
    ///   그대로 임계값으로 쓴다. 기준 속도에서 시간 간격이 지속시간과 정확히 일치해
    ///   (2m ÷ 5.0m/s = 0.40s, 6m ÷ 7.5m/s = 0.80s) 이동 중 파문이 끊기지 않는다.
    ///
    ///   **시간 기준(구 GAP-8)을 대체한 것이다.** 핵심 차이는 <b>제자리에서는 소리가 나지
    ///   않는다</b>는 것 — 제자리 회전·미세 이동으로 파문을 양산하거나 회피할 수 없고
    ///   (§3.6 캠핑 방지와 정합), 1m당 소음량이 상수가 되어 §8 무성 생존상이 계산 가능해진다.
    ///
    ///   **재질 배율은 여기 곱하지 않는다**(§5.1-1 "발생 간격에는 재질 배율을 적용하지 않는다").
    ///   간격이 구역마다 흔들리면 파문 점멸 리듬이 달라지고 재질 효과가 소음량에서 상쇄된다.
    ///   재질은 오직 **발생 반경**에만 적용되며, 그 적용 지점은 파문을 등록하는 쪽이다.
    /// - 질주는 이동 중에만(§4.3), 도망자 전용(§3.1).
    /// </summary>
    public sealed class LocomotionSimulator
    {
        private readonly RoleType _role;

        /// <summary>
        /// §5.1-1 등급별 누적 이동거리(m). <b>걷기와 질주를 따로 센다</b> — 등급이 바뀌었다고
        /// 진행 중이던 누적을 버리면 걷기↔질주를 번갈아 눌러 발소리를 지울 수 있다.
        /// 임계값 도달 시 0으로 밀지 않고 <b>임계값만큼 빼서</b> 나머지를 다음 파문으로 넘긴다 —
        /// 그래야 프레임률에 따라 1m당 파문 수가 달라지지 않는다.
        /// </summary>
        private float _walkDistance;
        private float _sprintDistance;

        public LocomotionSimulator(RoleType role)
        {
            _role = role;
        }

        public MovementState CurrentState { get; private set; } = MovementState.Idle;

        public LocomotionTick Tick(in LocomotionInput input, float deltaSeconds)
        {
            // §4.3: 잠수는 수면 위에서만 진입 가능. 홀드 방식.
            // §5.9-1: 숨이 0이면 "강제 부상" — 홀드 중이어도 잠수로 들어가지 않는다.
            if (input.DiveHeld && input.IsOnWaterSurface && input.CanSubmerge)
            {
                CurrentState = MovementState.Diving;
                ResetPulseDistance();
                return new LocomotionTick(MovementState.Diving, Vector3.zero, null);
            }

            Vector2 axis = input.MoveAxis;
            if (axis.sqrMagnitude > 1f)
                axis = axis.normalized; // 대각 입력이 직선보다 빨라지지 않도록

            bool isMoving = axis.sqrMagnitude > 0.0001f;
            if (!isMoving)
            {
                // §5.1-1의 핵심: 정지 중에는 이동거리가 쌓이지 않으므로 발소리가 나지 않는다.
                // 제자리 회전은 MoveAxis가 0이라 여기로 들어온다.
                CurrentState = MovementState.Idle;
                ResetPulseDistance();
                return new LocomotionTick(MovementState.Idle, Vector3.zero, null);
            }

            bool sprinting = input.SprintHeld && LocomotionConfig.CanSprint(_role); // §3.1 + §4.3 이동 중에만

            // §5.9-1 질식 페널티는 **실제 이동속도**를 낮춘다. 그래서 발소리 등급 판정
            // (§5.1 "이동속도 ≤ 5.0m/s")과 §5.1-1 이동거리 누적에도 그대로 반영된다 —
            // 느리게 움직이면 실제로 더 조용해지는 것이 §5.1의 문자 그대로다.
            float speed = (sprinting ? LocomotionConfig.RunnerSprintSpeed : LocomotionConfig.BaseSpeed(_role))
                          * input.SpeedMultiplier;
            CurrentState = sprinting ? MovementState.Sprint : MovementState.Walk;

            Vector3 velocity = new Vector3(axis.x, 0f, axis.y) * speed;

            FootstepPulse? pulse = null;
            if (_role != RoleType.Echo) // §3.2 비행형 — 발소리 없음
            {
                // §5.1 발생 조건: 속도 ≤ 5.0 → 걷기(2m), 속도 > 5.0 → 질주(6m)
                bool sprintTier = speed > LocomotionConfig.FootstepSpeedThreshold;

                // 이번 틱에 실제로 나아간 거리. 시뮬레이터가 낸 속도를 그대로 적분한다.
                float moved = speed * (deltaSeconds > 0f ? deltaSeconds : 0f);

                if (sprintTier)
                {
                    _sprintDistance += moved;
                    if (_sprintDistance >= LocomotionConfig.SprintPulseRadius)
                    {
                        _sprintDistance -= LocomotionConfig.SprintPulseRadius;
                        pulse = new FootstepPulse(SoundType.Sprint, LocomotionConfig.SprintPulseRadius, LocomotionConfig.SprintPulseDuration);
                    }
                }
                else
                {
                    _walkDistance += moved;
                    if (_walkDistance >= LocomotionConfig.WalkPulseRadius)
                    {
                        _walkDistance -= LocomotionConfig.WalkPulseRadius;
                        pulse = new FootstepPulse(SoundType.Walk, LocomotionConfig.WalkPulseRadius, LocomotionConfig.WalkPulseDuration);
                    }
                }
            }

            return new LocomotionTick(CurrentState, velocity, pulse);
        }

        private void ResetPulseDistance()
        {
            _walkDistance = 0f;
            _sprintDistance = 0f;
        }
    }
}
