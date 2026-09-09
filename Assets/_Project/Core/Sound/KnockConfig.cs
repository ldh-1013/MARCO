namespace Marco.Core.Sound
{
    /// <summary>
    /// §3.2 메아리 노크 능력의 수치(스프린트 27). **전부 기획서 원문에서 옮긴 값**이며
    /// 임의로 만든 상수가 없다.
    ///
    /// **§3.2 원문(기획서 갱신본)**:
    /// <code>
    /// - 노크 능력:
    ///   - 쿨다운 25초, 라운드당 정확히 5회(재충전 없음), 메아리 전환 후 20초간 사용 불가
    ///   - 발생 위치: 메아리의 현재 위치. 지점 클릭 방식은 채택하지 않는다
    ///   - 발생 소음: "대화" 등급과 동일 취급 (반경 9m, 지속 1.2초) — 술래·도망자 모두 인지 가능
    ///   - 자기 파문은 자기 화면에 표시하지 않는다
    /// </code>
    ///
    /// §5.1 표의 노크 행(<c>노크 | Knock | 9m | 1.2초 | 술래 청취 10.8m</c>)과 두 절이 정확히 일치한다.
    ///
    /// **규칙 강제 위치**: 라운드당 5회 하드캡과 전환 후 20초 잠금은
    /// <see cref="ServerKnockDriver.TryRequest"/>가, "현재 위치 발생"은 Net 계층이
    /// <c>caller.FirstObject</c>에서 위치를 읽어 넘기는 것으로 강제한다(갭 분석 B3·B4·B5 완료).
    ///
    /// **"대화와 동일 취급"이지만 <see cref="SoundType.Talk"/>로 보내지 않는 이유**: §3.4가
    /// 방향 게이지 트리거에서 노크를 **명시적으로 제외**한다("밸브 회전음, 발소리, 노크는
    /// 트리거 대상에서 제외"). 종류를 Talk로 뭉개면 그 구분이 사라져 메아리가 술래에게
    /// 게이지를 띄우게 된다. 반경·지속만 같고 종류는 <see cref="SoundType.Knock"/>이다.
    /// </summary>
    public static class KnockConfig
    {
        /// <summary>§3.2 "쿨다운 25초"(기획서 갱신으로 30 → 25).</summary>
        public const float CooldownSeconds = 25f;

        /// <summary>
        /// §3.2 "라운드당 정확히 5회(재충전 없음)".
        ///
        /// **이 하드캡은 타협하지 않는다.** 충전형·무제한으로 되돌리지 않는다 —
        /// 노크는 쿨다운마다 누르는 방해 버튼이 아니라 5번의 기회를 언제 쓸지 판단하는 도구다.
        /// </summary>
        public const int MaxUsesPerRound = 5;

        /// <summary>
        /// §3.2 "메아리 전환 후 20초간 사용 불가".
        ///
        /// 태그당한 직후의 보복성 즉시 노크를 막고, §3.2의 3초 암전과 이어져
        /// "상황을 파악한 뒤 쓴다"는 리듬을 만든다.
        /// </summary>
        public const float EchoLockoutSeconds = 20f;

        /// <summary>§3.2 "1.5초 지연 후 해당 지점에서 소음 발생".</summary>
        public const float ActivationDelaySeconds = 1.5f;

        /// <summary>§3.2/§5.1 "반경 9m"(대화 등급과 동일).</summary>
        public const float RadiusMeters = 9f;

        /// <summary>§3.2/§5.1 "지속 1.2초"(대화 등급과 동일).</summary>
        public const float DurationSeconds = 1.2f;
    }
}
