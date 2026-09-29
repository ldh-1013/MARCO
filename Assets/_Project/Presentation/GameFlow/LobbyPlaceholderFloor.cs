using Marco.Core.GameFlow;
using UnityEngine;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 로비(시스템) 씬의 임시 바닥을 **맵 로드 여부에 따라 켜고 끈다**(스프린트 18b 후속 실기 버그).
    ///
    /// **왜 필요한가**: 로비 페이즈에는 맵이 아직 로드되지 않아(§15.4 지연 로드) 월드에 지면이
    /// 이 임시 바닥 하나뿐이다. 그래서 이것을 꺼 두면 pawn이 스폰 즉시 <b>무한 낙하</b>해
    /// "상대가 접속되지 않은 것처럼" 보인다(낙하 한계·리스폰 로직이 없다). 반대로 켜 두면
    /// 맵이 로드된 뒤 맵 바닥과 <b>정확히 같은 평면(y=0)</b>에서 만나 Z-파이팅으로 바닥이 깨져
    /// 보인다. 두 증상은 같은 원인의 양면이며, 해법은 페이즈에 따른 전환이다.
    ///
    /// **맵 로드 판정은 새로 만들지 않는다** — <see cref="SpawnAnchorRegistry.HasAnchor"/>가 이미
    /// 그 신호이며(<c>PawnPhaseTeleporter</c>가 쓰는 것과 동일), 맵이 언로드되면 false로 돌아와
    /// 리매치 부결 후 로비 복귀에서도 바닥이 자동으로 되살아난다.
    ///
    /// 오브젝트 자체는 켜 둔 채 <see cref="MeshRenderer"/>·<see cref="Collider"/>만 토글한다 —
    /// 오브젝트를 끄면 이 컴포넌트의 <c>Update</c>도 멈춰 다시 켤 방법이 사라지기 때문이다.
    /// </summary>
    public sealed class LobbyPlaceholderFloor : MonoBehaviour
    {
        [Tooltip("전환 시점을 Console에 남긴다(실기에서 눈으로 확인하기 위한 진단).")]
        [SerializeField] private bool _logTransitions = true;

        [Tooltip("로비 복귀 배치에 쓸 텔레포터. 비우면 씬에서 찾는다.")]
        [SerializeField] private PawnPhaseTeleporter _teleporter;

        private Renderer[] _renderers;
        private Collider[] _colliders;

        /// <summary>가장자리 벽(09-29) — 맵 외곽벽과 같은 두께 · 높이.</summary>
        private const float EdgeWallThickness = 0.2f;
        private const float EdgeWallHeight = 3.5f;
        private const string EdgeWallPrefix = "EdgeWall_";

        /// <summary>현재 지면으로 작동 중인가(로비 페이즈면 true).</summary>
        private bool _active = true;
        private bool _initialized;

        private void Awake()
        {
            EnsureParts();
        }

        private void Update()
        {
            // 맵이 있으면 맵 바닥이 지면 역할을 하므로 임시 바닥은 물러난다.
            ApplyMapPresence(SpawnAnchorRegistry.HasAnchor);
        }

        /// <summary>
        /// 맵 유무에 맞춰 임시 바닥을 켜고 끈다. <b>켜지는 호출(= 맵이 내려감) 안에서</b> 로컬 pawn을 로비 배정 슬롯에 놓는다
        /// (09-29 — 로비로 돌아온 플레이어는 처음 로비에 들어온 플레이어와 같아야 한다). 메아리 포함. 상태가 같으면 아무것도 하지 않는다.
        /// </summary>
        public void ApplyMapPresence(bool mapPresent)
        {
            bool shouldBeActive = !mapPresent;

            if (_initialized && shouldBeActive == _active)
                return;

            _initialized = true;
            _active = shouldBeActive;
            Apply(shouldBeActive);

            if (_logTransitions)
            {
                Debug.Log(shouldBeActive
                    ? "[SceneFlow] 로비 임시 바닥 ON — 맵이 없어 이 바닥이 지면 역할을 한다(무한 낙하 방지)."
                    : "[SceneFlow] 로비 임시 바닥 OFF — 맵 바닥이 지면을 대신한다(같은 평면 Z-파이팅 방지).");
            }

            if (shouldBeActive)
                PlaceLocalPawnInLobby();
        }

        private void PlaceLocalPawnInLobby()
        {
            Player.FirstPersonController player = Player.LocalPlayerRegistry.Current;
            if (player == null)
                return; // 부팅 · 접속 전 — 옮길 pawn이 없다(접속하면 프리팹 위치 = 이 바닥 윗면 중심에 선다).

            if (_teleporter == null)
                _teleporter = FindAnyObjectByType<PawnPhaseTeleporter>();
            if (_teleporter == null || !_teleporter.PlaceInLobby(player, this))
                Debug.LogWarning("[SceneFlow] 맵 언로드 — 로컬 pawn을 로비 슬롯에 놓지 못했습니다(텔레포터 없음). 낙하 복구가 받아낸다.");
        }

        private void EnsureParts()
        {
            // 벽을 먼저 세워야 아래 캐시에 들어가 바닥과 함께 켜지고 꺼진다.
            if (_colliders == null)
                EnsureEdgeWalls();

            _renderers ??= GetComponentsInChildren<Renderer>(includeInactive: true);
            _colliders ??= GetComponentsInChildren<Collider>(includeInactive: true);
        }

        /// <summary>
        /// 임시 바닥 네 변 바깥에 벽을 세운다(09-29) — 가장자리에서 걸어 나가면 무한 낙하했다(FloorEdgeSceneTests). 맵 외곽벽과 같은 규칙:
        /// 바닥 <b>바깥</b>에 서서(안쪽 면 = 가장자리) 스폰 · 로비 슬롯 자리를 바꾸지 않는다. 바닥 BoxCollider의 월드 경계로 계산해 자식으로
        /// 두므로 바닥과 함께 켜지고 꺼진다. 씬은 바꾸지 않는다(런타임 부품). 이미 있으면 다시 만들지 않는다.
        /// </summary>
        private void EnsureEdgeWalls()
        {
            if (transform.Find(EdgeWallPrefix + "0") != null)
                return;

            var box = GetComponent<BoxCollider>();
            if (box == null)
                return;

            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            Matrix4x4 m = box.transform.localToWorldMatrix;
            Vector3 h = box.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(box.center + new Vector3(
                    (i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            float t = EdgeWallThickness;
            float y = max.y + EdgeWallHeight * 0.5f;
            float cx = (min.x + max.x) * 0.5f, cz = (min.z + max.z) * 0.5f;
            float sx = max.x - min.x + t * 2f, sz = max.z - min.z;
            var renderer = GetComponent<Renderer>();
            Material material = renderer != null ? renderer.sharedMaterial : null;

            MakeEdgeWall(0, new Vector3(cx, y, min.z - t * 0.5f), new Vector3(sx, EdgeWallHeight, t), material); // 남
            MakeEdgeWall(1, new Vector3(cx, y, max.z + t * 0.5f), new Vector3(sx, EdgeWallHeight, t), material); // 북
            MakeEdgeWall(2, new Vector3(min.x - t * 0.5f, y, cz), new Vector3(t, EdgeWallHeight, sz), material); // 서
            MakeEdgeWall(3, new Vector3(max.x + t * 0.5f, y, cz), new Vector3(t, EdgeWallHeight, sz), material); // 동
        }

        private void MakeEdgeWall(int index, Vector3 worldCenter, Vector3 worldSize, Material material)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = EdgeWallPrefix + index;
            wall.transform.SetParent(transform, worldPositionStays: false);
            wall.transform.SetPositionAndRotation(worldCenter, Quaternion.identity);

            // 바닥이 늘려진 큐브라 부모 배율을 나눠 월드 크기를 맞춘다(회전 없음 — 임시 바닥은 축 정렬).
            Vector3 parentScale = transform.lossyScale;
            wall.transform.localScale = new Vector3(
                worldSize.x / parentScale.x, worldSize.y / parentScale.y, worldSize.z / parentScale.z);

            if (material != null)
                wall.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>
        /// 임시 바닥 윗면의 중심(월드). 맵이 없을 때(로비) 배정 스폰의 기준점이다 — <c>PawnPhaseTeleporter.TryGetAssignedSpawn</c>.
        /// 콜라이더가 꺼져 있어도(맵이 있을 때) 계산되도록 <c>bounds</c>가 아니라 BoxCollider 크기 · 트랜스폼으로 구한다.
        /// </summary>
        public bool TryGetTopCenter(out Vector3 topCenter)
        {
            topCenter = default;
            BoxCollider box = GetComponentInChildren<BoxCollider>(includeInactive: true);
            if (box == null)
                return false;

            Matrix4x4 m = box.transform.localToWorldMatrix;
            Vector3 h = box.size * 0.5f;
            float top = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 local = box.center + new Vector3(
                    (i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                top = Mathf.Max(top, m.MultiplyPoint3x4(local).y);
            }

            Vector3 center = m.MultiplyPoint3x4(box.center);
            topCenter = new Vector3(center.x, top, center.z);
            return true;
        }

        private void Apply(bool value)
        {
            EnsureParts();

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                        _renderers[i].enabled = value;
                }
            }

            if (_colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] != null)
                        _colliders[i].enabled = value;
                }
            }
        }
    }
}
