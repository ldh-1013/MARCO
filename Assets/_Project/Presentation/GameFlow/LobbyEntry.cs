using UnityEngine;
using Marco.Core.Net;
using Marco.Presentation.UI;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 로비(시스템) 씬 진입 시 §12.2에서 고른 접속 의도를 실행한다(스프린트 18b).
    ///
    /// **순서가 중요하다**: 라운드 상태기계(<c>RoundNetworkSync</c>)는 이 씬의 씬 NetworkObject라
    /// 서버 시작 시점에 이미 로드돼 있어야 스폰된다. 그래서 접속은 메인 메뉴가 아니라
    /// **로비 씬에 도착한 뒤** 시작한다 — 이 컴포넌트가 그 한 프레임을 담당한다.
    ///
    /// 접속 자체는 <see cref="IConnectionService"/>(Core 계약, Net 구현)에 위임한다 —
    /// Presentation이 FishNet을 직접 만지지 않는다(§15.2).
    ///
    /// 메인 메뉴를 거치지 않고 로비 씬에서 바로 Play한 경우(개발 편의)에는 의도가 없으므로
    /// 아무것도 하지 않고, <c>LobbyScreen</c>의 접속 전 패널(H/J)이 그대로 쓰인다.
    /// </summary>
    public sealed class LobbyEntry : MonoBehaviour
    {
        private void Start()
        {
            MainMenuScreen.Intent intent = MainMenuScreen.Consume();
            if (intent == MainMenuScreen.Intent.None)
                return; // 메인 메뉴를 거치지 않음 — LobbyScreen의 H/J 패널로 접속한다.

            IConnectionService connection = ConnectionServiceRegistry.Current;
            if (connection == null)
            {
                // 실기에서 확인된 원인(스프린트 18b 후속): ConnectionService가 **NetworkObject가 붙은
                // 오브젝트**(PulseSystem)에 있으면, FishNet이 접속 전 그 오브젝트를 비활성화하므로
                // (NetworkObject.Start → TryStartDeactivation → SetActive(false)) OnDisable에서
                // 등록이 해제돼 여기서 null이 된다. 접속을 시작해야 활성화되는데 그 접속을 시작할
                // 컴포넌트가 꺼지는 순환이라, 다음 프레임 재시도로는 절대 풀리지 않는다.
                // → ConnectionService는 NetworkObject가 없는 오브젝트(SceneFlow)에 있어야 한다.
                Debug.LogError("[LobbyEntry] ConnectionService를 찾지 못했습니다 — 접속할 수 없습니다. " +
                               "ConnectionService가 NetworkObject가 붙은 오브젝트(PulseSystem)에 있으면 " +
                               "FishNet이 접속 전 비활성화하여 등록이 해제됩니다. " +
                               "Tools/MARCO/Scene Flow — 5. 로비 배선 정리를 실행해 SceneFlow 오브젝트로 옮기세요.");
                return;
            }

            if (intent == MainMenuScreen.Intent.Host)
                connection.StartHost();
            else
                connection.StartClient(MainMenuScreen.PendingAddress);
        }
    }
}
