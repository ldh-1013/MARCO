using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// 라운드 진행(타이머·탈출·최종 판정)의 Presentation ↔ Net 연결 계약(스프린트 12,
    /// 네트워크 2단계). 밸브의 <see cref="IValveNetworkBridge"/>·태그의 <c>ITagTarget</c>과
    /// 같은 원리를 라운드 전체에 적용한다.
    ///
    /// **신뢰 모델**: 지금(스프린트 6)은 각 클라이언트가 로컬 타이머를 돌리고 §6.3 판정을
    /// 독립적으로 계산한다 — 타이밍 차이로 클라이언트마다 다른 결과가 나올 위험이 있다.
    /// 이 브릿지가 활성이면, <b>서버 하나</b>가 타이머를 소유하고, 탈출을 재검증하고,
    /// <see cref="WinConditionEvaluator"/>를 서버에서만 호출해 확정한 <see cref="Result"/>를
    /// 전 클라이언트에 전파한다. 클라이언트(<c>RoundCoordinator</c>)는 더 이상 자체 판정하지
    /// 않고 서버가 알려준 값만 반영한다.
    ///
    /// **왜 Core에 두는가**: 구현체는 <c>RoundNetworkSync</c>(Net, <c>NetworkBehaviour</c>)지만,
    /// 소비자는 Presentation(<c>RoundCoordinator</c>·<c>EscapePointTrigger</c>)이다. §15.2상
    /// 둘은 서로를 참조하지 않으므로 공통 조상 Core에 계약만 둔다. Core는 FishNet을 모른다.
    ///
    /// **로컬 폴백**: 네트워크가 시작되지 않은 단독 실행에서는 <see cref="NetworkActive"/>가
    /// false다. 그때 <c>RoundCoordinator</c>는 이 브릿지를 무시하고 스프린트 6의 로컬
    /// 타이머·판정 경로를 그대로 쓴다(회귀 없음).
    /// </summary>
    public interface IRoundNetworkBridge
    {
        /// <summary>
        /// 이 라운드가 서버 권위(네트워크)로 관리되는가. NetworkObject가 스폰된 뒤에만 true다.
        /// false면 <c>RoundCoordinator</c>가 로컬 판정 경로를 쓴다.
        /// </summary>
        bool NetworkActive { get; }

        /// <summary>서버가 소유·전파하는 남은 시간(초).</summary>
        float RemainingSeconds { get; }

        /// <summary>서버가 재검증·확정해 전파한 누적 탈출자 수.</summary>
        int EscapedCount { get; }

        /// <summary>
        /// 서버가 <see cref="WinConditionEvaluator"/>로 확정한 최종 결과.
        /// 아직 승패가 안 갈렸으면 <see cref="RoundResult.InProgress"/>.
        /// </summary>
        RoundResult Result { get; }

        /// <summary>
        /// 클라이언트가 탈출 의사를 서버에 전달한다(ServerRpc). 게이트 개방·역할(러너)
        /// 재검증과 확정은 서버가 한다(§5.3) — 클라이언트는 게이트 개방을 사전 필터로만 본다.
        /// </summary>
        void SubmitEscapeIntent(ulong playerId, RoleType role);
    }
}
