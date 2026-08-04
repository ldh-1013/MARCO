using System.Collections.Generic;
using UnityEngine;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;

namespace Marco.Core.Sound
{
    /// <summary>
    /// 서버 권위 파문 구동기(스프린트 14). §14.3 `SoundPulse`(Client → Server, 서버 내부 판정용)를
    /// 받아 §5.6 청취자별 판정을 수행하고, §14.3 `PerceivedPulse`(Server → 해당 리스너만)의
    /// 전송 지시를 산출한다. FishNet도 UnityEngine 수명주기도 모른다 — EditMode 테스트 가능
    /// (<c>ServerValveDriver</c>·<c>ServerTagDriver</c>·<c>ServerRoundDriver</c>와 같은 구조).
    ///
    /// **기존 판정 재사용(새 로직 없음)**: 청취자별 재판정은 <see cref="ActivePulseTracker"/>(T7),
    /// 차폐·인지 배율은 <see cref="SoundPulseResolver"/>(§5.6/§5.7)를 그대로 호출한다.
    /// 이 클래스가 추가하는 것은 **서버가 무엇을 신뢰하는가**(아래)뿐이다.
    ///
    /// **청취자별 트래커 인스턴스가 필요 없는 이유**: <see cref="ActivePulseTracker"/>는 내부에서
    /// 펄스별로 <c>Dictionary&lt;청취자ID, PerceivedPulse?&gt;</c>를 유지한다 — 이미 청취자별
    /// 직전 판정 결과를 구분해 보관하도록 설계돼 있어(GAP-3), 서버 트래커 하나로 전원을 처리한다.
    ///
    /// **서버 권위 범위(GAP-24)**: 클라이언트는 "무슨 소리가 났는가"(<see cref="SoundType"/>)만
    /// 주장하고, 반경·지속시간은 서버가 §5.1 표에서 <b>재계산</b>한다
    /// (<see cref="TryGetPulseSpec"/>). 발생 위치도 클라 주장값이 아니라 서버가 아는 플레이어
    /// 위치를 쓰며(호출자가 넘긴다), 발생원 ID도 서버가 RPC 호출자에서 얻는다. 따라서
    /// 클라이언트는 반경을 부풀리거나 남의 발소리를 위장할 수 없다.
    /// </summary>
    public sealed class ServerPulseDriver
    {
        private readonly ActivePulseTracker _tracker = new ActivePulseTracker();

        /// <summary>현재 서버가 추적 중인 파문 수(자연 만료 시 자동 감소).</summary>
        public int ActivePulseCount => _tracker.ActivePulseCount;

        /// <summary>
        /// §5.1 표의 반경·지속을 소리 종류에서 서버가 재계산한다. 표에 없는 종류는 false —
        /// 클라이언트가 임의 종류를 보내도 서버가 파문을 만들지 않는다.
        ///
        /// 지원 범위: 걷기·질주(§5.1 발소리) + 밸브 회전(§5.1/§6.1 "의도된 유인 장치" — 이게
        /// 빠지면 네트워크에서 밸브가 무음이 되어 유인 설계가 붕괴한다) + **음성 3등급**
        /// (속삭임·대화·고함 — 스프린트 26b에서 §5.2 파이프라인이 들어오며 추가).
        /// 노크는 메아리 능력(미구현)이라 아직 대상이 아니다.
        /// </summary>
        public static bool TryGetPulseSpec(SoundType type, out float radius, out float duration)
        {
            switch (type)
            {
                case SoundType.Walk:
                    radius = LocomotionConfig.WalkPulseRadius;      // §5.1 2m
                    duration = LocomotionConfig.WalkPulseDuration;  // §5.1 0.4s
                    return true;

                case SoundType.Sprint:
                    radius = LocomotionConfig.SprintPulseRadius;     // §5.1 6m
                    duration = LocomotionConfig.SprintPulseDuration; // §5.1 0.8s
                    return true;

                case SoundType.Valve:
                    radius = Valve.SoundRadiusMeters;                // §5.1 12m
                    duration = Valve.DefaultRotationSeconds;         // §5.1 "회전 내내"(4인 MVP 3초)
                    return true;

                // §5.2 음성 파이프라인(스프린트 26b). 클라이언트는 **등급만** 주장하고,
                // 반경·지속·위치는 여기서 서버가 정한다 — 발소리·밸브와 완전히 같은 규칙이다(GAP-24).
                case SoundType.Whisper:
                    radius = Voice.VoiceConfig.WhisperRadiusMeters;    // §5.1 4m
                    duration = Voice.VoiceConfig.WhisperDurationSeconds; // §5.1 0.6s
                    return true;

                case SoundType.Talk:
                    radius = Voice.VoiceConfig.TalkRadiusMeters;       // §5.1 9m
                    duration = Voice.VoiceConfig.TalkDurationSeconds;  // §5.1 1.2s
                    return true;

                case SoundType.Shout:
                    radius = Voice.VoiceConfig.ShoutRadiusMeters;      // §5.1 22m
                    duration = Voice.VoiceConfig.ShoutDurationSeconds; // §5.1 2.5s
                    return true;

                default:
                    radius = 0f;
                    duration = 0f;
                    return false;
            }
        }

        /// <summary>
        /// 서버가 파문을 등록한다. 반환값은 펄스 ID(§5.6 판정 대상), 거부되면 -1.
        ///
        /// <paramref name="serverPosition"/>은 <b>서버가 아는 발생원 위치</b>여야 한다(클라 주장값
        /// 금지 — GAP-24). <paramref name="sourcePlayerId"/>도 서버가 RPC 호출자에서 얻은 값이다.
        /// </summary>
        public int AddPulse(ulong sourcePlayerId, SoundType type, Vector3 serverPosition, float now)
        {
            if (!TryGetPulseSpec(type, out float radius, out float duration))
                return -1;

            return _tracker.AddPulse(new SoundPulse(sourcePlayerId, serverPosition, radius, duration, type, now));
        }

        /// <summary>
        /// §5.6 재판정을 진행하고 청취자별 전송 지시를 반환한다. 0.25초 재판정 주기(GAP-3)·
        /// 본인 제외(GAP-1)·좌표 공개 여부(GAP-2)는 전부 기존 Core 로직이 결정한다.
        ///
        /// 반환된 각 <see cref="PulseDelivery"/>는 <see cref="PulseDelivery.ListenerId"/>에게만
        /// 개별 전송돼야 한다(§14.3 "Server → 해당 리스너만").
        /// </summary>
        public List<PulseDelivery> Tick(float now, IReadOnlyList<ListenerSnapshot> listeners, IOcclusionProbe occlusionProbe)
        {
            return _tracker.Tick(now, listeners, occlusionProbe);
        }
    }
}
