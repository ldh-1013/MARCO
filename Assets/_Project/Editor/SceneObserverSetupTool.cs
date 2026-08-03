using System.Collections.Generic;
using FishNet.Component.Observing;
using FishNet.Object;
using FishNet.Observing;
using Marco.Presentation.Objectives;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.EditorTools
{
    /// <summary>
    /// 맵 씬의 NetworkObject에 <see cref="SceneCondition"/>을 붙인다(스프린트 25 후속 실기 버그).
    ///
    /// **확정된 원인**(클라이언트 `Player.log`):
    /// <code>
    /// SceneId of 3945420526 not found in SceneObjects. …
    /// or if networked scene objects do not have a SceneCondition.
    /// </code>
    /// 이 세 SceneId는 밸브 3개의 것이고, 로그 순서상 **맵 로드가 끝나기 전에** 스폰 메시지가
    /// 도착했다. 맵이 애디티브로 로드되는 §15.1 구조에서는 서버가 자기 씬 로드 직후 씬
    /// NetworkObject를 스폰하는데(<c>ServerObjects.SetupSceneObjects</c>), 관측 조건이 없으면
    /// **아직 그 씬을 로드하지 않은 클라이언트에게도 즉시 전송**된다. 클라이언트는 해당 SceneId를
    /// 모르므로 그 스폰을 버리고, 이후 다시 받지 못해 밸브가 영영 보이지 않는다.
    ///
    /// <see cref="SceneCondition"/>은 <c>connection.Scenes.Contains(오브젝트의 씬)</c>일 때만
    /// 관측자로 인정한다 — 클라이언트가 씬 로드를 마쳐 <c>AddConnectionToScene</c>이 실행된
    /// **뒤에** 스폰이 나가므로 이 경쟁이 사라진다.
    ///
    /// **ObserverManager 전역 기본 조건을 쓰지 않는 이유**: 그러면 플레이어 pawn에도 적용된다.
    /// pawn은 로비/맵 사이를 오가며 씬이 바뀌므로 전환 순간 서로 안 보일 위험이 있다. 문제가
    /// 확인된 **맵 씬 오브젝트에만** 붙이는 것이 최소 수정이다.
    ///
    /// 멱등: 이미 조건이 붙어 있으면 건너뛴다.
    /// </summary>
    public static class SceneObserverSetupTool
    {
        private const string ConditionAssetPath = "Assets/_Project/Settings/SceneCondition.asset";

        [MenuItem("Tools/MARCO/Setup Scene Observers (맵 씬 오브젝트)", priority = 207)]
        public static void Run()
        {
            SceneCondition condition = LoadOrCreateCondition();
            if (condition == null)
                return;

            var valves = Object.FindObjectsByType<ValveBehaviour>(FindObjectsInactive.Include);
            if (valves.Length == 0)
            {
                if (!MarcoSetupPipeline.Automated)
                    EditorUtility.DisplayDialog("씬 관측 조건 구성",
                        "열린 씬에서 밸브를 찾지 못했습니다. 맵(Game) 씬을 열고 다시 실행하세요.", "확인");
                else
                    Debug.LogWarning("[SceneObserver] 밸브를 찾지 못해 건너뜁니다(맵 씬 미로드).");

                return;
            }

            int changed = 0;
            foreach (ValveBehaviour valve in valves)
            {
                if (ApplyCondition(valve.gameObject, condition))
                    changed++;
            }

            if (changed > 0)
                EditorSceneManager.MarkSceneDirty(valves[0].gameObject.scene);

            Debug.Log($"[SceneObserver] 완료 — 밸브 {valves.Length}개 중 {changed}개에 SceneCondition 부착. " +
                      (changed > 0
                          ? "**맵 씬을 저장(Ctrl+S)**하세요. 이제 클라이언트가 맵 로드를 마친 뒤에 밸브가 스폰됩니다."
                          : "이미 전부 구성돼 있습니다(멱등)."));
        }

        /// <summary>대상 오브젝트에 NetworkObserver + SceneCondition을 보장한다. 변경이 있었으면 true.</summary>
        private static bool ApplyCondition(GameObject go, SceneCondition condition)
        {
            if (go.GetComponent<NetworkObject>() == null)
                return false; // 네트워크 오브젝트가 아니면 관측 조건이 의미 없다.

            var observer = go.GetComponent<NetworkObserver>();
            if (observer == null)
                observer = Undo.AddComponent<NetworkObserver>(go);

            var so = new SerializedObject(observer);
            SerializedProperty conditions = so.FindProperty("_observerConditions");
            if (conditions == null)
            {
                Debug.LogError("[SceneObserver] NetworkObserver의 _observerConditions를 찾지 못했습니다 " +
                               "— FishNet 버전이 바뀐 것일 수 있습니다. 변경 없음.");
                return false;
            }

            for (int i = 0; i < conditions.arraySize; i++)
            {
                if (conditions.GetArrayElementAtIndex(i).objectReferenceValue == condition)
                    return false; // 이미 있음
            }

            conditions.InsertArrayElementAtIndex(conditions.arraySize);
            conditions.GetArrayElementAtIndex(conditions.arraySize - 1).objectReferenceValue = condition;

            // 매니저 기본 조건과 섞이지 않게 이 오브젝트의 조건만 쓴다(기본값 그대로).
            SerializedProperty overrideType = so.FindProperty("_overrideType");
            if (overrideType != null)
                overrideType.enumValueIndex = (int)NetworkObserver.ConditionOverrideType.IgnoreManager;

            so.ApplyModifiedProperties();
            Debug.Log($"[SceneObserver] '{go.name}'에 NetworkObserver + SceneCondition 부착");
            return true;
        }

        /// <summary>공용 SceneCondition 에셋을 읽거나 만든다(밸브 3개가 같은 인스턴스를 공유한다).</summary>
        private static SceneCondition LoadOrCreateCondition()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneCondition>(ConditionAssetPath);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Settings"))
                AssetDatabase.CreateFolder("Assets/_Project", "Settings");

            var created = ScriptableObject.CreateInstance<SceneCondition>();
            AssetDatabase.CreateAsset(created, ConditionAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneObserver] {ConditionAssetPath} 생성");
            return AssetDatabase.LoadAssetAtPath<SceneCondition>(ConditionAssetPath);
        }
    }
}
