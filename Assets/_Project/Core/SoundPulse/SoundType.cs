namespace Marco.Core.Sound
{
    /// <summary>기획서 §5.5 SoundType. 발생원 기준 소리 종류.</summary>
    public enum SoundType
    {
        Whisper,
        Talk,
        Shout,

        /// <summary>
        /// §5.1 비명 — 9m / 1.0초. 술래 외침(§3.5)의 공포 반경 안에서 억제에 실패하면
        /// **자동 SFX로 강제 발생**한다. <see cref="Shout"/>(플레이어가 의도해서 내는 22m 고함)과
        /// 완전히 별개이며, §8 어워드에서 둘을 혼용하지 않는다.
        /// </summary>
        Scream,

        Walk,
        Sprint,
        Valve,
        Knock,

        /// <summary>
        /// §5.1 호흡음 — 3m → 9m / 0.6초, 방향 게이지 ✗. §3.6 캠핑 방지(20초 정지 감지)가
        /// <b>서버에서만</b> 발생시킨다 — 클라이언트가 주장할 수 없는 종류다
        /// (<c>ServerPulseDriver.IsServerOnly</c>).
        ///
        /// <para>
        /// §3.3 "새 SoundType 금지"의 예외가 아니다 — §5.1 표에 이미 정의된 등급을 코드에 옮긴 것이다.
        /// <b>맨 끝에 둔다</b>: 중간에 끼우면 뒤따르는 값의 정수가 밀려 네트워크로 오가는
        /// 기존 종류가 전부 다른 소리로 읽힌다.
        /// </para>
        /// </summary>
        Breath
    }
}
