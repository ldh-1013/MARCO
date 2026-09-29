using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Tagging;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 한 플레이어를 태그 대상으로서 서버 권위 동기화한다(스프린트 11, 밸브 패턴 재사용).
    ///
    /// **신뢰 모델**: 술래 클라이언트는 "이 플레이어를 태그하겠다"는 요청만 보내고
    /// (<see cref="ServerRequestTag"/>), 서버가 §3.1 규칙(<see cref="ServerTagDriver"/>)으로
    /// 거리·역할·이미태그됨을 <b>재검증</b>한 뒤에만 확정한다. 확정되면
    /// <see cref="SyncVar{T}"/>가 모든 피어에 전파되고, 각 피어에서 대상의 역할이 Echo로
    /// 바뀐다 — 대상 본인 화면에도 반영된다.
    ///
    /// **밸브와 다른 점**: 밸브는 대상 오브젝트 위에서 홀드 타이머를 서버가 굴렸지만,
    /// 태그는 순간 판정이라 타이머가 없다. 대신 "술래가 누구이고 어디 있나"를 서버가
    /// 알아야 거리 재검증이 가능한데, 이는 RPC 호출자(<see cref="NetworkConnection"/>)의
    /// <see cref="NetworkConnection.FirstObject"/>(= 술래 플레이어)에서 위치를 읽어 해결한다.
    ///
    /// **어셈블리 경계**: 대상의 역할은 <see cref="IRoleState"/>(Core, FirstPersonController가 구현)로
    /// 읽고 바꾸며, 라운드 집계 통지는 <see cref="TagTargetRegistry"/>(Core)로 한다 —
    /// Net은 Presentation을 직접 참조하지 않는다.
    ///
    /// **로컬 폴백 + NRE 가드**: 네트워크 미시작 시 스폰되지 않아 <see cref="NetworkActive"/>가
    /// false다. FishNet의 <c>IsSpawned</c>/<c>OwnerId</c>는 초기화 전 내부 캐시가 null이라
    /// 직접 호출하면 NRE가 나므로(스프린트 10 교훈), 여기서는 <see cref="NetworkBehaviour.NetworkObject"/>
    /// null 여부를 먼저 확인한다.
    /// </summary>
    public sealed class TagNetworkSync : NetworkBehaviour, ITagTarget
    {
        // 서버가 확정해 전 피어에 전파하는 태그 상태.
        private readonly SyncVar<bool> _tagged = new();

        /// <summary>
        /// §3.1-1 태그가 확정된 서버 시각. 도망자 3.0초 암전의 기준점이며
        /// 0이면 태그되지 않은 상태다. <b>전 피어에 전파해야</b> 각자 암전 연출을
        /// 같은 시점에 시작한다.
        /// </summary>
        private readonly SyncVar<float> _taggedAt = new();

        /// <summary>
        /// §3.1-1 술래별 경직 시작 시각(서버 전용, 술래 playerId → 시각).
        ///
        /// <para>
        /// <b>술래 오브젝트가 아니라 여기서 관리하는 이유</b>: 경직은 "태그가 성립한 순간"에
        /// 걸리는 것이고 그 순간을 아는 것은 태그 대상 쪽의 이 RPC다. 술래 컴포넌트에
        /// 두면 태그 성립을 다시 알려 줘야 해서 경로가 둘로 갈린다.
        /// <c>static</c>인 이유는 태그 대상마다 인스턴스가 다르지만 술래는 하나이기 때문이다.
        /// </para>
        /// </summary>
        private static readonly Dictionary<ulong, float> ServerSeekerStunnedAt =
            new Dictionary<ulong, float>();

        private IRoleState _roleState;
        private RoleNetworkSync _roleSyncOwner;
        private bool _subscribed;

        /// <summary>
        /// 스폰된 태그 동기화 컴포넌트들(스프린트 17). 라운드 재시작 시 <c>RoundNetworkSync</c>가
        /// 태그 상태를 전체 초기화하려고 열거한다(<c>RoleNetworkSync.Spawned</c>와 같은 패턴).
        /// </summary>
        internal static readonly List<TagNetworkSync> Spawned = new List<TagNetworkSync>();

        private void Awake()
        {
            _roleState = GetComponent<IRoleState>();
        }

        /// <summary>이 pawn의 역할 효과 규칙(Core) — <see cref="RoleNetworkSync"/>와 같은 인스턴스.</summary>
        private PawnRoleSync RoleSync
        {
            get
            {
                _roleSyncOwner ??= GetComponent<RoleNetworkSync>();
                if (_roleSyncOwner == null)
                    return null;

                PawnRoleSync sync = _roleSyncOwner.RoleSync;
                if (!_subscribed)
                {
                    _subscribed = true;
                    sync.EchoEffectApplied += OnEchoEffectApplied;
                }

                return sync;
            }
        }

        // ── ITagTarget ────────────────────────────────────────────────────

        /// <summary>NetworkObject가 스폰된 뒤에만 true. 로컬/연결 전에는 false(§NetworkActive NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        public ulong PlayerId => NetworkObject != null && OwnerId >= 0 ? (ulong)OwnerId : 0UL;

        public RoleType Role => _roleState != null ? _roleState.Role : RoleType.Runner;

        public bool IsTagged => _tagged.Value;

        /// <summary>§3.1-1 태그 확정 시각(네트워크 공유). 암전 연출의 기준점이다.</summary>
        public float TaggedAt => _taggedAt.Value;

        /// <summary>§3.1-1 이 대상이 지금 암전 중인가(태그 후 3.0초).</summary>
        public bool IsBlackedOut =>
            Core.Tagging.TagAftermath.IsRunnerBlackedOut(_taggedAt.Value, Time.time);

        /// <summary>§3.1-1 암전 잔여(초).</summary>
        public float BlackoutRemaining =>
            Core.Tagging.TagAftermath.BlackoutRemaining(_taggedAt.Value, Time.time);

        /// <summary>
        /// §3.1-1 이 술래가 경직 중인가. <b>서버 전용</b>(경직 맵이 서버에만 있다) —
        /// 이동 제한을 클라이언트에도 걸려면 별도 전파가 필요하다(GAP-85).
        /// </summary>
        internal static bool IsSeekerStunnedOnServer(ulong seekerId, float now) =>
            ServerSeekerStunnedAt.TryGetValue(seekerId, out float at)
            && Core.Tagging.TagAftermath.IsSeekerStunned(at, now);

        public Vector3 WorldPosition => transform.position;

        /// <summary>
        /// 술래 신원(<paramref name="seekerId"/>·<paramref name="seekerRole"/>)은 **전송하지 않는다** —
        /// 서버가 RPC 호출자에서 직접 읽는다(아래 <see cref="ServerRequestTag"/>). 인자는
        /// <see cref="ITagTarget"/> 계약을 로컬 대역(<c>TaggableRunner</c>)과 공유하기 위해 남아 있다.
        /// </summary>
        public void RequestTag(ulong seekerId, RoleType seekerRole)
        {
            if (!NetworkActive)
                return;

            ServerRequestTag();
        }

        // ── 서버: 재검증 ──────────────────────────────────────────────────

        /// <summary>
        /// 대상은 특정 플레이어가 소유하지 않는 관점에서 호출되므로 소유권 검사를 끈다
        /// (술래가 대상 오브젝트의 RPC를 부른다). <paramref name="caller"/>는 FishNet이 주입하는
        /// 술래의 커넥션 — 그 FirstObject에서 **위치와 역할을 모두** 서버가 읽는다.
        ///
        /// <b>페이로드가 비어 있다(GAP-18 해소)</b>. 이전에는 술래가 자기 역할을 주장했고 서버가
        /// 그 값을 <see cref="ServerTagDriver.Validate"/>에 그대로 넘겨, 러너가 <c>seekerRole=Seeker</c>를
        /// 보내면 §3.1("술래만 태그 가능")을 우회할 수 있었다. GAP-18이 이월 사유로 적었던
        /// "`RoleAssigned` 네트워크화"는 스프린트 13에서 완료됐으므로 이제 서버가 실제 역할을 읽는다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestTag(NetworkConnection caller = null)
        {
            // 스프린트 18: 태그는 라운드 중에만 성립한다(로비·카운트다운·결과 화면 차단).
            // 부결 후 로비에서 직전 라운드의 술래 역할이 잠시 남아 있어도 여기서 걸린다.
            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.InGame)
            {
                Debug.Log($"[TagNet:Server] targetId={PlayerId} 태그 무시 — 라운드 중이 아님({RoundNetworkSync.ServerPhase})");
                return;
            }

            if (_tagged.Value)
                return; // 이미 태그됨 — 재확정 불필요.

            // 술래의 서버 측 신원(역할·ID). 플레이어 오브젝트를 못 찾으면 검증 불가라 거부.
            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType seekerRole, out ulong seekerId))
            {
                Debug.LogWarning($"[TagNet:Server] targetId={PlayerId} 태그 거부 — 술래 오브젝트/역할을 찾을 수 없음");
                return;
            }

            // 위치도 같은 출처에서 읽는다(§5.3 거리 재검증 — 스프린트 11부터 이미 서버 권위).
            Vector3 seekerPosition = caller.FirstObject.transform.position;
            RoleType targetRole = Role;

            // §6.5-3 잠수 중인 대상은 **바닥에 있는 것으로** 잰다(GAP-89) — 발 위치로 재면
            //   술래가 수면에 선 채 3.5m 아래 배수구 작업자를 잡는다. 잠수 여부는 숨 게이지가
            //   쓰는 것과 같은 서버 판정이다(클라이언트 주장이 아니다).
            Vector3 targetPosition = Core.Locomotion.DiveRules.ContactPosition(
                transform.position,
                Core.Water.WaterVolumeRegistry.Sample(transform.position),
                PulseNetworkSync.ServerIsSubmerged(PlayerId));

            if (!ServerTagDriver.Validate(seekerRole, targetRole, _tagged.Value, seekerPosition, targetPosition))
            {
                Debug.Log($"[TagNet:Server] targetId={PlayerId} 태그 거부 — 서버 재검증 실패 " +
                          $"(seekerRole={seekerRole} ← 서버 측 실제 역할, targetRole={targetRole}, 거리검증 포함 §5.3)");
                return;
            }

            // §3.1-1 ★ 술래 경직이 남아 있으면 태그 자체가 성립하지 않는다.
            //   **이동만 막으면 안 된다** — 제자리 연속 태그로 붙어 있는 도망자 둘이
            //   한 번에 정리된다.
            if (ServerSeekerStunnedAt.TryGetValue(seekerId, out float stunnedAt)
                && Core.Tagging.TagAftermath.IsSeekerStunned(stunnedAt, Time.time))
            {
                Debug.Log($"[TagNet:Server] targetId={PlayerId} 태그 거부 — 술래 경직 중 " +
                          $"(잔여 {Core.Tagging.TagAftermath.StunRemaining(stunnedAt, Time.time):0.00}초, §3.1-1)");
                return;
            }

            _tagged.Value = true; // → OnChange가 전 피어에 전파.
            _taggedAt.Value = Time.time;

            // §3.1-1 술래 1.0초 경직(이동·태그 모두 불가).
            ServerSeekerStunnedAt[seekerId] = Time.time;

            // §3.1-1 태그 지점에서 고함급 파문 1회(발생 22m / 지속 2.5초).
            //   **새 SoundType을 만들지 않는다** — 기존 Shout 등급 그대로다(§3.3).
            //   발생원을 WorldSourceId로 두므로 §8.1 최다 비명상(Scream만 집계)에
            //   섞이지 않고, 어떤 플레이어의 §8.2 소음량으로도 귀속되지 않는다.
            PulseNetworkSync.ServerEmitWorldPulse(
                Core.Tagging.TagAftermath.PulseType, transform.position);

            Debug.Log($"[TagNet:Server] targetId={PlayerId} 태그 확정 — 서버 재검증 통과 " +
                      $"(seeker={seekerId}, 술래 경직 {Core.Tagging.TagAftermath.SeekerStunSeconds:0.0}초, " +
                      $"암전 {Core.Tagging.TagAftermath.RunnerBlackoutSeconds:0.0}초, " +
                      $"태그 파문 {Core.Tagging.TagAftermath.PulseType} 1회 §3.1-1)");
        }

        // ── 전 피어: 확정 반영 ────────────────────────────────────────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _tagged.OnChange += OnTaggedChanged;
            TagTargetRegistry.Register(this);

            if (!Spawned.Contains(this))
                Spawned.Add(this);

            // [진단] 원격 플레이어가 호스트의 TagTargetRegistry에 실제로 등록되는지 실기에서 확인용.
            // 이 로그는 로컬/원격 게이트 없이 스폰된 모든 피어에서 찍혀야 정상이다 —
            // 호스트라면 자기 플레이어 + 원격 플레이어(들) 각각에 대해 한 번씩 나와야 한다.
            // 원격 플레이어에 대한 이 로그가 호스트 콘솔에 안 뜨면, 등록이 아니라 TagNetworkSync
            // 컴포넌트가 스폰 프리팹에 실제로 위빙/부착되지 않은 것(Reserialize 필요, 스프린트 11 경고).
            Debug.Log($"[TagNet:Register] 태그 대상 등록 — targetId={PlayerId}, obj={gameObject.name}, " +
                      $"서버컨텍스트={IsServerInitialized}, 클라컨텍스트={IsClientInitialized}");

            // 재접속·늦은 스폰 대비: 이미 태그된 상태로 들어오면 효과를 즉시 반영.
            RoleSync?.ReceiveTagged(_tagged.Value);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _tagged.OnChange -= OnTaggedChanged;
            TagTargetRegistry.Unregister(this);
            Spawned.Remove(this);
        }

        /// <summary>
        /// 새 라운드를 위해 태그 상태를 초기화한다(스프린트 17). 서버 전용.
        ///
        /// SyncVar를 false로 되돌리면 <see cref="OnTaggedChanged"/>가 <b>모든 피어에서</b> 유효 역할을 다시 계산한다
        /// (<see cref="PawnRoleSync"/> — 호스트와 클라이언트가 같은 경로). 예전의 피어별 1회성 가드는 서버에서만 풀려
        /// 비호스트 클라이언트는 2판 태그를 건너뛰었다(09-29) — 가드 없이 "직전 적용값과 다를 때만"으로 대체했다.
        /// </summary>
        internal void ServerResetForNewRound()
        {
            _tagged.Value = false;
            _taggedAt.Value = 0f;

            // §3.1-1 술래 경직 기록도 라운드 경계에서 비운다 — 지난 라운드의 경직이
            // 살아남으면 새 라운드 첫 태그가 거부된다(더블체크 2).
            ServerSeekerStunnedAt.Clear();
        }

        /// <summary>도메인 리로드를 끈 채 Play를 반복할 때의 static 잔여 상태 정리.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Spawned.Clear();

        private void OnTaggedChanged(bool prev, bool next, bool asServer) => RoleSync?.ReceiveTagged(next);

        /// <summary>
        /// 메아리 효과 — 역할 전환(Echo)은 <see cref="RoleNetworkSync"/>가 적용하고, 여기서는 라운드 집계에 통지한다.
        /// 몇 번 적용할지는 규칙(<see cref="PawnRoleSync"/>)이 정한다.
        /// </summary>
        private void OnEchoEffectApplied()
        {
            TagTargetRegistry.NotifyTagged(this); // 각 피어의 RoundCoordinator가 서버 확정 태그를 집계

            Debug.Log($"[TagNet:Client] targetId={PlayerId} 메아리로 전환 — 서버 확정 수신, 화면 반영 (§3.1)");
        }
    }
}
