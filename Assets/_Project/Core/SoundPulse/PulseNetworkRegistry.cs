using UnityEngine;
using Marco.Core.Net;

namespace Marco.Core.Sound
{
    /// <summary>
    /// 서버 파문 판정(<c>PulseNetworkSync</c>, Net)이 어셈블리 경계를 넘어 Presentation의
    /// 두 구현체에 닿게 해주는 지연 바인딩 지점(스프린트 14).
    ///
    /// **왜 필요한가**: 서버 판정에는 두 가지가 필요한데 둘 다 Presentation에 있다 —
    /// ① §5.6 차폐 레이캐스트 구현체(<c>PhysicsOcclusionProbe</c>: Unity Physics 사용),
    /// ② 수신 델리버리를 그릴 렌더러(<c>PulseVisualRenderer</c>: T8). §15.2상 Net은
    /// Presentation을 참조할 수 없으므로, 구현체가 스스로 여기 등록하고 Net은 Core
    /// 인터페이스만 본다 — <c>EscapeGateRegistry</c>·<c>TagTargetRegistry</c>와 같은 패턴.
    ///
    /// 둘 다 라운드당 하나이므로 단일 슬롯으로 노출한다.
    /// </summary>
    public static class PulseNetworkRegistry
    {
        /// <summary>
        /// §5.6 차폐 판정 구현체. 서버가 청취자별 판정에 쓴다. 미등록이면 null —
        /// 그때 서버는 판정을 건너뛴다(차폐를 "없음"으로 가정해 좌표를 흘리지 않기 위함).
        /// </summary>
        public static IOcclusionProbe OcclusionProbe { get; private set; }

        /// <summary>서버가 개별 전송한 델리버리를 그릴 소비자(T8 렌더러). 미등록이면 null.</summary>
        public static IPulseDeliverySink DeliverySink { get; private set; }

        public static void RegisterProbe(IOcclusionProbe probe)
        {
            if (probe != null)
                OcclusionProbe = probe;
        }

        public static void UnregisterProbe(IOcclusionProbe probe)
        {
            if (ReferenceEquals(OcclusionProbe, probe))
                OcclusionProbe = null;
        }

        public static void RegisterSink(IPulseDeliverySink sink)
        {
            if (sink != null)
                DeliverySink = sink;
        }

        public static void UnregisterSink(IPulseDeliverySink sink)
        {
            if (ReferenceEquals(DeliverySink, sink))
                DeliverySink = null;
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 static 상태가 남는다.
        /// 이전 판의 파괴된 구현체를 물지 않도록 진입 시 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            OcclusionProbe = null;
            DeliverySink = null;
        }
    }
}
