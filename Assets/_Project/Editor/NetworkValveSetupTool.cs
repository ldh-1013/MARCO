using FishNet.Object;
using Marco.Net;
using Marco.Presentation.Objectives;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// 스프린트 10: 씬의 밸브(ValveBehaviour)를 서버 권위 동기화 대상으로 만든다.
    /// 각 밸브 오브젝트에 <see cref="NetworkObject"/> + <see cref="ValveNetworkSync"/>를
    /// 정상 UnityEditor API로 부착한다.
    ///
    /// **원칙(스프린트 8·9와 동일)**: <see cref="NetworkObject"/>의 SceneId·AssetPathHash 등
    /// 에디터가 생성하는 값은 손으로 채우지 않는다. <c>Undo.AddComponent</c>로 부착하고
    /// 씬을 저장하면 FishNet의 에디터 코드가 스스로 채운다. 그래서 이 작업은 씬 YAML을
    /// 직접 편집하는 대신 반드시 에디터에서 이 메뉴로 실행해야 한다.
    ///
    /// 멱등: 이미 NetworkObject/ValveNetworkSync가 있는 밸브는 건너뛴다.
    /// </summary>
    public static class NetworkValveSetupTool
    {
        [MenuItem("Tools/MARCO/Setup Network Valves")]
        public static void Run()
        {
            var valves = Object.FindObjectsByType<ValveBehaviour>(FindObjectsInactive.Include);
            if (valves.Length == 0)
            {
                EditorUtility.DisplayDialog("네트워크 밸브 구성",
                    "씬에서 ValveBehaviour를 찾지 못했습니다. Game 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            // 전체 셋업 파이프라인이 부를 때는 확인을 건너뛴다(사람 개입 없이 도는 것이 목적).
            // 개별 메뉴 실행 시에는 Automated가 항상 false라 기존 동작 그대로다.
            if (!MarcoSetupPipeline.Automated)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    "네트워크 밸브 구성 (스프린트 10)",
                    $"씬의 밸브 {valves.Length}개에 NetworkObject + ValveNetworkSync를 부착합니다.\n\n" +
                    "SceneId 등은 FishNet이 자동 생성하므로, 실행 후 반드시 씬을 저장(Ctrl+S)하세요. " +
                    "씬 변경은 Ctrl+Z로 되돌릴 수 있지만 작업 전 Assets/Scenes/Game.unity 백업을 권장합니다.\n\n계속하시겠습니까?",
                    "계속", "취소");

                if (!proceed)
                {
                    Debug.Log("[NetworkValveSetupTool] 사용자가 취소했습니다. 변경 없음.");
                    return;
                }
            }

            Undo.SetCurrentGroupName("Setup Network Valves");
            int group = Undo.GetCurrentGroup();

            int changed = 0;
            foreach (ValveBehaviour valve in valves) // 각 밸브 오브젝트에 네트워크 컴포넌트 부착
            {
                GameObject go = valve.gameObject;

                if (go.GetComponent<NetworkObject>() == null)
                {
                    // NetworkObject를 먼저 붙여야 ValveNetworkSync(NetworkBehaviour)가 성립한다.
                    Undo.AddComponent<NetworkObject>(go);
                    Debug.Log($"[NetworkValveSetupTool] {go.name}에 NetworkObject 부착");
                    changed++;
                }

                if (go.GetComponent<ValveNetworkSync>() == null)
                {
                    Undo.AddComponent<ValveNetworkSync>(go);
                    Debug.Log($"[NetworkValveSetupTool] {go.name}에 ValveNetworkSync 부착");
                    changed++;
                }
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log(
                $"[NetworkValveSetupTool] 완료 — 밸브 {valves.Length}개 처리(변경 {changed}건). 다음을 확인하세요:\n" +
                "  1) 각 Valve_* 오브젝트에 Network Object / Valve Network Sync가 붙었는지\n" +
                "  2) NetworkObject의 SceneId가 0이 아닌 값으로 채워졌는지(씬 저장 후 자동 생성)\n" +
                "  3) 씬 저장(Ctrl+S) — 이 도구는 씬을 dirty로 표시만 합니다\n" +
                "  4) docs/수동검증_절차.md §11 절차로 H/J 2-클라이언트 밸브 동기화를 검증하세요");
        }
    }
}
