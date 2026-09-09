using Marco.Core.Role;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §3.4 방향 게이지의 트리거·사거리 규칙(스프린트 26c, GAP-4 해소).
    ///
    /// **§5.4의 "방향 힌트(항상 표시)"와는 다른 기능이다.** 그쪽은 판정을 통과한 모든 파문에
    /// 대해 누구에게나 뜨는 vignette이고, 이 게이지는 §3.4가 못박은 **술래 전용 정보 우위**다.
    ///
    /// **§3.4 원문(기획서 갱신본)**:
    /// <code>
    /// - 속삭임 등급은 트리거하지 않는다. 대화·고함 등급만 트리거된다.
    /// - 밸브 회전음, 발소리, 노크는 트리거 대상에서 제외된다
    ///   (이 게이지는 어디까지나 "말"에 대한 술래 전용 정보 우위).
    ///
    /// | 발화 등급 | 발생 반경 | 술래 청취 반경(×1.2) | 방향 게이지 최대 사거리 |
    /// | 대화     | 9m       | 10.8m                | 10.8m |
    /// | 고함     | 22m      | 26.4m                | 26.4m |
    ///
    /// - 8방위 스냅, 표시 지속시간은 §5.7의 perceivedDuration과 동일.
    /// </code>
    ///
    /// **[갱신] 사거리 배율 ×1.5 → ×1.2.** 기존 `대화 13.5m / 고함 33m`은 발생 반경 × 1.5로
    /// 계산한 값이었으나, 술래의 **반경** 청취 배율은 ×1.2(§5.7)이므로 **청취 반경보다 큰**
    /// 논리적 모순이었다 — 인지하지도 못한 소리에 방향 게이지가 뜨게 된다.
    /// §5.0의 불변 조건 `방향 표시 반경 ≤ 청취 반경`에 맞춰 **청취 반경과 동일하게** 정정했다.
    /// ×1.5는 반경이 아니라 **지속시간 배율**이며 그 값은 그대로 유지한다.
    ///
    /// > 원문 주석: "맵 전체(52m×40m, 대각선 약 65.6m)를 기준으로 고함조차 맵 전체를 커버하지
    /// > 못하도록 설계해, 한 번의 실수가 맵 전체에 광고되는 최악의 시나리오를 방지한다."
    /// </summary>
    public static class DirectionGaugeRules
    {
        /// <summary>
        /// §3.4 사거리 배율. **술래의 반경 청취 배율(×1.2)과 동일하다** —
        /// 방향 표시 반경은 청취 반경을 넘을 수 없기 때문이다(§5.0 불변 조건).
        /// 기획서 갱신으로 ×1.5 → ×1.2.
        /// </summary>
        public const float RangeMultiplier = 1.2f;

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
        /// 게이지 최대 사거리 = **발생 반경 × 1.2**(= 술래 청취 반경). 트리거 대상이 아니면 0.
        ///
        /// **인자는 §5.1 발생 반경**(재질 배율·차폐가 적용되지 않은 원본)이다. 여기에
        /// 술래의 반경 청취 배율 ×1.2를 곱하면 §3.4 표의 값이 그대로 나온다
        /// (9×1.2=10.8, 22×1.2=26.4). 즉 방향 표시 반경 = 청취 반경이며,
        /// §5.0 불변 조건 `방향 표시 ≤ 청취`가 등호로 성립한다.
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
