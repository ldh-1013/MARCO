using System;
using System.Collections.Generic;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Core.Items
{
    /// <summary>
    /// §7 찰칵이 수치. 전부 §7 표에서 옮겼다.
    ///
    /// <para>
    /// <b>파문 이벤트가 없다</b>(§7 "사운드 시스템과 별개 처리"). 그래서 이 파일은 <c>SoundType</c>도
    /// <c>ServerPulseDriver</c>도 참조하지 않는다 — 참조가 생기는 순간 이 아이템의 존재 이유가 사라진다
    /// (연출.md §9.1 "소리가 없다는 것이 이 아이템의 전부다").
    /// </para>
    /// </summary>
    public static class ClickerConfig
    {
        /// <summary>§7 "반경 6m".</summary>
        public const float RadiusMeters = 6f;

        /// <summary>§7 "0.3초 순간 섬광".</summary>
        public const float FlashSeconds = 0.3f;

        /// <summary>§7 리스폰 "120초".</summary>
        public const float RespawnSeconds = 120f;

        /// <summary>§7 "맵당 4곳".</summary>
        public const int SpawnCount = 4;

        /// <summary>
        /// 찰칵이를 주울 수 있는 역할. <b>§7에 명시가 없다 — GAP-98, 도망자 전용 잠정.</b>
        /// 메아리는 GAP-5(물리 상호작용 불가)로 제외가 확정이고, 술래는 §3.1 표 "특수 능력"에
        /// 아이템이 없으며 §19가 찰칵이를 "무마이크 <b>도망자</b>의 최소 참여 경로"로 둔다.
        /// </summary>
        public static bool CanPickUp(RoleType role) => role == RoleType.Runner;
    }

    public enum ClickerPickupResult
    {
        Accepted,
        NotAllowedRole,
        AlreadyHolding, // §7 "동시 소지 1개(쓰기 전에는 새로 주울 수 없음)"
        NotAvailable,   // 리스폰 대기 중
        InvalidSpawn,
    }

    /// <summary>
    /// §7 찰칵이 서버 권위 상태 — 리스폰 지점 4곳의 가용 여부·리스폰 타이머, 플레이어별 소지.
    /// UnityEngine 수명주기를 모른다(dt 주입) — EditMode 테스트 가능.
    /// </summary>
    public sealed class ServerClickerDriver
    {
        private readonly float[] _respawnRemaining;
        private readonly HashSet<ulong> _holders = new HashSet<ulong>();

        public ServerClickerDriver(int spawnCount = ClickerConfig.SpawnCount)
        {
            _respawnRemaining = new float[Math.Max(0, spawnCount)];
        }

        public int SpawnCount => _respawnRemaining.Length;

        public bool IsAvailable(int spawnIndex) =>
            spawnIndex >= 0 && spawnIndex < _respawnRemaining.Length && _respawnRemaining[spawnIndex] <= 0f;

        public float RespawnRemaining(int spawnIndex) =>
            spawnIndex >= 0 && spawnIndex < _respawnRemaining.Length ? Math.Max(0f, _respawnRemaining[spawnIndex]) : 0f;

        public bool IsHolding(ulong playerId) => _holders.Contains(playerId);

        /// <summary>가용 지점 비트마스크(동기화용). i번 비트 = i번 지점 가용.</summary>
        public int AvailabilityMask
        {
            get
            {
                int mask = 0;
                for (int i = 0; i < _respawnRemaining.Length && i < 31; i++)
                {
                    if (IsAvailable(i))
                        mask |= 1 << i;
                }

                return mask;
            }
        }

        public ClickerPickupResult TryPickUp(ulong playerId, RoleType role, int spawnIndex)
        {
            if (spawnIndex < 0 || spawnIndex >= _respawnRemaining.Length)
                return ClickerPickupResult.InvalidSpawn;

            if (!ClickerConfig.CanPickUp(role))
                return ClickerPickupResult.NotAllowedRole;

            if (_holders.Contains(playerId))
                return ClickerPickupResult.AlreadyHolding;

            if (!IsAvailable(spawnIndex))
                return ClickerPickupResult.NotAvailable;

            _holders.Add(playerId);

            // §7 "리스폰 120초" — 주운 순간부터 센다(사용 시점이 아니다: 사용 여부와 무관하게 지점이 비었다).
            _respawnRemaining[spawnIndex] = ClickerConfig.RespawnSeconds;
            return ClickerPickupResult.Accepted;
        }

        /// <summary>
        /// 사용한다. 소지 중이어야 하며 <b>1회용</b>이라 즉시 소진된다(§7).
        /// 사용자가 이미 태그됐으면(메아리) 쓸 수 없다 — 호출부가 현재 역할을 넘긴다.
        /// </summary>
        public bool TryUse(ulong playerId, RoleType currentRole)
        {
            if (!_holders.Contains(playerId))
                return false;

            if (!ClickerConfig.CanPickUp(currentRole))
            {
                // 소지 중 태그 — 아이템은 사라진다(메아리는 물리 상호작용 불가, GAP-5).
                _holders.Remove(playerId);
                return false;
            }

            _holders.Remove(playerId);
            return true;
        }

        /// <summary>태그 등으로 소지를 잃는다.</summary>
        public void Drop(ulong playerId) => _holders.Remove(playerId);

        /// <summary>리스폰 타이머를 진전시킨다. 새로 가용해진 지점이 있으면 true.</summary>
        public bool Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return false;

            bool changed = false;
            for (int i = 0; i < _respawnRemaining.Length; i++)
            {
                if (_respawnRemaining[i] <= 0f)
                    continue;

                _respawnRemaining[i] -= deltaSeconds;
                if (_respawnRemaining[i] <= 1e-4f)
                {
                    _respawnRemaining[i] = 0f;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>새 라운드 — 4곳 전부 가용, 소지 전원 해제.</summary>
        public void Reset()
        {
            for (int i = 0; i < _respawnRemaining.Length; i++)
                _respawnRemaining[i] = 0f;
            _holders.Clear();
        }

        /// <summary>
        /// §7 "시야가 닿는 6m 안의 플레이어에게는 빛이 보인다". 거리 판정만 여기서 한다(3D, 경계 포함) —
        /// 시야(가림)는 서버의 차폐 프로브가 따로 본다.
        /// </summary>
        public static bool WithinFlashRange(Vector3 flash, Vector3 observer) =>
            Vector3.Distance(flash, observer) <= ClickerConfig.RadiusMeters;
    }

    /// <summary>
    /// §7 리스폰 지점 위치 — 맵 v2 <c>ClickerSpawns</c> 마커가 스스로 등록한다
    /// (<c>DrainRegistry</c>·<c>EscapePointRegistry</c>와 같은 지연 바인딩 패턴).
    /// </summary>
    public static class ClickerSpawnRegistry
    {
        private static readonly Dictionary<int, Transform> Points = new Dictionary<int, Transform>();

        public static int Count => Points.Count;

        public static void Register(int index, Transform point)
        {
            if (point != null)
                Points[index] = point;
        }

        public static void Unregister(int index, Transform point)
        {
            if (Points.TryGetValue(index, out Transform current) && current == point)
                Points.Remove(index);
        }

        public static bool TryGetPosition(int index, out Vector3 position)
        {
            if (Points.TryGetValue(index, out Transform t) && t != null)
            {
                position = t.position;
                return true;
            }

            position = default;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => Points.Clear();
    }

    /// <summary>
    /// §7 찰칵이 Presentation → Net 요청 계약. 구현체는 <c>ClickerNetworkSync</c>(Net)이고 소비자는
    /// Presentation(마커·플레이어 입력)이라 Core에 계약만 둔다(<c>IRoundNetworkBridge</c>와 같은 패턴).
    /// 요청은 "이 지점에서 줍는다" · "쓴다" 둘뿐이다 — 나머지는 서버가 정한다(GAP-24).
    /// </summary>
    public interface IClickerNetworkBridge
    {
        bool NetworkActive { get; }
        void RequestPickUp(int spawnIndex);
        void RequestUse();
    }

    /// <summary>
    /// 서버 결론을 Presentation에 알리는 클라이언트 측 신호. Net(<c>ClickerNetworkSync</c>)이 올리고
    /// Presentation(렌더러·HUD)이 구독한다 — §15.2상 둘이 서로를 참조하지 않으므로 Core에 둔다.
    /// </summary>
    public static class ClickerClientEvents
    {
        /// <summary>현재 클라이언트의 요청 경로(스폰된 <c>ClickerNetworkSync</c>). 없으면 null.</summary>
        public static IClickerNetworkBridge Bridge { get; set; }

        /// <summary>섬광이 보였다. <c>self</c>면 자기 섬광 — 잔상(§16.4)은 자기 것에만 남는다.</summary>
        public static event Action<Vector3, bool> FlashObserved;

        /// <summary>로컬 플레이어의 소지 상태(§12.4 아이템 슬롯).</summary>
        public static bool LocalHolding { get; private set; }

        /// <summary>리스폰 지점 가용 비트마스크(마커 표시용).</summary>
        public static int AvailabilityMask { get; private set; } = (1 << ClickerConfig.SpawnCount) - 1;

        public static void RaiseFlash(Vector3 position, bool self) => FlashObserved?.Invoke(position, self);

        public static void SetLocalHolding(bool holding) => LocalHolding = holding;

        public static void SetAvailability(int mask) => AvailabilityMask = mask;

        public static bool IsSpawnAvailable(int index) => (AvailabilityMask & (1 << index)) != 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            FlashObserved = null;
            Bridge = null;
            LocalHolding = false;
            AvailabilityMask = (1 << ClickerConfig.SpawnCount) - 1;
        }
    }
}
