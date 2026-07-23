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
        private readonly List<ListenerSnapshot> _listeners = new List<ListenerSnapshot>();

        /// <summary>
        /// 발생원 ID. 스폰·소유권 확정 타이밍 때문에 생성 시점이 아니라 발행 시점에
        /// 최신 값을 읽어야 하므로, 생성자 고정이 아니라 세터로 갱신 가능하게 둔다.
        /// Behaviour가 플레이어 바인딩 시 실제 신원으로 즉시 덮어쓴다.
        ///
        /// 초기값 1은 <c>FirstPersonController.LocalFallbackPlayerId</c>와 동일하다.
        /// 이 순수 클래스는 Presentation 컴포넌트를 참조할 수 없어 상수를 직접 쓴다 —
        /// 두 값은 반드시 같아야 하며, 테스트가 이를 고정한다.
        /// </summary>
        public ulong SourcePlayerId { get; set; } = 1;

        /// <summary>Appeared/Updated/Disappeared 전송 지시. 이번 스프린트 소비자는 Debug.Log뿐(T8에서 렌더러로 교체).</summary>
        public event Action<PulseDelivery> DeliveryEmitted;

        public int ActivePulseCount => _tracker.ActivePulseCount;

        public LocalPulsePipeline(IOcclusionProbe occlusionProbe, ulong sourcePlayerId)
        {
            _occlusionProbe = occlusionProbe ?? throw new ArgumentNullException(nameof(occlusionProbe));
            SourcePlayerId = sourcePlayerId;
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
        /// §5.1 표의 어떤 소리든 §5.5 SoundPulse로 변환해 트래커에 올린다.
        /// 판정은 다음 Tick에서 이뤄진다(T7 의미론 그대로).
        /// </summary>
        public int EmitPulse(SoundType type, float radius, float duration, Vector3 position, float timestamp)
        {
            return _tracker.AddPulse(new SoundPulse(SourcePlayerId, position, radius, duration, type, timestamp));
        }

        /// <summary>
        /// FirstPersonController.FootstepPulseEmitted 이벤트용 진입점.
        /// 발소리도 §5.1의 한 등급일 뿐이라 <see cref="EmitPulse"/>에 그대로 위임한다.
        /// </summary>
        public int OnFootstepPulse(SoundType type, float radius, float duration, Vector3 position, float timestamp)
        {
            return EmitPulse(type, radius, duration, position, timestamp);
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
