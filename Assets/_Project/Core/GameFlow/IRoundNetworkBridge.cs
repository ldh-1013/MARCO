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

        /// <summary>
        /// 리매치 **찬성 투표**를 서버에 보낸다(스프린트 18 — GAP-27 해소, §12.5 사양 승격).
        ///
        /// 스프린트 17의 "요청 즉시 재시작" 골격이 §12.5 정식 사양(15초 카운트다운·과반 찬성 시
        /// 즉시 재시작)으로 대체됐다. 서버가 <c>RematchVoteDriver</c>로 집계·판정하며,
        /// 가결 → RoleAssign(즉시 재시작) / 부결 → Lobby로 §15.4 분기를 따른다.
        /// 중복 호출은 멱등이다(같은 플레이어의 표는 1표).
        /// </summary>
        void RequestRestart();

        // ── 스프린트 18: 로비·리매치 상태 노출(표시 전용) ─────────────────

        /// <summary>
        /// 서버가 확정한 현재 진행 페이즈(§15.4). 로비 게이팅·카운트다운·결과 화면 전환의
        /// 단일 진실 소스다. 쓰이는 값: Lobby / RoleAssign(3초 카운트다운) / InGame / RoundEnd.
        /// </summary>
        GameFlowState Phase { get; }

        /// <summary>RoleAssign(카운트다운) 페이즈의 남은 초(§12.3 "3초 카운트다운").</summary>
        float CountdownRemaining { get; }

        /// <summary>리매치 유효 찬성 수(§12.5, 현재 접속자 기준).</summary>
        int RematchVotesFor { get; }

        /// <summary>리매치 가결에 필요한 표 수(과반 = 인원/2 + 1).</summary>
        int RematchVotesNeeded { get; }

        /// <summary>리매치 투표 남은 초(§12.5 "15초 카운트다운").</summary>
        float RematchSecondsRemaining { get; }

        /// <summary>
        /// 통산 라운드 번호(0-기반, 스프린트 21). §2.3 술래 로테이션의 순번 기준이며,
        /// 소비자는 이 값이 **바뀌는 순간을 "새 라운드 시작" 신호**로도 쓴다(리매치 스폰 리셋).
        /// </summary>
        int RoundNumber { get; }

        /// <summary>§8 최다 비명상 수상자(플레이어 id). -1이면 수상자 없음.</summary>
        int AwardLoudestScream { get; }

        /// <summary>§8 무성 생존상 수상자(플레이어 id). -1이면 수상자 없음.</summary>
        int AwardSilentSurvivor { get; }

        /// <summary>§8 최고의 거짓말상 수상자(플레이어 id). -1이면 수상자 없음.</summary>
        int AwardBestLiar { get; }
    }
}
