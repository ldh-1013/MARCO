using Marco.Core.Role;

namespace Marco.Core.Locomotion
{
    /// <summary>
    /// §3.1/§6.2 역할별 이동 속도 상수. 모든 값은 baseline(§0 문서 갱신 원칙)이며
    /// M3 플레이테스트 결과에 따라 조정 대상이다.
    /// </summary>
    public static class LocomotionConfig
    {
        /// <summary>§3.1 도망자 기본(걷기) 속도.</summary>
        public const float RunnerWalkSpeed = 5.0f;

        /// <summary>§3.1 도망자 질주 속도.</summary>
        public const float RunnerSprintSpeed = 7.5f;

        /// <summary>§3.1 × §6.2 4인(MVP) 술래 속도: 5.0 × 1.08.</summary>
        public const float SeekerSpeed = 5.4f;

        /// <summary>
        /// §3.2 메아리(유령 카메라) 속도.
        ///
        /// **8.0 → 6.0으로 낮췄다(기획서 갱신).** 도망자 질주(7.5)보다 느려야
        /// 메아리가 러너를 따라다니며 실시간 중계하는 플레이가 성립하지 않는다.
        /// </summary>
        public const float EchoSpeed = 6.0f;

        /// <summary>§4.2 캐릭터 콜라이더 반경.</summary>
        public const float ColliderRadius = 0.35f;

        /// <summary>§5.1 걷기 발소리: 반경 2m, 지속 0.4s.</summary>
        public const float WalkPulseRadius = 2f;
        public const float WalkPulseDuration = 0.4f;

        /// <summary>§5.1 질주 발소리: 반경 6m, 지속 0.8s.</summary>
        public const float SprintPulseRadius = 6f;
        public const float SprintPulseDuration = 0.8f;

        /// <summary>
        /// §5.1 걷기/질주 판정 경계 속도. 걷기 = "이동속도 ≤ 5.0m/s 상태에서 이동",
        /// 질주 = "이동속도 > 5.0m/s" — 발소리 등급은 상태가 아니라 실제 속도로 판정한다.
        /// </summary>
        public const float FootstepSpeedThreshold = 5.0f;

        /// <summary>역할별 걷기(기본) 속도.</summary>
        public static float BaseSpeed(RoleType role)
        {
            switch (role)
            {
                case RoleType.Seeker: return SeekerSpeed;
                case RoleType.Echo: return EchoSpeed;
                default: return RunnerWalkSpeed;
            }
        }

        /// <summary>§3.1: 질주는 도망자 전용. 다른 역할은 기본 속도가 곧 최고 속도다.</summary>
        public static bool CanSprint(RoleType role)
        {
            return role == RoleType.Runner;
        }
    }
}
