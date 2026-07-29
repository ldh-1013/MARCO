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

        private Renderer[] _renderers;
        private Collider[] _colliders;

        /// <summary>현재 지면으로 작동 중인가(로비 페이즈면 true).</summary>
        private bool _active = true;
        private bool _initialized;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            _colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        }

        private void Update()
        {
            // 맵이 있으면 맵 바닥이 지면 역할을 하므로 임시 바닥은 물러난다.
            bool shouldBeActive = !SpawnAnchorRegistry.HasAnchor;

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
        }

        private void Apply(bool value)
        {
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
