using System.Collections.Generic;
using System.Text;
using FishNet.Managing;
using FishNet.Object;
using Marco.Net;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Objectives;
using Marco.Presentation.Sound;
using Marco.Presentation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// §15.1 씬 아키텍처(<c>Boot → MainMenu → Lobby → Game(맵별 어디티브 로드)</c>)를 실제 씬 자산에
    /// 적용하는 마이그레이션 도구(스프린트 18b).
    ///
    /// **왜 도구인가**: NetworkObject의 SceneId·ComponentIndex는 FishNet/Unity 에디터가 생성하는
    /// 값이라 씬 YAML을 손으로 편집할 수 없다(스프린트 8~14에서 확립한 원칙). 오브젝트를 다른 씬으로
    /// 옮기면 SceneId가 바뀌므로 **정상 에디터 API + 씬 저장 + Reserialize**를 거쳐야 한다.
    ///
    /// **무엇을 옮기는가**(스프린트 18b 설계):
    /// - <c>Game.unity</c>(현재 모든 것이 들어 있음) → 시스템 오브젝트를 <c>Lobby.unity</c>로 이동:
    ///   NetworkManager, PulseSystem(라운드 상태기계·UI·파문 시스템 — <b>자기완결적</b>이라 이동 안전),
    ///   Main Camera. 남는 것은 맵(Graybox·밸브 3개·EscapePoint·대역 러너)이며, 그 <c>Game.unity</c>가
    ///   InGame 진입 시 애디티브로 로드되는 "맵 씬"이 된다.
    /// - <c>Boot.unity</c>/<c>MainMenu.unity</c>: 화면 컴포넌트만 추가(네트워크 오브젝트 없음).
    /// - <c>Game.unity</c>: 스폰 지점(<see cref="SpawnAnchor"/>)을 플레이어 기존 스폰 좌표에 만든다.
    ///
    /// **안전장치**: 실행 전 <see cref="Report"/>(드라이런)로 무엇이 바뀔지 먼저 볼 수 있고,
    /// 각 단계가 멱등하다(이미 옮겨진 것은 건너뛴다).
    /// </summary>
    public static class SceneFlowSetupTool
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string BootScene = ScenesFolder + "/Boot.unity";
        private const string MainMenuScene = ScenesFolder + "/MainMenu.unity";
        private const string LobbyScene = ScenesFolder + "/Lobby.unity";
        private const string MapScene = ScenesFolder + "/Game.unity";

        /// <summary>플레이어 프리팹의 기존 스폰 좌표(§10.1 입구 로비) — 스폰 앵커를 여기에 만든다.</summary>
        private static readonly Vector3 SpawnPosition = new Vector3(12f, 0.05f, 27f);
        private static readonly Vector3 SpawnEuler = new Vector3(0f, 180f, 0f);

        // ── 드라이런 ──────────────────────────────────────────────────────

        [MenuItem("Tools/MARCO/Scene Flow — 1. 현재 상태 점검(변경 없음)", priority = 200)]
        public static void Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== §15.1 씬 아키텍처 점검(읽기 전용) ===");

            foreach (string path in new[] { BootScene, MainMenuScene, LobbyScene, MapScene })
                sb.AppendLine($"  {(SceneExists(path) ? "✔" : "✖")} {path}");

            sb.AppendLine();
            Scene active = EditorSceneManager.GetActiveScene();
            sb.AppendLine($"현재 열린 씬: {active.path}");

            var nm = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            var pulse = Object.FindAnyObjectByType<LocalPulsePipelineBehaviour>(FindObjectsInactive.Include);
            var valves = Object.FindObjectsByType<ValveBehaviour>(FindObjectsInactive.Include);
            var anchor = Object.FindAnyObjectByType<SpawnAnchor>(FindObjectsInactive.Include);
            var flow = Object.FindAnyObjectByType<SceneFlowController>(FindObjectsInactive.Include);

            sb.AppendLine($"  NetworkManager: {Where(nm)}");
            sb.AppendLine($"  PulseSystem(시스템 묶음): {Where(pulse)}");
            sb.AppendLine($"  SceneFlowController: {Where(flow)}");
            sb.AppendLine($"  밸브: {valves.Length}개");
            sb.AppendLine($"  SpawnAnchor: {Where(anchor)}");

            sb.AppendLine();
            sb.AppendLine("다음 단계: 메뉴의 '2. Lobby 씬 구성', '3. 맵 씬 정리', '4. Boot/MainMenu 구성'을 순서대로 실행하고 " +
                          "각 단계 후 씬을 저장(Ctrl+S)하세요. 마지막에 Fish-Networking → Utility → Reserialize NetworkObjects를 " +
                          "실행하고, Build Settings에 4개 씬을 등록해야 합니다(도구가 자동 등록합니다).");

            Debug.Log(sb.ToString());
        }

        private static string Where(Component c) =>
            c == null ? "없음" : $"'{c.gameObject.name}' (씬: {c.gameObject.scene.name})";

        private static bool SceneExists(string path) =>
            AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;

        // ── 2단계: Lobby 씬 = 시스템 씬 ───────────────────────────────────

        [MenuItem("Tools/MARCO/Scene Flow — 2. Lobby 씬 구성(시스템 이동)", priority = 201)]
        public static void SetupLobbyScene()
        {
            if (!ConfirmDestructive(
                "Lobby 씬 구성 (스프린트 18b)",
                "현재 Game 씬의 시스템 오브젝트를 Lobby 씬으로 옮깁니다:\n" +
                "  • NetworkManager (+Tugboat/PlayerSpawner)\n" +
                "  • PulseSystem (라운드 상태기계·HUD·결과·로비 UI·파문 시스템)\n" +
                "  • Main Camera\n" +
                "그리고 Lobby 씬에 SceneFlowController·LobbyEntry·PawnPhaseTeleporter·임시 바닥을 추가합니다.\n\n" +
                "Game 씬에는 맵(Graybox·밸브·EscapePoint)만 남습니다.\n\n" +
                "⚠ 이 작업은 두 씬 파일을 모두 수정합니다. 먼저 Assets/Scenes 백업을 권장합니다."))
                return;

            if (!EnsureSceneAssets())
                return;

            // 맵 씬(현재 Game)을 열고, Lobby 씬을 애디티브로 열어 오브젝트를 옮긴다.
            Scene map = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            Scene lobby = EditorSceneManager.OpenScene(LobbyScene, OpenSceneMode.Additive);

            int moved = 0;
            moved += MoveRootToScene<NetworkManager>(lobby);
            moved += MoveRootToScene<LocalPulsePipelineBehaviour>(lobby); // PulseSystem 묶음
            moved += MoveCameraToScene(lobby);

            // 시스템 씬 전용 컴포넌트 추가.
            GameObject flowGo = EnsureSceneObject(lobby, "SceneFlow");
            EnsureComponent<SceneFlowController>(flowGo);
            EnsureComponent<LobbyEntry>(flowGo);
            EnsureComponent<PawnPhaseTeleporter>(flowGo);

            // 로비에서 pawn이 무한 낙하하지 않도록 임시 바닥(맵 로드 전 대기용).
            EnsureLobbyFloor(lobby);

            EditorSceneManager.MarkSceneDirty(map);
            EditorSceneManager.MarkSceneDirty(lobby);

            Debug.Log($"[SceneFlow] Lobby 씬 구성 완료 — 오브젝트 {moved}개 이동. " +
                      "**두 씬을 모두 저장(Ctrl+S)**한 뒤 3단계로 진행하세요. " +
                      "저장 후 Fish-Networking → Utility → Reserialize NetworkObjects도 실행해야 " +
                      "PulseSystem의 새 SceneId가 확정됩니다.");
        }

        /// <summary>지정 타입을 가진 오브젝트의 **루트**를 대상 씬으로 옮긴다(이미 그 씬이면 건너뜀).</summary>
        private static int MoveRootToScene<T>(Scene target) where T : Component
        {
            var found = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (found == null)
            {
                Debug.LogWarning($"[SceneFlow] {typeof(T).Name}을 찾지 못해 이동을 건너뜁니다.");
                return 0;
            }

            GameObject root = found.transform.root.gameObject;
            if (root.scene == target)
                return 0;

            EditorSceneManager.MoveGameObjectToScene(root, target);
            Debug.Log($"[SceneFlow] '{root.name}' → {target.name} 씬으로 이동");
            return 1;
        }

        private static int MoveCameraToScene(Scene target)
        {
            // 대상 씬에 이미 카메라가 있으면 옮기지 않는다. 옮기면 **전체화면 카메라가 2대**가 되어
            // 같은 장면을 매 프레임 두 번 렌더링하고(프레임 저하), 같은 depth에서 서로를 덮어써
            // 화면이 깨진다(스프린트 18b 실기에서 확인된 증상).
            foreach (Camera existing in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (existing.gameObject.scene == target)
                {
                    Debug.Log($"[SceneFlow] '{target.name}' 씬에 이미 카메라('{existing.gameObject.name}')가 있어 " +
                              "이동을 건너뜁니다 — 카메라 중복을 만들지 않습니다.");
                    return 0;
                }
            }

            // 맵에 속하지 않은 폴백 카메라(플레이어 카메라는 프리팹에 있다).
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                GameObject root = cam.transform.root.gameObject;
                if (root.scene == target)
                    continue;

                // 맵의 조명·볼륨 등과 함께 있는 루트는 건드리지 않는다 — 이름으로 폴백 카메라만.
                if (!root.name.Contains("Camera"))
                    continue;

                EditorSceneManager.MoveGameObjectToScene(root, target);
                Debug.Log($"[SceneFlow] '{root.name}' → {target.name} 씬으로 이동(폴백 카메라)");
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// 로비의 임시 바닥을 준비한다. **이미 있으면 건너뛰지 않고 보강한다** — 예전 구현은
        /// 존재하면 즉시 반환해서, 나중에 추가된 머티리얼·컴포넌트 수정이 기존 씬에 영영
        /// 적용되지 않았다(실기에서 "고쳤다는데 그대로"였던 원인).
        /// </summary>
        private static void EnsureLobbyFloor(Scene lobby)
        {
            GameObject floor = null;
            foreach (GameObject root in lobby.GetRootGameObjects())
            {
                if (root.name == "Lobby Floor")
                {
                    floor = root;
                    break;
                }
            }

            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Lobby Floor";
                floor.transform.position = new Vector3(SpawnPosition.x, -0.5f, SpawnPosition.z);
                floor.transform.localScale = new Vector3(20f, 1f, 20f);
                EditorSceneManager.MoveGameObjectToScene(floor, lobby);
                Debug.Log("[SceneFlow] Lobby 씬에 임시 바닥 생성 — 맵 로드 전 pawn 낙하 방지");
            }

            // CreatePrimitive는 **빌트인 기본 머티리얼**(밝은 회백색)을 붙인다. 맵 바닥과 같은
            // 평면에서 만나면 그 밝은 색이 Z-파이팅으로 튀어 보이므로 맵과 같은 머티리얼을 쓴다.
            var grayboxFloor = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Maps/Graybox/GrayboxFloor.mat");
            var renderer = floor.GetComponent<MeshRenderer>();
            if (grayboxFloor != null && renderer != null && renderer.sharedMaterial != grayboxFloor)
            {
                Undo.RecordObject(renderer, "Set lobby floor material");
                renderer.sharedMaterial = grayboxFloor;
                Debug.Log("[SceneFlow] 임시 바닥 머티리얼 → GrayboxFloor.mat");
            }

            // 핵심: 맵이 로드되면 스스로 물러나게 한다. 이 컴포넌트가 없으면 로비에서는 낙하하거나
            // (바닥을 끈 경우) 인게임에서 맵 바닥과 Z-파이팅한다(둘 다 실기에서 확인된 증상).
            if (floor.GetComponent<LobbyPlaceholderFloor>() == null)
            {
                Undo.AddComponent<LobbyPlaceholderFloor>(floor);
                Debug.Log("[SceneFlow] 임시 바닥에 LobbyPlaceholderFloor 부착 — " +
                          "로비에서는 지면, 맵 로드 후에는 자동으로 물러납니다.");
            }
        }

        // ── 3단계: 맵 씬 정리 ────────────────────────────────────────────

        [MenuItem("Tools/MARCO/Scene Flow — 3. 맵 씬 정리(스폰 앵커)", priority = 202)]
        public static void SetupMapScene()
        {
            Scene map = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);

            var anchor = Object.FindAnyObjectByType<SpawnAnchor>(FindObjectsInactive.Include);
            if (anchor == null)
            {
                var go = new GameObject("SpawnAnchor");
                go.transform.SetPositionAndRotation(SpawnPosition, Quaternion.Euler(SpawnEuler));
                go.AddComponent<SpawnAnchor>();
                Debug.Log($"[SceneFlow] 맵 씬에 SpawnAnchor 생성 — {SpawnPosition} (§10.1 입구 로비, 기존 플레이어 스폰 좌표)");
            }
            else
            {
                Debug.Log("[SceneFlow] SpawnAnchor가 이미 있습니다 — 건너뜀");
            }

            EditorSceneManager.MarkSceneDirty(map);
            Debug.Log("[SceneFlow] 맵 씬 정리 완료 — 저장(Ctrl+S) 후 4단계로 진행하세요.");
        }

        // ── 4단계: Boot / MainMenu ───────────────────────────────────────

        [MenuItem("Tools/MARCO/Scene Flow — 4. Boot·MainMenu 구성 + 빌드 설정", priority = 203)]
        public static void SetupEntryScenes()
        {
            if (!EnsureSceneAssets())
                return;

            // Boot
            Scene boot = EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
            EnsureComponent<BootFlow>(EnsureSceneObject(boot, "BootFlow"));
            EnsureCamera(boot);
            EditorSceneManager.MarkSceneDirty(boot);
            EditorSceneManager.SaveScene(boot);

            // MainMenu
            Scene menu = EditorSceneManager.OpenScene(MainMenuScene, OpenSceneMode.Single);
            EnsureComponent<MainMenuScreen>(EnsureSceneObject(menu, "MainMenuScreen"));
            EnsureCamera(menu);
            EditorSceneManager.MarkSceneDirty(menu);
            EditorSceneManager.SaveScene(menu);

            RegisterBuildScenes();

            Debug.Log("[SceneFlow] Boot·MainMenu 구성 + 빌드 설정 완료.\n" +
                      "  마지막 확인: ① 4개 씬이 Build Settings에 Boot→MainMenu→Lobby→Game 순서로 있는지 " +
                      "② Fish-Networking → Utility → Reserialize NetworkObjects 실행 " +
                      "③ docs/수동검증_절차.md §20 절차로 실기 검증");
        }

        private static void EnsureCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<Camera>(true) != null)
                    return;
            }

            var go = new GameObject("Main Camera");
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black; // §16.1 흑 배경
            go.AddComponent<AudioListener>();
            Debug.Log($"[SceneFlow] {scene.name} 씬에 카메라 생성");
        }

        private static void RegisterBuildScenes()
        {
            string[] order = { BootScene, MainMenuScene, LobbyScene, MapScene };
            var list = new List<EditorBuildSettingsScene>();

            foreach (string path in order)
            {
                if (SceneExists(path))
                    list.Add(new EditorBuildSettingsScene(path, true));
            }

            // 위 4개 외에 기존 등록 씬은 뒤에 유지한다.
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                bool already = false;
                foreach (string path in order)
                {
                    if (existing.path == path)
                    {
                        already = true;
                        break;
                    }
                }

                if (!already)
                    list.Add(existing);
            }

            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[SceneFlow] Build Settings 갱신 — {list.Count}개 씬(Boot→MainMenu→Lobby→Game 우선 순서)");
        }

        // ── 5단계: 로비 배선 정리(접속 전 살아 있어야 하는 컴포넌트 분리) ──

        [MenuItem("Tools/MARCO/Scene Flow — 5. 로비 배선 정리(접속 컴포넌트 분리)", priority = 204)]
        public static void FixLobbyWiring()
        {
            // **왜 필요한가**(실기에서 확정): FishNet은 접속이 시작되기 전까지 씬 NetworkObject를
            // 비활성화한다(벤더 소스: NetworkObject.Start → TryStartDeactivation → SetActive(false)).
            // ConnectionService가 NetworkObject를 가진 PulseSystem에 붙어 있으면 그 순간 OnDisable로
            // 레지스트리 등록이 풀려, 접속을 시작할 수단 자체가 사라진다(접속해야 켜지는데 켜져야
            // 접속하는 순환). 그래서 접속 컴포넌트는 NetworkObject가 없는 오브젝트에 있어야 한다.
            // ① 중복 카메라 정리(스프린트 18b 실기: 바닥이 검게 깨지고 프레임이 떨어지는 원인).
            DeduplicateCameras();

            // ② 임시 바닥 보강 — 이미 만들어진 씬에도 머티리얼·LobbyPlaceholderFloor가 적용되도록.
            //    이 수리 경로에서도 처리해야 "2단계를 다시 돌리지 않으면 안 고쳐지는" 상황을 피한다.
            Scene lobbyScene = SceneManager.GetSceneByName("Lobby");
            if (lobbyScene.IsValid() && lobbyScene.isLoaded)
            {
                EnsureLobbyFloor(lobbyScene);
                EditorSceneManager.MarkSceneDirty(lobbyScene);
            }

            var connection = Object.FindAnyObjectByType<ConnectionService>(FindObjectsInactive.Include);
            if (connection == null)
            {
                if (!MarcoSetupPipeline.Automated)
                    EditorUtility.DisplayDialog("로비 배선 정리",
                        "씬에서 ConnectionService를 찾지 못했습니다. Lobby 씬을 열고 다시 실행하세요.", "확인");
                else
                    Debug.LogWarning("[SceneFlow] ConnectionService를 찾지 못해 접속 컴포넌트 분리를 건너뜁니다.");
                return;
            }

            GameObject source = connection.gameObject;
            if (source.GetComponent<NetworkObject>() == null)
            {
                Debug.Log($"[SceneFlow] ConnectionService가 이미 NetworkObject 없는 오브젝트('{source.name}')에 " +
                          "있습니다 — 정리할 것이 없습니다(멱등).");
                return;
            }

            Scene lobby = source.scene;
            GameObject target = EnsureSceneObject(lobby, "SceneFlow");
            if (target.GetComponent<SceneFlowController>() == null)
            {
                EditorUtility.DisplayDialog("로비 배선 정리",
                    "SceneFlow 오브젝트를 찾지 못했습니다 — 먼저 '2. Lobby 씬 구성'을 실행하세요.", "확인");
                return;
            }

            if (!ConfirmDestructive(
                "로비 배선 정리 (스프린트 18b 후속)",
                $"ConnectionService를 '{source.name}'(NetworkObject 있음) → '{target.name}'(NetworkObject 없음)으로 옮깁니다.\n\n" +
                "이유: FishNet이 접속 전 NetworkObject를 비활성화하므로, 지금 위치에서는 접속을 시작할 수 없습니다.\n" +
                "설정값(기본 주소)은 그대로 복사됩니다."))
                return;

            // 컴포넌트는 오브젝트 간 '이동'이 불가능하므로 값을 보존한 채 복사 후 원본 제거한다.
            UnityEditorInternal.ComponentUtility.CopyComponent(connection);
            if (target.GetComponent<ConnectionService>() == null)
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(target);
            else
                UnityEditorInternal.ComponentUtility.PasteComponentValues(target.GetComponent<ConnectionService>());

            Undo.DestroyObjectImmediate(connection);

            EditorSceneManager.MarkSceneDirty(lobby);
            Debug.Log($"[SceneFlow] ConnectionService를 '{target.name}'으로 옮겼습니다(설정값 보존). " +
                      "**Lobby 씬을 저장(Ctrl+S)**한 뒤 Play로 재확인하세요 — " +
                      "[LobbyEntry] ConnectionService 미발견 오류가 사라져야 합니다.");
        }

        /// <summary>
        /// 시스템 씬에 전체화면 카메라가 2대 이상이면 하나만 남기고 나머지를 비활성화한다.
        ///
        /// **왜**: 스프린트 18b 마이그레이션이 Game 씬의 'Main Camera'를 Lobby로 옮겼는데, Lobby에는
        /// Phase 2 스켈레톤이 만든 'Main Camera'가 이미 있었다. 그 결과 같은 depth(-1)·같은 Clear Flags
        /// (Skybox)를 가진 카메라 2대가 남아 **같은 장면을 매 프레임 두 번 렌더링**했고(프레임 저하),
        /// 그리기 순서가 정해지지 않아 서로를 덮어써 바닥이 검게 깨져 보였다. AudioListener 중복 경고도
        /// 같은 원인이다.
        ///
        /// 삭제가 아니라 **비활성화**한다 — 사용자 씬 내용을 지우지 않고 되돌릴 수 있어야 한다.
        /// </summary>
        private static void DeduplicateCameras()
        {
            var cameras = new List<Camera>();
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                // 활성 상태인 씬 카메라만 대상(플레이어 프리팹 카메라는 런타임 스폰이라 여기 없다).
                if (cam.gameObject.activeInHierarchy)
                    cameras.Add(cam);
            }

            if (cameras.Count <= 1)
                return;

            Debug.LogWarning($"[SceneFlow] 활성 카메라가 {cameras.Count}대입니다 — 1대만 남기고 비활성화합니다 " +
                             "(중복 렌더링으로 프레임이 떨어지고 화면이 깨집니다).");

            for (int i = 1; i < cameras.Count; i++)
            {
                GameObject go = cameras[i].gameObject;
                Undo.RecordObject(go, "Disable duplicate camera");
                go.SetActive(false);
                Debug.Log($"[SceneFlow] 중복 카메라 '{go.name}'({go.scene.name} 씬) 비활성화 — " +
                          $"'{cameras[0].gameObject.name}'만 남깁니다.");
            }
        }

        // ── 공통 헬퍼 ────────────────────────────────────────────────────

        private static bool EnsureSceneAssets()
        {
            var missing = new List<string>();
            foreach (string path in new[] { BootScene, MainMenuScene, LobbyScene, MapScene })
            {
                if (!SceneExists(path))
                    missing.Add(path);
            }

            if (missing.Count == 0)
                return true;

            EditorUtility.DisplayDialog("씬 자산 없음",
                "다음 씬 파일을 찾지 못했습니다:\n  " + string.Join("\n  ", missing) +
                "\n\nPhase 2에서 만든 스켈레톤 씬이 있어야 합니다.", "확인");
            return false;
        }

        private static GameObject EnsureSceneObject(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            var go = new GameObject(name);
            EditorSceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null)
                return existing;

            T added = go.AddComponent<T>();
            Debug.Log($"[SceneFlow] '{go.name}'에 {typeof(T).Name} 부착");
            return added;
        }

        private static bool ConfirmDestructive(string title, string body)
        {
            // 전체 셋업 파이프라인은 사람 개입 없이 도는 것이 목적이라 확인을 건너뛴다.
            // 그 진입점에서 이미 한 번 포괄 확인을 받았다(개별 메뉴 실행 시에는 기존 동작 그대로).
            if (MarcoSetupPipeline.Automated)
                return true;

            bool ok = EditorUtility.DisplayDialog(title, body + "\n\n계속하시겠습니까?", "계속", "취소");
            if (!ok)
                Debug.Log("[SceneFlow] 사용자가 취소했습니다. 변경 없음.");
            return ok;
        }
    }
}
