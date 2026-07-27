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

        [MenuItem("Tools/MARCO/Diagnose Network Setup")]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("=== MARCO 네트워크 셋업 점검 (읽기 전용) ===");
            report.AppendLine($"씬: {SceneManager.GetActiveScene().name}");
            report.AppendLine();

            bool allOk = true;

            // ── 씬 오브젝트 점검 ──────────────────────────────────────────
            report.AppendLine("[씬 컴포넌트]");
            allOk &= CheckSceneComponent<LocalPulsePipelineBehaviour, PulseNetworkSync>(
                report, "파문(스프린트 14)", "Setup Network Pulse");
            allOk &= CheckSceneComponent<RoundCoordinator, RoundNetworkSync>(
                report, "라운드(스프린트 12)", "Setup Network Round");
            allOk &= CheckValves(report);
            allOk &= CheckHud(report);

            // ── Player 프리팹 점검 ────────────────────────────────────────
            report.AppendLine();
            report.AppendLine("[Player 프리팹]");
            allOk &= CheckPlayerPrefab(report);

            // ── 저장 상태 ─────────────────────────────────────────────────
            report.AppendLine();
            if (SceneManager.GetActiveScene().isDirty)
            {
                allOk = false;
                report.AppendLine("⚠ 씬에 저장되지 않은 변경이 있습니다 — Ctrl+S로 저장하세요. " +
                                  "저장하지 않으면 빌드·재생 시 변경이 반영되지 않습니다.");
            }
            else
            {
                report.AppendLine("✔ 씬에 저장되지 않은 변경 없음");
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
        }

        /// <summary>씬의 <typeparamref name="THost"/> 오브젝트에 NetworkObject와 <typeparamref name="TSync"/>가 있는지.</summary>
        private static bool CheckSceneComponent<THost, TSync>(StringBuilder report, string label, string toolName)
            where THost : Component
            where TSync : Component
        {
            var host = Object.FindAnyObjectByType<THost>(FindObjectsInactive.Include);
            if (host == null)
            {
                report.AppendLine($"  ✖ {label}: 씬에서 {typeof(THost).Name}를 찾지 못했습니다(Game 씬이 열려 있는지 확인).");
                return false;
            }

            bool hasNob = host.GetComponent<NetworkObject>() != null;
            bool hasSync = host.GetComponent<TSync>() != null;

            if (hasNob && hasSync)
            {
                report.AppendLine($"  ✔ {label}: '{host.gameObject.name}'에 NetworkObject + {typeof(TSync).Name} 부착됨");
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
                report.AppendLine("  ✖ 밸브(스프린트 10): 씬에서 ValveBehaviour를 찾지 못했습니다.");
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
        /// 스프린트 16 HUD 점검. HUD는 자기 uGUI 계층을 런타임에 만들므로 씬에 이 컴포넌트
        /// 하나만 있으면 되지만, 팔레트 참조가 비어 있으면 색상이 하드코딩 폴백으로 떨어진다.
        /// </summary>
        private static bool CheckHud(StringBuilder report)
        {
            var hud = Object.FindAnyObjectByType<InGameHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                report.AppendLine("  ✖ HUD(스프린트 16): 씬에서 InGameHud를 찾지 못했습니다 " +
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
            return !hudBannerOn;
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
