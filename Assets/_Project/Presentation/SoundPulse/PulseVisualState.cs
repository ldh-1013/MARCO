using UnityEngine;
using Marco.Core.Sound;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// GAP-2 결정이 렌더 표현으로 갈리는 지점. Core가 이미 "좌표를 줄지 방향만 줄지"를
    /// 판정해 넘겨주므로 Presentation은 그 판단을 다시 하지 않고 분기만 한다.
    /// </summary>
    public enum PulseVisualKind
    {
        /// <summary>벽 0개 — 발생 지점에 월드스페이스 파문 링을 그린다(§5.4).</summary>
        WorldRing,

        /// <summary>차폐됨 — 정밀 좌표 없이 8방위 인디케이터만 표시(§3.4).</summary>
        DirectionOnly
    }

    /// <summary>
    /// 화면에 살아있는 파문 하나의 상태. 렌더러가 매 프레임 이 값으로 그린다.
    ///
    /// 만료는 <see cref="StartTime"/> + <see cref="Duration"/>으로 스스로 판정한다 —
    /// 자연 만료 시 Disappeared 델리버리가 오지 않는 것이 T7의 의도된 설계이므로
    /// (스프린트 3 버그 조사 결론), 델리버리를 기다리면 파문이 영원히 안 사라진다.
    /// </summary>
    public readonly struct PulseVisualState
    {
        public readonly int PulseId;
        public readonly PulseVisualKind Kind;

        /// <summary>WorldRing일 때만 유효. DirectionOnly면 Vector3.zero.</summary>
        public readonly Vector3 SourcePos;

        public readonly DirectionOctant Direction;
        public readonly float Radius;
        public readonly float Duration;
        public readonly float StartTime;

        /// <summary>
        /// §3.4 술래 전용 방향 게이지를 밝히는 파문인가. 서버가 역할·등급·사거리를 이미
        /// 판정해 보낸 값이라(<see cref="PulseDelivery.GaugeLit"/>) 여기서 다시 따지지 않는다.
        /// </summary>
        public readonly bool GaugeLit;

        public PulseVisualState(int pulseId, PulseVisualKind kind, Vector3 sourcePos,
            DirectionOctant direction, float radius, float duration, float startTime, bool gaugeLit = false)
        {
            PulseId = pulseId;
            Kind = kind;
            SourcePos = sourcePos;
            Direction = direction;
            Radius = radius;
            Duration = duration;
            StartTime = startTime;
            GaugeLit = gaugeLit;
        }

        /// <summary>0(발생) → 1(만료). 링 확장·페이드아웃의 기준값.</summary>
        public float Progress01(float now)
        {
            if (Duration <= 0f)
                return 1f;
            return Mathf.Clamp01((now - StartTime) / Duration);
        }

        public bool IsExpired(float now) => now - StartTime >= Duration;

        /// <summary>StartTime을 보존한 채 판정 결과만 갈아끼운다(Updated 처리용).</summary>
        public PulseVisualState WithPerceived(in PerceivedPulse perceived, bool gaugeLit)
        {
            return new PulseVisualState(
                PulseId,
                perceived.WorldSpaceRingVisible ? PulseVisualKind.WorldRing : PulseVisualKind.DirectionOnly,
                perceived.SourcePos ?? Vector3.zero,
                perceived.Direction,
                perceived.PerceivedRadius,
                perceived.PerceivedDuration,
                StartTime,
                gaugeLit);
        }

        public static PulseVisualState FromPerceived(int pulseId, in PerceivedPulse perceived, float now, bool gaugeLit = false)
        {
            return new PulseVisualState(
                pulseId,
                perceived.WorldSpaceRingVisible ? PulseVisualKind.WorldRing : PulseVisualKind.DirectionOnly,
                perceived.SourcePos ?? Vector3.zero,
                perceived.Direction,
                perceived.PerceivedRadius,
                perceived.PerceivedDuration,
                now,
                gaugeLit);
        }
    }
}
