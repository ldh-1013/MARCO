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

        private IRoleState _roleState;
        private bool _appliedTagEffect; // 피어별 태그 효과(역할 전환·통지)를 정확히 1회만 적용

        /// <summary>
        /// 스폰된 태그 동기화 컴포넌트들(스프린트 17). 라운드 재시작 시 <c>RoundNetworkSync</c>가
        /// 태그 상태를 전체 초기화하려고 열거한다(<c>RoleNetworkSync.Spawned</c>와 같은 패턴).
        /// </summary>
        internal static readonly List<TagNetworkSync> Spawned = new List<TagNetworkSync>();

        private void Awake()
        {
            _roleState = GetComponent<IRoleState>();
        }

        // ── ITagTarget ────────────────────────────────────────────────────

        /// <summary>NetworkObject가 스폰된 뒤에만 true. 로컬/연결 전에는 false(§NetworkActive NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        public ulong PlayerId => NetworkObject != null && OwnerId >= 0 ? (ulong)OwnerId : 0UL;

        public RoleType Role => _roleState != null ? _roleState.Role : RoleType.Runner;

        public bool IsTagged => _tagged.Value;

        public Vector3 WorldPosition => transform.position;

        public void RequestTag(ulong seekerId, RoleType seekerRole)
        {
            if (!NetworkActive)
                return;

            ServerRequestTag(seekerId, seekerRole);
        }

        // ── 서버: 재검증 ──────────────────────────────────────────────────

        /// <summary>
        /// 대상은 특정 플레이어가 소유하지 않는 관점에서 호출되므로 소유권 검사를 끈다
        /// (술래가 대상 오브젝트의 RPC를 부른다). <paramref name="caller"/>는 FishNet이 주입하는
        /// 술래의 커넥션 — 그 FirstObject에서 술래 위치를 얻어 거리를 서버가 재검증한다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestTag(ulong seekerId, RoleType seekerRole, NetworkConnection caller = null)
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

            // 술래의 서버 측 위치. 술래 플레이어 오브젝트를 못 찾으면 거리 검증 불가라 거부.
            if (caller == null || caller.FirstObject == null)
            {
                Debug.LogWarning($"[TagNet:Server] targetId={PlayerId} 태그 거부 — 술래 오브젝트를 찾을 수 없음");
                return;
            }

            Vector3 seekerPosition = caller.FirstObject.transform.position;
            RoleType targetRole = Role;

            if (!ServerTagDriver.Validate(seekerRole, targetRole, _tagged.Value, seekerPosition, transform.position))
            {
                Debug.Log($"[TagNet:Server] targetId={PlayerId} 태그 거부 — 서버 재검증 실패 " +
                          $"(seekerRole={seekerRole}, targetRole={targetRole}, 거리검증 포함 §5.3)");
                return;
            }

            _tagged.Value = true; // → OnChange가 전 피어에 전파.
            Debug.Log($"[TagNet:Server] targetId={PlayerId} 태그 확정 — 서버 재검증 통과 (seeker={seekerId})");
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
            if (_tagged.Value)
                ApplyTagEffect();
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
        /// SyncVar를 false로 되돌리면 <see cref="OnTaggedChanged"/>가 전 피어에서 불리지만
        /// <c>next == false</c>라 태그 효과를 적용하지 않는다. 효과 1회성 가드도 함께 풀어,
        /// 새 라운드에서 다시 태그되면 정상적으로 Echo 전환이 일어나게 한다.
        ///
        /// 역할 복구는 여기서 하지 않는다 — <c>RoleNetworkSync</c>가 배정을 지우고 다시 배정하며,
        /// 그 경로가 §6.2 표를 단일 소유하기 때문이다(역할 규칙을 두 곳에 두지 않는다).
        /// </summary>
        internal void ServerResetForNewRound()
        {
            _tagged.Value = false;
            _appliedTagEffect = false;
        }

        /// <summary>도메인 리로드를 끈 채 Play를 반복할 때의 static 잔여 상태 정리.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Spawned.Clear();

        private void OnTaggedChanged(bool prev, bool next, bool asServer)
        {
            if (next)
                ApplyTagEffect();
        }

        /// <summary>
        /// 태그 확정 효과를 피어마다 정확히 1회 적용한다(호스트에서 OnChange가 서버·클라
        /// 양쪽으로 불려도 중복되지 않게 가드). 대상 역할을 Echo로 바꾸고 라운드 집계에 통지한다.
        /// </summary>
        private void ApplyTagEffect()
        {
            if (_appliedTagEffect)
                return;
            _appliedTagEffect = true;

            _roleState?.ApplyRole(RoleType.Echo); // §3.1 태그 1회 → 메아리 즉시 전환
            TagTargetRegistry.NotifyTagged(this); // 각 피어의 RoundCoordinator가 서버 확정 태그를 집계

            Debug.Log($"[TagNet:Client] targetId={PlayerId} 메아리로 전환 — 서버 확정 수신, 화면 반영 (§3.1)");
        }
    }
}
