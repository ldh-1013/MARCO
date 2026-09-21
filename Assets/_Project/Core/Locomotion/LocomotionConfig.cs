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

        /// <summary>
        /// §3.1 술래 <b>기본</b> 이동속도. v0.3의 단일값 5.4f를 <b>기본 5.0 + 인원별 보정</b>으로
        /// 쪼갠 것이다(§6.2 [v0.4] 개정) — 5.4는 4인 기준 5.0 × 1.08이었고, v0.4에서
        /// 보정이 전 구간 하향됐다.
        /// </summary>
        public const float SeekerBaseSpeed = 5.0f;

        /// <summary>
        /// §6.2 [v0.4] 술래 이속 보정 <b>하한</b>. 절대 0%가 될 수 없다.
        ///
        /// <para>
        /// <b>근거</b>(§6.2 원문): §5.1은 발소리 등급을 <i>실제 속도</i>로 판정하며
        /// (걷기 ≤ 5.0 / 질주 &gt; 5.0) 술래 기본속도가 5.0이다. 보정이 0%가 되는 순간
        /// 술래 발소리가 <b>6m에서 2m로 떨어져</b> 도망자가 접근을 들을 수 없게 된다.
        /// 술래가 조용히 움직이는 수단은 §3.1 잠행(3.5 m/s)이며 그것이 의도된 카운터플레이다.
        /// </para>
        /// </summary>
        public const float SeekerSpeedCorrectionFloor = 0.01f;

        /// <summary>
        /// §6.2 [v0.4] 인원별 술래 이속 보정. 총원 2→+5% / 3→+5% / 4→+4% / 5→+6% / 6→+8%.
        ///
        /// <para>
        /// <b>회전 시간이 8초로 늘면서 인터셉트 빈도가 올라간 것을 상쇄한다</b>(§6.2) —
        /// 봉쇄가 아니라 순찰 중 마주칠 확률이 두 배 가까이 된다. 확정값이 아니라
        /// 플레이테스트 시작값이다.
        /// </para>
        ///
        /// <para>
        /// 하한 <see cref="SeekerSpeedCorrectionFloor"/>이 적용되므로 표 밖의 인원수에서도
        /// 술래가 무음이 되지 않는다.
        /// </para>
        /// </summary>
        public static float SeekerSpeedCorrection(int totalPlayers)
        {
            float correction;
            switch (totalPlayers)
            {
                case 2:
                case 3: correction = 0.05f; break;
                case 4: correction = 0.04f; break;
                case 5: correction = 0.06f; break;
                case 6: correction = 0.08f; break;
                default: correction = 0.05f; break; // 표 밖 — 3인 값을 폴백으로 쓴다
            }

            return correction < SeekerSpeedCorrectionFloor ? SeekerSpeedCorrectionFloor : correction;
        }

        /// <summary>
        /// §6.2-1 종반 압박 — <b>잔여 1분에 +5% 추가</b>. §6.2 표의 하향된 기준값 <i>위에</i>
        /// 곱으로 더해진다(§6.2-1 "이 +5%는 6.2절 표의 하향된 기준값 위에 더해진다",
        /// 5인 기준 5.30 → 5.57 = 5.30 × 1.05).
        ///
        /// <para>
        /// <b>§9.4 "이중 보정 금지"와 충돌하지 않는다.</b> 그 원칙이 금지하는 것은
        /// <i>같은 성질</i>의 보정을 두 번 거는 것이다(§9.4의 예: 인원이 늘면 요구 개수를
        /// 여는 속도도 빨라지므로 역류에 인원별 보정을 또 걸면 이중 보정). 여기서는
        /// 인원별 보정이 <b>인원수</b>에, 종반 보정이 <b>잔여 시간</b>에 붙어 축이 다르다.
        /// 기획서가 5.30 → 5.57이라는 결과값을 직접 적어 곱셈임을 확정했다.
        /// </para>
        /// </summary>
        public const float EndgameSpeedBonus = 0.05f;

        /// <summary>
        /// §6.2-1 종반 압박이 시작되는 잔여 시간(초). "잔여 1분".
        /// <b>1회 적용되고 라운드 종료까지 유지된다</b> — 껐다 켜지 않는다(경계에서
        /// 속도가 오락가락하면 추격 감각이 무너진다).
        /// </summary>
        public const float EndgamePressureSeconds = 60f;

        /// <summary>§6.2-1 저역 드론 시작 잔여 시간(초). 연출만, 규칙 변화 없음.</summary>
        public const float EndgameDroneSeconds = 120f;

        /// <summary>§6.2-1 출구 파문 시작 잔여 시간(초).</summary>
        public const float EndgameExitPulseSeconds = 30f;

        /// <summary>
        /// §3.1 술래 잠행 속도. <b>인원 보정을 받지 않는 고정값</b>이다 —
        /// 기획서가 "잠행 3.5 m/s"를 인원 표와 분리해 적었고, 보정을 걸면
        /// 6인에서 3.78 m/s가 되어 §5.1 걷기 경계(5.0) 안이라 효과는 같지만
        /// "조용한 이동"의 속도 손실이 인원에 따라 달라진다 — 근거가 없다(GAP-84).
        /// </summary>
        public const float SeekerStealthSpeed = 3.5f;

        /// <summary>
        /// §3.1 × §6.2 술래 실제 이동속도. <b>5.30을 상수로 박지 않는다</b> —
        /// 기본 5.0 × 인원별 보정으로 유도한다(5인: 5.0 × 1.06 = 5.30).
        /// </summary>
        public static float SeekerSpeedFor(int totalPlayers, bool endgamePressure = false)
        {
            float speed = SeekerBaseSpeed * (1f + SeekerSpeedCorrection(totalPlayers));
            if (endgamePressure)
                speed *= 1f + EndgameSpeedBonus;

            return speed;
        }

        /// <summary>
        /// v0.3 호환 상수. <b>새 코드는 <see cref="SeekerSpeedFor"/>를 쓴다</b> —
        /// 이 값은 인원을 모르는 로컬 단독 실행(스프린트 3~9 스모크 리그)의 폴백이며,
        /// §6.2 권장 구성 5인 기준값이다.
        /// </summary>
        public static float SeekerSpeed => SeekerSpeedFor(5);

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
