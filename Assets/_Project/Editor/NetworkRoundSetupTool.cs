using FishNet.Object;
using Marco.Net;
using Marco.Presentation.GameFlow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// 스프린트 12: 씬의 라운드 지휘부(<see cref="RoundCoordinator"/>) 오브젝트를 서버 권위
    /// 라운드 동기화 대상으로 만든다. 같은 오브젝트에 <see cref="NetworkObject"/> +
    /// <see cref="RoundNetworkSync"/>를 정상 UnityEditor API로 부착한다.
    ///
    /// <see cref="RoundCoordinator"/>는 <c>GetComponent&lt;IRoundNetworkBridge&gt;()</c>로
    /// 같은 오브젝트의 이 브릿지를 찾으므로, 반드시 <b>같은 GameObject</b>에 붙여야 한다
    /// (밸브의 <c>ValveBehaviour</c>+<c>ValveNetworkSync</c>와 같은 배치 규칙).
    ///
    /// **원칙(스프린트 8~11과 동일)**: <see cref="NetworkObject"/>의 SceneId 등 에디터 생성값은
    /// 손으로 채우지 않는다. <c>Undo.AddComponent</c>로 부착하고 씬을 저장하면 FishNet의
    /// 에디터 코드가 스스로 채운다. 그래서 씬 YAML 직접 편집이 아니라 이 메뉴로 실행해야 한다.
    ///
    /// 멱등: 이미 NetworkObject/RoundNetworkSync가 있으면 건너뛴다.
    /// </summary>
    public static class NetworkRoundSetupTool
    {
        [MenuItem("Tools/MARCO/Setup Network Round")]
        public static void Run()
        {
            var coordinator = Object.FindAnyObjectByType<RoundCoordinator>(FindObjectsInactive.Include);
            if (coordinator == null)
            {
                EditorUtility.DisplayDialog("네트워크 라운드 구성",
                    "씬에서 RoundCoordinator를 찾지 못했습니다. Game 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            GameObject go = coordinator.gameObject;

            bool proceed = EditorUtility.DisplayDialog(
                "네트워크 라운드 구성 (스프린트 12)",
                $"'{go.name}' 오브젝트(RoundCoordinator)에 NetworkObject + RoundNetworkSync를 부착합니다.\n\n" +
                "SceneId 등은 FishNet이 자동 생성하므로, 실행 후 반드시 씬을 저장(Ctrl+S)하고 " +
                "Fish-Networking → Utility → Reserialize NetworkObjects를 실행하세요.\n" +
                "씬 변경은 Ctrl+Z로 되돌릴 수 있지만 작업 전 Assets/Scenes/Game.unity 백업을 권장합니다.\n\n계속하시겠습니까?",
                "계속", "취소");

            if (!proceed)
            {
                Debug.Log("[NetworkRoundSetupTool] 사용자가 취소했습니다. 변경 없음.");
                return;
            }

            Undo.SetCurrentGroupName("Setup Network Round");
            int group = Undo.GetCurrentGroup();

            int changed = 0;

            if (go.GetComponent<NetworkObject>() == null)
            {
                // NetworkObject를 먼저 붙여야 RoundNetworkSync(NetworkBehaviour)가 성립한다.
                Undo.AddComponent<NetworkObject>(go);
                Debug.Log($"[NetworkRoundSetupTool] {go.name}에 NetworkObject 부착");
                changed++;
            }

            if (go.GetComponent<RoundNetworkSync>() == null)
            {
                Undo.AddComponent<RoundNetworkSync>(go);
                Debug.Log($"[NetworkRoundSetupTool] {go.name}에 RoundNetworkSync 부착");
                changed++;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log(
                $"[NetworkRoundSetupTool] 완료 — '{go.name}' 처리(변경 {changed}건). 다음을 확인하세요:\n" +
                "  1) 해당 오브젝트에 Network Object / Round Network Sync가 붙었는지\n" +
                "  2) NetworkObject의 SceneId가 0이 아닌 값으로 채워졌는지(씬 저장 후 자동 생성)\n" +
                "  3) 씬 저장(Ctrl+S) 후 Fish-Networking → Utility → Reserialize NetworkObjects 실행\n" +
                "  4) docs/수동검증_절차.md §14 절차로 H/J 2-클라이언트 라운드 결과 일치를 검증하세요");
        }
    }
}
