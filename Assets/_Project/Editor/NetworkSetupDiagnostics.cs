using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using Marco.Net;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Objectives;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;
using Marco.Presentation.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// 네트워크 컴포넌트 부착 상태를 한 번에 점검한다(스프린트 14 실기 디버그에서 필요해져 추가).
    ///
    /// **왜 필요한가**: 스프린트 14 실기에서 "Setup Network Pulse를 실행했는데 발소리가 원격에
    /// 전달되지 않는" 문제의 원인이 <b>씬에 <see cref="PulseNetworkSync"/>가 실제로는 부착되지
    /// 않은 것</b>이었다. 셋업 도구들은 씬을 dirty로 표시만 하므로 저장(Ctrl+S)을 놓치면 변경이
    /// 사라지고, 빌드는 저장된 씬을 쓴다. 런타임 로그로 뒤늦게 알기 전에 에디터에서 즉시 확인할
    /// 수단을 둔다.
    ///
    /// 아무것도 수정하지 않는다 — 읽기 전용 점검이다.
    /// </summary>
    public static class NetworkSetupDiagnostics
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        /// <summary>§15.1 시스템 씬 / 맵 씬 이름(<c>SceneFlowSetupTool</c>과 같은 값).</summary>
        private const string SystemSceneName = "Lobby";
        private const string MapSceneName = "Game";

        /// <summary>
        /// 스프린트 18b 분리 배치에서 **시스템 씬이 열려 있지 않은가**. 참이면 라운드·파문·HUD 등이
        /// 없는 것이 정상이므로 실패로 세지 않는다(경고 대신 안내).
        /// </summary>
        private static bool _systemSceneClosed;

        /// <summary>맵 씬이 열려 있지 않은가. 참이면 밸브·탈출 지점이 없는 것이 정상이다.</summary>
        private static bool _mapSceneClosed;

        [MenuItem("Tools/MARCO/Diagnose Network Setup")]
        public static void Run() => RunAndReport();

        /// <summary>점검을 수행하고 전 항목 정상 여부를 돌려준다(전체 셋업 파이프라인이 마지막 관문으로 쓴다).</summary>
        internal static bool RunAndReport()
        {
            var report = new StringBuilder();
            report.AppendLine("=== MARCO 네트워크 셋업 점검 (읽기 전용) ===");

            bool allOk = true;

            // ── 씬 구성 점검(스프린트 18b: 멀티 씬) ───────────────────────
            allOk &= CheckSceneComposition(report);

            // ── 씬 오브젝트 점검 ──────────────────────────────────────────
            report.AppendLine();
            report.AppendLine("[씬 컴포넌트]");
            allOk &= CheckSceneComponent<LocalPulsePipelineBehaviour, PulseNetworkSync>(
                report, "파문(스프린트 14)", "Setup Network Pulse", inSystemScene: true);
            allOk &= CheckSceneComponent<RoundCoordinator, RoundNetworkSync>(
                report, "라운드(스프린트 12)", "Setup Network Round", inSystemScene: true);
            allOk &= CheckValves(report);
            allOk &= CheckMapContent(report);
            allOk &= CheckHud(report);

            // ── 렌더링 점검 ───────────────────────────────────────────────
            report.AppendLine();
            report.AppendLine("[렌더링]");
            allOk &= CheckMaterials(report);

            // ── Player 프리팹 점검 ────────────────────────────────────────
            report.AppendLine();
            report.AppendLine("[Player 프리팹]");
            allOk &= CheckPlayerPrefab(report);

            // ── 저장 상태 ─────────────────────────────────────────────────
            // 멀티 씬에서는 **열린 모든 씬**을 봐야 한다 — 마이그레이션은 두 씬을 함께 고치므로
            // 활성 씬만 보면 Lobby의 미저장 변경을 놓친다(스프린트 14 "저장 누락" 사고 유형).
            report.AppendLine();
            var dirty = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty)
                    dirty.Add(s.name);
            }

            if (dirty.Count > 0)
            {
                allOk = false;
                report.AppendLine($"⚠ 저장되지 않은 씬: {string.Join(", ", dirty)} — Ctrl+S로 저장하세요. " +
                                  "저장하지 않으면 빌드·재생 시 변경이 반영되지 않습니다.");
            }
            else
            {
                report.AppendLine("✔ 열린 씬에 저장되지 않은 변경 없음");
            }

            report.AppendLine();
            report.AppendLine(allOk
                ? "결과: 전 항목 정상. 문제가 계속되면 런타임 로그([Pulse:Diag]/[PulseNet:Diag])를 확인하세요."
                : "결과: 누락 항목이 있습니다. 위 안내대로 셋업 도구 실행 → 씬 저장 → " +
                  "Fish-Networking → Utility → Reserialize NetworkObjects 순서로 처리하세요.");

            if (allOk)
                Debug.Log(report.ToString());
            else
                Debug.LogWarning(report.ToString());

            return allOk;
        }

        /// <summary>
        /// 열린 씬 구성을 보고, 스프린트 18b 분리 배치인지 판정한다(§15.1).
        ///
        /// **왜 필요한가**: 분리 후에는 시스템 오브젝트(PulseSystem — 라운드·HUD·파문·UI 일체)가
        /// <c>Lobby</c> 씬에, 맵(밸브·탈출 지점·스폰 앵커)이 <c>Game</c> 씬에 있다. 따라서 Game 씬만
        /// 열고 점검하면 시스템 컴포넌트가 "없음"으로 보이는데 이것이 **정상**이다. 이를 실패로
        /// 보고하면 사용자가 없는 문제를 쫓게 된다.
        /// </summary>
        private static bool CheckSceneComposition(StringBuilder report)
        {
            bool systemLoaded = IsSceneLoaded(SystemSceneName);
            bool mapLoaded = IsSceneLoaded(MapSceneName);

            _systemSceneClosed = !systemLoaded;
            _mapSceneClosed = !mapLoaded;

            report.AppendLine("[씬 구성 (§15.1)]");
            var open = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                open.Add(SceneManager.GetSceneAt(i).name);

            report.AppendLine($"  열린 씬: {string.Join(" + ", open)} (활성: {SceneManager.GetActiveScene().name})");

            // 활성 카메라가 2대 이상이면 같은 장면을 매 프레임 두 번 그린다 — 프레임 저하 + 화면 깨짐.
            var activeCameras = new List<string>();
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (cam.gameObject.activeInHierarchy)
                    activeCameras.Add($"{cam.gameObject.name}({cam.gameObject.scene.name})");
            }

            if (activeCameras.Count > 1)
            {
                report.AppendLine($"  ✖ 활성 카메라가 {activeCameras.Count}대입니다: {string.Join(", ", activeCameras)} — " +
                                  "같은 장면을 매 프레임 중복 렌더링해 **프레임이 떨어지고 바닥이 검게 깨집니다**. " +
                                  "→ Tools/MARCO/Scene Flow — 5. 로비 배선 정리 실행(중복 카메라 자동 비활성화) 후 씬 저장");
                return false;
            }

            report.AppendLine($"  ✔ 활성 씬 카메라 1대: {(activeCameras.Count == 1 ? activeCameras[0] : "없음(플레이어 카메라만 사용)")}");

            if (systemLoaded && mapLoaded)
            {
                report.AppendLine("  ✔ 시스템 씬(Lobby) + 맵 씬(Game)이 함께 열려 있어 전 항목을 점검할 수 있습니다.");
                return true;
            }

            // 한쪽만 열려 있어도 그 씬에 대한 점검은 유효하다 — 실패가 아니라 범위 안내다.
            report.AppendLine("  ⓘ 스프린트 18b 분리 배치에서는 시스템(PulseSystem: 라운드·HUD·결과·로비·파문·접속)이 " +
                              $"'{SystemSceneName}' 씬에, 맵(밸브·탈출 지점·스폰 앵커)이 '{MapSceneName}' 씬에 있습니다.");
            if (!systemLoaded)
                report.AppendLine($"     → '{SystemSceneName}' 씬이 열려 있지 않아 **시스템 컴포넌트가 '없음'으로 나오는 것이 정상**입니다(아래 ⓘ 항목).");
            if (!mapLoaded)
                report.AppendLine($"     → '{MapSceneName}' 씬이 열려 있지 않아 **맵 컴포넌트가 '없음'으로 나오는 것이 정상**입니다(아래 ⓘ 항목).");

            report.AppendLine("     전 항목을 한 번에 보려면 Tools/MARCO/Diagnose Network Setup (Lobby + Game 함께)를 실행하세요.");
            return true;
        }

        private static bool IsSceneLoaded(string name)
        {
            Scene scene = SceneManager.GetSceneByName(name);
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>
        /// 두 씬을 함께 연 뒤 점검한다. 분리 배치에서 "전체" 상태를 한 번에 보기 위한 편의 진입점이다.
        /// 미저장 변경이 있으면 Unity 표준 저장 확인 대화상자를 거친다(사용자 변경을 임의로 버리지 않는다).
        /// </summary>
        [MenuItem("Tools/MARCO/Diagnose Network Setup (Lobby + Game 함께)")]
        public static void RunAcrossScenes()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[Diagnose] 사용자가 취소했습니다 — 씬을 열지 않았습니다.");
                return;
            }

            if (!IsSceneLoaded(SystemSceneName))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    $"Assets/Scenes/{SystemSceneName}.unity",
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);

            if (!IsSceneLoaded(MapSceneName))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    $"Assets/Scenes/{MapSceneName}.unity",
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);

            Run();
        }

        /// <summary>씬의 <typeparamref name="THost"/> 오브젝트에 NetworkObject와 <typeparamref name="TSync"/>가 있는지.</summary>
        /// <param name="inSystemScene">
        /// 분리 배치에서 이 컴포넌트가 시스템 씬(Lobby)에 속하는가. 참이고 시스템 씬이 닫혀 있으면
        /// "없음"은 실패가 아니라 점검 범위 밖이다.
        /// </param>
        private static bool CheckSceneComponent<THost, TSync>(StringBuilder report, string label, string toolName,
                                                              bool inSystemScene = false)
            where THost : Component
            where TSync : Component
        {
            var host = Object.FindAnyObjectByType<THost>(FindObjectsInactive.Include);
            if (host == null)
            {
                if (inSystemScene && _systemSceneClosed)
                {
                    report.AppendLine($"  ⓘ {label}: {typeof(THost).Name}는 '{SystemSceneName}' 씬 소속입니다 — " +
                                      "그 씬이 열려 있지 않아 점검을 건너뜁니다(정상).");
                    return true;
                }

                report.AppendLine($"  ✖ {label}: 열린 씬에서 {typeof(THost).Name}를 찾지 못했습니다 " +
                                  $"(분리 배치라면 '{SystemSceneName}' 씬을 함께 여세요).");
                return false;
            }

            bool hasNob = host.GetComponent<NetworkObject>() != null;
            bool hasSync = host.GetComponent<TSync>() != null;

            if (hasNob && hasSync)
            {
                report.AppendLine($"  ✔ {label}: '{host.gameObject.name}'({host.gameObject.scene.name} 씬)에 " +
                                  $"NetworkObject + {typeof(TSync).Name} 부착됨");
                return true;
            }

            report.AppendLine($"  ✖ {label}: '{host.gameObject.name}'에 " +
                              $"{(hasNob ? "" : "NetworkObject 없음 ")}{(hasSync ? "" : $"{typeof(TSync).Name} 없음")}" +
                              $" → Tools/MARCO/{toolName} 실행 후 **씬 저장(Ctrl+S)**");
            return false;
        }

        private static bool CheckValves(StringBuilder report)
        {
            var valves = Object.FindObjectsByType<ValveBehaviour>(FindObjectsInactive.Include);
            if (valves.Length == 0)
            {
                if (_mapSceneClosed)
                {
                    report.AppendLine($"  ⓘ 밸브(스프린트 10): 밸브는 '{MapSceneName}'(맵) 씬 소속입니다 — " +
                                      "그 씬이 열려 있지 않아 점검을 건너뜁니다(정상).");
                    return true;
                }

                report.AppendLine("  ✖ 밸브(스프린트 10): 열린 씬에서 ValveBehaviour를 찾지 못했습니다.");
                return false;
            }

            int wired = 0;
            foreach (ValveBehaviour valve in valves)
            {
                if (valve.GetComponent<NetworkObject>() != null && valve.GetComponent<ValveNetworkSync>() != null)
                    wired++;
            }

            if (wired == valves.Length)
            {
                report.AppendLine($"  ✔ 밸브(스프린트 10): {valves.Length}개 전부 NetworkObject + ValveNetworkSync 부착됨");
                return true;
            }

            report.AppendLine($"  ✖ 밸브(스프린트 10): {wired}/{valves.Length}개만 배선됨 → " +
                              "Tools/MARCO/Setup Network Valves 실행 후 **씬 저장(Ctrl+S)**");
            return false;
        }

        /// <summary>
        /// 스프린트 18b 맵 씬 내용물 점검: 스폰 앵커(§10.1)와 탈출 지점.
        ///
        /// 스폰 앵커가 없으면 맵 로드 후 pawn을 옮길 좌표가 없어 로비의 임시 바닥에 남는다
        /// (<c>SpawnAnchorRegistry.HasAnchor</c>가 false → <c>PawnPhaseTeleporter</c>가 아무것도 하지 않음).
        /// </summary>
        private static bool CheckMapContent(StringBuilder report)
        {
            bool ok = true;

            var anchor = Object.FindAnyObjectByType<SpawnAnchor>(FindObjectsInactive.Include);
            if (anchor == null)
            {
                if (_mapSceneClosed)
                {
                    report.AppendLine($"  ⓘ 스폰 앵커(스프린트 18b): '{MapSceneName}'(맵) 씬이 열려 있지 않아 점검을 건너뜁니다(정상).");
                }
                else
                {
                    ok = false;
                    report.AppendLine("  ✖ 스폰 앵커(스프린트 18b): 맵 씬에서 SpawnAnchor를 찾지 못했습니다 → " +
                                      "Tools/MARCO/Scene Flow — 3. 맵 씬 정리(스폰 앵커) 실행 후 **씬 저장(Ctrl+S)**. " +
                                      "없으면 맵 로드 후에도 pawn이 로비 임시 바닥에 남습니다.");
                }
            }
            else
            {
                report.AppendLine($"  ✔ 스폰 앵커(스프린트 18b): '{anchor.gameObject.name}'({anchor.gameObject.scene.name} 씬) " +
                                  $"위치 {anchor.transform.position}");
            }

            // 로비 임시 바닥: 로비 페이즈의 유일한 지면이자, 맵 로드 후에는 맵 바닥과 같은 평면에서
            // Z-파이팅을 일으키는 양날의 오브젝트다. LobbyPlaceholderFloor가 그 전환을 담당한다.
            var placeholder = Object.FindAnyObjectByType<LobbyPlaceholderFloor>(FindObjectsInactive.Include);
            if (placeholder == null && !_systemSceneClosed)
            {
                bool floorExists = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    {
                        if (root.name == "Lobby Floor")
                            floorExists = true;
                    }
                }

                if (floorExists)
                {
                    ok = false;
                    report.AppendLine("  ✖ 로비 임시 바닥: 'Lobby Floor'에 LobbyPlaceholderFloor가 없습니다 — " +
                                      "켜 두면 맵 바닥과 같은 평면(y=0)에서 **Z-파이팅**, 꺼 두면 로비에서 " +
                                      "**pawn이 무한 낙하**합니다(접속 안 된 것처럼 보임). " +
                                      "→ Tools/MARCO/Scene Flow — 5. 로비 배선 정리 실행 후 씬 저장");
                }
            }
            else if (placeholder != null)
            {
                report.AppendLine($"  ✔ 로비 임시 바닥: '{placeholder.gameObject.name}'" +
                                  $"({placeholder.gameObject.scene.name} 씬)에 LobbyPlaceholderFloor 부착됨 " +
                                  "— 맵 로드 시 자동으로 물러납니다.");
            }

            // 스프린트 19: 제거된 디버그 도구의 씬 잔재. 타입이 없어졌으므로 이름으로 확인한다
            // (남아 있으면 "Missing script" 경고가 계속 나고, 빌드에도 빈 오브젝트가 들어간다).
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                {
                    if (root.name != "NetworkTestBootstrap")
                        continue;

                    ok = false;
                    report.AppendLine($"  ✖ 디버그 도구 잔재(스프린트 19): '{root.name}' 오브젝트가 " +
                                      $"{root.scene.name} 씬에 남아 있습니다(스크립트는 삭제됨) → " +
                                      "Tools/MARCO/Cleanup Debug Tools 실행 후 **씬 저장(Ctrl+S)**");
                }
            }

            var escape = Object.FindAnyObjectByType<EscapePointTrigger>(FindObjectsInactive.Include);
            if (escape == null)
            {
                if (!_mapSceneClosed)
                {
                    ok = false;
                    report.AppendLine("  ✖ 탈출 지점(§10.1): 맵 씬에서 EscapePointTrigger를 찾지 못했습니다.");
                }
            }
            else
            {
                // 분리 배치에서 이 인스펙터 참조는 **비어 있는 것이 정상**이다 — 교차 씬 참조는 Unity가
                // 지원하지 않아 저장 시 null로 정리되며, 런타임에 EnsureCoordinator()가 지연 탐색한다.
                var so = new SerializedObject(escape);
                bool wired = so.FindProperty("_roundCoordinator")?.objectReferenceValue != null;
                report.AppendLine($"  ✔ 탈출 지점(§10.1): '{escape.gameObject.name}'({escape.gameObject.scene.name} 씬) — " +
                                  (wired
                                      ? "RoundCoordinator 인스펙터 연결됨(단일 씬 배치)"
                                      : "RoundCoordinator 인스펙터 참조 비어 있음 → **분리 배치에서 정상**. " +
                                        "런타임에 지연 탐색으로 연결되며, 실기에서 [Escape:Diag] 로그로 확인하세요."));
            }

            return ok;
        }

        /// <summary>
        /// 스프린트 16 HUD 점검. HUD는 자기 uGUI 계층을 런타임에 만들므로 씬에 이 컴포넌트
        /// 하나만 있으면 되지만, 팔레트 참조가 비어 있으면 색상이 하드코딩 폴백으로 떨어진다.
        /// </summary>
        private static bool CheckHud(StringBuilder report)
        {
            var hud = Object.FindAnyObjectByType<InGameHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                if (_systemSceneClosed)
                {
                    // 분리 배치: HUD·결과·로비·접속은 모두 PulseSystem(시스템 씬)에 함께 있으므로
                    // 한 번에 건너뛴다.
                    report.AppendLine($"  ⓘ HUD·결과 화면·로비·접속 서비스(스프린트 16~18): 모두 PulseSystem 오브젝트에 있고 " +
                                      $"'{SystemSceneName}' 씬 소속입니다 — 그 씬이 열려 있지 않아 점검을 건너뜁니다(정상).");
                    return true;
                }

                report.AppendLine("  ✖ HUD(스프린트 16): 열린 씬에서 InGameHud를 찾지 못했습니다 " +
                                  "→ PulseSystem 오브젝트에 In Game Hud 컴포넌트를 추가하세요.");
                return false;
            }

            var so = new SerializedObject(hud);
            bool paletteWired = so.FindProperty("_palette")?.objectReferenceValue != null;

            report.AppendLine($"  ✔ HUD(스프린트 16): '{hud.gameObject.name}'에 InGameHud 부착됨" +
                              (paletteWired ? " (팔레트 연결됨)" : " — ⚠ ColorPalette 미연결(하드코딩 색 폴백 사용)"));

            // 스프린트 17 결과 화면. HUD의 최소 배너와 중복되지 않는지도 함께 본다.
            var result = Object.FindAnyObjectByType<ResultScreen>(FindObjectsInactive.Include);
            if (result == null)
            {
                report.AppendLine("  ✖ 결과 화면(스프린트 17): 씬에서 ResultScreen을 찾지 못했습니다 " +
                                  "→ PulseSystem 오브젝트에 Result Screen 컴포넌트를 추가하세요.");
                return false;
            }

            bool hudBannerOn = so.FindProperty("_showResultBanner")?.boolValue ?? false;
            report.AppendLine($"  ✔ 결과 화면(스프린트 17): '{result.gameObject.name}'에 ResultScreen 부착됨" +
                              (hudBannerOn ? " — ⚠ HUD의 Show Result Banner도 켜져 있어 결과가 두 번 표시됩니다" : ""));

            // 스프린트 18 로비: 접속 서비스(Net) + 로비 화면(Presentation). 스프린트 19에서 임시
            // 폴백(DebugTools H/J)을 제거했으므로 **이제 이것이 유일한 접속 경로**다 — 하나라도
            // 없으면 게임에 접속할 방법 자체가 사라진다.
            bool lobbyOk = true;
            var connection = Object.FindAnyObjectByType<ConnectionService>(FindObjectsInactive.Include);
            if (connection == null)
            {
                lobbyOk = false;
                report.AppendLine("  ✖ 접속 서비스(스프린트 18): 씬에서 ConnectionService를 찾지 못했습니다 " +
                                  "→ PulseSystem 오브젝트에 Connection Service 컴포넌트를 추가하세요.");
            }
            else if (connection.GetComponent<NetworkObject>() != null)
            {
                // 실기에서 확정된 치명적 배선 오류: FishNet은 접속 전 씬 NetworkObject를 비활성화한다
                // (NetworkObject.Start → TryStartDeactivation → SetActive(false)). 접속을 시작할
                // 컴포넌트가 그 위에 있으면 OnDisable로 등록이 풀려 **접속 자체가 불가능**해진다.
                lobbyOk = false;
                report.AppendLine($"  ✖ 접속 서비스(스프린트 18): '{connection.gameObject.name}'은 NetworkObject를 가진 " +
                                  "오브젝트입니다 — FishNet이 **접속 전 이 오브젝트를 비활성화**하므로 " +
                                  "ConnectionService 등록이 해제되어 접속을 시작할 수 없습니다(로비가 멈춥니다). " +
                                  "→ Tools/MARCO/Scene Flow — 5. 로비 배선 정리 실행 후 **씬 저장(Ctrl+S)**");
            }
            else
            {
                report.AppendLine($"  ✔ 접속 서비스(스프린트 18): '{connection.gameObject.name}'" +
                                  $"({connection.gameObject.scene.name} 씬)에 ConnectionService 부착됨 " +
                                  "— NetworkObject 없는 오브젝트라 접속 전에도 살아 있습니다.");
            }

            var lobby = Object.FindAnyObjectByType<LobbyScreen>(FindObjectsInactive.Include);
            if (lobby == null)
            {
                lobbyOk = false;
                report.AppendLine("  ✖ 로비 화면(스프린트 18): 씬에서 LobbyScreen을 찾지 못했습니다 " +
                                  "→ PulseSystem 오브젝트에 Lobby Screen 컴포넌트를 추가하세요.");
            }
            else
            {
                report.AppendLine($"  ✔ 로비 화면(스프린트 18): '{lobby.gameObject.name}'에 LobbyScreen 부착됨");
            }

            // 스프린트 18b 씬 흐름: 시스템 씬에 SceneFlowController가 있어야 맵 지연 로드가 동작한다.
            // 없으면 "맵과 시스템이 한 씬"인 스프린트 18 배치로 간주된다(MapReadyForRound가 항상 true).
            var flow = Object.FindAnyObjectByType<SceneFlowController>(FindObjectsInactive.Include);
            if (flow == null)
            {
                report.AppendLine("  ⓘ 씬 흐름(스프린트 18b): SceneFlowController가 없습니다 — 맵이 시스템과 같은 씬에 " +
                                  "있는 스프린트 18 배치로 동작합니다(§15.4 맵 지연 로드 미적용). " +
                                  "Tools/MARCO/Scene Flow 메뉴로 씬을 분리하세요.");
            }
            else
            {
                report.AppendLine($"  ✔ 씬 흐름(스프린트 18b): '{flow.gameObject.name}'에 SceneFlowController 부착됨 " +
                                  $"(맵 씬: {flow.MapSceneName})");

                // 스프린트 20: 낙하 복구가 없으면 맵 밖으로 떨어진 플레이어가 영영 돌아오지 못한다
                // (상대 화면에서는 접속이 끊긴 것처럼 보인다).
                if (flow.GetComponent<FallRecoveryDriver>() == null)
                {
                    lobbyOk = false;
                    report.AppendLine("  ✖ 낙하 복구(스프린트 20): SceneFlow 오브젝트에 FallRecoveryDriver가 없습니다 — " +
                                      "맵 밖으로 떨어지면 되돌아올 방법이 없습니다. " +
                                      "→ Tools/MARCO/Scene Flow — 5. 로비 배선 정리 실행 후 씬 저장");
                }
                else
                {
                    report.AppendLine("  ✔ 낙하 복구(스프린트 20): FallRecoveryDriver 부착됨");
                }
            }

            return !hudBannerOn && lobbyOk;
        }

        /// <summary>
        /// URP 프로젝트에서 빌트인 Standard 셰이더를 쓰는 머티리얼을 잡아낸다 — 그 표면은
        /// 정상 조명을 받지 못한다(스프린트 18b 실기: 바닥이 깨져 보이던 원인).
        /// </summary>
        private static bool CheckMaterials(StringBuilder report)
        {
            bool usingSrp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            report.AppendLine($"  · 렌더 파이프라인: {(usingSrp ? "SRP(URP)" : "빌트인")}");

            if (!usingSrp)
                return true; // 빌트인 파이프라인이면 Standard 셰이더가 정상이다.

            var broken = GrayboxMaterialFixTool.FindBuiltInMaterials();
            if (broken.Count == 0)
            {
                report.AppendLine("  ✔ 머티리얼: 빌트인 Standard 셰이더를 쓰는 것이 없습니다.");
                return true;
            }

            var names = new List<string>();
            foreach (Material mat in broken)
                names.Add(mat.name);

            report.AppendLine($"  ✖ 머티리얼 {broken.Count}개가 **빌트인 Standard 셰이더**를 씁니다: {string.Join(", ", names)} — " +
                              "URP는 빌트인 셰이더를 지원하지 않아 그 표면이 정상 조명을 받지 못합니다(바닥이 깨져 보임). " +
                              "→ Tools/MARCO/Fix Graybox Materials (URP 셰이더 복원) 실행");
            return false;
        }

        private static bool CheckPlayerPrefab(StringBuilder report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                report.AppendLine($"  ✖ {PlayerPrefabPath}를 찾지 못했습니다 → Tools/MARCO/Setup Network Player 실행");
                return false;
            }

            bool ok = true;
            ok &= CheckPrefabComponent<NetworkObject>(report, prefab, "Setup Network Player");
            ok &= CheckPrefabComponent<PlayerOwnershipGate>(report, prefab, "Setup Network Player");
            ok &= CheckPrefabComponent<TagNetworkSync>(report, prefab, "Setup Network Player");
            ok &= CheckPrefabComponent<RoleNetworkSync>(report, prefab, "Setup Network Player");
            ok &= CheckPrefabComponent<ReadyNetworkSync>(report, prefab, "Setup Network Player");

            // 스프린트 12 실기 버그(프리팹 _role 기본값이 Seeker로 굳어 원격 러너가 인식되지 않던 것) 재발 감지.
            var controller = prefab.GetComponent<FirstPersonController>();
            if (controller != null)
            {
                var so = new SerializedObject(controller);
                SerializedProperty roleProp = so.FindProperty("_role");
                if (roleProp != null)
                {
                    // RoleType: 0=Seeker, 1=Runner, 2=Echo
                    string roleName = roleProp.enumValueIndex == 0 ? "Seeker" :
                                      roleProp.enumValueIndex == 1 ? "Runner" : "Echo";
                    if (roleProp.enumValueIndex == 1)
                    {
                        report.AppendLine($"  ✔ FirstPersonController._role = {roleName}(기본값 정상)");
                    }
                    else
                    {
                        ok = false;
                        report.AppendLine($"  ✖ FirstPersonController._role = {roleName} — 스폰 기본값은 Runner여야 합니다. " +
                                          "스프린트 12 실기 버그(전원이 술래로 보임)가 재발할 수 있습니다.");
                    }
                }
            }

            return ok;
        }

        private static bool CheckPrefabComponent<T>(StringBuilder report, GameObject prefab, string toolName)
            where T : Component
        {
            if (prefab.GetComponent<T>() != null)
            {
                report.AppendLine($"  ✔ {typeof(T).Name} 부착됨");
                return true;
            }

            report.AppendLine($"  ✖ {typeof(T).Name} 없음 → Tools/MARCO/{toolName} 실행 후 " +
                              "Fish-Networking → Utility → Reserialize NetworkObjects");
            return false;
        }
    }
}
