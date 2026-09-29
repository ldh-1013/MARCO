using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.Net;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 한 플레이어의 역할을 서버 권위로 동기화한다(스프린트 13, §14.3 `RoleAssigned`).
    ///
    /// **신뢰 모델**: 서버가 <see cref="RoleAssigner"/>(Core, §6.2 표)로 배정한 역할을
    /// <see cref="SyncVar{T}"/>로 전 피어에 전파하고, 각 피어가 <see cref="IRoleState"/>(Core,
    /// <c>FirstPersonController</c>가 구현)에 반영한다. 그래서 <b>자기 역할뿐 아니라 다른
    /// 플레이어의 역할도</b> 모든 피어가 일관되게 인지한다 — 태그 판정(§3.1 상대 역할 참조)·
    /// 밸브 GAP-5(메아리 거부)·전원태그(GAP-19)가 실제 배정 결과 위에서 동작하게 되는 근거.
    ///
    /// **배정과 태그는 한 규칙으로(09-29)**: 유효 역할은 (배정, 태그)에서 계산한다 —
    /// 미배정이면 로비 기본값(처음 입장과 같음), 태그됐으면 메아리, 그 외에는 배정 역할
    /// (<see cref="RoleEffect.Resolve"/>). 이 컴포넌트(배정)와 <see cref="TagNetworkSync"/>(태그)가 같은
    /// <see cref="PawnRoleSync"/>를 호출하고, 각 피어는 계산값이 직전 적용값과 다를 때만 적용한다.
    /// 호스트와 클라이언트가 같은 경로다. 로비로 돌아오면(미배정) 메아리도 풀린다.
    ///
    /// **배정은 SyncVar 하나(09-29 원자성)**: 예전에는 배정 플래그와 역할이 서로 다른 SyncVar였고,
    /// FishNet이 플래그를 먼저 보내 클라이언트가 역할 SyncVar의 기본값(Seeker, 열거형 0번)을 한 번 적용한 뒤
    /// Runner로 바꿨다(실기 로그 "Seeker 반영 → Runner 반영"). 이제 <see cref="RoleAssignment"/>를
    /// <c>SyncVar&lt;byte&gt;</c> 하나로 보낸다 — 0 = 미배정이라 기본값이 "술래"로 읽히는 함정도 없다.
    ///
    /// **로컬 폴백 + NRE 가드**: 네트워크 미시작 시 스폰되지 않아 아무 배정도 일어나지 않고,
    /// <c>FirstPersonController</c>의 인스펙터 기본값(Runner)이 유지된다. FishNet의
    /// <c>IsSpawned</c>/<c>OwnerId</c>는 초기화 전 내부 캐시가 null이라 직접 호출하면 NRE가
    /// 나므로(스프린트 10 교훈), <see cref="NetworkBehaviour.NetworkObject"/> null 여부를 먼저 확인한다.
    /// </summary>
    public sealed class RoleNetworkSync : NetworkBehaviour
    {
        /// <summary>
        /// 스폰된 역할 동기화 컴포넌트들(배정 주체가 열거한다). 소비자
        /// (<see cref="RoundNetworkSync"/>)도 Net이라 Core 레지스트리를 거칠 필요가 없다.
        /// </summary>
        internal static readonly List<RoleNetworkSync> Spawned = new List<RoleNetworkSync>();

        // 서버가 확정해 전 피어에 전파하는 배정 결과 — RoleAssignment 부호(0 = 미배정, 1 + RoleType). 하나라서 원자적이다.
        private readonly SyncVar<byte> _assignment = new();

        private IRoleState _roleState;
        private IEscapeState _escapeState;
        private PawnRoleSync _roleSync;

        private void Awake()
        {
            _roleState = GetComponent<IRoleState>();
            _escapeState = GetComponent<IEscapeState>();
        }

        /// <summary>
        /// 이 피어에서 이 pawn의 역할 효과 규칙(Core). 배정(여기)과 태그(<see cref="TagNetworkSync"/>)가 같은 인스턴스를
        /// 호출한다 — 컴포넌트 Awake 순서와 무관하게 쓰이도록 지연 생성.
        /// </summary>
        internal PawnRoleSync RoleSync
        {
            get
            {
                if (_roleSync != null)
                    return _roleSync;

                _roleState ??= GetComponent<IRoleState>();
                _roleSync = new PawnRoleSync(_roleState != null ? _roleState.Role : RoleType.Runner);
                _roleSync.RoleApplied += OnRoleApplied;
                _roleSync.EscapeApplied += OnEscapeApplied;
                return _roleSync;
            }
        }

        /// <summary>서버 SyncVar 값(배정 · 해제 계산의 기준).</summary>
        private RoleSyncState State => new RoleSyncState(RoleAssignment.FromCode(_assignment.Value));

        /// <summary>NetworkObject가 스폰된 뒤에만 true(연결 전 NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        /// <summary>배정 정렬 키(§GAP-22: OwnerId 오름차순). 소유자가 없으면 -1.</summary>
        internal int OrderKey => NetworkObject != null ? OwnerId : -1;

        /// <summary>서버가 이 플레이어에게 역할을 배정했는가.</summary>
        internal bool HasAssignment => State.Assignment.IsAssigned;

        /// <summary>현재 역할(배정 전에는 로컬 기본값).</summary>
        internal RoleType CurrentRole => _roleState != null ? _roleState.Role : RoleType.Runner;

        /// <summary>
        /// 이미 태그돼 메아리가 된 플레이어인가. 초기 배정 대상에서 제외하기 위한 확인이며,
        /// 태그 상태의 진실은 <see cref="TagNetworkSync"/>의 SyncVar(<see cref="ITagTarget.IsTagged"/>)다.
        /// </summary>
        internal bool IsTaggedOut
        {
            get
            {
                var tagTarget = GetComponent<ITagTarget>();

                // 태그 상태의 진실은 서버가 확정한 SyncVar다 — 있으면 그것만 신뢰한다.
                // 스프린트 17: 여기서 현재 역할(Echo)까지 함께 보면, 라운드 재시작으로 태그가
                // 풀린 뒤에도 "아직 Echo 상태"라는 이유로 재배정이 영구히 차단된다(메아리 고착).
                if (tagTarget != null)
                    return tagTarget.IsTagged;

                // ITagTarget이 없는 구성에서는 현재 역할로 대신 판단한다(방어용 폴백).
                return CurrentRole == RoleType.Echo;
            }
        }

        /// <summary>
        /// 게임 규칙이 실제로 보는 역할. <b>태그 아웃이 역할 SyncVar보다 우선한다</b> —
        /// 태그 직후 몇 프레임 동안 <see cref="CurrentRole"/>이 아직 Runner일 수 있는데,
        /// 그 사이에 메아리 능력(§3.2 노크)의 판정이 달라지면 안 된다.
        /// </summary>
        internal RoleType EffectiveRole => IsTaggedOut ? RoleType.Echo : CurrentRole;

        /// <summary>탈출해 월드에서 빠졌는가 — 진실은 <see cref="TagNetworkSync"/>의 서버 SyncVar(09-29).</summary>
        internal bool IsEscapedOut => GetComponent<ITagTarget>() is { } target && target.IsEscaped;

        /// <summary>RPC 호출자가 탈출자인가(서버 측 값).</summary>
        internal static bool CallerEscaped(NetworkConnection caller)
        {
            if (caller == null || caller.FirstObject == null)
                return false;

            RoleNetworkSync sync = caller.FirstObject.GetComponent<RoleNetworkSync>();
            return sync != null && sync.IsEscapedOut;
        }

        /// <summary>
        /// RPC 호출자가 월드 행동(밸브 · 배수구 · 줍기 · 파문)을 할 수 있는가(09-29) — 탈출자는 월드에서 빠졌다.
        /// 신원 확인(<see cref="TryGetCallerIdentity"/>) <b>다음에</b> 부른다.
        /// </summary>
        internal static bool CallerInWorld(NetworkConnection caller) => WorldPresence.CanAct(CallerEscaped(caller));

        // ── 서버: RPC 호출자 신원 조회 ────────────────────────────────────

        /// <summary>
        /// ServerRpc 호출자의 **서버 측 실제 신원**(역할·플레이어 ID)을 얻는다.
        /// 클라이언트가 주장한 값을 쓰지 않기 위한 **단일 진입점**이다.
        ///
        /// <b>왜 필요한가</b>: 밸브·태그·탈출 RPC는 오랫동안 클라이언트가 보낸 <see cref="RoleType"/>을
        /// 그대로 판정에 썼다. 그래서 메아리가 <c>role=Runner</c>를 주장해 밸브를 돌리거나(GAP-5 우회),
        /// 러너가 <c>seekerRole=Seeker</c>로 태그하거나(§3.1 우회), 술래가 탈출로 라운드를 끝낼 수
        /// 있었다(GAP-11 우회). 악의가 없어도 클라이언트의 로컬 역할이 잠깐 어긋나면 같은 결과가 난다.
        ///
        /// <b>신원의 출처</b>: 위치를 <c>caller.FirstObject</c>에서, 발생원 ID를 <c>caller.ClientId</c>에서
        /// 얻는 <see cref="PulseNetworkSync"/>의 패턴(GAP-24)을 역할까지 확장한 것이다.
        ///
        /// <b>태그 아웃 우선</b>: <see cref="IsTaggedOut"/>이면 역할 SyncVar와 무관하게 Echo로 본다 —
        /// 태그 확정과 역할 반영 사이의 프레임에서도 메아리가 물리 상호작용을 하지 못하게 한다.
        /// </summary>
        /// <returns>호출자의 플레이어 오브젝트나 역할 컴포넌트를 찾지 못하면 false(호출자는 요청을 거부해야 한다).</returns>
        internal static bool TryGetCallerIdentity(NetworkConnection caller, out RoleType role, out ulong playerId)
        {
            role = RoleType.Runner;
            playerId = 0UL;

            // 연결이 끊겼거나 아직 플레이어가 스폰되지 않았으면 신원을 확정할 수 없다.
            if (caller == null || caller.FirstObject == null)
                return false;

            RoleNetworkSync sync = caller.FirstObject.GetComponent<RoleNetworkSync>();
            if (sync == null)
                return false;

            role = sync.EffectiveRole;
            playerId = (ulong)caller.ClientId;
            return true;
        }

        // ── 서버: 배정 ────────────────────────────────────────────────────

        /// <summary>
        /// 서버가 배정 결과를 확정한다(서버 전용). 값이 같으면 SyncVar를 건드리지 않아 멱등이며,
        /// 재배정이 반복돼도 대역폭을 쓰지 않는다.
        /// </summary>
        internal void ServerAssign(RoleType role)
        {
            RoleSyncState next = State.Assign(role);
            if (next.Equals(State))
                return;

            _assignment.Value = next.Assignment.ToCode();
            Debug.Log($"[RoleNet:Server] ownerId={OrderKey} 역할 배정 = {role} (§6.2)");
        }

        /// <summary>
        /// 새 라운드를 위해 배정을 지운다(스프린트 17). 서버 전용.
        ///
        /// 미배정이 되면 모든 피어에서 이 pawn의 유효 역할이 로비 기본값으로 돌아간다(메아리 해제 포함, 09-29).
        /// 다음 판의 재배정은 <c>RoundNetworkSync.EnsureRolesAssigned</c>가 §6.2 표대로 한다 — 배정 규칙을 여기
        /// 복제하지 않는다.
        /// </summary>
        internal void ServerClearAssignmentForNewRound()
        {
            _assignment.Value = State.Clear().Assignment.ToCode();
        }

        // ── 전 피어: 확정 배정 반영 ──────────────────────────────────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _assignment.OnChange += OnAssignmentChanged;

            if (!Spawned.Contains(this))
                Spawned.Add(this);

            // 늦은 스폰·재접속 대비: 이미 배정 · 태그 · 탈출된 상태로 들어오면 한 번에 계산해 반영한다(중간 상태 없음).
            RoleSync.ReceiveSnapshot(State, IsTaggedOut, IsEscapedOut);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _assignment.OnChange -= OnAssignmentChanged;
            Spawned.Remove(this);
        }

        private void OnAssignmentChanged(byte prev, byte next, bool asServer) =>
            RoleSync.Receive(RoleSyncUpdate.Of(RoleAssignment.FromCode(next)));

        /// <summary>
        /// 규칙(<see cref="PawnRoleSync"/>)이 정한 역할을 이 피어의 <see cref="IRoleState"/>에 반영한다.
        /// 메아리 전환 로그는 <see cref="TagNetworkSync"/>가 남긴다.
        /// </summary>
        private void OnRoleApplied(RoleType role)
        {
            _roleState?.ApplyRole(role);
            if (role != RoleType.Echo)
                Debug.Log($"[RoleNet:Client] ownerId={OrderKey} 역할 반영 = {role} — 서버 배정 수신");
        }

        /// <summary>규칙이 정한 탈출(월드 제외)을 이 피어의 <see cref="IEscapeState"/>에 반영한다(09-29).</summary>
        private void OnEscapeApplied(bool escaped)
        {
            _escapeState?.ApplyEscaped(escaped);
            Debug.Log($"[RoleNet:Client] ownerId={OrderKey} {(escaped ? "탈출 반영 — 월드에서 제외" : "탈출 해제 — 월드 복귀")}");
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 static 상태가 남는다.
        /// 이전 판의 파괴된 컴포넌트를 물지 않도록 진입 시 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Spawned.Clear();
    }
}
