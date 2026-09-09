using Marco.Core.Voice;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §5.1 비명(<see cref="SoundType.Scream"/>) 수치.
    ///
    /// **<see cref="SoundType.Shout"/>(고함)과 완전히 별개의 종류다**(§5.1 신설 주석):
    /// 고함은 <b>플레이어가 의도해서</b> 마이크로 내는 소리(22m·2.5초),
    /// 비명은 술래 외침에 의해 <b>강제로 발생</b>하는 캐릭터 자동 SFX(9m·1.0초)다.
    /// 어워드 판정(§8)에서 둘을 혼용하지 않는다.
    ///
    /// **대화와 반경이 같지만 종류를 Talk로 뭉개지 않는 이유**는 노크와 같다 —
    /// §5.1 "구분 불가 그룹"은 <b>술래가 구분할 수 없다</b>는 뜻이지 같은 종류라는 뜻이 아니고,
    /// §8 어워드가 비명만 따로 세야 하기 때문이다.
    ///
    /// **술래 청취 반경 10.8m에 새 상수를 만들지 않았다.** 9m × 1.2이며 그 ×1.2는
    /// <see cref="SoundPulseResolver.RoleRadiusMultiplier"/>(§5.7)가 이미 전 파문에 적용하고 있다.
    /// </summary>
    public static class ScreamConfig
    {
        /// <summary>§5.1 비명 "발생 반경 9m".</summary>
        public const float RadiusMeters = 9f;

        /// <summary>§5.1 비명 "지속시간 1.0초". 대화(1.2초)와 다르다.</summary>
        public const float DurationSeconds = 1.0f;
    }

    /// <summary>
    /// §3.5 술래 외침 능력의 수치. **전부 기획서 원문에서 옮긴 값**이다.
    ///
    /// **§3.5가 못박은 "세 가지 22m"를 여기서도 분리해 둔다** — 같은 숫자라고 한 상수로
    /// 합치면, 나중에 한쪽만 조정할 때 반드시 다른 쪽까지 끌려간다.
    ///
    /// <code>
    /// | # | 개념                  | 값     | 성격                            | 차폐   |
    /// | A | 외침 SoundPulse 발생 반경 | 22m  | 술래가 만드는 소리 이벤트(고함 등급) | 적용   |
    /// | B | 공포 반경               | 22m  | 자동 비명을 트리거하는 능력 효과 범위 | 미적용 |
    /// | C | 비명 청취 반경           | 10.8m | 술래가 그 비명을 인지하는 범위 = 9×1.2 | 적용   |
    /// </code>
    ///
    /// A는 <see cref="ShoutPulseRadiusMeters"/>, B는 <see cref="FearRadiusMeters"/>다.
    /// C는 상수가 아니라 §5.7 역할 배율의 계산 결과라 여기 없다.
    /// </summary>
    public static class SeekerShoutConfig
    {
        /// <summary>
        /// **A — 외침이 만드는 소리의 발생 반경.** §3.5 "술래가 발생시키는 소리 =
        /// 고함 등급 SoundPulse(§5.1) — 발생 반경 22m".
        ///
        /// 고함 등급을 그대로 쓰므로 <see cref="VoiceConfig.ShoutRadiusMeters"/>를 참조한다 —
        /// §5.1 고함이 조정되면 외침도 함께 따라가야 하기 때문이다. 이 소리는 <b>차폐가 적용된다</b>.
        /// </summary>
        public const float ShoutPulseRadiusMeters = VoiceConfig.ShoutRadiusMeters;

        /// <summary>외침 소리의 지속시간. 고함 등급이므로 §5.1 고함과 같다.</summary>
        public const float ShoutPulseDurationSeconds = VoiceConfig.ShoutDurationSeconds;

        /// <summary>
        /// **B — 공포 반경.** §3.5 "22m 안의 살아있는 도망자가 자동으로 짧은 비명을 낸다".
        ///
        /// <b>소리가 아니라 능력 효과 범위이며 차폐가 적용되지 않는다</b>(§3.5) —
        /// "A는 소리라 벽을 지나면 감쇠하지만, B는 능력 판정이라 벽 뒤 도망자도 놀란다."
        /// 값이 A와 같은 22m인 것은 우연이며, <b>별개의 상수로 유지한다.</b>
        /// </summary>
        public const float FearRadiusMeters = 22f;

        /// <summary>
        /// §3.5 "선딜레이 1초 정지(선딜레이 중 이동하면 취소, 쿨다운 소모 없음)".
        /// 러너가 §3.5 "숨 참기(선딜레이 1초 안에 입력)"를 넣을 수 있는 창이기도 하다.
        /// </summary>
        public const float WindupSeconds = 1f;

        /// <summary>§3.5 "쿨다운 45초 (선딜레이 완료 시점부터 계산)".</summary>
        public const float CooldownSeconds = 45f;
    }
}
