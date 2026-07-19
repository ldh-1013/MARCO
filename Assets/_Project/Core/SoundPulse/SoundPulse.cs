using UnityEngine;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §5.5 데이터 구조. 발생원(source) 기준 물리값만 담는다.
    /// 리스너별 인지 배율(§5.7)과 차폐 감쇠(§5.6)는 SoundPulseResolver가 별도로 계산한다.
    /// </summary>
    public readonly struct SoundPulse
    {
        public readonly ulong SourcePlayerId;
        public readonly Vector3 Position;
        public readonly float Radius;      // m, 발생원 기준 원본값
        public readonly float Duration;    // s, 발생원 기준 원본값
        public readonly SoundType Type;
        public readonly float Timestamp;   // 서버 기준 시각

        public SoundPulse(ulong sourcePlayerId, Vector3 position, float radius, float duration, SoundType type, float timestamp)
        {
            SourcePlayerId = sourcePlayerId;
            Position = position;
            Radius = radius;
            Duration = duration;
            Type = type;
            Timestamp = timestamp;
        }
    }
}
