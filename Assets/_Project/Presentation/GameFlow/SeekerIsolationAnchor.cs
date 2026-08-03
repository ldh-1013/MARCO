using Marco.Core.GameFlow;
using UnityEngine;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// §10.1 술래 격리 공간의 위치를 알리는 맵 쪽 표식(스프린트 24).
    /// <see cref="SpawnAnchor"/>와 같은 패턴 — 맵이 애디티브로 로드/언로드되므로
    /// 좌표 스냅샷만 <see cref="IsolationAnchorRegistry"/>에 넘긴다.
    ///
    /// **위치는 기획서에 없다**(§10.1 구역 구성표에 "격리 공간" 행이 자체가 없다 — GAP-45).
    /// 그래서 이 오브젝트를 옮기는 것만으로 위치를 조정할 수 있게 했다.
    /// </summary>
    public sealed class SeekerIsolationAnchor : MonoBehaviour
    {
        private void OnEnable() =>
            IsolationAnchorRegistry.Register(this, new SpawnPose(transform.position, transform.rotation));

        private void OnDisable() => IsolationAnchorRegistry.Unregister(this);
    }
}
