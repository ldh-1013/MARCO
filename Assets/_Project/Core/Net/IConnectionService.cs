using UnityEngine;

namespace Marco.Core.Net
{
    /// <summary>
    /// 네트워크 접속 시작(호스트/참가)의 Presentation ↔ Net 계약(스프린트 18, §12.2 방 만들기/코드 입장).
    ///
    /// **왜 필요한가**: 접속 시작은 FishNet(`InstanceFinder.ServerManager` 등)을 호출해야 하는데,
    /// 로비 화면(<c>LobbyScreen</c>)은 Presentation이라 §15.2상 FishNet을 참조할 수 없다.
    /// 지금까지는 제3 어셈블리(<c>DebugTools</c>)의 H/J 키가 이 틈을 임시로 메웠다 —
    /// 이 계약이 그 역할을 정식 UI 경로로 대체하며, 실기 검증 후 DebugTools를 제거할 수 있게 한다.
    /// </summary>
    public interface IConnectionService
    {
        /// <summary>접속 시작을 이미 요청했는가(중복 시작 방지·UI 상태 전환용).</summary>
        bool HasStarted { get; }

        /// <summary>호스트로 시작(서버+클라이언트 동시 — §12.2 "방 만들기").</summary>
        void StartHost();

        /// <summary>클라이언트로 참가(§12.2 "코드 입장" — MVP는 주소 직결, GAP-28).</summary>
        void StartClient(string address);

        /// <summary>참가 기본 주소(방코드 대용 표시에도 쓴다 — GAP-28).</summary>
        string DefaultAddress { get; }
    }

    /// <summary>
    /// <see cref="IConnectionService"/> 단일 슬롯 등록소(<c>EscapeGateRegistry</c>와 같은 패턴 —
    /// 접속 서비스는 세션당 하나다).
    /// </summary>
    public static class ConnectionServiceRegistry
    {
        public static IConnectionService Current { get; private set; }

        public static void Register(IConnectionService service)
        {
            if (service != null)
                Current = service;
        }

        public static void Unregister(IConnectionService service)
        {
            if (ReferenceEquals(Current, service))
                Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => Current = null;
    }
}
