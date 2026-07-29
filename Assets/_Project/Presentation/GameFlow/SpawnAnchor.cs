using UnityEngine;
using Marco.Core.GameFlow;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 맵(Game 씬)의 도망자 시작 지점 표시자(스프린트 18b, §10.1 입구 로비).
    ///
    /// 맵이 애디티브로 로드될 때 자신의 Transform을 <see cref="SpawnAnchorRegistry"/>에 등록해,
    /// 시스템 씬의 <see cref="PawnPhaseTeleporter"/>가 씬 간 참조 없이 스폰 좌표를 읽게 한다.
    /// 언로드 시 등록이 해제되므로 "맵이 로드됐는가"의 신호로도 쓰인다.
    ///
    /// 순수 표시자다 — 게임플레이 로직이 없다.
    /// </summary>
    public sealed class SpawnAnchor : MonoBehaviour
    {
        private void OnEnable() =>
            SpawnAnchorRegistry.Register(this, new SpawnPose(transform.position, transform.rotation));

        private void OnDisable() => SpawnAnchorRegistry.Unregister(this);
    }
}
