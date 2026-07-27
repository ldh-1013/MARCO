using System.IO;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using Marco.Net;
using Marco.Presentation.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// docs/수동검증_절차.md §10-1~10-5(에디터 GUI 절차)를 코드로 재현하는 원클릭 자동화 도구.
    ///
    /// **원칙(사용자 요구사항)**: <see cref="NetworkObject"/>의 PrefabId·AssetPathHash처럼
    /// FishNet/Unity 에디터가 자동 생성하는 값은 이 스크립트가 직접 채우지 않는다.
    /// 전부 <see cref="PrefabUtility"/>·<see cref="AddComponent"/> 등 정상 API 호출 경로를
    /// 통해서만 만들어지며, 그 값들은 Unity가 컴포넌트 부착/프리팹 저장 시점에
    /// <c>NetworkObject.OnValidate</c>/<c>Reset</c>을 통해 스스로 채운다(코드 직접 대입 없음).
    ///
    /// 대상 범위는 §10 순서 그대로다: NetworkManager+Tugboat+PlayerSpawner 배치(10-1) →
    /// 씬 Player 프리팹화(10-2) → NetworkObject/NetworkTransform/PlayerOwnershipGate 부착(10-3) →
    /// PlayerSpawner 배선(10-4) → 씬 Player 인스턴스 제거(10-5). 10-6(빌드/접속)·10-7(검증)은
    /// 사람이 직접 확인해야 하는 런타임 절차라 자동화 대상이 아니며, 실행 후 Console 안내로 대신한다.
    /// </summary>
    public static class NetworkPlayerSetupTool
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        private const string PrefabPath = PrefabFolder + "/Player.prefab";
        private const string DefaultPrefabObjectsPath = "Assets/DefaultPrefabObjects.asset";

        [MenuItem("Tools/MARCO/Setup Network Player")]
        public static void Run()
        {
            bool proceed = EditorUtility.DisplayDialog(
                "네트워크 플레이어 구성 (§10 자동화)",
                "이 작업은 현재 씬과 프로젝트 자산을 수정합니다:\n" +
                "  • NetworkManager(+Tugboat+PlayerSpawner) 오브젝트 배치\n" +
                "  • 씬의 Player를 Assets/_Project/Prefabs/Player.prefab으로 프리팹화\n" +
                "  • 프리팹에 NetworkObject/NetworkTransform/PlayerOwnershipGate/TagNetworkSync/RoleNetworkSync 부착\n" +
                "  • 씬의 기존 Player 인스턴스 삭제\n\n" +
                "씬 변경은 Ctrl+Z로 되돌릴 수 있지만, 프리팹·에셋 변경(DefaultPrefabObjects.asset 포함)은 " +
                "에디터 Undo로 되돌아가지 않습니다. 계속하기 전에 Assets/Scenes/Game.unity 백업을 권장합니다 " +
                "(docs/수동검증_절차.md §10 참고).\n\n계속하시겠습니까?",
                "계속", "취소");

            if (!proceed)
            {
                Debug.Log("[NetworkPlayerSetupTool] 사용자가 취소했습니다. 아무것도 변경되지 않았습니다.");
                return;
            }

            Undo.SetCurrentGroupName("Setup Network Player (§10)");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                GameObject networkManagerGo = SetupNetworkManagerObject();
                PlayerSpawner spawner = networkManagerGo.GetComponent<PlayerSpawner>();
                NetworkManager networkManager = networkManagerGo.GetComponent<NetworkManager>();

                GameObject scenePlayer = FindScenePlayerInstance();
                NetworkObject playerNob = CreateOrUpdatePlayerPrefab(scenePlayer);

                if (playerNob == null)
                {
                    Debug.LogError("[NetworkPlayerSetupTool] Player 프리팹을 만들거나 찾지 못해 중단합니다. " +
                        "씬에 FirstPersonController를 가진 'Player' 오브젝트가 있는지, 또는 " +
                        $"{PrefabPath}가 이미 올바르게 구성돼 있는지 확인하세요.");
                    return;
                }

                RegisterSpawnablePrefab(networkManager, playerNob);
                WirePlayerSpawner(spawner, playerNob);
                RemoveScenePlayerInstance(scenePlayer);

                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                LogVerificationGuidance();
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        // ── 10-1: NetworkManager 오브젝트 배치 ──────────────────────────────

        private static GameObject SetupNetworkManagerObject()
        {
            var existing = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                Debug.Log($"[NetworkPlayerSetupTool] 기존 NetworkManager 오브젝트 재사용: {go.name}");
            }
            else
            {
                go = new GameObject("NetworkManager");
                Undo.RegisterCreatedObjectUndo(go, "Create NetworkManager");
                Debug.Log("[NetworkPlayerSetupTool] NetworkManager 오브젝트 생성");
            }

            GetOrAddComponent<NetworkManager>(go);
            // 하위 매니저(ServerManager·ClientManager·TransportManager 등)는 NetworkManager.Awake()가
            // GetOrCreateComponent로 런타임에 직접 만든다(§10-1 안내 그대로) — 여기서 부착하지 않는다.
            GetOrAddComponent<Tugboat>(go); // 기본값(Port 7770, localhost) 그대로 둔다.
            GetOrAddComponent<PlayerSpawner>(go);

            return go;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp != null)
                return comp;

            comp = Undo.AddComponent<T>(go);
            Debug.Log($"[NetworkPlayerSetupTool] {go.name}에 {typeof(T).Name} 부착");
            return comp;
        }

        // ── 10-2/10-3: 씬 Player 탐색 및 프리팹화 + FishNet 컴포넌트 부착 ──────

        private static GameObject FindScenePlayerInstance()
        {
            var fpc = Object.FindAnyObjectByType<FirstPersonController>(FindObjectsInactive.Include);
            return fpc != null ? fpc.gameObject : null;
        }

        private static NetworkObject CreateOrUpdatePlayerPrefab(GameObject scenePlayer)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            if (prefabAsset == null)
            {
                if (scenePlayer == null)
                    return null; // 만들 소스도 없고 기존 프리팹도 없다 — 호출부에서 에러 처리.

                EnsureFolderExists(PrefabFolder);

                // GUI에서 Hierarchy → Project로 드래그하는 것과 동일한 결과(원본 프리팹 생성 +
                // 씬 인스턴스를 그 프리팹에 연결)를 내는 공식 API.
                prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    scenePlayer, PrefabPath, InteractionMode.AutomatedAction);
                Debug.Log($"[NetworkPlayerSetupTool] {PrefabPath} 생성 (씬 Player 기반)");
            }
            else
            {
                Debug.Log($"[NetworkPlayerSetupTool] 기존 {PrefabPath} 재사용 — 컴포넌트 구성만 확인합니다.");
            }

            // 프리팹 콘텐츠를 열어 컴포넌트를 추가한다. 이 스코프를 벗어나면 Unity가 자동으로
            // 변경 사항을 프리팹 에셋에 저장한다(PrefabUtility.SaveAsPrefabAsset과 동등).
            using (var editScope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = editScope.prefabContentsRoot;

                // ① NetworkObject — 값은 전부 기본값. PrefabId/AssetPathHash는 Unity가
                //    컴포넌트 부착·프리팹 저장 시 OnValidate/Reset을 통해 스스로 채운다.
                if (root.GetComponent<NetworkObject>() == null)
                {
                    root.AddComponent<NetworkObject>();
                    Debug.Log("[NetworkPlayerSetupTool] Player 프리팹에 NetworkObject 부착");
                }

                // ② NetworkTransform — Position/Rotation 동기화는 기본값(on)을 그대로 쓰고,
                //    Scale만 §10-3 표대로 끈다(스케일은 변하지 않으므로 대역폭 절약).
                var nt = root.GetComponent<NetworkTransform>();
                if (nt == null)
                {
                    nt = root.AddComponent<NetworkTransform>();
                    Debug.Log("[NetworkPlayerSetupTool] Player 프리팹에 NetworkTransform 부착");
                }
                var ntSerialized = new SerializedObject(nt);
                var scaleProp = ntSerialized.FindProperty("_synchronizeScale");
                if (scaleProp != null && scaleProp.boolValue)
                {
                    scaleProp.boolValue = false;
                    ntSerialized.ApplyModifiedProperties();
                    Debug.Log("[NetworkPlayerSetupTool] NetworkTransform.Synchronize Scale = false 설정");
                }

                // ③ PlayerOwnershipGate — 설정할 값 없음(Awake에서 스스로 배선).
                if (root.GetComponent<PlayerOwnershipGate>() == null)
                {
                    root.AddComponent<PlayerOwnershipGate>();
                    Debug.Log("[NetworkPlayerSetupTool] Player 프리팹에 PlayerOwnershipGate 부착");
                }

                // ④ TagNetworkSync — 스프린트 11 서버 권위 태그. 설정할 값 없음
                //    (Awake에서 IRoleState를 스스로 찾고, 태그 상태는 SyncVar가 관리한다).
                if (root.GetComponent<TagNetworkSync>() == null)
                {
                    root.AddComponent<TagNetworkSync>();
                    Debug.Log("[NetworkPlayerSetupTool] Player 프리팹에 TagNetworkSync 부착");
                }

                // ⑤ RoleNetworkSync — 스프린트 13 서버 권위 역할 배정(§6.2/§14.3). 설정할 값 없음
                //    (Awake에서 IRoleState를 스스로 찾고, 배정 결과는 SyncVar가 관리한다).
                if (root.GetComponent<RoleNetworkSync>() == null)
                {
                    root.AddComponent<RoleNetworkSync>();
                    Debug.Log("[NetworkPlayerSetupTool] Player 프리팹에 RoleNetworkSync 부착");
                }

                // ⑥ ReadyNetworkSync — 스프린트 18 로비 준비 상태(§12.3/§14.3 ReadyToggle). 설정할 값 없음.
                if (root.GetComponent<ReadyNetworkSync>() == null)
                {
                    root.AddComponent<ReadyNetworkSync>();
                    Debug.Log("[NetworkPlayerSetupTool] Player 프리팹에 ReadyNetworkSync 부착");
                }
            }

            // EditPrefabContentsScope가 저장을 마친 뒤의 확정 에셋에서 참조를 다시 읽는다.
            AssetDatabase.SaveAssets();
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return savedPrefab != null ? savedPrefab.GetComponent<NetworkObject>() : null;
        }

        private static void EnsureFolderExists(string projectPath)
        {
            if (AssetDatabase.IsValidFolder(projectPath))
                return;

            string parent = Path.GetDirectoryName(projectPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(projectPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolderExists(parent);

            AssetDatabase.CreateFolder(parent, leaf);
            Debug.Log($"[NetworkPlayerSetupTool] 폴더 생성: {projectPath}");
        }

        // ── 10-1 확인 + 10-4: PlayerSpawner 배선 ────────────────────────────

        private static void RegisterSpawnablePrefab(NetworkManager networkManager, NetworkObject playerNob)
        {
            var defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
            if (defaultPrefabs == null)
            {
                Debug.LogWarning($"[NetworkPlayerSetupTool] {DefaultPrefabObjectsPath}를 찾지 못했습니다. " +
                    "NetworkManager의 Spawnable Prefabs를 수동으로 지정해야 합니다.");
                return;
            }

            if (networkManager.SpawnablePrefabs == null)
            {
                networkManager.SpawnablePrefabs = defaultPrefabs;
                EditorUtility.SetDirty(networkManager);
                Debug.Log("[NetworkPlayerSetupTool] NetworkManager.Spawnable Prefabs = DefaultPrefabObjects.asset 연결");
            }

            // FishNet 공식 등록 API — 이미 등록돼 있으면 checkForDuplicates가 중복 추가를 막는다.
            defaultPrefabs.AddObject(playerNob, checkForDuplicates: true);
            EditorUtility.SetDirty(defaultPrefabs);
            AssetDatabase.SaveAssets();
            Debug.Log("[NetworkPlayerSetupTool] Player NetworkObject를 DefaultPrefabObjects에 등록");
        }

        private static void WirePlayerSpawner(PlayerSpawner spawner, NetworkObject playerNob)
        {
            spawner.SetPlayerPrefab(playerNob);
            EditorUtility.SetDirty(spawner);
            // Spawns 배열은 비워 둔다 — §10-4대로 프리팹 자신의 Transform 위치에서 스폰된다.
            Debug.Log("[NetworkPlayerSetupTool] PlayerSpawner.Player Prefab = Player.prefab 배선 (Spawns는 비워 둠)");
        }

        // ── 10-5: 씬 Player 인스턴스 제거 ────────────────────────────────────

        private static void RemoveScenePlayerInstance(GameObject scenePlayer)
        {
            if (scenePlayer == null)
            {
                Debug.Log("[NetworkPlayerSetupTool] 제거할 씬 Player 인스턴스가 없습니다(이미 제거됨).");
                return;
            }

            Undo.DestroyObjectImmediate(scenePlayer);
            Debug.Log("[NetworkPlayerSetupTool] 씬의 기존 Player 인스턴스를 삭제했습니다 — " +
                "이제 PlayerSpawner가 런타임에 스폰합니다. " +
                "PulseSystem/EscapePointTrigger 등의 참조는 LocalPlayerRegistry 지연 바인딩으로 자동 연결됩니다(스프린트 8).");
        }

        // ── 실행 후 안내 ─────────────────────────────────────────────────

        private static void LogVerificationGuidance()
        {
            Debug.Log(
                "[NetworkPlayerSetupTool] 완료. docs/수동검증_절차.md §10-6~10-7 기준으로 다음을 확인하세요:\n" +
                "  1) NetworkManager 오브젝트에 Network Manager / Tugboat / Player Spawner 3개 컴포넌트가 붙어 있는지\n" +
                "  2) NetworkManager.Spawnable Prefabs가 DefaultPrefabObjects.asset을 가리키는지\n" +
                "  3) Player Spawner.Player Prefab이 Assets/_Project/Prefabs/Player.prefab을 가리키는지\n" +
                "  4) Player.prefab에 NetworkObject/NetworkTransform(Position·Rotation on, Scale off)/PlayerOwnershipGate가 붙어 있는지\n" +
                "  5) 씬에 기존 Player 인스턴스가 더 이상 없는지\n" +
                "  6) §10-6대로 빌드 후 에디터=호스트, 빌드 exe=클라이언트로 접속해 §10-7 체크리스트(캐릭터 2개 보임 / " +
                "NetworkTransform 동기화 / 원격 캐릭터가 내 입력에 반응 안 함 / Player (Local)·Player (Remote) 이름 분기)를 확인하세요\n" +
                "  7) 씬 저장을 잊지 마세요(Ctrl+S) — 이 도구는 씬을 dirty로 표시만 합니다.");
        }
    }
}
