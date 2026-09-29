using System;
using System.Collections.Generic;

namespace Marco.Core.Role
{
    /// <summary>
    /// 서버가 확정한 역할 배정 — <b>미배정이거나, 배정된 역할 하나</b>. <c>RoleNetworkSync</c>는 이 값을 SyncVar <b>하나</b>로
    /// 보낸다(09-29 원자성). 예전에는 플래그와 역할이 서로 다른 SyncVar였고 FishNet이 플래그를 먼저 보내, 클라이언트가
    /// 역할 SyncVar의 기본값(Seeker)을 한 번 적용한 뒤 Runner로 바꿨다(실기 로그 "Seeker 반영 → Runner 반영").
    /// </summary>
    public readonly struct RoleAssignment : IEquatable<RoleAssignment>
    {
        public readonly bool IsAssigned;
        public readonly RoleType Role;

        private RoleAssignment(bool isAssigned, RoleType role)
        {
            IsAssigned = isAssigned;
            Role = role;
        }

        /// <summary>미배정(로비 · 판 사이). SyncVar 기본값과 같다 — 기본값이 "술래"로 읽히는 함정이 없다.</summary>
        public static readonly RoleAssignment Unassigned = default;

        public static RoleAssignment Of(RoleType role) => new RoleAssignment(true, role);

        /// <summary>SyncVar&lt;byte&gt; 부호: 0 = 미배정, 1 + <see cref="RoleType"/>.</summary>
        public byte ToCode() => IsAssigned ? (byte)(1 + (int)Role) : (byte)0;

        public static RoleAssignment FromCode(byte code) => code == 0 ? Unassigned : Of((RoleType)(code - 1));

        public bool Equals(RoleAssignment other) => IsAssigned == other.IsAssigned && (!IsAssigned || Role == other.Role);

        public override bool Equals(object obj) => obj is RoleAssignment other && Equals(other);

        public override int GetHashCode() => ToCode();

        public override string ToString() => IsAssigned ? Role.ToString() : "Unassigned";
    }

    /// <summary>
    /// 한 pawn의 <b>유효 상태</b> — 역할과 월드 존재(09-29). 탈출자는 역할(도망자)을 그대로 두고 <b>월드에서 빠진다</b>
    /// (<see cref="WorldPresence"/>: 몸 숨김 · 충돌 없음 · 상호작용 · 파문 제외 · 이동 입력 정지).
    /// </summary>
    public readonly struct PawnEffect : IEquatable<PawnEffect>
    {
        public readonly RoleType Role;
        public readonly bool Escaped;

        public PawnEffect(RoleType role, bool escaped)
        {
            Role = role;
            Escaped = escaped;
        }

        public static PawnEffect InWorld(RoleType role) => new PawnEffect(role, false);

        public bool Equals(PawnEffect other) => Role == other.Role && Escaped == other.Escaped;

        public override bool Equals(object obj) => obj is PawnEffect other && Equals(other);

        public override int GetHashCode() => ((int)Role << 1) | (Escaped ? 1 : 0);

        public override string ToString() => Escaped ? $"{Role}(탈출)" : Role.ToString();
    }

    /// <summary>
    /// 한 pawn의 <b>유효 상태</b>를 (배정, 태그, 탈출)에서 계산하는 규칙(09-29). 로비로 돌아온 플레이어는 처음 로비에 들어온 플레이어와
    /// 같아야 한다 — "다음 배정 때 메아리를 푼다"는 설계는 폐기.
    /// </summary>
    public static class RoleEffect
    {
        /// <summary>미배정이면 로비 기본 상태(처음 입장과 같음), 태그됐으면 메아리, 그 외에는 배정 역할.</summary>
        public static RoleType Resolve(RoleAssignment assignment, bool tagged, RoleType lobbyDefault) =>
            Resolve(assignment, tagged, escaped: false, lobbyDefault).Role;

        /// <summary>
        /// 우선순위: 미배정(로비) → 로비 기본 상태 / 탈출 → 배정 역할 그대로 + 월드에서 제외 / 태그 → 메아리 / 그 외 배정 역할.
        /// </summary>
        public static PawnEffect Resolve(RoleAssignment assignment, bool tagged, bool escaped, RoleType lobbyDefault)
        {
            if (!assignment.IsAssigned)
                return PawnEffect.InWorld(lobbyDefault);

            // §6.3 "탈출과 태그가 동일 프레임(같은 도망자) — 탈출 우선". 탈출자는 메아리가 되지 않는다.
            if (escaped)
                return new PawnEffect(assignment.Role, true);

            return PawnEffect.InWorld(tagged ? RoleType.Echo : assignment.Role);
        }
    }

    /// <summary><c>RoleNetworkSync</c>의 배정 SyncVar가 바뀌었다는 통지(클라이언트가 받는 단위).</summary>
    public readonly struct RoleSyncUpdate
    {
        public readonly RoleAssignment Assignment;

        private RoleSyncUpdate(RoleAssignment assignment) => Assignment = assignment;

        public static RoleSyncUpdate Of(RoleAssignment assignment) => new RoleSyncUpdate(assignment);

        public override string ToString() => $"Assignment({Assignment})";
    }

    /// <summary>
    /// 서버가 가진 역할 배정 SyncVar 값. <c>RoleNetworkSync</c>는 배정 · 해제를 이 값으로 계산해 SyncVar에 쓴다.
    /// </summary>
    public readonly struct RoleSyncState : IEquatable<RoleSyncState>
    {
        public readonly RoleAssignment Assignment;

        public RoleSyncState(RoleAssignment assignment) => Assignment = assignment;

        /// <summary>스폰 직후(SyncVar 기본값) — 미배정.</summary>
        public static readonly RoleSyncState Initial = default;

        /// <summary>배정. 이미 같은 배정이면 그대로(멱등 — SyncVar를 건드리지 않는다).</summary>
        public RoleSyncState Assign(RoleType role) =>
            Assignment.Equals(RoleAssignment.Of(role)) ? this : new RoleSyncState(RoleAssignment.Of(role));

        /// <summary>새 판 해제 — 미배정으로.</summary>
        public RoleSyncState Clear() => new RoleSyncState(RoleAssignment.Unassigned);

        public bool Equals(RoleSyncState other) => Assignment.Equals(other.Assignment);

        public override bool Equals(object obj) => obj is RoleSyncState other && Equals(other);

        public override int GetHashCode() => Assignment.GetHashCode();
    }

    /// <summary>
    /// 서버 SyncVar 값이 바뀌었을 때 <b>클라이언트가 통지를 받는 순서</b>. 배정은 SyncVar 하나라 통지도 하나다 — 한 번에 온다.
    /// (09-28까지는 플래그 · 역할 두 SyncVar였고 FishNet이 플래그를 먼저 보냈다: 실기 로그 P0 189–193 · P1 165–169.)
    /// 바뀐 경우에만 온다(값이 같으면 SyncVar가 더러워지지 않는다).
    /// </summary>
    public static class RoleSyncWire
    {
        public static IEnumerable<RoleSyncUpdate> ClientArrival(RoleSyncState before, RoleSyncState after)
        {
            if (!before.Assignment.Equals(after.Assignment))
                yield return RoleSyncUpdate.Of(after.Assignment);
        }
    }

    /// <summary>
    /// 한 피어(서버 · 호스트 · 클라이언트)에서 한 pawn의 역할 효과를 정한다 — <c>RoleNetworkSync</c>(배정)와 <c>TagNetworkSync</c>(태그)가
    /// <b>둘 다 이 인스턴스를 호출한다</b>. 호스트와 클라이언트가 같은 경로다. 실제 적용(<c>IRoleState.ApplyRole</c>) · 태그 집계 통지는
    /// 이벤트로 Net에 돌려준다. Unity · FishNet과 무관 — EditMode 테스트 가능.
    ///
    /// <para>
    /// <b>규칙(09-29)</b>: 통지를 받을 때마다 <see cref="RoleEffect.Resolve"/>로 유효 역할을 다시 계산하고, <b>직전 적용값과 다를 때만</b>
    /// 적용한다. 예전의 피어별 1회성 가드(<c>_appliedTagEffect</c> — 서버에서만 풀려 비호스트는 2판 태그를 건너뛰었다)와
    /// 두 콜백의 <c>if (next)</c>(해제 통지를 무시해 로비에서 메아리가 남았다)를 이것으로 대체한다. 호스트에서 OnChange가
    /// 서버 · 클라 양쪽으로 불려도 두 번째는 같은 값이라 아무것도 하지 않는다.
    /// </para>
    /// </summary>
    public sealed class PawnRoleSync
    {
        private readonly List<RoleType> _timeline = new List<RoleType>();

        // 이 피어가 받은 SyncVar 값의 거울.
        private RoleAssignment _assignment;
        private bool _tagged;
        private bool _escaped;

        public PawnRoleSync(RoleType initialRole)
        {
            LobbyDefault = initialRole;
            AppliedEffect = PawnEffect.InWorld(initialRole);
        }

        /// <summary>처음 로비에 들어올 때의 역할(프리팹 기본값) — 미배정일 때의 유효 역할.</summary>
        public RoleType LobbyDefault { get; }

        /// <summary>직전 적용값 — 지금 이 피어의 <c>IRoleState</c> · <c>IEscapeState</c>에 적용된 상태.</summary>
        public PawnEffect AppliedEffect { get; private set; }

        /// <summary>직전 적용 역할 — 지금 이 피어의 <c>IRoleState</c>에 적용된 역할.</summary>
        public RoleType Applied => AppliedEffect.Role;

        /// <summary>통지를 받을 때마다의 유효 역할(연속 중복은 하나로). 중간 역할이 끼면 여기 드러난다.</summary>
        public IReadOnlyList<RoleType> Timeline => _timeline;

        /// <summary>메아리 효과(역할 전환 + 태그 집계 통지)가 이 피어에서 적용된 횟수.</summary>
        public int EchoEffectCount { get; private set; }

        /// <summary>역할을 실제로 바꿔야 할 때 — Net이 <c>IRoleState.ApplyRole</c>을 부른다.</summary>
        public event Action<RoleType> RoleApplied;

        /// <summary>메아리 효과 — Net이 태그 집계(<c>TagTargetRegistry.NotifyTagged</c>)를 통지한다.</summary>
        public event Action EchoEffectApplied;

        /// <summary>탈출(월드 제외)이 바뀌어야 할 때 — Net이 <c>IEscapeState.ApplyEscaped</c>를 부른다.</summary>
        public event Action<bool> EscapeApplied;

        /// <summary>배정 SyncVar 변경 통지.</summary>
        public void Receive(RoleSyncUpdate update)
        {
            _assignment = update.Assignment;
            Evaluate();
        }

        /// <summary>늦은 스폰 · 재접속 — 이미 값이 있는 상태로 시작한다(배정과 태그를 한 번에 계산).</summary>
        public void ReceiveSnapshot(RoleSyncState state, bool tagged) => ReceiveSnapshot(state, tagged, _escaped);

        /// <summary>늦은 스폰 · 재접속 — 배정 · 태그 · 탈출을 한 번에 계산한다(중간 상태 없음).</summary>
        public void ReceiveSnapshot(RoleSyncState state, bool tagged, bool escaped)
        {
            _assignment = state.Assignment;
            _tagged = tagged;
            _escaped = escaped;
            Evaluate();
        }

        /// <summary>탈출 SyncVar 변경 통지 — 태그와 같은 경로로 다시 계산한다(따로 1회성 처리를 두지 않는다).</summary>
        public void ReceiveEscaped(bool escaped)
        {
            _escaped = escaped;
            Evaluate();
        }

        /// <summary>태그 SyncVar 변경 통지 — true(태그)든 false(새 판 초기화)든 똑같이 다시 계산한다.</summary>
        public void ReceiveTagged(bool tagged)
        {
            _tagged = tagged;
            Evaluate();
        }

        private void Evaluate()
        {
            PawnEffect target = RoleEffect.Resolve(_assignment, _tagged, _escaped, LobbyDefault);
            if (!target.Equals(AppliedEffect))
            {
                PawnEffect previous = AppliedEffect;
                AppliedEffect = target;

                if (target.Role != previous.Role)
                {
                    RoleApplied?.Invoke(target.Role);

                    if (target.Role == RoleType.Echo)
                    {
                        EchoEffectCount++;
                        EchoEffectApplied?.Invoke();
                    }
                }

                if (target.Escaped != previous.Escaped)
                    EscapeApplied?.Invoke(target.Escaped);
            }

            if (_timeline.Count == 0 || _timeline[_timeline.Count - 1] != Applied)
                _timeline.Add(Applied);
        }
    }
}
