namespace Marco.Core.Sound
{
    /// <summary>
    /// §3.2 메아리 노크 능력의 수치(스프린트 27). **전부 기획서 원문에서 옮긴 값**이며
    /// 임의로 만든 상수가 없다.
    ///
    /// **§3.2 원문**:
    /// <code>
    /// - 노크 능력:
    ///   - 쿨다운 30초, 사거리 제한 없음(맵 내 임의 지점 지정)
    ///   - 조작: 미니맵/탑다운 뷰에서 지점 클릭 → 1.5초 지연 후 해당 지점에서 소음 발생
    ///   - 발생 소음: "대화" 등급과 동일 취급 (반경 9m, 지속 1.2초) — 술래·도망자 모두 인지 가능
    /// </code>
    ///
    /// §5.1 표의 노크 행(<c>노크(메아리) | 9m | 1.2초 | 메아리 능력 사용 | "대화"와 동일 취급</c>)과
    /// 두 절이 정확히 일치한다.
    ///
    /// **"대화와 동일 취급"이지만 <see cref="SoundType.Talk"/>로 보내지 않는 이유**: §3.4가
    /// 방향 게이지 트리거에서 노크를 **명시적으로 제외**한다("밸브 회전음, 발소리, 노크는
    /// 트리거 대상에서 제외"). 종류를 Talk로 뭉개면 그 구분이 사라져 메아리가 술래에게
    /// 게이지를 띄우게 된다. 반경·지속만 같고 종류는 <see cref="SoundType.Knock"/>이다.
    /// </summary>
    public static class KnockConfig
    {
        /// <summary>§3.2 "쿨다운 30초".</summary>
        public const float CooldownSeconds = 30f;

        /// <summary>§3.2 "1.5초 지연 후 해당 지점에서 소음 발생".</summary>
        public const float ActivationDelaySeconds = 1.5f;

        /// <summary>§3.2/§5.1 "반경 9m"(대화 등급과 동일).</summary>
        public const float RadiusMeters = 9f;

        /// <summary>§3.2/§5.1 "지속 1.2초"(대화 등급과 동일).</summary>
        public const float DurationSeconds = 1.2f;
    }
}
