using System.Collections.Generic;
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
    /// **초기 배정과 태그 전환의 구분(지시서 §2)**:
    /// - <b>초기 배정</b>(이 컴포넌트): 라운드 시작 시 서버가 1회 배정. Seeker 또는 Runner만.
    /// - <b>역할 전환</b>(<see cref="TagNetworkSync"/>, 스프린트 11): 태그 확정 시 Echo로.
    /// 두 경로가 같은 <see cref="IRoleState"/>를 쓰므로, 이 컴포넌트는 <b>이미 Echo인 플레이어의
    /// 역할을 절대 덮어쓰지 않는다</b>(<see cref="ApplyAssignedRole"/>의 Echo 가드). 태그는
    /// 단방향(Runner→Echo)이고 되돌아가지 않으므로 이 가드만으로 두 경로의 충돌이 사라진다.
    ///
    /// **왜 배정 플래그가 따로 필요한가**: <c>SyncVar&lt;RoleType&gt;</c>의 기본값은 열거형
    /// 0번인 <see cref="RoleType.Seeker"/>다. 플래그 없이 값만 보면 <b>배정 전 모든 플레이어가
    /// 술래로 보인다</b> — 스프린트 12 실기에서 Player 프리팹 <c>_role</c> 기본값이 Seeker(0)로
    /// 굳어 있어 원격 러너가 인식되지 않던 것과 같은 함정이다. 그래서 <see cref="_hasAssignment"/>가
    /// true일 때만 역할을 반영한다.
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

        // 서버가 확정해 전 피어에 전파하는 배정 결과.
        private readonly SyncVar<RoleType> _assignedRole = new();
        private readonly SyncVar<bool> _hasAssignment = new();

        private IRoleState _roleState;

        private void Awake()
        {
            _roleState = GetComponent<IRoleState>();
        }

        /// <summary>NetworkObject가 스폰된 뒤에만 true(연결 전 NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        /// <summary>배정 정렬 키(§GAP-22: OwnerId 오름차순). 소유자가 없으면 -1.</summary>
        internal int OrderKey => NetworkObject != null ? OwnerId : -1;

        /// <summary>서버가 이 플레이어에게 역할을 배정했는가.</summary>
        internal bool HasAssignment => _hasAssignment.Value;

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

        // ── 서버: 배정 ────────────────────────────────────────────────────

        /// <summary>
        /// 서버가 배정 결과를 확정한다(서버 전용). 값이 같으면 SyncVar를 건드리지 않아 멱등이며,
        /// 재배정이 반복돼도 대역폭을 쓰지 않는다.
        /// </summary>
        internal void ServerAssign(RoleType role)
        {
            if (_hasAssignment.Value && _assignedRole.Value == role)
                return;

            _assignedRole.Value = role;
            _hasAssignment.Value = true;
            Debug.Log($"[RoleNet:Server] ownerId={OrderKey} 역할 배정 = {role} (§6.2)");
        }

        /// <summary>
        /// 새 라운드를 위해 배정을 지운다(스프린트 17). 서버 전용.
        ///
        /// 배정 플래그를 내리면 <c>RoundNetworkSync.EnsureRolesAssigned</c>가 다음 프레임에
        /// **§6.2 표대로 다시 배정**한다 — 배정 규칙을 여기 복제하지 않고 기존 경로를 재사용한다.
        /// 술래 재추첨(§2.2 로테이션)은 이번 스코프가 아니므로, GAP-22의 결정론적 정렬대로
        /// 같은 사람이 다시 술래가 된다.
        ///
        /// 태그로 Echo가 된 플레이어도 이 리셋 후 다시 배정 대상이 된다 —
        /// <c>TagNetworkSync.ServerResetForNewRound</c>가 먼저 태그 상태를 풀기 때문이다.
        /// </summary>
        internal void ServerClearAssignmentForNewRound()
        {
            _hasAssignment.Value = false;
        }

        // ── 전 피어: 확정 배정 반영 ──────────────────────────────────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _assignedRole.OnChange += OnAssignedRoleChanged;
            _hasAssignment.OnChange += OnHasAssignmentChanged;

            if (!Spawned.Contains(this))
                Spawned.Add(this);

            // 늦은 스폰·재접속 대비: 이미 배정된 상태로 들어오면 즉시 반영한다.
            if (_hasAssignment.Value)
                ApplyAssignedRole();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _assignedRole.OnChange -= OnAssignedRoleChanged;
            _hasAssignment.OnChange -= OnHasAssignmentChanged;
            Spawned.Remove(this);
        }

        private void OnAssignedRoleChanged(RoleType prev, RoleType next, bool asServer) => ApplyAssignedRole();

        private void OnHasAssignmentChanged(bool prev, bool next, bool asServer)
        {
            if (next)
                ApplyAssignedRole();
        }

        /// <summary>
        /// 서버가 배정한 역할을 이 피어의 <see cref="IRoleState"/>에 반영한다.
        ///
        /// <b>Echo 가드</b>: 이미 태그돼 메아리가 된 플레이어는 건드리지 않는다 — 초기 배정
        /// SyncVar(Runner)가 뒤늦게 도착해 태그 결과(Echo)를 되돌리는 것을 막는다. 두 SyncVar의
        /// 도착 순서나 컴포넌트 콜백 순서에 의존하지 않으려면 이 가드가 필요하다.
        /// </summary>
        private void ApplyAssignedRole()
        {
            if (!_hasAssignment.Value || _roleState == null)
                return;

            if (IsTaggedOut)
                return; // 태그 전환(스프린트 11) 결과를 초기 배정이 덮지 않는다.

            if (_roleState.Role == _assignedRole.Value)
                return;

            _roleState.ApplyRole(_assignedRole.Value);
            Debug.Log($"[RoleNet:Client] ownerId={OrderKey} 역할 반영 = {_assignedRole.Value} — 서버 배정 수신");
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 static 상태가 남는다.
        /// 이전 판의 파괴된 컴포넌트를 물지 않도록 진입 시 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Spawned.Clear();
    }
}
