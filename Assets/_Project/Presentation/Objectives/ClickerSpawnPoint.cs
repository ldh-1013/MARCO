using Marco.Core.Items;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// §7 찰칵이 리스폰 지점(맵 v2 <c>ClickerSpawns</c> 마커에 붙는다).
    ///
    /// <list type="number">
    /// <item><see cref="ClickerSpawnRegistry"/>에 스스로 등록한다 — 서버가 줍기 거리를 이 좌표로 재검증한다.</item>
    /// <item>가용할 때만 작은 표지를 켠다(서버가 동기화한 비트마스크).</item>
    /// <item>로컬 도망자가 근처에서 E를 누르면 줍기를 요청한다(§4.3 "상호작용(밸브·아이템 줍기) — E").</item>
    /// </list>
    ///
    /// 역할·거리·가용·소지 검사는 사전 필터일 뿐이다 — 서버가 전부 다시 판정한다(GAP-24).
    /// </summary>
    public sealed class ClickerSpawnPoint : MonoBehaviour
    {
        [SerializeField] private int _index;
        [SerializeField] private Key _pickUpKey = Key.E;

        [Tooltip("가용 표지 크기(m). 규칙 수치가 아니라 표시용이다.")]
        [SerializeField] private float _markerSize = 0.25f;

        private GameObject _marker;

        public int Index => _index;

        /// <summary>맵 생성기(에디터)가 마커에 붙이면서 번호를 지정한다(0~3).</summary>
        public void Configure(int index) => _index = index;

        private void OnEnable() => ClickerSpawnRegistry.Register(_index, transform);
        private void OnDisable() => ClickerSpawnRegistry.Unregister(_index, transform);

        private void Awake()
        {
            _marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _marker.name = "ClickerMarker";
            _marker.transform.SetParent(transform, worldPositionStays: false);
            _marker.transform.localScale = Vector3.one * _markerSize;

            // 표지는 충돌·소리 차폐에 끼면 안 된다.
            Collider c = _marker.GetComponent<Collider>();
            if (c != null)
                Destroy(c);
        }

        private void Update()
        {
            bool available = ClickerClientEvents.IsSpawnAvailable(_index);
            if (_marker != null && _marker.activeSelf != available)
                _marker.SetActive(available);

            if (!available || ClickerClientEvents.LocalHolding)
                return;

            IClickerNetworkBridge bridge = ClickerClientEvents.Bridge;
            if (bridge == null || !bridge.NetworkActive)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[_pickUpKey].wasPressedThisFrame)
                return;

            // GAP-61: 로컬 플레이어는 매 프레임 재조회한다.
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null || !player.IsLocallyControlled || !ClickerConfig.CanPickUp(player.Role))
                return;

            if (!InteractionRules.InRange(player.transform.position, transform.position, underwaterTarget: false))
                return;

            bridge.RequestPickUp(_index);
        }
    }
}
