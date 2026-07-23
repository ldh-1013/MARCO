namespace Marco.Core.Net
{
    /// <summary>
    /// "이 플레이어가 발생시킨 이벤트의 발생원 ID는 무엇인가"를 답하는 최소 계약.
    ///
    /// 발소리 파문(§5.5 SourcePlayerId)·밸브 조작자(§6.1)·태그 주체(§3.1)가 전부
    /// 이 ID로 발생원을 식별한다. 지금까지는 각 컴포넌트가 상수 `1`을 따로 박아
    /// 썼는데(스프린트 3~7), 실제로 여러 클라이언트가 접속하면 전원이 같은 값을
    /// 참조해 "누구의 이벤트인지"가 뒤섞인다.
    ///
    /// <para><b>왜 Core에 두는가</b>: 실제 소유자 식별자는 FishNet(Net)이 알고,
    /// 그 값을 소비하는 파이프라인은 Presentation에 있다. §15.2상 Net과
    /// Presentation은 서로를 참조하지 않으므로, 둘이 공통으로 아는 Core에 계약만
    /// 두어 연결한다 — `ILocalControlGate`(스프린트 8, GAP-14)와 완전히 같은 패턴.</para>
    ///
    /// <para><b>타입이 ulong인 이유</b>: Core 파이프라인 전체(`SoundPulse.SourcePlayerId`,
    /// `Valve` 조작자, `RoundOutcomeTracker`의 집합)가 이미 <c>ulong</c>을 쓴다.
    /// FishNet의 <c>OwnerId</c>는 <c>int</c>(소유자 없으면 -1)라, Net 구현체가
    /// 그 값을 ulong으로 변환해 넣는다(GAP-15 참조).</para>
    ///
    /// <para><b>기본값 폴백</b>: 네트워크가 없는 로컬 단독 실행에서는 아무도
    /// <see cref="SetPlayerId"/>를 호출하지 않는다. 그러므로 구현체의 기본값은
    /// 스프린트 3~7이 하드코딩했던 값과 동일해야(로컬 플레이어 = 1) 기존 스모크
    /// 리그가 그대로 동작한다.</para>
    /// </summary>
    public interface IPlayerIdentity
    {
        /// <summary>이 플레이어가 발생시키는 이벤트의 발생원 ID.</summary>
        ulong PlayerId { get; }

        /// <summary>
        /// Net 레이어(소유권 게이트)가 실제 네트워크 소유자 ID를 전달한다.
        /// 로컬 단독 실행에서는 호출되지 않으므로 기본값이 유지된다.
        /// </summary>
        void SetPlayerId(ulong playerId);
    }
}
