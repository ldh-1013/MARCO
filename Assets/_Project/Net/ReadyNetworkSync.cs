using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.Net;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 한 플레이어의 로비 준비 상태를 서버 권위로 동기화한다(스프린트 18, §12.3 준비 버튼 ·
    /// §14.3 `ReadyToggle {playerId, isReady} | Client → Server | Server → All`).
    ///
    /// **패턴(설계 근거는 <see cref="IReadyState"/> 문서 참고)**: 복제는 <c>RoleNetworkSync</c>
    /// (per-플레이어 SyncVar — 전 피어 자동 전파 = "Server → All"), 열거는 <c>TagNetworkSync</c>
    /// (Core 레지스트리 등록 — 로비 UI가 전원 목록을 그린다)의 하이브리드.
    ///
    /// **소유권 검증은 프레임워크에 위임**: <see cref="ServerSetReady"/>는 `RequireOwnership`
    /// 기본값(true, 벤더 소스 확인)을 그대로 쓴다 — 자기 pawn의 RPC만 호출할 수 있으므로
    /// "남의 준비 상태를 바꾸는" 위장이 프레임워크 수준에서 차단된다(밸브·태그가
    /// `RequireOwnership = false` + 수동 재검증이 필요했던 것과 대조적으로, 이 시스템은
    /// 기본값이 정확히 맞는 첫 사례다).
    ///
    /// **로비 페이즈에서만 반영(GAP-29)**: 라운드 중 준비 해제로 진행 상태를 흔들 수 없도록,
    /// 서버는 <see cref="RoundNetworkSync.ServerPhase"/>가 Lobby일 때만 토글을 반영한다.
    /// NRE 가드(스프린트 10 교훈)는 다른 *NetworkSync와 동일하다.
    /// </summary>
    public sealed class ReadyNetworkSync : NetworkBehaviour, IReadyState
    {
        /// <summary>스폰된 준비 동기화 컴포넌트들 — 서버 로비 게이트의 실측 입력(Net 내부 전용).</summary>
        internal static readonly List<ReadyNetworkSync> Spawned = new List<ReadyNetworkSync>();

        // 서버가 확정해 전 피어에 전파하는 준비 상태(§14.3 "Server → All"은 SyncVar 복제가 담당).
        private readonly SyncVar<bool> _isReady = new();

        /// <summary>NetworkObject가 스폰된 뒤에만 true(연결 전 NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        // ── IReadyState ──────────────────────────────────────────────────

        public ulong PlayerId => NetworkObject != null && OwnerId >= 0 ? (ulong)OwnerId : 0UL;

        public bool IsReady => _isReady.Value;

        public bool IsLocalPlayer => NetworkActive && IsOwner;

        public void RequestToggleReady()
        {
            // 소유자가 아니면 보내지 않는다(보내도 RequireOwnership이 서버에서 거부하지만,
            // 불필요한 트래픽과 경고 로그를 만들지 않는다).
            if (!IsLocalPlayer)
                return;

            ServerSetReady(!_isReady.Value);
        }

        // ── 서버: 반영 ────────────────────────────────────────────────────

        /// <summary>
        /// RequireOwnership 기본값(true)을 그대로 사용 — 소유자만 호출 가능하므로 playerId
        /// 위장 검증이 불필요하다(§14.3 ReadyToggle의 서버 재검증을 프레임워크가 대신한다).
        /// </summary>
        [ServerRpc]
        private void ServerSetReady(bool ready)
        {
            // GAP-29: 준비 토글은 로비에서만 의미가 있다. 카운트다운·라운드 중 토글로
            // 게이트를 흔드는 것을 서버가 차단한다(카운트다운 중단은 이탈만 유발할 수 있다).
            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.Lobby)
            {
                Debug.Log($"[ReadyNet:Server] ownerId={OwnerId} 준비 토글 무시 — 로비 페이즈가 아님({RoundNetworkSync.ServerPhase})");
                return;
            }

            if (_isReady.Value == ready)
                return;

            _isReady.Value = ready;
            Debug.Log($"[ReadyNet:Server] ownerId={OwnerId} 준비 = {ready} (§14.3 ReadyToggle)");
        }

        /// <summary>부결로 로비에 돌아갈 때 전원 준비를 해제한다(서버 전용 — 재라운드는 새 합의로).</summary>
        internal void ServerClearReady()
        {
            _isReady.Value = false;
        }

        // ── 수명주기 ─────────────────────────────────────────────────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            ReadyStateRegistry.Register(this);

            if (!Spawned.Contains(this))
                Spawned.Add(this);

            Debug.Log($"[ReadyNet] 준비 상태 동기화 시작 — ownerId={(NetworkObject != null ? OwnerId : -1)}");
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            ReadyStateRegistry.Unregister(this);
            Spawned.Remove(this);
        }

        /// <summary>도메인 리로드 꺼짐 대비 static 잔여 상태 정리(다른 *NetworkSync와 동일).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Spawned.Clear();
    }
}
