using System;
using System.Collections.Generic;
using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// 스프린트 3 배선의 순수 로직부: 발소리 이벤트 → SoundPulse 변환 →
    /// ActivePulseTracker 등록 → 재판정 틱 구동 → PulseDelivery 방출.
    ///
    /// 새 게임플레이 규칙은 없다 — §5.5 SoundPulse 구성과 T7 ActivePulseTracker의
    /// 기존 의미론을 그대로 통과시키는 접착 코드다.
    ///
    /// 시간(now)·차폐(IOcclusionProbe)·청취자 목록을 전부 주입받으므로
    /// MonoBehaviour 없이 EditMode 테스트가 가능하다. Unity 수명주기 연결은
    /// LocalPulsePipelineBehaviour(어댑터)가 담당한다.
    /// </summary>
    public sealed class LocalPulsePipeline
    {
        private readonly ActivePulseTracker _tracker = new ActivePulseTracker();
        private readonly IOcclusionProbe _occlusionProbe;
        private readonly ulong _sourcePlayerId;
        private readonly List<ListenerSnapshot> _listeners = new List<ListenerSnapshot>();

        /// <summary>Appeared/Updated/Disappeared 전송 지시. 이번 스프린트 소비자는 Debug.Log뿐(T8에서 렌더러로 교체).</summary>
        public event Action<PulseDelivery> DeliveryEmitted;

        public int ActivePulseCount => _tracker.ActivePulseCount;

        public LocalPulsePipeline(IOcclusionProbe occlusionProbe, ulong sourcePlayerId)
        {
            _occlusionProbe = occlusionProbe ?? throw new ArgumentNullException(nameof(occlusionProbe));
            _sourcePlayerId = sourcePlayerId;
        }

        /// <summary>
        /// [임시 스모크 리그 — 실제 게임플레이 기능 아님]
        /// 로컬 단일 클라이언트에는 "남의 소리를 듣는 청취자"가 없어서, 파이프라인이
        /// 예외 없이 도는지 확인할 고정 청취점 하나를 등록한다(스프린트 지시 §3 Step 3).
        /// 실제 청취자 목록은 네트워크 스프린트에서 원격 플레이어 스냅샷으로 대체된다.
        /// GAP-1에 따라 listenerId를 발생원과 다르게 줘야 델리버리가 관측된다.
        /// </summary>
        public void SetDebugListener(ulong listenerId, Vector3 position, RoleType role)
        {
            _listeners.Clear();
            _listeners.Add(new ListenerSnapshot(listenerId, position, role));
        }

        /// <summary>
        /// FirstPersonController.FootstepPulseEmitted 이벤트 데이터를 §5.5 SoundPulse로
        /// 변환해 트래커에 올린다. 판정은 다음 Tick에서 이뤄진다(T7 의미론 그대로).
        /// </summary>
        public int OnFootstepPulse(SoundType type, float radius, float duration, Vector3 position, float timestamp)
        {
            return _tracker.AddPulse(new SoundPulse(_sourcePlayerId, position, radius, duration, type, timestamp));
        }

        /// <summary>매 프레임 호출. 재판정 주기(0.25s) 게이팅은 트래커 내부 책임이다.</summary>
        public void Tick(float now)
        {
            List<PulseDelivery> deliveries = _tracker.Tick(now, _listeners, _occlusionProbe);
            for (int i = 0; i < deliveries.Count; i++)
                DeliveryEmitted?.Invoke(deliveries[i]);
        }
    }
}
