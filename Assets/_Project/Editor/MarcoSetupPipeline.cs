using System.Collections.Generic;
using System.Text;
using FishNet.Managing;
using Marco.Net;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Objectives;
using Marco.Presentation.Sound;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// 스프린트 8~18b에서 하나씩 쌓인 셋업 도구 전체를 **버튼 하나로** 순서대로 실행하는
    /// 오케스트레이션 계층.
    ///
    /// **왜 필요한가**: 지금까지는 도구 실행 → Ctrl+S → 다음 도구 → Ctrl+S → Reserialize →
    /// 진단을 사람이 순서대로 반복해야 했고, 그 사이의 **저장 누락**이 스프린트 14·18b에서
    /// 반복적으로 실기 실패를 만들었다(씬을 dirty로만 표시하는 도구 특성). 이 파이프라인은
    /// 저장을 코드로 수행해 그 실패 원인을 구조적으로 제거한다.
    ///
    /// **기존 개별 도구는 그대로 둔다** — 문제가 생겼을 때 한 단계만 다시 돌릴 수 있어야 한다.
    /// 이 클래스는 그 위에 얹는 얇은 계층이며, 각 도구의 로직을 복제하지 않고 그대로 호출한다.
    ///
    /// **멱등**: 각 도구가 이미 멱등이므로 이 파이프라인도 몇 번이든 다시 실행할 수 있다.
    ///
    /// **단계 순서의 근거**(의존성):
    /// <list type="number">
    /// <item>Scene Flow 2가 먼저다 — 시스템 오브젝트를 Lobby로 옮겨야 이후 도구들이 올바른 씬에서 동작한다.
    ///       (이 단계는 Game 씬을 Single로 열므로 다른 열린 씬을 닫는다. 그래서 맨 앞이어야 한다.)</item>
    /// <item>Scene Flow 5(접속 컴포넌트 분리)는 2 직후 — 2가 만든 SceneFlow 오브젝트가 필요하다.</item>
    /// <item>Player 셋업은 <b>Lobby가 활성 씬일 때</b> 실행해야 한다 — NetworkManager를 찾지 못하면
    ///       활성 씬에 새로 만들기 때문에, Game이 활성이면 NetworkManager가 중복 생성된다.</item>
    /// <item>Valve는 맵(Game), Round·Pulse는 시스템(Lobby) 대상이라 두 씬이 함께 열린 이 시점에 실행한다.</item>
    /// <item>Scene Flow 3·4는 씬을 Single로 열므로 마지막이다 — 그 전에 반드시 저장한다.</item>
    /// </list>
    /// </summary>
    public static class MarcoSetupPipeline
    {
        /// <summary>
        /// 참이면 각 셋업 도구가 확인 대화상자를 건너뛴다. 파이프라인이 사람 개입 없이 도는
        /// 유일한 목적이며, 개별 메뉴로 실행할 때는 항상 거짓이라 기존 동작이 그대로 유지된다.
        /// </summary>
        internal static bool Automated { get; private set; }

        private const string ScenesFolder = "Assets/Scenes";
        private const string LobbyScene = ScenesFolder + "/Lobby.unity";
        private const string MapScene = ScenesFolder + "/Game.unity";

        [MenuItem("Tools/MARCO/Setup Everything (전체 셋업 원버튼)", priority = 0)]
        public static void RunAll()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("전체 셋업", "재생 모드에서는 실행할 수 없습니다. Play를 멈추고 다시 실행하세요.", "확인");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "MARCO 전체 셋업 (원버튼)",
                    "아래를 순서대로 자동 실행하고, 각 단계 사이의 씬 저장까지 코드로 처리합니다:\n\n" +
                    "  1. Scene Flow — Lobby 씬 구성(시스템 이동)\n" +
                    "  2. Scene Flow — 로비 배선 정리(접속 컴포넌트 분리)\n" +
                    "  3. Setup Network Player (프리팹 + NetworkManager)\n" +
                    "  4. Setup Network Valves / Round / Pulse\n" +
                    "  5. Scene Flow — 맵 정리(스폰 앵커)\n" +
                    "  6. Scene Flow — Boot·MainMenu + 빌드 설정\n" +
                    "  7. 진단(Lobby + Game 함께)\n\n" +
                    "⚠ 여러 씬 자산과 프리팹을 수정하며 일부는 Undo로 되돌아가지 않습니다.\n" +
                    "실행 전 Assets/Scenes 폴더 백업을 강력히 권장합니다.\n\n계속하시겠습니까?",
                    "실행", "취소"))
            {
                Debug.Log("[Pipeline] 사용자가 취소했습니다 — 변경 없음.");
                return;
            }

            // 현재 열린 씬의 미저장 변경을 먼저 처리한다(파이프라인이 씬을 Single로 열어 닫기 때문).
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[Pipeline] 씬 저장을 취소해 파이프라인을 중단합니다 — 변경 없음.");
                return;
            }

            var log = new StringBuilder();
            log.AppendLine("=== MARCO 전체 셋업 파이프라인 ===");

            Automated = true;
            try
            {
                if (!Execute(log))
                {
                    log.AppendLine();
                    log.AppendLine("결과: **중단됨** — 위 실패 단계를 해결한 뒤 다시 실행하세요. " +
                                   "각 단계는 멱등이라 다시 실행해도 안전합니다.");
                    Debug.LogError(log.ToString());
                    return;
                }
            }
            finally
            {
                Automated = false;
            }

            log.AppendLine();
            log.AppendLine("결과: 전 단계 완료. 아래 진단 결과를 확인하세요.");
            Debug.Log(log.ToString());

            // 마지막 관문: 두 씬을 함께 연 상태로 전 항목 점검.
            OpenSystemAndMap();
            bool diagnosticsOk = NetworkSetupDiagnostics.RunAndReport();

            PromptReserialize(diagnosticsOk);
        }

        /// <summary>실제 파이프라인. 한 단계라도 실패하면 즉시 false를 반환하고 이후 단계를 실행하지 않는다.</summary>
        private static bool Execute(StringBuilder log)
        {
            // ── 1. Lobby 씬 구성(시스템 이동) ────────────────────────────
            // 이 단계가 Game 씬을 Single로 열고 Lobby를 애디티브로 얹는다. 이후 단계는 이 두 씬이
            // 함께 열려 있다는 전제로 동작한다.
            if (!Step(log, "Scene Flow — Lobby 씬 구성", SceneFlowSetupTool.SetupLobbyScene))
                return false;

            if (!Require(log, IsSceneLoaded("Lobby"), "Lobby 씬이 열리지 않았습니다 — Assets/Scenes/Lobby.unity가 있는지 확인하세요."))
                return false;

            SaveOpenScenes(log);

            // ── 2. 접속 컴포넌트 분리 ────────────────────────────────────
            // FishNet이 접속 전 NetworkObject를 비활성화하므로 ConnectionService가 그 위에 있으면
            // 접속 자체가 불가능하다(스프린트 18b 실기에서 확정).
            if (!Step(log, "Scene Flow — 로비 배선 정리", SceneFlowSetupTool.FixLobbyWiring))
                return false;

            SaveOpenScenes(log);

            // ── 3. Player 프리팹 + NetworkManager ────────────────────────
            // **활성 씬을 Lobby로 고정**한다: NetworkManager를 못 찾으면 활성 씬에 새로 만드는데,
            // Game이 활성이면 맵 씬에 NetworkManager가 중복 생성되어 DestroyNewest 경고의 원인이 된다.
            if (!ActivateScene(log, "Lobby"))
                return false;

            if (!Step(log, "Setup Network Player", NetworkPlayerSetupTool.Run))
                return false;

            if (!Require(log, CountNetworkManagers() == 1,
                    $"NetworkManager가 {CountNetworkManagers()}개입니다 — 정확히 1개(Lobby)여야 합니다."))
                return false;

            SaveOpenScenes(log);

            // ── 4. 밸브(맵) / 라운드·파문(시스템) ────────────────────────
            if (!Require(log, Object.FindObjectsByType<ValveBehaviour>(FindObjectsInactive.Include).Length > 0,
                    "맵 씬에서 밸브(ValveBehaviour)를 찾지 못했습니다 — Game 씬이 함께 열려 있는지 확인하세요."))
                return false;

            if (!Step(log, "Setup Network Valves", NetworkValveSetupTool.Run))
                return false;

            if (!Require(log, Object.FindAnyObjectByType<RoundCoordinator>(FindObjectsInactive.Include) != null,
                    "RoundCoordinator를 찾지 못했습니다 — Lobby 씬에 PulseSystem이 있는지 확인하세요."))
                return false;

            if (!Step(log, "Setup Network Round", NetworkRoundSetupTool.Run))
                return false;

            if (!Require(log, Object.FindAnyObjectByType<LocalPulsePipelineBehaviour>(FindObjectsInactive.Include) != null,
                    "LocalPulsePipelineBehaviour를 찾지 못했습니다 — Lobby 씬에 PulseSystem이 있는지 확인하세요."))
                return false;

            if (!Step(log, "Setup Network Pulse", NetworkPulseSetupTool.Run))
                return false;

            // 이 시점 이후 단계는 씬을 Single로 열어 지금 열린 씬을 닫는다 — 반드시 저장한다.
            SaveOpenScenes(log);

            // ── 5. 맵 정리(스폰 앵커) ────────────────────────────────────
            if (!Step(log, "Scene Flow — 맵 씬 정리", SceneFlowSetupTool.SetupMapScene))
                return false;

            SaveOpenScenes(log);

            // ── 6. Boot·MainMenu + 빌드 설정(자체 저장) ──────────────────
            if (!Step(log, "Scene Flow — Boot·MainMenu + 빌드 설정", SceneFlowSetupTool.SetupEntryScenes))
                return false;

            // ── 7. 머티리얼 셰이더 복원 ──────────────────────────────────
            // URP 프로젝트인데 그레이박스 머티리얼이 빌트인 Standard 셰이더를 가리키면 표면이
            // 정상 조명을 받지 못한다(스프린트 18b 실기 렌더링 버그).
            if (!Step(log, "Fix Graybox Materials (URP 셰이더 복원)", GrayboxMaterialFixTool.Run))
                return false;

            AssetDatabase.SaveAssets();
            return true;
        }

        // ── 단계 실행/검증 헬퍼 ───────────────────────────────────────────

        private static bool Step(StringBuilder log, string name, System.Action action)
        {
            try
            {
                action();
                log.AppendLine($"  ✔ {name}");
                return true;
            }
            catch (System.Exception ex)
            {
                // 예외를 삼키지 않는다 — 어느 단계에서 무엇이 터졌는지 그대로 남긴다.
                log.AppendLine($"  ✖ {name} — 예외 발생: {ex.GetType().Name}: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }

        private static bool Require(StringBuilder log, bool condition, string failureMessage)
        {
            if (condition)
                return true;

            log.AppendLine($"  ✖ 전제 조건 실패: {failureMessage}");
            return false;
        }

        private static bool ActivateScene(StringBuilder log, string name)
        {
            Scene scene = SceneManager.GetSceneByName(name);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                log.AppendLine($"  ✖ '{name}' 씬이 열려 있지 않아 활성 씬으로 지정할 수 없습니다.");
                return false;
            }

            SceneManager.SetActiveScene(scene);
            log.AppendLine($"  · 활성 씬을 '{name}'으로 지정(NetworkManager 중복 생성 방지)");
            return true;
        }

        /// <summary>열린 모든 씬을 저장한다 — 사람이 Ctrl+S를 놓쳐 발생하던 실패를 없애는 핵심 단계.</summary>
        private static void SaveOpenScenes(StringBuilder log)
        {
            bool saved = EditorSceneManager.SaveOpenScenes();
            log.AppendLine(saved ? "  · 열린 씬 저장 완료" : "  · ⚠ 씬 저장에 실패했습니다(권한/잠금 확인)");
        }

        private static int CountNetworkManagers() =>
            Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include).Length;

        private static bool IsSceneLoaded(string name)
        {
            Scene scene = SceneManager.GetSceneByName(name);
            return scene.IsValid() && scene.isLoaded;
        }

        private static void OpenSystemAndMap()
        {
            if (!IsSceneLoaded("Lobby"))
                EditorSceneManager.OpenScene(LobbyScene, OpenSceneMode.Single);

            if (!IsSceneLoaded("Game"))
                EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);

            SceneManager.SetActiveScene(SceneManager.GetSceneByName("Lobby"));
        }

        /// <summary>
        /// FishNet의 Reserialize는 코드로 직접 실행할 수 없다 — 실제 작업 메서드가
        /// <c>ReserializeNetworkObjectsEditor</c>의 **private 인스턴스 메서드**이고 창 상태
        /// (Reserialize Prefabs/Scenes 토글, EditorPrefs에 저장된 마지막 값)에 의존하며,
        /// 개별 API <c>NetworkObject.ReserializeEditorSetValues</c>는 FishNet.Runtime의
        /// <c>internal</c>이라 이 어셈블리에서 호출할 수 없다. 리플렉션으로 우회하면 벤더
        /// 업데이트마다 조용히 깨지므로, 대신 **정확한 타이밍에 창을 열어** 클릭을 유도한다.
        /// </summary>
        private static void PromptReserialize(bool diagnosticsOk)
        {
            string tail = diagnosticsOk
                ? "진단은 전 항목 정상입니다."
                : "⚠ 진단에서 실패 항목이 나왔습니다 — Console의 점검 보고를 먼저 확인하세요.";

            bool open = EditorUtility.DisplayDialog(
                "마지막 수동 단계 — Reserialize NetworkObjects",
                "자동 단계는 모두 끝났습니다.\n\n" +
                "남은 단계는 하나입니다: Fish-Networking의 **Reserialize NetworkObjects**.\n" +
                "이 작업만은 코드로 실행할 수 없습니다(벤더 창 내부 상태에 의존하며 관련 API가 " +
                "FishNet 내부 전용입니다).\n\n" +
                "지금 창을 열어 드릴까요? 창에서 Reserialize Prefabs / Reserialize Scenes를 켜고 " +
                "'Run Task'를 누른 뒤, 열린 씬을 저장(Ctrl+S)하면 전체 셋업이 끝납니다.\n\n" + tail,
                "창 열기", "나중에");

            if (open)
                EditorApplication.ExecuteMenuItem("Tools/Fish-Networking/Utility/Reserialize NetworkObjects");
            else
                Debug.LogWarning("[Pipeline] Reserialize를 건너뛰었습니다 — 씬 NetworkObject의 SceneId가 " +
                                 "확정되지 않으면 라운드 상태기계가 스폰되지 않을 수 있습니다(스프린트 14 실기 실패 유형).");
        }
    }
}
