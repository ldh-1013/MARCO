using UnityEngine;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// §12.4 로비 브리핑 평면도의 원천 데이터. 맵 v2 생성기(에디터)가 루트에 붙이면서 §10.1 구역
    /// 좌표를 그대로 복사해 둔다 — 레이아웃 정본(<c>MapV2Layout</c>)은 에디터 어셈블리라 런타임에서
    /// 읽을 수 없기 때문이다. <b>좌표를 새로 만들지 않는다</b>(생성기가 같은 표에서 채운다).
    ///
    /// <para>
    /// 밸브 위치는 여기 두지 않는다 — 씬의 <c>ValveBehaviour</c> 위치와 서버가 정한 활성 여부를
    /// HUD가 직접 읽는다(두 곳에 적으면 어긋난다).
    /// </para>
    /// </summary>
    public sealed class MapPlanData : MonoBehaviour
    {
        [System.Serializable]
        public struct Area
        {
            public string Name;
            public Rect Rect;       // 월드 (x, z) — 도면 좌표
            public bool UpperFloor;
            public bool Water;
        }

        [SerializeField] private Rect _bounds;
        [SerializeField] private Area[] _areas = System.Array.Empty<Area>();

        /// <summary>평면도 전체 범위(월드 x, z).</summary>
        public Rect Bounds => _bounds;

        public Area[] Areas => _areas;

        /// <summary>생성기가 채운다.</summary>
        public void Configure(Rect bounds, Area[] areas)
        {
            _bounds = bounds;
            _areas = areas ?? System.Array.Empty<Area>();
        }

        /// <summary>월드 (x, z)를 평면도 정규 좌표(0~1)로 옮긴다. 순수 계산 — 테스트 대상.</summary>
        public static Vector2 Normalize(Rect bounds, Vector2 worldXZ)
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
                return Vector2.zero;

            return new Vector2((worldXZ.x - bounds.xMin) / bounds.width, (worldXZ.y - bounds.yMin) / bounds.height);
        }
    }
}
