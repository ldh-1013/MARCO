using FishNet.Object;
using Marco.Net;
using Marco.Presentation.Sound;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// 스프린트 14: 씬의 파문 시스템 오브젝트(<see cref="LocalPulsePipelineBehaviour"/>)를 서버 권위
    /// 파문 판정 대상으로 만든다. 같은 오브젝트에 <see cref="NetworkObject"/> +
    /// <see cref="PulseNetworkSync"/>를 정상 UnityEditor API로 부착한다.
    ///
    /// <see cref="LocalPulsePipelineBehaviour"/>가 <c>GetComponent&lt;IPulseNetworkBridge&gt;()</c>로
    /// 같은 오브젝트의 브릿지를 찾으므로 반드시 <b>같은 GameObject</b>에 붙여야 한다
    /// (밸브·라운드와 같은 배치 규칙).
    ///
    /// **원칙(스프린트 8~13과 동일)**: <see cref="NetworkObject"/>의 SceneId 등 에디터 생성값은
    /// 손으로 채우지 않는다. <c>Undo.AddComponent</c>로 부착하고 씬을 저장하면 FishNet의
    /// 에디터 코드가 스스로 채운다.
    ///
    /// 멱등: 이미 붙어 있으면 건너뛴다.
    /// </summary>
    public static class NetworkPulseSetupTool
    {
        [MenuItem("Tools/MARCO/Setup Network Pulse")]
        public static void Run()
        {
            var pipeline = Object.FindAnyObjectByType<LocalPulsePipelineBehaviour>(FindObjectsInactive.Include);
            if (pipeline == null)
            {
                EditorUtility.DisplayDialog("네트워크 파문 구성",
                    "씬에서 LocalPulsePipelineBehaviour(PulseSystem)를 찾지 못했습니다. Game 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            GameObject go = pipeline.gameObject;

            // 전체 셋업 파이프라인이 부를 때는 확인을 건너뛴다(개별 메뉴 실행 시에는 기존 동작 그대로).
            bool proceed = MarcoSetupPipeline.Automated || EditorUtility.DisplayDialog(
                "네트워크 파문 구성 (스프린트 14)",
                $"'{go.name}' 오브젝트에 NetworkObject + PulseNetworkSync를 부착합니다.\n\n" +
                "SceneId 등은 FishNet이 자동 생성하므로, 실행 후 반드시 씬을 저장(Ctrl+S)하고 " +
                "Fish-Networking → Utility → Reserialize NetworkObjects를 실행하세요.\n" +
                "작업 전 Assets/Scenes/Game.unity 백업을 권장합니다.\n\n계속하시겠습니까?",
                "계속", "취소");

            if (!proceed)
            {
                Debug.Log("[NetworkPulseSetupTool] 사용자가 취소했습니다. 변경 없음.");
                return;
            }

            Undo.SetCurrentGroupName("Setup Network Pulse");
            int group = Undo.GetCurrentGroup();

            int changed = 0;

            if (go.GetComponent<NetworkObject>() == null)
            {
                // NetworkObject를 먼저 붙여야 PulseNetworkSync(NetworkBehaviour)가 성립한다.
                Undo.AddComponent<NetworkObject>(go);
                Debug.Log($"[NetworkPulseSetupTool] {go.name}에 NetworkObject 부착");
                changed++;
            }

            if (go.GetComponent<PulseNetworkSync>() == null)
            {
                Undo.AddComponent<PulseNetworkSync>(go);
                Debug.Log($"[NetworkPulseSetupTool] {go.name}에 PulseNetworkSync 부착");
                changed++;
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log(
                $"[NetworkPulseSetupTool] 완료 — '{go.name}' 처리(변경 {changed}건). 다음을 확인하세요:\n" +
                "  1) 해당 오브젝트에 Network Object / Pulse Network Sync가 붙었는지\n" +
                "  2) NetworkObject의 SceneId가 0이 아닌 값으로 채워졌는지(씬 저장 후 자동 생성)\n" +
                "  3) 씬 저장(Ctrl+S) 후 Fish-Networking → Utility → Reserialize NetworkObjects 실행\n" +
                "  4) docs/수동검증_절차.md §16 절차로 H/J 2-클라이언트 파문 전달을 검증하세요");
        }
    }
}
