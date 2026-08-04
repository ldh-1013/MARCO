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
            WarnIfVoiceMissing();

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

        /// <summary>
        /// §5.2 음성 파이프라인이 씬에 없으면 **Play 시작 시점에** 알린다(스프린트 26b 후속).
        ///
        /// **왜 런타임 경고인가**: 26a에서 컴포넌트를 <c>SceneFlowSetupTool</c>에 추가했지만,
        /// 도구를 실행하지 않으면 씬에는 반영되지 않는다. 그 상태에서는 <c>[Voice]</c> 로그도
        /// HUD 음성 표시도 **아무 흔적 없이 조용히 없어서**, "왜 안 되지"를 씬 파일을 열어
        /// 확인해야만 알 수 있다. 이 프로젝트에서 같은 유형의 사고가 반복됐으므로
        /// (스프린트 14 씬 저장 누락, 18b 머티리얼 미적용) Play만 해도 보이게 한다.
        ///
        /// 게임 진행은 막지 않는다 — 밸브·태그·라운드는 음성과 무관하다.
        /// </summary>
        private static void WarnIfVoiceMissing()
        {
            if (FindAnyObjectByType<Voice.LocalVoicePipeline>() != null)
                return;

            Debug.LogWarning("[Voice] 씬에 LocalVoicePipeline이 없습니다 — 발화 분류·전송이 동작하지 않습니다" +
                             "([Voice] 로그도 HUD 음성 표시도 나오지 않습니다). " +
                             "Tools/MARCO/Scene Flow — 5. 로비 배선 정리를 실행하고 Lobby 씬을 저장하세요. " +
                             "밸브·태그·라운드는 음성과 무관하므로 게임 진행에는 지장이 없습니다.");
        }
    }
}
