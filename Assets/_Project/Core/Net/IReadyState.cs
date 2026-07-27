namespace Marco.Core.Net
{
    /// <summary>
    /// 한 플레이어의 로비 준비 상태 계약(스프린트 18, §12.3 준비 버튼 · §14.3 `ReadyToggle`).
    ///
    /// **구조 판단(지시서 §2 질문에 대한 답)**: Ready-up은 두 기존 패턴의 하이브리드다.
    /// - **복제**는 <c>RoleNetworkSync</c> 패턴 — Player 프리팹의 per-플레이어 SyncVar.
    ///   SyncVar는 모든 관전자에게 복제되므로 "전원이 서로의 준비 상태를 안다"가 추가 비용
    ///   없이 성립한다(§14.3 `ReadyToggle | Server → All`).
    /// - **열거**는 <c>ITagTarget</c>/<c>TagTargetRegistry</c> 패턴 — 로비 UI(Presentation)가
    ///   전원 목록을 그려야 하는데 §15.2상 Net 구체 타입을 찾을 수 없으므로, 구현체가
    ///   스스로 <see cref="ReadyStateRegistry"/>에 등록하고 UI는 Core 인터페이스 목록만 본다.
    /// - **입력 검증**은 태그·밸브보다 단순하다 — `ServerRpc`의 기본값
    ///   `RequireOwnership = true`(벤더 소스 확인)가 "자기 pawn의 준비 상태만 토글 가능"을
    ///   프레임워크 수준에서 강제하므로 별도 신원 재검증이 필요 없다.
    /// </summary>
    public interface IReadyState
    {
        /// <summary>플레이어 ID(OwnerId — <see cref="IPlayerIdentity"/>와 같은 번호 공간).</summary>
        ulong PlayerId { get; }

        /// <summary>서버가 확정해 전파한 준비 상태.</summary>
        bool IsReady { get; }

        /// <summary>이 기기의 로컬 플레이어인가(UI가 "(나)" 표기와 토글 대상 선택에 쓴다).</summary>
        bool IsLocalPlayer { get; }

        /// <summary>
        /// 준비 상태 토글을 서버에 요청한다(§14.3 `ReadyToggle`, Client → Server).
        /// 로컬 소유 pawn이 아니면 무시된다. 서버는 로비 페이즈에서만 반영한다(GAP-29).
        /// </summary>
        void RequestToggleReady();
    }
}
