using UnityEngine;

namespace Marco.Core.Net
{
    /// <summary>
    /// 네트워크 접속 시작(호스트/참가)의 Presentation ↔ Net 계약(스프린트 18, §12.2 방 만들기/코드 입장).
    ///
    /// **왜 필요한가**: 접속 시작은 FishNet(`InstanceFinder.ServerManager` 등)을 호출해야 하는데,
    /// 접속을 시작 · 표시하는 화면(<c>LobbyEntry</c> · <c>JoinProgressOverlay</c> · <c>LobbyScreen</c>)은 Presentation이라
    /// §15.2상 FishNet을 참조할 수 없다.
    /// 스프린트 18b까지는 제3 어셈블리(<c>DebugTools</c>)의 H/J 키가 이 틈을 임시로 메웠다 —
    /// 이 계약이 그 역할을 정식 UI 경로로 대체했고, 그 도구는 스프린트 19에서 제거됐다.
    /// </summary>
    public interface IConnectionService
    {
        /// <summary>접속 시작을 이미 요청했는가(중복 시작 방지·UI 상태 전환용).</summary>
        bool HasStarted { get; }

        /// <summary>호스트로 시작(서버+클라이언트 동시 — §12.2 "방 만들기").</summary>
        void StartHost();

        /// <summary>
        /// 클라이언트로 참가(§12.2 "코드 입장" — 주소 직결, GAP-28). 09-30부터 호스트 이름(DNS)과 임의 포트를 받는다 —
        /// 주소 파싱은 <see cref="JoinAddressParser"/>가 먼저 끝낸다(형식 오류는 접속 시도 없이 화면에서 걸러진다).
        /// </summary>
        void StartClient(JoinAddress address);

        /// <summary>
        /// 진행 중인 접속을 중단하고 <see cref="HasStarted"/>를 되돌린다(§12.2 재시도 경로).
        ///
        /// 접속이 성립하지 않은 채 대기하고 있을 때 사용자가 빠져나올 유일한 수단이다 —
        /// 이것이 없으면 주소를 한 번만 틀려도 "접속 중…"에서 벗어나지 못한다.
        /// 이미 접속돼 플레이 중일 때 호출하면 접속 종료로 동작한다.
        /// </summary>
        void Cancel();

        /// <summary>로비 화면의 주소 줄(09-30) — 호스트는 내 LAN IP:포트, 참가자는 접속한 주소.</summary>
        string AddressLine { get; }

        /// <summary>이번 접속 시도의 상태(09-30) — 접속 중 화면이 읽는다(시도 중 · 연결됨 · 실패 · 취소).</summary>
        JoinAttempt Attempt { get; }

        /// <summary>
        /// 클라이언트 · 서버를 모두 내린다(09-30) — 실패 · 취소 뒤 메인 메뉴로 돌아가기 전에 부른다. 다시 H/J로 시작할 수 있는 상태로 정리한다.
        /// </summary>
        void StopAll();
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
