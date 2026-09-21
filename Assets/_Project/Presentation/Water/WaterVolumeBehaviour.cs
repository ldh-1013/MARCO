using Marco.Core.Water;
using UnityEngine;

namespace Marco.Presentation.Water
{
    /// <summary>
    /// §10.1 수면(물) 영역 하나를 씬에 심는 컴포넌트. 맵 생성 툴(블록 1-B)이 붙인다.
    ///
    /// <para>
    /// <b>트리거가 아니라 좌표 상자다.</b> <c>OnTriggerEnter</c>로 "들어왔다/나갔다"를 세지
    /// 않는 이유는 두 가지다 — ① 서버가 <b>임의 시점에</b> 임의 플레이어의 숨 상태를
    /// 다시 계산해야 하는데(§5.9-1 매 프레임) 트리거 이벤트는 그 질문에 답하지 못한다.
    /// ② 트리거 진입/이탈은 놓칠 수 있고(텔레포트·리스폰·§6.5 페이즈 전환), 한 번 놓치면
    /// 그 플레이어의 숨 상태가 라운드 끝까지 틀린 채 남는다. <b>매번 좌표로 묻는 쪽이
    /// 상태를 들고 있지 않아 어긋날 수 없다.</b>
    /// </para>
    ///
    /// <para>
    /// 렌더링·시각 효과는 여기 없다 — 블록 7(연출)이 담당한다. 이 컴포넌트는
    /// <b>판정용 지오메트리</b>만 들고 있다.
    /// </para>
    /// </summary>
    public sealed class WaterVolumeBehaviour : MonoBehaviour, IWaterVolume
    {
        [Header("§10.1 수면 영역 (월드 XZ, m)")]
        [Tooltip("남서 귀퉁이 (x, z)")]
        [SerializeField] private Vector2 _min = new Vector2(21f, 17f);

        [Tooltip("북동 귀퉁이 (x, z)")]
        [SerializeField] private Vector2 _max = new Vector2(35f, 25f);

        [Tooltip("수면 높이(월드 Y). 지상층 바닥이 0이면 0이다.")]
        [SerializeField] private float _surfaceY;

        [Header("수심")]
        [Tooltip("이 수면의 기본 수심(m). 메인 풀 깊은쪽 3.5 / 유아풀 얕음.")]
        [SerializeField] private float _depth = 3.5f;

        [Header("§6.5-2 국소 침강부(sump) — 없으면 반지름 0")]
        [Tooltip("침강부 중심 (x, z). 유아풀 배수구 (8, 9.5).")]
        [SerializeField] private Vector2 _sumpCenter;

        [Tooltip("침강부 반지름(m). 0이면 침강부 없음.")]
        [SerializeField] private float _sumpRadius;

        [Tooltip("침강부 수심(m). §6.5-2는 3.5m를 요구한다.")]
        [SerializeField] private float _sumpDepth = 3.5f;

        public float SurfaceY => _surfaceY;

        public bool ContainsHorizontally(Vector3 point)
        {
            return point.x >= _min.x && point.x <= _max.x
                && point.z >= _min.y && point.z <= _max.y;
        }

        public float BedYAt(Vector3 point)
        {
            return _surfaceY - DepthAt(point);
        }

        /// <summary>
        /// 이 지점의 수심. 침강부 안이면 <see cref="_sumpDepth"/>다 —
        /// §6.5-2 *"유아풀은 얕은 풀이므로 배수구 지점만 국소 침강부(sump)로 판다"*.
        /// </summary>
        public float DepthAt(Vector3 point)
        {
            if (_sumpRadius > 0f)
            {
                float dx = point.x - _sumpCenter.x;
                float dz = point.z - _sumpCenter.y;
                if (dx * dx + dz * dz <= _sumpRadius * _sumpRadius)
                    return Mathf.Max(_depth, _sumpDepth);
            }

            return _depth;
        }

        /// <summary>맵 생성 툴이 좌표를 코드로 심을 때 쓴다(에디터·런타임 공통).</summary>
        public void Configure(Vector2 min, Vector2 max, float surfaceY, float depth,
            Vector2 sumpCenter, float sumpRadius, float sumpDepth)
        {
            _min = min;
            _max = max;
            _surfaceY = surfaceY;
            _depth = depth;
            _sumpCenter = sumpCenter;
            _sumpRadius = sumpRadius;
            _sumpDepth = sumpDepth;
        }

        private void OnEnable()
        {
            WaterVolumeRegistry.Register(this);
        }

        private void OnDisable()
        {
            WaterVolumeRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        /// <summary>에디터에서 수면 범위와 침강부를 눈으로 확인한다(배치 오류를 도면 없이 잡는다).</summary>
        private void OnDrawGizmosSelected()
        {
            var center = new Vector3((_min.x + _max.x) * 0.5f, _surfaceY - _depth * 0.5f, (_min.y + _max.y) * 0.5f);
            var size = new Vector3(_max.x - _min.x, _depth, _max.y - _min.y);

            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireCube(center, size);

            if (_sumpRadius > 0f)
            {
                Gizmos.color = new Color(0f, 0.2f, 0.6f, 0.9f);
                Gizmos.DrawWireSphere(
                    new Vector3(_sumpCenter.x, _surfaceY - _sumpDepth, _sumpCenter.y), _sumpRadius);
            }
        }
#endif
    }
}
