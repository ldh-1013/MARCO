using System;

namespace Marco.Core.Locomotion
{
    /// <summary>
    /// §3.1 질주 스태미나 수치. "<b>5초. 소진 시 3초간 이동속도 -20%</b>".
    /// </summary>
    public static class SprintConfig
    {
        /// <summary>§3.1 질주 스태미나 총량(초).</summary>
        public const float StaminaSeconds = 5f;

        /// <summary>§3.1 소진 페널티 지속(초).</summary>
        public const float ExhaustPenaltySeconds = 3f;

        /// <summary>
        /// §3.1 소진 페널티 이동속도 배율(-20%).
        ///
        /// <para>
        /// <b>§5.9-1 질식 페널티(<c>BreathConfig.ChokeSpeedMultiplier</c>)와 값이 같지만 재사용하지 않는다.</b>
        /// 둘은 기획서의 <b>다른 절</b>(§3.1 vs §5.9-1)에 따로 적힌 규칙이고 테스트 범위도 따로 조정된다 —
        /// 한쪽을 튜닝했을 때 다른 쪽이 딸려 움직이면 그것이 사고다(더블체크 8의 판단 근거: "같은 값"이
        /// 아니라 "같은 규칙"일 때만 공유한다). 겹칠 때의 처리는 <see cref="CombineSpeedMultipliers"/>.
        /// </para>
        /// </summary>
        public const float ExhaustSpeedMultiplier = 0.8f;

        /// <summary>
        /// 스태미나 회복 속도(초당 초). <b>기획서에 회복 규칙이 없다 — GAP-92, 잠정값.</b>
        ///
        /// <para>
        /// 잠정 규칙: 질주하지 않는 동안 <b>소모와 같은 속도(1:1)</b>로 회복하고, 소진 페널티 3초 동안은
        /// 회복하지 않는다. 새 크기를 만들지 않으려고 소모율(1초에 1초)을 그대로 뒤집었다 —
        /// 0에서 가득 차기까지 5초. §10.2-1 "질주 5초 + 소진 페널티 3초 + 걷기"의 편도 계산과
        /// §10.4 "L1(96m)은 3회 분할"이 회복을 전제하지만 속도는 적혀 있지 않다.
        /// </para>
        /// </summary>
        public const float RecoveryPerSecond = 1f;

        /// <summary>
        /// §10.2 "스프린트 1회 사거리 = 7.5 m/s × 5초 = <b>37.5m</b>". 유도값 — 상수로 적지 않는다.
        /// </summary>
        public static float SingleSprintRangeMeters => LocomotionConfig.RunnerSprintSpeed * StaminaSeconds;

        /// <summary>
        /// 질식(§5.9-1)과 스태미나 소진(§3.1) 페널티가 <b>겹칠 때</b>의 배율. <b>기획서에 없다 — GAP-93.</b>
        ///
        /// <para>
        /// 결정: <b>곱하지 않고 더 느린 쪽 하나만</b> 적용한다(0.8 × 0.8 = 0.64가 아니라 0.8).
        /// 각 페널티는 자기 타이머를 따로 센다(한쪽이 끝나도 다른 쪽이 남아 있으면 0.8 유지).
        /// 근거 — §9.4 "이중 보정 금지"는 <i>같은 성질</i>의 보정을 두 번 거는 것을 금지한다.
        /// 두 페널티는 원인만 다르고 성질(이동속도 -20%, 3초)이 같다. 곱하면 두 절이 각자 정한
        /// "-20%"라는 크기가 어디에도 없는 -36%가 된다.
        /// </para>
        /// </summary>
        public static float CombineSpeedMultipliers(float a, float b) => Math.Min(a, b);
    }

    /// <summary>
    /// §3.1 질주 스태미나 상태. 도망자 전용이며 <see cref="LocomotionSimulator"/>가 소유한다.
    /// UnityEngine에 의존하지 않는다 — EditMode 테스트 가능.
    /// </summary>
    public sealed class SprintStamina
    {
        private const float NumericTolerance = 1e-4f;

        public float Remaining { get; private set; } = SprintConfig.StaminaSeconds;

        /// <summary>소진 페널티 잔여(초). 0보다 크면 감속 중이다.</summary>
        public float PenaltyRemaining { get; private set; }

        public bool IsExhausted => PenaltyRemaining > 0f;

        /// <summary>남은 스태미나가 있고 페널티 중이 아니어야 질주할 수 있다.</summary>
        public bool CanSprint => Remaining > 0f && !IsExhausted;

        /// <summary>§3.1 소진 페널티 배율(-20%). 평소 1.0.</summary>
        public float SpeedMultiplier => IsExhausted ? SprintConfig.ExhaustSpeedMultiplier : 1f;

        public float Normalized => Remaining / SprintConfig.StaminaSeconds;

        /// <summary>
        /// 시간을 진전시킨다. <paramref name="sprinting"/>은 <b>이번 틱에 실제로 질주했는가</b>
        /// (이동 중 + Shift + 도망자 + <see cref="CanSprint"/>)다. 소진되면 true를 돌려준다(1회성).
        /// </summary>
        public bool Tick(bool sprinting, float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return false;

            if (sprinting)
            {
                // 부동소수 누적 허용오차 — 프레임 dt를 더해 5.00초에 닿아도 1e-7이 남아 한 프레임 더
                // 질주하는 일을 막는다. 설계 수치가 아니라 수치 오차 처리다.
                Remaining -= deltaSeconds;
                if (Remaining > NumericTolerance)
                    return false;

                Remaining = 0f;

                PenaltyRemaining = SprintConfig.ExhaustPenaltySeconds;
                return true;
            }

            if (IsExhausted)
            {
                // GAP-92 잠정: 페널티 동안에는 회복하지 않는다.
                PenaltyRemaining -= deltaSeconds;
                if (PenaltyRemaining <= NumericTolerance)
                    PenaltyRemaining = 0f;
                return false;
            }

            Remaining = Math.Min(SprintConfig.StaminaSeconds, Remaining + deltaSeconds * SprintConfig.RecoveryPerSecond);
            return false;
        }

        public void Reset()
        {
            Remaining = SprintConfig.StaminaSeconds;
            PenaltyRemaining = 0f;
        }
    }
}
