using FishNet;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Net
{
    /// <summary>
    /// §15.1 씬 구성(<c>Boot → MainMenu → Lobby → Game(맵별 어디티브 로드)</c>)의 런타임 구동자
    /// (스프린트 18b). 시스템 씬(Lobby)에 상주하며 두 종류의 씬 전환을 담당한다:
    ///
    /// 1. **오프라인 전환**(접속 전): Boot → MainMenu → Lobby. 평범한 Unity
    ///    <see cref="UnityEngine.SceneManagement.SceneManager"/>로 로드한다 — 네트워크가 없는
    ///    구간이므로 FishNet을 거칠 이유가 없다.
    /// 2. **네트워크 맵 로드**(§15.4 "InGame: 맵 로드"): 서버가 FishNet
    ///    <see cref="FishNet.Managing.Scened.SceneManager.LoadGlobalScenes"/>로 맵을 **애디티브**
    ///    (<see cref="ReplaceOption.None"/>)로 올린다. 전역 씬이므로 접속 중인 클라이언트와
    ///    **이후 접속자 모두** 자동으로 같은 맵을 로드하며, 맵 안의 씬 NetworkObject(밸브 3개 등)는
    ///    FishNet이 로드 시점에 스스로 스폰한다(벤더 확인: <c>ServerObjects.SceneManager_sceneLoaded</c>가
    ///    <c>GetSceneNetworkObjects</c> → <c>InitializeRootNetworkObjects</c>).
    ///
    /// **왜 애디티브인가**: §15.1이 "Game(맵별 어디티브 로드)"라고 명시한다. 시스템 씬(NetworkManager·
    /// 라운드 상태기계·UI)이 살아 있어야 로비/결과 화면이 맵과 무관하게 유지되고, 맵만 갈아끼울 수 있다.
    ///
    /// **로드 완료 판정은 서버가 소유**: <see cref="MapLoaded"/>는 서버에서 실제 씬 로드 여부로 갱신되며,
    /// <c>RoundNetworkSync</c>가 라운드 시작 전에 이 값을 확인한다(맵 없이 라운드가 시작되면 밸브가
    /// 0개라 §6.1 게이트가 영구히 닫힌다).
    /// </summary>
    public sealed class SceneFlowController : MonoBehaviour
    {
        /// <summary>시스템 씬에 하나만 존재한다(같은 어셈블리의 <c>RoundNetworkSync</c>가 참조).</summary>
        internal static SceneFlowController Instance { get; private set; }

        [Header("씬 이름 (§15.1)")]
        [SerializeField] private string _mainMenuSceneName = "MainMenu";
        [SerializeField] private string _lobbySceneName = "Lobby";

        [Tooltip("애디티브로 로드되는 맵 씬(§15.1 \"Game(맵별 어디티브 로드)\"). 맵을 추가하면 이 값만 바꾼다.")]
        [SerializeField] private string _mapSceneName = "Game";

        [Header("진단")]
        [SerializeField] private bool _logSceneFlow = true;

        private bool _mapLoadRequested;

        /// <summary>맵(Game 씬)이 실제로 로드돼 있는가. 서버·클라이언트 모두 로컬 씬 상태로 판정한다.</summary>
        internal bool MapLoaded => IsSceneLoaded(_mapSceneName);

        /// <summary>맵 로드를 이미 요청했는가(서버 전용 — 중복 요청 방지).</summary>
        internal bool MapLoadRequested => _mapLoadRequested;

        /// <summary>애디티브로 로드되는 맵 씬 이름. 에디터 진단 도구가 읽으므로 어셈블리 밖에 공개한다.</summary>
        public string MapSceneName => _mapSceneName;

        private void Awake()
        {
            // 진단(스프린트 18b 후속): "NetworkManager ... DestroyNewest" 경고는 **씬이 두 번 로드**됐다는
            // 뜻이다(NetworkManager는 _dontDestroyOnLoad=1이라 씬 전환에도 살아남는다). 4개 씬 자산 어디에도
            // NetworkManager는 Lobby 1개뿐임을 확인했으므로, 중복은 런타임 재로드 외엔 생길 수 없다.
            // 이 로그가 한 세션에 두 번 찍히면 그 재로드가 실제로 일어난 것이다.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[SceneFlow:Diag] SceneFlowController가 이미 있습니다 — " +
                                 $"기존='{Instance.gameObject.scene.name}' 씬, 새것='{gameObject.scene.name}' 씬. " +
                                 "시스템 씬이 두 번 로드됐다는 뜻이며, NetworkManager 중복(DestroyNewest) 경고의 원인입니다.");
            }
            else
            {
                Debug.Log($"[SceneFlow:Diag] 시스템 씬 진입 — '{gameObject.scene.name}' 씬에서 초기화(§15.1).");
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // 맵 의존 시스템(밸브 집계 등)은 **각자** 씬 이벤트를 구독해 스스로 갱신한다 —
        // Net이 Presentation 타입을 호출하면 §15.2 경계가 깨지기 때문이다
        // (`ValveObjectiveTracker.OnEnable`의 sceneLoaded 구독 참고).

        // ── 오프라인 전환 (Boot → MainMenu → Lobby) ───────────────────────

        /// <summary>메인 메뉴로 이동(§12.2). 네트워크 시작 전이라 일반 씬 로드다.</summary>
        public void GoToMainMenu()
        {
            LoadSingleOffline(_mainMenuSceneName);
        }

        /// <summary>
        /// 로비(시스템) 씬으로 이동(§12.3). 접속은 이 씬에 도착한 뒤 시작한다 —
        /// 라운드 상태기계(<c>RoundNetworkSync</c>)가 이 씬의 씬 NetworkObject라서,
        /// 서버가 시작될 때 이미 존재해야 스폰된다.
        /// </summary>
        public void GoToLobby()
        {
            LoadSingleOffline(_lobbySceneName);
        }

        private void LoadSingleOffline(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            if (_logSceneFlow)
                Debug.Log($"[SceneFlow] 오프라인 씬 로드 → {sceneName} (§15.1)");

            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        // ── 네트워크 맵 로드/언로드 (§15.4 InGame) ────────────────────────

        /// <summary>
        /// 서버가 맵을 애디티브로 올린다(§15.4 "맵 로드"). 이미 요청·로드됐으면 아무것도 하지 않는다.
        /// 전역 씬이라 현재 접속자 전원 + 이후 접속자에게도 동일하게 적용된다.
        /// </summary>
        internal void ServerLoadMap()
        {
            if (_mapLoadRequested || MapLoaded)
                return;

            if (InstanceFinder.SceneManager == null)
            {
                Debug.LogError("[SceneFlow] FishNet SceneManager를 찾지 못했습니다 — 맵을 로드할 수 없습니다.");
                return;
            }

            _mapLoadRequested = true;

            var data = new SceneLoadData(_mapSceneName)
            {
                // 애디티브: 시스템 씬(Lobby)을 유지한 채 맵만 얹는다(§15.1).
                ReplaceScenes = ReplaceOption.None
            };

            InstanceFinder.SceneManager.LoadGlobalScenes(data);

            if (_logSceneFlow)
                Debug.Log($"[SceneFlow:Server] 맵 로드 요청 → {_mapSceneName} (전역 애디티브, §15.4)");
        }

        /// <summary>
        /// 서버가 맵을 내린다(라운드 종료 후 로비 복귀 — §15.4 RoundEnd → Lobby).
        /// 맵 안의 밸브·탈출 지점 NetworkObject도 함께 사라지며, 다음 라운드에서 새로 로드된다.
        /// </summary>
        internal void ServerUnloadMap()
        {
            if (!_mapLoadRequested && !MapLoaded)
                return;

            if (InstanceFinder.SceneManager == null)
                return;

            _mapLoadRequested = false;
            InstanceFinder.SceneManager.UnloadGlobalScenes(new SceneUnloadData(_mapSceneName));

            if (_logSceneFlow)
                Debug.Log($"[SceneFlow:Server] 맵 언로드 → {_mapSceneName} (로비 복귀, §15.4)");
        }

        // ── 맵 의존 시스템 갱신 ───────────────────────────────────────────

        private static bool IsSceneLoaded(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }
    }
}
