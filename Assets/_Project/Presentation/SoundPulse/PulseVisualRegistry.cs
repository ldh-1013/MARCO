using System;
using System.Collections.Generic;
using Marco.Core.Sound;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// PulseDelivery → 화면에 살아있는 파문 집합의 매핑. Unity 타입에 의존하지 않아
    /// (Vector3 제외) EditMode 테스트로 수명주기 규칙 전체를 고정할 수 있다.
    ///
    /// 수명 규칙 세 가지:
    /// - Appeared  → 새 시각 오브젝트 생성, 자체 만료 타이머 시작
    /// - Updated   → StartTime은 보존한 채 반경·지속·표현종류(GAP-2 분기)만 갱신
    /// - Disappeared → 즉시 제거(조기 소실 전용 신호)
    /// 그리고 <see cref="Tick"/>이 duration 경과분을 스스로 걷어낸다 — 자연 만료는
    /// 델리버리가 오지 않기 때문이다(T7 설계, 스프린트 3 버그 조사에서 확정).
    /// </summary>
    public sealed class PulseVisualRegistry
    {
        private readonly Dictionary<int, PulseVisualState> _visuals = new Dictionary<int, PulseVisualState>();
        private readonly List<int> _expiredScratch = new List<int>();

        public event Action<PulseVisualState> VisualAdded;
        public event Action<PulseVisualState> VisualUpdated;
        public event Action<int> VisualRemoved;

        public int ActiveVisualCount => _visuals.Count;

        public bool TryGet(int pulseId, out PulseVisualState state) => _visuals.TryGetValue(pulseId, out state);

        /// <summary>
        /// 현재 살아있는 파문을 호출자 버퍼에 복사한다.
        ///
        /// 사전을 <c>IReadOnlyDictionary</c>로 노출해 foreach를 돌리면 struct 열거자가
        /// 박싱되어 **프레임마다 힙 할당**이 생긴다. 렌더러는 매 프레임(+ OnGUI는 이벤트마다)
        /// 순회하므로, 재사용 버퍼에 채워주는 방식으로 그 할당을 없앤다.
        /// </summary>
        public void CopyTo(List<PulseVisualState> buffer)
        {
            buffer.Clear();
            foreach (KeyValuePair<int, PulseVisualState> entry in _visuals)
                buffer.Add(entry.Value);
        }

        public void Apply(in PulseDelivery delivery, float now)
        {
            switch (delivery.Kind)
            {
                case PulseDeliveryKind.Appeared:
                    if (!delivery.Perceived.HasValue)
                        return;
                    // 재등장(차폐가 걷힌 경우)도 여기로 온다. 원래 발생 시각을 알 수 없으므로
                    // 타이머를 새로 시작한다 — 드문 케이스이고 시각 표현상만의 오차다.
                    var added = PulseVisualState.FromPerceived(delivery.PulseId, delivery.Perceived.Value, now);
                    _visuals[delivery.PulseId] = added;
                    VisualAdded?.Invoke(added);
                    break;

                case PulseDeliveryKind.Updated:
                    if (!delivery.Perceived.HasValue)
                        return;
                    if (!_visuals.TryGetValue(delivery.PulseId, out PulseVisualState existing))
                        return;
                    PulseVisualState updated = existing.WithPerceived(delivery.Perceived.Value);
                    _visuals[delivery.PulseId] = updated;
                    VisualUpdated?.Invoke(updated);
                    break;

                case PulseDeliveryKind.Disappeared:
                    if (_visuals.Remove(delivery.PulseId))
                        VisualRemoved?.Invoke(delivery.PulseId);
                    break;
            }
        }

        /// <summary>자체 타이머 만료분을 제거한다. 매 프레임 호출.</summary>
        public void Tick(float now)
        {
            if (_visuals.Count == 0)
                return;

            _expiredScratch.Clear();
            foreach (KeyValuePair<int, PulseVisualState> entry in _visuals)
            {
                if (entry.Value.IsExpired(now))
                    _expiredScratch.Add(entry.Key);
            }

            for (int i = 0; i < _expiredScratch.Count; i++)
            {
                _visuals.Remove(_expiredScratch[i]);
                VisualRemoved?.Invoke(_expiredScratch[i]);
            }
        }

        public void Clear()
        {
            _expiredScratch.Clear();
            foreach (int id in _visuals.Keys)
                _expiredScratch.Add(id);

            _visuals.Clear();
            for (int i = 0; i < _expiredScratch.Count; i++)
                VisualRemoved?.Invoke(_expiredScratch[i]);
        }
    }
}
