using UnityEngine;

namespace Marco.Core.Sound
{
    /// <summary>§5.6 차폐 판정 결과. from→to 사이 SoundBlocking 레이어 충돌 정보만 담는다.</summary>
    public readonly struct OcclusionResult
    {
        public readonly bool HasHardBlocker;
        public readonly int WallCount;

        public OcclusionResult(bool hasHardBlocker, int wallCount)
        {
            HasHardBlocker = hasHardBlocker;
            WallCount = wallCount;
        }
    }

    /// <summary>
    /// §5.6 차폐 레이캐스트를 추상화한 인터페이스.
    /// SoundPulseResolver가 UnityEngine.Physics를 직접 호출하지 않고 이 인터페이스를 통해
    /// 주입받도록 해, 유닛 테스트에서 Physics.Linecast 없이도 차폐 판정 로직을 검증할 수 있다.
    /// 실제 구현(UnityPhysicsOcclusionProbe 등)은 Presentation/Net 레이어에서 제공한다.
    /// </summary>
    public interface IOcclusionProbe
    {
        OcclusionResult Probe(Vector3 from, Vector3 to);
    }
}
