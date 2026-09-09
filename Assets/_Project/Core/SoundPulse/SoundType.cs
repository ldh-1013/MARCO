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
        Knock
    }
}
