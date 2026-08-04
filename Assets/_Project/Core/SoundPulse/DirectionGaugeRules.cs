using Marco.Core.Role;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §3.4 방향 게이지의 트리거·사거리 규칙(스프린트 26c, GAP-4 해소).
    ///
    /// **§5.4의 "방향 힌트(항상 표시)"와는 다른 기능이다.** 그쪽은 판정을 통과한 모든 파문에
    /// 대해 누구에게나 뜨는 vignette이고, 이 게이지는 §3.4가 못박은 **술래 전용 정보 우위**다.
    ///
    /// **§3.4 원문**:
    /// <code>
    /// - 속삭임 등급은 트리거하지 않는다. 대화·고함 등급만 트리거된다.
    /// - 밸브 회전음, 발소리, 노크는 트리거 대상에서 제외된다
    ///   (이 게이지는 어디까지나 "말"에 대한 술래 전용 정보 우위).
    ///
    /// | 발화 등급 | 물리 반경 | 방향 게이지 최대 사거리(물리 반경 × 1.5) |
    /// | 대화     | 9m       | 13.5m |
    /// | 고함     | 22m      | 33m   |
    ///
    /// - 8방위 스냅, 표시 지속시간은 §5.7의 perceivedDuration과 동일.
    /// </code>
    ///
    /// > 원문 주석: "맵 전체(45m×35m, 대각선 약 57m)를 기준으로 고함조차 맵 전체를 커버하지
    /// > 못하도록 설계해, 한 번의 실수가 맵 전체에 광고되는 최악의 시나리오를 방지한다."
    /// </summary>
    public static class DirectionGaugeRules
    {
        /// <summary>§3.4 "물리 반경 × 1.5".</summary>
        public const float RangeMultiplier = 1.5f;

        /// <summary>
        /// 이 소리가 방향 게이지를 트리거하는가. **대화·고함만** — 속삭임은 §3.1 표에서
        /// "방향 게이지 트리거 안 됨"으로 명시돼 있고, 발소리·밸브·노크도 제외 대상이다.
        /// </summary>
        public static bool TriggersGauge(SoundType type) =>
            type == SoundType.Talk || type == SoundType.Shout;

        /// <summary>
        /// 이 청취자가 게이지를 볼 수 있는가. §3.4가 "술래 전용 정보 우위"로 못박았다.
        /// 메아리는 관전자라 대상이 아니다(§3.1 방향 게이지 = "없음").
        /// </summary>
        public static bool CanSeeGauge(RoleType listenerRole) => listenerRole == RoleType.Seeker;

        /// <summary>
        /// 게이지 최대 사거리 = 물리 반경 × 1.5. 트리거 대상이 아니면 0.
        ///
        /// **§5.7 역할 배율(술래 ×1.2)과는 별개다** — §3.4 표가 "물리 반경(5.1)"을 기준으로
        /// 잡았으므로 배율이 곱해지지 않은 원본 반경에서 계산한다. 표의 값(대화 13.5m,
        /// 고함 33m)이 그 해석에서만 나온다(9×1.5=13.5, 22×1.5=33).
        /// </summary>
        public static float MaxRange(SoundType type, float physicalRadius)
        {
            if (!TriggersGauge(type) || physicalRadius <= 0f)
                return 0f;

            return physicalRadius * RangeMultiplier;
        }

        /// <summary>
        /// 이 발화가 이 청취자의 게이지를 밝히는가 — 역할·등급·거리를 모두 만족해야 한다.
        /// </summary>
        public static bool ShouldLight(SoundType type, RoleType listenerRole, float physicalRadius, float distance)
        {
            if (!CanSeeGauge(listenerRole) || !TriggersGauge(type))
                return false;

            if (distance < 0f)
                return false;

            return distance <= MaxRange(type, physicalRadius);
        }
    }
}
