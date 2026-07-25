using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// 씬의 밸브 게이트 상태(<see cref="IEscapeGateState"/>)를 Net 레이어가 어셈블리 경계를
    /// 넘어 읽게 해주는 지연 바인딩 지점(스프린트 12).
    ///
    /// **왜 필요한가**: 서버 라운드 판정(<c>RoundNetworkSync</c>, Net)이 밸브 개방 집계
    /// (<c>ValveObjectiveTracker</c>, Presentation)를 읽어야 하는데, §15.2상 Net은
    /// Presentation의 구체 타입을 <c>FindObjectsByType&lt;T&gt;()</c>로 찾을 수 없다.
    /// 집계기가 스스로 여기 등록하면, 서버는 Core 인터페이스만 본다 —
    /// <c>TagTargetRegistry</c>·<c>LocalPlayerRegistry</c>와 같은 패턴이다.
    ///
    /// 게이트 집계기는 라운드당 하나이므로(밸브 전체를 한 곳에서 집계) 단일 <see cref="Current"/>로
    /// 노출한다. 여러 개가 등록되면 마지막 것이 유효하다(씬 구조상 발생하지 않아야 정상).
    /// </summary>
    public static class EscapeGateRegistry
    {
        /// <summary>현재 활성 게이트 집계기. 아직 없으면 null.</summary>
        public static IEscapeGateState Current { get; private set; }

        public static void Register(IEscapeGateState gate)
        {
            if (gate != null)
                Current = gate;
        }

        public static void Unregister(IEscapeGateState gate)
        {
            if (Current == gate)
                Current = null;
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 static 상태가 남는다.
        /// 이전 판의 파괴된 집계기를 물지 않도록 진입 시 초기화한다
        /// (<c>TagTargetRegistry</c>·<c>LocalPlayerRegistry</c>와 동일한 안전장치).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            Current = null;
        }
    }
}
