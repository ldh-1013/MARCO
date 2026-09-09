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

                // §5.1 비명(§3.5 술래 외침의 강제 결과). 고함(Shout)과 **다른 종류**다 —
                // 반경·지속이 다르고(9m/1.0초 vs 22m/2.5초), §8 어워드가 둘을 구분해야 한다.
                // 술래 청취 반경 10.8m는 새 상수가 아니라 9 × §5.7 역할 배율(1.2)의 결과다.
                case SoundType.Scream:
                    radius = ScreamConfig.RadiusMeters;      // §5.1 9m
                    duration = ScreamConfig.DurationSeconds; // §5.1 1.0초
                    return true;

                // §3.2 메아리 노크(스프린트 27). "대화 등급과 동일 취급"이라 반경·지속이 같지만
                // **종류는 Knock으로 남긴다** — §3.4가 게이지 트리거에서 노크를 명시적으로
                // 제외하므로, Talk로 뭉개면 메아리가 술래에게 방향 게이지를 띄우게 된다.
                //
                // 발생 위치도 서버가 정한다(§3.2 갱신 "발생 위치: 메아리의 현재 위치") —
                // 이제 다른 소리와 완전히 같은 규칙이다. 역할·횟수·잠금·쿨다운·지연은
                // ServerKnockDriver가 서버에서 강제한다.
                case SoundType.Knock:
                    radius = KnockConfig.RadiusMeters;      // §3.2/§5.1 9m
                    duration = KnockConfig.DurationSeconds; // §3.2/§5.1 1.2s
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
        /// <param name="material">
        /// §5.9 바닥 재질. **서버가 판정한 값**이며 클라이언트가 보내지 않는다.
        /// 발생 <b>반경</b>에만 곱해지고 발생 간격에는 영향이 없다(§5.1-1) — 간격은 애초에
        /// 이 클래스가 아니라 <c>LocomotionSimulator</c>의 이동거리 누적이 정한다.
        /// 물이면 파문 자체를 만들지 않는다(§5.9 "파문 발생 안 함").
        /// </param>
        public int AddPulse(ulong sourcePlayerId, SoundType type, Vector3 serverPosition, float now,
            FootstepMaterial material = FootstepMaterialRules.Default)
        {
            if (!TryGetAppliedSpec(type, material, out float radius, out float duration))
                return -1;

            return _tracker.AddPulse(new SoundPulse(sourcePlayerId, serverPosition, radius, duration, type, now));
        }

        /// <summary>
        /// **실제로 등록되는** 반경·지속을 낸다 — §5.1 표 값에 §5.9 재질 배율까지 적용한 결과다.
        /// 물(§5.9 "파문 발생 안 함")이거나 §5.1 표에 없는 종류면 false.
        ///
        /// <see cref="AddPulse"/>가 이 메서드를 쓰고, §8.2 무성 생존상 집계도 이 값을 써야 한다 —
        /// 기획서가 "발생 반경은 **재질 배율 적용 후**의 값 사용"이라고 못박았기 때문이다.
        /// 두 곳이 각자 계산하면 재질 효과가 소음량에서 조용히 사라진다.
        /// </summary>
        public static bool TryGetAppliedSpec(SoundType type, FootstepMaterial material,
            out float radius, out float duration)
        {
            if (!TryGetPulseSpec(type, out radius, out duration))
                return false;

            if (!FootstepMaterialRules.AppliesTo(type))
                return true;

            if (!FootstepMaterialRules.EmitsPulse(material))
            {
                radius = 0f;
                duration = 0f;
                return false; // §5.9 물(수면 아래) — 발소리 파문이 없다.
            }

            radius = FootstepMaterialRules.ApplyToRadius(radius, material);
            return true;
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
