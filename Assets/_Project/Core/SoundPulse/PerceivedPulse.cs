using UnityEngine;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §5.5/§5.6/§5.7 결과값. 리스너별로 서버가 개별 계산해 전달하는 값만 담는다.
    ///
    /// GAP-2 결정: SourcePos는 WorldSpaceRingVisible == true일 때만 값을 가진다.
    /// 방향 힌트만 필요한 경우(§3.4) 정밀 좌표 대신 Direction(8방위)만 사용한다 —
    /// 서버가 "리스너가 정확한 위치를 몰라야 한다"고 판정한 상황에서 클라이언트에
    /// 정밀 좌표를 아예 보내지 않기 위함.
    ///
    /// 본인이 발생시킨 펄스에 대해서는 이 구조체가 생성되지 않는다(GAP-1 결정).
    /// 본인 목소리는 항상 로컬 0ms 렌더 경로로만 처리되며 인지 배율이 적용되지 않는다.
    /// </summary>
    public readonly struct PerceivedPulse
    {
        public readonly float PerceivedRadius;
        public readonly float PerceivedDuration;
        public readonly Vector3? SourcePos;
        public readonly DirectionOctant Direction;
        public readonly bool WorldSpaceRingVisible;

        public PerceivedPulse(float perceivedRadius, float perceivedDuration, Vector3? sourcePos, DirectionOctant direction, bool worldSpaceRingVisible)
        {
            PerceivedRadius = perceivedRadius;
            PerceivedDuration = perceivedDuration;
            SourcePos = sourcePos;
            Direction = direction;
            WorldSpaceRingVisible = worldSpaceRingVisible;
        }
    }
}
