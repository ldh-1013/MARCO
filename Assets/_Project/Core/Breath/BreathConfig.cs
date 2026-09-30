using Marco.Core.Locomotion;

namespace Marco.Core.Breath
{
    /// <summary>
    /// §5.9-1 숨 게이지의 **상호 배타적 3상태**.
    ///
    /// **회복 대기는 여기 없다.** §5.9-1 표가 회복 대기를 *"위 3상태와 별개로, 마지막 소모
    /// 시점부터 2초간 유지되는 <b>플래그</b>"* 로 정의하기 때문이다 — 잠수하면서 동시에
    /// 회복 대기일 수 있으므로 같은 enum에 넣으면 표현할 수 없는 조합이 생긴다.
    /// 플래그는 <see cref="BreathGauge.IsRecoveryDelayed"/>가 들고 있다.
    /// </summary>
    public enum BreathZone
    {
        /// <summary>§5.9-1 잠수 — 카메라(머리)가 수면 아래. 초당 -1.</summary>
        Submerged,

        /// <summary>§5.9-1 수면 — 몸이 물 안, 머리는 수면 위. 초당 +2.</summary>
        Surface,

        /// <summary>§5.9-1 물 밖 — 콜라이더가 물 볼륨과 완전 분리. 초당 +4.</summary>
        OutOfWater
    }

    /// <summary>
    /// §5.9-1 숨 게이지 수치. 기획서 원문에서 옮긴 값이다 — 총량만 10-01 플레이 보고로 20초가 됐다(아래).
    ///
    /// **게이지는 하나뿐이다(§5.9-1 첫 줄).** 잠수와 비명 억제(§3.5)가 같은 게이지를 공유한다.
    /// </summary>
    public static class BreathConfig
    {
        /// <summary>
        /// §5.9-1 숨 게이지 총량 <b>20초</b>(v0.3 8초 → v0.4 12초 → 10-01 20초). <b>총량은 이 상수 한 곳만 정의한다.</b>
        ///
        /// <para>
        /// 10-01 플레이 보고 "최후 생존자 페이즈의 배수구를 절대 못 연다": 동시 개방 0개면 배수구 총 점유(진입 1 + T 14 + 부상 1)
        /// 16초가 12초를 넘어 작업 도중 강제 부상했고, 진행도 감쇠(유예 3초 뒤 −0.10/s = 작업 1.4초/s)가 수면 회복(대기 2초 + 초당 2)보다
        /// 빨라 다음 잠수로도 따라잡지 못했다. 20초면 동시 개방 0~3개 모두 한 번 잠수로 끝난다(잔여 4 · 7 · 10 · 13초).
        /// </para>
        /// <para>소모(초당 −1) · 회복(수면 +2 · 물 밖 +4, 대기 2초) · 비명 억제 비용(4.5)은 바꾸지 않았다 — 총량만 바뀌었다.</para>
        /// </summary>
        public const float TotalSeconds = 20f;

        /// <summary>§5.9-1 소모 표 "잠수 유지 — 초당 -1(연속)".</summary>
        public const float DivePerSecond = 1f;

        /// <summary>
        /// §5.9-1 [v0.4] 소모 표 "비명 억제 — <b>-4.5</b>(즉시, 1회성)".
        /// §3.5 "숨 참기(선딜레이 1초 안에 입력) | 숨 게이지 <b>4.5초</b>".
        ///
        /// v0.4(8→12초)에서는 총량과 함께 3→4.5로 올려 비율을 고정했다(§5.9-1 "12 ÷ 4.5 = 2.67회로 비율을 고정해 3.5절 밸런스를
        /// 보존"). <b>10-01 총량 20초 상향 때는 지시로 이 값을 그대로 두었다</b> — 연속 억제가 2회 → 4회(20 ÷ 4.5 = 4.44)로 늘어
        /// §3.5 외침의 위력이 약해진다. 비율을 되돌리려면 이 값을 7.5(= 20 × 4.5 ÷ 12)로 올리면 된다.
        /// </summary>
        public const float SuppressionCost = 4.5f;

        /// <summary>§5.9-1 회복 "상태 = 수면 → 초당 +2".</summary>
        public const float SurfaceRecoveryPerSecond = 2f;

        /// <summary>
        /// §5.9-1 회복 "상태 = 물 밖 → 초당 +4".
        ///
        /// **기존 "물 밖이면 즉시 100% 회복" 규칙은 §5.9-1 갱신으로 삭제됐다** —
        /// 그 규칙은 ①육상에서 억제 비용이 0이 되고 ②잠수↔물 밖 반복으로 게이지가
        /// 무제한이 되는 두 악용을 만들었다.
        /// </summary>
        public const float OutOfWaterRecoveryPerSecond = 4f;

        /// <summary>§5.9-1 "회복 대기 — 마지막 소모 시점부터 2초간".</summary>
        public const float RecoveryDelaySeconds = 2f;

        /// <summary>§5.9-1 질식 페널티 "이동속도 -20% 3초".</summary>
        public const float ChokePenaltySeconds = 3f;

        /// <summary>§5.9-1 질식 페널티 "-20%" → 속도 배율 0.8.</summary>
        public const float ChokeSpeedMultiplier = 0.8f;

        /// <summary>
        /// §5.9-1 연속 억제 최대 횟수 = 총량 ÷ 억제 비용(내림).
        ///
        /// **상수로 저장하지 않고 나눗셈으로 유도한다.** 총량이나 비용이 바뀌면 이 값도 따라
        /// 바뀌어야 하는데, 따로 적어 두면 셋이 어긋난 채 남는다. v0.4: 12 ÷ 4.5 = 2.67 → 2회.
        /// <b>10-01: 20 ÷ 4.5 = 4.44 → 4회</b>(억제 비용을 그대로 둔 결과 — <see cref="SuppressionCost"/> 참조).
        /// </summary>
        public static int MaxConsecutiveSuppressions => (int)(TotalSeconds / SuppressionCost);

        /// <summary>
        /// §5.9-1 회복 속도. 잠수 중에는 회복이 없으므로 0이다
        /// (잠수의 소모는 <see cref="DivePerSecond"/>가 따로 담당한다).
        /// </summary>
        public static float RecoveryPerSecond(BreathZone zone)
        {
            switch (zone)
            {
                case BreathZone.Surface: return SurfaceRecoveryPerSecond;
                case BreathZone.OutOfWater: return OutOfWaterRecoveryPerSecond;
                default: return 0f;
            }
        }

        /// <summary>
        /// §4.2 이동 상태 + 수면 존 여부 → §5.9-1 숨 상태.
        ///
        /// **잠수 상태를 새로 만들지 않았다.** <see cref="MovementState.Diving"/>이 이미
        /// §4.3 "잠수(Left Ctrl 홀드, 수면 위에서만)"의 진입 판정을 소유하고 있어,
        /// 숨 게이지는 그 결과를 **읽기만** 한다. 두 곳에서 잠수를 판정하면 반드시 어긋난다.
        /// </summary>
        public static BreathZone ZoneOf(MovementState state, bool isOnWaterSurface)
        {
            if (state == MovementState.Diving)
                return BreathZone.Submerged;

            return isOnWaterSurface ? BreathZone.Surface : BreathZone.OutOfWater;
        }
    }
}
