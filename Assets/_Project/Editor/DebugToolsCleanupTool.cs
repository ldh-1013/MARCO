using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// 스프린트 19: 임시 디버그 도구(<c>NetworkTestBootstrap</c>)의 **씬 잔재**를 제거한다.
    ///
    /// 코드와 <c>Marco.DebugTools</c> 어셈블리는 이 스프린트에서 삭제됐지만, 씬에 남은
    /// GameObject는 자동으로 사라지지 않는다 — 스크립트가 없어진 컴포넌트("Missing script")만
    /// 남아 매번 경고를 낸다. 그 오브젝트를 정상 에디터 API로 지운다.
    ///
    /// **씬 YAML을 직접 편집하지 않는다**(스프린트 8~18b에서 확립한 원칙) — <see cref="Undo"/>로
    /// 지우고 씬을 저장하면 Unity가 참조를 정리한다.
    ///
    /// **이름으로 찾는 이유**: 타입(<c>NetworkTestBootstrap</c>)이 이미 삭제돼 이 어셈블리에서
    /// 참조할 수 없다. 대신 이름이 일치하고 <b>Transform + 사라진 스크립트</b>만 가진 루트
    /// 오브젝트인지 확인해, 다른 것이 붙어 있으면 지우지 않고 보고만 한다(안전장치).
    ///
    /// 멱등: 이미 지워졌으면 아무것도 하지 않는다.
    /// </summary>
    public static class DebugToolsCleanupTool
    {
        private const string BootstrapObjectName = "NetworkTestBootstrap";

        [MenuItem("Tools/MARCO/Cleanup Debug Tools (씬 잔재 제거)", priority = 206)]
        public static void Run()
        {
            int removed = 0;
            var skipped = new List<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name != BootstrapObjectName)
                        continue;

                    if (!IsSafeToRemove(root, out string reason))
                    {
                        skipped.Add($"'{root.name}'({scene.name} 씬): {reason}");
                        continue;
                    }

                    Undo.DestroyObjectImmediate(root);
                    EditorSceneManager.MarkSceneDirty(scene);
                    removed++;
                    Debug.Log($"[DebugCleanup] '{BootstrapObjectName}' 오브젝트를 {scene.name} 씬에서 제거했습니다.");
                }
            }

            foreach (string s in skipped)
                Debug.LogWarning($"[DebugCleanup] 건너뜀 — {s}");

            if (removed == 0 && skipped.Count == 0)
            {
                Debug.Log("[DebugCleanup] 제거할 디버그 도구 잔재가 없습니다(멱등).");
                return;
            }

            Debug.Log($"[DebugCleanup] 완료 — {removed}개 제거. **씬을 저장(Ctrl+S)**하세요. " +
                      "접속은 이제 MainMenu(H/J)와 로비 화면이 담당합니다(스프린트 18).");
        }

        /// <summary>Transform과 사라진 스크립트 외의 것이 붙어 있으면 지우지 않는다.</summary>
        private static bool IsSafeToRemove(GameObject go, out string reason)
        {
            if (go.transform.childCount > 0)
            {
                reason = "자식 오브젝트가 있어 수동 확인이 필요합니다.";
                return false;
            }

            foreach (Component component in go.GetComponents<Component>())
            {
                // 스크립트가 삭제된 컴포넌트는 null로 잡힌다 — 이번에 지운 부트스트랩이 그것이다.
                if (component == null || component is Transform)
                    continue;

                reason = $"예상치 못한 컴포넌트({component.GetType().Name})가 있어 수동 확인이 필요합니다.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
