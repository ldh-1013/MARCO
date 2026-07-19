using UnityEngine;
using Marco.Core.Role;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §5.6/§5.7 차폐·인지 배율 판정의 순수 로직 구현.
    ///
    /// 설계 결정 (docs/phase-1-분석.md 참조):
    /// - GAP-1: 인지 배율은 타인이 발생시킨 펄스에만 적용된다. listenerPlayerId가
    ///   pulse.SourcePlayerId와 같으면 항상 null을 반환한다(본인 파문은 이 리졸버를
    ///   거치지 않고 Net/Presentation 레이어에서 0ms 로컬 렌더로 별도 처리).
    /// - GAP-2: 정밀 좌표(SourcePos)는 벽 0개(완전 시야 확보)일 때만 채운다.
    ///   벽에 막힌 경우는 8방위 방향(Direction)만 제공한다. 카메라 프러스텀 기준
    ///   최종 "실제로 보이는가" 판정(§5.4 시네마틱 하이라이트)은 이 리졸버의 책임이
    ///   아니라 Presentation 레이어가 SourcePos를 받은 뒤 매 프레임 수행한다.
    /// - GAP-3: 이 함수는 단발 순수 함수다. "지속 중 0.25초 간격 재판정"은 Net 레이어가
    ///   이 함수를 반복 호출하는 방식으로 구현한다(리졸버 자체에 재판정 루프를 두지 않음).
    ///
    /// Physics.Linecast를 직접 호출하지 않고 IOcclusionProbe로 주입받아
    /// EditMode 유닛 테스트에서 Physics 없이 검증 가능하게 한다.
    /// </summary>
    public static class SoundPulseResolver
    {
        public static float RoleRadiusMultiplier(RoleType role)
        {
            return role == RoleType.Seeker ? 1.2f : 1.0f;
        }

        public static float RoleDurationMultiplier(RoleType role)
        {
            return role == RoleType.Seeker ? 1.5f : 1.0f;
        }

        /// <summary>
        /// §5.6 ResolvePulseForListener 구현. 판정을 통과하지 못하면 null.
        /// </summary>
        public static PerceivedPulse? Resolve(
            SoundPulse pulse,
            ulong listenerPlayerId,
            Vector3 listenerPosition,
            RoleType listenerRole,
            IOcclusionProbe occlusionProbe)
        {
            // GAP-1: 본인 발생 펄스는 이 경로로 절대 생성되지 않는다.
            if (listenerPlayerId == pulse.SourcePlayerId)
                return null;

            float straightDist = Vector3.Distance(pulse.Position, listenerPosition);

            // 1) 역할별 인지 배율 적용 (§5.7)
            float baseRadius = pulse.Radius * RoleRadiusMultiplier(listenerRole);
            float baseDuration = pulse.Duration * RoleDurationMultiplier(listenerRole);

            // 2) 1차 컷: 배율 적용 후에도 물리적으로 안 닿으면 즉시 탈락 (레이캐스트 절약)
            if (straightDist > baseRadius)
                return null;

            // 3) 차폐 레이캐스트 (§5.6)
            OcclusionResult occlusion = occlusionProbe.Probe(pulse.Position, listenerPosition);

            if (occlusion.HasHardBlocker)
                return null;

            float attenuation = Mathf.Pow(0.5f, occlusion.WallCount);
            float perceivedRadius = baseRadius * attenuation;
            float perceivedDuration = baseDuration * Mathf.Max(attenuation, 0.5f);

            if (straightDist > perceivedRadius)
                return null;

            DirectionOctant direction = ComputeOctant(pulse.Position, listenerPosition);

            // GAP-2: 벽이 하나도 없어 완전히 뚫린 시선일 때만 정밀 좌표를 공개한다.
            bool disclosePosition = occlusion.WallCount == 0;
            Vector3? sourcePos = disclosePosition ? pulse.Position : (Vector3?)null;

            return new PerceivedPulse(perceivedRadius, perceivedDuration, sourcePos, direction, disclosePosition);
        }

        /// <summary>listener → source 방향을 8방위로 스냅한다(§3.4).</summary>
        public static DirectionOctant ComputeOctant(Vector3 sourcePos, Vector3 listenerPos)
        {
            Vector3 delta = sourcePos - listenerPos;
            float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg; // 0=N(+z), 90=E(+x), 시계 방향
            if (angle < 0f)
                angle += 360f;

            int index = Mathf.RoundToInt(angle / 45f) % 8;
            return (DirectionOctant)index;
        }
    }
}
