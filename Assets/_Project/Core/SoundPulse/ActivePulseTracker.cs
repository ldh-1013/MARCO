using System;
using System.Collections.Generic;
using UnityEngine;
using Marco.Core.Role;

namespace Marco.Core.Sound
{
    /// <summary>재판정 시점의 리스너 상태 스냅샷. Net 레이어가 매 Tick 구성해 넘긴다.</summary>
    public readonly struct ListenerSnapshot
    {
        public readonly ulong PlayerId;
        public readonly Vector3 Position;
        public readonly RoleType Role;

        public ListenerSnapshot(ulong playerId, Vector3 position, RoleType role)
        {
            PlayerId = playerId;
            Position = position;
            Role = role;
        }
    }

    public enum PulseDeliveryKind
    {
        /// <summary>이 리스너에게 처음 보이게 됨 → PerceivedPulse 전송.</summary>
        Appeared,

        /// <summary>보이는 값이 달라짐(차폐 변화 등) → 갱신 전송.</summary>
        Updated,

        /// <summary>지속시간이 남았는데 차폐/이탈로 더는 안 보임 → 소멸 통지.</summary>
        Disappeared
    }

    /// <summary>Tick이 산출하는 리스너별 전송 지시. Net 레이어가 그대로 개별 송신한다.</summary>
    public readonly struct PulseDelivery
    {
        public readonly ulong ListenerId;
        public readonly int PulseId;
        public readonly PulseDeliveryKind Kind;
        public readonly PerceivedPulse? Perceived;

        /// <summary>
        /// §3.4 방향 게이지를 밝혀야 하는가(<see cref="DirectionGaugeRules"/> 판정 결과).
        /// **서버가 역할·등급·사거리를 전부 확인한 뒤 내리는 결론**이며, 클라이언트는 이 값을
        /// 그대로 그리기만 한다 — 술래 전용 정보이므로 판정을 클라에 맡기면 안 된다.
        /// </summary>
        public readonly bool GaugeLit;

        public PulseDelivery(ulong listenerId, int pulseId, PulseDeliveryKind kind, PerceivedPulse? perceived,
            bool gaugeLit = false)
        {
            ListenerId = listenerId;
            PulseId = pulseId;
            Kind = kind;
            Perceived = perceived;
            GaugeLit = gaugeLit;
        }
    }

    /// <summary>
    /// GAP-3 재판정 오케스트레이션(§5.6 의사코드의 재판정 루프)의 순수 구현.
    ///
    /// - 펄스 등록 후 첫 Tick에서 최초 판정, 이후 0.25초 간격으로만 재판정한다.
    /// - 판정 결과가 직전과 달라진 리스너에게만 delivery를 방출한다
    ///   (null→값 Appeared, 값→값′ Updated, 값→null Disappeared).
    /// - 리스너별 유효 시간은 §5.7 역할 지속 배율을 따른다: 같은 펄스라도
    ///   술래(×1.5)는 러너(×1.0)보다 오래 재판정 대상으로 남는다.
    /// - 자연 만료는 delivery 없이 조용히 끝난다 — 클라이언트는 이미 받은
    ///   perceivedDuration으로 스스로 렌더를 종료하므로 통지가 불필요하다.
    ///   Disappeared는 지속시간이 남았는데 차폐·거리로 소실된 경우 전용이다.
    ///
    /// 시간(now)·리스너·Physics(IOcclusionProbe)를 전부 주입받는 순수 클래스라
    /// EditMode 테스트에서 시간을 임의로 진행시켜 검증할 수 있다.
    /// </summary>
    public sealed class ActivePulseTracker
    {
        /// <summary>GAP-3 결정: 재판정 간격. M1 스파이크(T6)에서 실측 후 확정.</summary>
        public const float ReevaluationInterval = 0.25f;

        /// <summary>§5.7 최대 지속 배율(술래 ×1.5). 펄스 전체 보관 상한 계산용.</summary>
        public const float MaxDurationMultiplier = 1.5f;

        private sealed class TrackedPulse
        {
            public int Id;
            public SoundPulse Pulse;
            public float LastEvaluationTime;
            public bool EvaluatedOnce;
            public readonly Dictionary<ulong, PerceivedPulse?> LastResults = new Dictionary<ulong, PerceivedPulse?>();
        }

        private readonly List<TrackedPulse> _pulses = new List<TrackedPulse>();
        private readonly List<TrackedPulse> _expired = new List<TrackedPulse>();
        private int _nextPulseId;

        public int ActivePulseCount => _pulses.Count;

        /// <summary>펄스를 추적 대상에 올린다. 판정은 다음 Tick에서 수행된다.</summary>
        public int AddPulse(SoundPulse pulse)
        {
            var tracked = new TrackedPulse { Id = _nextPulseId++, Pulse = pulse };
            _pulses.Add(tracked);
            return tracked.Id;
        }

        /// <summary>
        /// 재판정 주기가 도래한 펄스를 리스너별로 재판정하고, 변화분만 반환한다.
        /// </summary>
        public List<PulseDelivery> Tick(float now, IReadOnlyList<ListenerSnapshot> listeners, IOcclusionProbe occlusionProbe)
        {
            var deliveries = new List<PulseDelivery>();
            _expired.Clear();

            foreach (TrackedPulse tracked in _pulses)
            {
                float age = now - tracked.Pulse.Timestamp;

                if (age > tracked.Pulse.Duration * MaxDurationMultiplier)
                {
                    _expired.Add(tracked);
                    continue;
                }

                bool due = !tracked.EvaluatedOnce || now - tracked.LastEvaluationTime >= ReevaluationInterval;
                if (!due)
                    continue;

                tracked.EvaluatedOnce = true;
                tracked.LastEvaluationTime = now;

                foreach (ListenerSnapshot listener in listeners)
                {
                    // §5.7: 이 리스너 기준으로는 이미 끝난 펄스 — 재판정하지 않는다.
                    // (자연 만료이므로 Disappeared도 보내지 않는다.)
                    float listenerDuration = tracked.Pulse.Duration * SoundPulseResolver.RoleDurationMultiplier(listener.Role);
                    if (age > listenerDuration)
                        continue;

                    PerceivedPulse? current = SoundPulseResolver.Resolve(
                        tracked.Pulse, listener.PlayerId, listener.Position, listener.Role, occlusionProbe);

                    // §3.4 방향 게이지(GAP-4 결정: "§5.6을 통과한 펄스만 트리거").
                    // 사거리는 §5.7 배율이 곱해지지 않은 **물리 반경**(§5.1) 기준이다 —
                    // 그 해석에서만 §3.4 표의 13.5m/33m이 나온다.
                    bool gaugeLit = current != null && DirectionGaugeRules.ShouldLight(
                        tracked.Pulse.Type,
                        listener.Role,
                        tracked.Pulse.Radius,
                        Vector3.Distance(tracked.Pulse.Position, listener.Position));

                    tracked.LastResults.TryGetValue(listener.PlayerId, out PerceivedPulse? previous);

                    if (previous == null && current != null)
                        deliveries.Add(new PulseDelivery(listener.PlayerId, tracked.Id, PulseDeliveryKind.Appeared, current, gaugeLit));
                    else if (previous != null && current == null)
                        deliveries.Add(new PulseDelivery(listener.PlayerId, tracked.Id, PulseDeliveryKind.Disappeared, null));
                    else if (previous != null && current != null && !AreEqual(previous.Value, current.Value))
                        deliveries.Add(new PulseDelivery(listener.PlayerId, tracked.Id, PulseDeliveryKind.Updated, current, gaugeLit));

                    tracked.LastResults[listener.PlayerId] = current;
                }
            }

            foreach (TrackedPulse tracked in _expired)
                _pulses.Remove(tracked);

            return deliveries;
        }

        private static bool AreEqual(in PerceivedPulse a, in PerceivedPulse b)
        {
            return a.PerceivedRadius == b.PerceivedRadius
                && a.PerceivedDuration == b.PerceivedDuration
                && a.WorldSpaceRingVisible == b.WorldSpaceRingVisible
                && a.Direction == b.Direction
                && Nullable.Equals(a.SourcePos, b.SourcePos);
        }
    }
}
