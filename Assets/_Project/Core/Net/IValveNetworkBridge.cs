using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Core.Net
{
    /// <summary>
    /// 서버 권위 밸브의 Presentation ↔ Net 연결 계약(스프린트 10, 네트워크 2단계).
    ///
    /// **역할**: 밸브가 네트워크로 관리될 때, 입력 계층(<c>ValveInteractor</c>, Presentation)은
    /// 더 이상 Core <see cref="Valve"/>를 직접 조작하지 않고 이 브릿지에 **홀드 의사만 전달**한다.
    /// 실제 상태 변경은 서버가 결정하며, 확정된 상태는 <see cref="State"/>·<see cref="Progress01"/>로
    /// 되돌아온다. 시각·집계 계층(<c>ValveBehaviour</c>·<c>ValveObjectiveTracker</c>)은
    /// 네트워크가 활성일 때 이 값을 읽어 표시한다.
    ///
    /// **왜 Core에 두는가**: 구현체는 `ValveNetworkSync`(Net, `NetworkBehaviour`)지만,
    /// 소비자는 Presentation이다. §15.2상 둘은 서로를 참조하지 않으므로 공통 조상 Core에
    /// 계약만 둔다 — <see cref="ILocalControlGate"/>·<see cref="IPlayerIdentity"/>와 같은 패턴.
    /// Core는 FishNet을 모르므로 이 파일에는 어떤 FishNet 타입도 없다.
    ///
    /// **로컬 폴백**: 네트워크가 시작되지 않은 단독 실행에서는 <see cref="NetworkActive"/>가
    /// false다. 그때 Presentation은 이 브릿지를 무시하고 스프린트 5의 클라이언트 권위 경로
    /// (로컬 <see cref="Valve"/> 직접 조작)를 그대로 쓴다.
    /// </summary>
    public interface IValveNetworkBridge
    {
        /// <summary>
        /// 이 밸브가 서버 권위(네트워크)로 관리되는 상태인가. 네트워크가 시작되어
        /// NetworkObject가 스폰된 뒤에만 true다. false면 로컬 클라이언트 권위 경로를 쓴다.
        /// </summary>
        bool NetworkActive { get; }

        /// <summary>서버가 확정한 현재 상태.</summary>
        ValveState State { get; }

        /// <summary>서버가 확정한 현재 진행도(0~1).</summary>
        float Progress01 { get; }

        /// <summary>
        /// 클라이언트가 이번 프레임의 홀드 의사를 서버에 전달한다.
        /// <paramref name="held"/>는 "E를 누르고 있고 상호작용 범위 안"일 때 true다
        /// (범위 판정은 클라이언트가 하며, 서버는 역할·상태만 재검증한다 — GAP-17).
        /// 구현체는 값이 바뀔 때만 ServerRpc를 보내 대역폭을 아낀다.
        /// </summary>
        void SubmitHoldIntent(ulong playerId, RoleType role, bool held);
    }
}
