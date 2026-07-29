using UnityEngine;

namespace Marco.Core.GameFlow
{
    /// <summary>맵이 알려주는 플레이어 시작 지점(좌표 스냅샷).</summary>
    public readonly struct SpawnPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public SpawnPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// 맵(Game 씬)의 플레이어 시작 지점을 다른 씬의 소비자에게 알리는 지연 바인딩 지점
    /// (스프린트 18b, §15.4 "InGame: 맵 로드").
    ///
    /// **왜 필요한가**: 맵이 InGame 진입 시점에 애디티브로 로드되므로(§15.1 "Game(맵별 어디티브 로드)"),
    /// 로비 단계에는 맵의 스폰 지점이 **존재하지 않는다**. 플레이어 pawn은 그보다 먼저 스폰돼
    /// 시스템 씬에 있으므로, 맵이 도착한 뒤 그 위치로 옮겨야 한다. 맵 쪽 컴포넌트가 스스로
    /// 여기 등록하면, 시스템 씬의 소비자는 씬 간 참조 없이 좌표만 읽는다
    /// (<c>EscapeGateRegistry</c>·<c>ReadyStateRegistry</c>와 같은 패턴).
    ///
    /// **Transform이 아니라 좌표 스냅샷을 보관하는 이유**: 맵이 언로드되면 그 씬의 Transform은
    /// 파괴된다. 참조를 들고 있으면 파괴된 Unity 객체를 만지는 위험이 생기므로, 등록 시점의
    /// 좌표·회전만 값으로 복사해 둔다. 해제 판정은 등록자를 식별하는 <see cref="object"/> 토큰으로 한다
    /// (파괴된 객체라도 참조 비교는 안전하다).
    ///
    /// 맵이 언로드되면 <see cref="HasAnchor"/>가 false가 되어 "맵이 로드된 상태인가"의 신호로도 쓰인다.
    /// </summary>
    public static class SpawnAnchorRegistry
    {
        private static object _token;

        /// <summary>맵의 도망자 스폰 지점(§10.1 입구 로비). 맵 미로드 시 의미 없음.</summary>
        public static SpawnPose Pose { get; private set; }

        /// <summary>맵의 스폰 지점이 등록돼 있는가(= 맵이 로드된 상태인가).</summary>
        public static bool HasAnchor => _token != null;

        /// <summary>
        /// 스폰 지점을 등록한다. <paramref name="token"/>은 등록자 식별용이며
        /// (보통 호출하는 컴포넌트 자신), 같은 토큰으로만 해제할 수 있다.
        /// </summary>
        public static void Register(object token, SpawnPose pose)
        {
            if (token == null)
                return;

            _token = token;
            Pose = pose;
        }

        /// <summary>
        /// 등록을 해제한다. 현재 등록자가 아니면 무시한다 — 맵 교체 중 옛 앵커의 해제가
        /// 늦게 도착해도 새 앵커를 지우지 않게 하려는 것이다.
        /// </summary>
        public static void Unregister(object token)
        {
            if (token != null && ReferenceEquals(_token, token))
                _token = null;
        }

        /// <summary>도메인 리로드 꺼짐 대비 세션 진입 초기화(다른 레지스트리와 동일한 안전장치).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            _token = null;
            Pose = default;
        }
    }
}
