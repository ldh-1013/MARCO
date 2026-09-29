using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.GameFlow;
using Marco.Core.Items;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// §7 찰칵이 — 서버 권위 줍기·사용·섬광 전달(블록 6).
    ///
    /// <para>
    /// <b>파문 경로와 완전히 분리돼 있다.</b> <c>PulseNetworkSync</c>·<c>ServerPulseDriver</c>를 거치지 않고,
    /// <c>SoundType</c>을 만들지 않으며, §8 어워드에도 기록하지 않는다(§7 "파문 이벤트 없음 — 사운드
    /// 시스템과 별개 처리"). 술래의 방향 게이지·메아리 소나에도 나타나지 않는다 — 소리가 아니기 때문이다.
    /// </para>
    ///
    /// <para>
    /// 클라이언트가 보내는 것은 "이 지점에서 줍는다"(지점 번호)와 "쓴다" 둘뿐이다. 역할·거리·가용·소지·
    /// 섬광 위치·볼 수 있는 사람은 전부 서버가 정한다(GAP-24).
    /// </para>
    ///
    /// <para>
    /// 씬 배치: <c>RoundCoordinator</c> 오브젝트(이미 NetworkObject)에 <c>Tools/MARCO/Setup Network Round</c>가
    /// 함께 부착한다.
    /// </para>
    /// </summary>
    public sealed class ClickerNetworkSync : NetworkBehaviour, IClickerNetworkBridge
    {
        /// <summary>리스폰 지점 가용 비트마스크 — 전원 공개(마커 표시용).</summary>
        private readonly SyncVar<int> _availability = new SyncVar<int>((1 << ClickerConfig.SpawnCount) - 1);

        private ServerClickerDriver _driver;
        private GameFlowState _lastPhase = GameFlowState.Boot;
        private bool _subscribedToTags;

        public bool NetworkActive => NetworkObject != null && IsSpawned;

        // ── 클라이언트 → 서버 ────────────────────────────────────────────

        public void RequestPickUp(int spawnIndex)
        {
            if (NetworkActive)
                ServerRequestPickUp(spawnIndex);
        }

        public void RequestUse()
        {
            if (NetworkActive)
                ServerRequestUse();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _availability.OnChange += OnAvailabilityChanged;
            ClickerClientEvents.SetAvailability(_availability.Value);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _availability.OnChange -= OnAvailabilityChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ClickerClientEvents.Bridge = this;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (ReferenceEquals(ClickerClientEvents.Bridge, this))
                ClickerClientEvents.Bridge = null;
            ClickerClientEvents.SetLocalHolding(false);
        }

        private void OnAvailabilityChanged(int prev, int next, bool asServer) =>
            ClickerClientEvents.SetAvailability(next);

        // ── 서버 ─────────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            _driver = new ServerClickerDriver();
            _availability.Value = _driver.AvailabilityMask;

            if (!_subscribedToTags)
            {
                TagTargetRegistry.TargetTagged += OnServerTargetTagged;
                _subscribedToTags = true;
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (_subscribedToTags)
            {
                TagTargetRegistry.TargetTagged -= OnServerTargetTagged;
                _subscribedToTags = false;
            }
        }

        private void Update()
        {
            if (_driver == null || !NetworkActive || !IsServerStarted)
                return;

            GameFlowState phase = RoundNetworkSync.ServerPhase;
            if (phase != _lastPhase)
            {
                // 새 라운드 — 4곳 전부 가용, 소지 전원 해제(더블체크 2).
                if (phase == GameFlowState.InGame)
                {
                    _driver.Reset();
                    ObserversResetHolding();
                    Debug.Log($"[Clicker:Server] 라운드 시작 — 리스폰 {ClickerConfig.SpawnCount}곳 전부 가용, 소지 초기화(§7)");
                }

                _lastPhase = phase;
            }

            if (phase != GameFlowState.InGame)
                return;

            if (_driver.Tick(Time.deltaTime))
                Debug.Log("[Clicker:Server] 리스폰 — §7 120초 경과");

            int mask = _driver.AvailabilityMask;
            if (_availability.Value != mask)
                _availability.Value = mask;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestPickUp(int spawnIndex, NetworkConnection caller = null)
        {
            if (_driver == null || RoundNetworkSync.ServerPhase != GameFlowState.InGame)
                return;

            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
                return;

            // 탈출자는 월드에서 빠졌다(09-29).
            if (!RoleNetworkSync.CallerInWorld(caller))
                return;

            // 거리 재검증 — 줍기도 E 상호작용이다(§4.3 "상호작용(밸브·아이템 줍기)"). 같은 GAP-10/73 범위.
            if (!ClickerSpawnRegistry.TryGetPosition(spawnIndex, out Vector3 spawnPos))
                return;

            Vector3 playerPos = caller.FirstObject.transform.position;
            if (!InteractionRules.InRange(playerPos, spawnPos, underwaterTarget: false))
            {
                Debug.Log($"[Clicker:Server] 줍기 거부 — playerId={playerId} 거리 " +
                          $"{InteractionRules.DistanceTo(playerPos, spawnPos, false):0.0}m > {InteractionRules.RangeMeters:0.0}m");
                return;
            }

            ClickerPickupResult result = _driver.TryPickUp(playerId, role, spawnIndex);
            if (result != ClickerPickupResult.Accepted)
            {
                Debug.Log($"[Clicker:Server] 줍기 거부 — playerId={playerId} 지점={spawnIndex} 사유={result}");
                return;
            }

            _availability.Value = _driver.AvailabilityMask;
            TargetSetHolding(caller, true);
            Debug.Log($"[Clicker:Server] 줍기 — playerId={playerId} 지점={spawnIndex} → {ClickerConfig.RespawnSeconds:0}초 후 리스폰(§7)");
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestUse(NetworkConnection caller = null)
        {
            if (_driver == null || RoundNetworkSync.ServerPhase != GameFlowState.InGame)
                return;

            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
                return;

            if (!RoleNetworkSync.CallerInWorld(caller))
                return;

            if (!_driver.TryUse(playerId, role))
            {
                TargetSetHolding(caller, _driver.IsHolding(playerId));
                return;
            }

            TargetSetHolding(caller, false);

            // 섬광 위치는 서버가 아는 사용자 위치(GAP-24). 눈높이 기준으로 가림을 본다.
            Vector3 flash = caller.FirstObject.transform.position;
            int seen = DeliverFlash(flash, playerId);

            Debug.Log($"[Clicker:Server] 사용 — playerId={playerId} 섬광 {ClickerConfig.RadiusMeters:0}m / " +
                      $"{ClickerConfig.FlashSeconds:0.0}초, 파문 없음(§7). 섬광을 본 다른 플레이어 {seen}명");
        }

        /// <summary>
        /// §7 "시야가 닿는 6m 안의 플레이어에게는 빛이 보인다". 사용자 본인은 항상 본다(잔상 포함).
        ///
        /// <list type="bullet">
        /// <item><b>거리</b> — 3D 6m 이내(경계 포함).</item>
        /// <item><b>시야</b> — 서버 차폐 프로브(§5.6 레이)로 <b>벽 0장</b>일 때만. GAP-2가 "좌표 공개"를 벽 0장으로
        ///   정한 것과 같은 기준이다 — 빛이 보인다는 것은 그 자리가 보인다는 뜻이기 때문이다.</item>
        /// <item><b>메아리 제외</b> — 메아리 시야는 소나(파문)뿐이고 "지형은 끝까지 보이지 않는다"(§3.2-1).
        ///   섬광은 파문이 아니다.</item>
        /// </list>
        /// </summary>
        private int DeliverFlash(Vector3 flash, ulong userId)
        {
            IOcclusionProbe probe = PulseNetworkRegistry.OcclusionProbe;
            Vector3 eye = Vector3.up * Core.Locomotion.DiveRules.StandingHeadHeight;
            int seen = 0;

            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0 || p.Owner == null)
                    continue;

                ulong id = (ulong)p.OrderKey;
                if (id == userId)
                {
                    TargetFlash(p.Owner, flash, true);
                    continue;
                }

                if (p.EffectiveRole == RoleType.Echo)
                    continue;

                Vector3 observer = p.transform.position;
                if (!ServerClickerDriver.WithinFlashRange(flash, observer))
                    continue;

                if (probe != null)
                {
                    OcclusionResult los = probe.Probe(flash + eye, observer + eye);
                    if (los.HasHardBlocker || los.WallCount > 0)
                        continue;
                }

                TargetFlash(p.Owner, flash, false);
                seen++;
            }

            return seen;
        }

        /// <summary>소지 중 태그 — 메아리는 아이템을 쓸 수 없다(GAP-5). 소지를 잃는다.</summary>
        private void OnServerTargetTagged(ITagTarget target)
        {
            if (_driver == null || target == null || !_driver.IsHolding(target.PlayerId))
                return;

            _driver.Drop(target.PlayerId);
            if (ServerManager != null && ServerManager.Clients.TryGetValue((int)target.PlayerId, out NetworkConnection conn))
                TargetSetHolding(conn, false);
        }

        // ── 서버 → 클라이언트 ────────────────────────────────────────────

        [TargetRpc]
        private void TargetSetHolding(NetworkConnection conn, bool holding) =>
            ClickerClientEvents.SetLocalHolding(holding);

        [TargetRpc]
        private void TargetFlash(NetworkConnection conn, Vector3 position, bool self) =>
            ClickerClientEvents.RaiseFlash(position, self);

        [ObserversRpc]
        private void ObserversResetHolding() => ClickerClientEvents.SetLocalHolding(false);
    }
}
