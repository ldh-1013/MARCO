using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.EditorTools
{
    /// <summary>
    /// 그레이박스 머티리얼의 셰이더를 URP용으로 되돌린다(스프린트 18b 후속 렌더링 버그).
    ///
    /// **확정된 원인**: 프로젝트는 URP(<c>Assets/Settings/PC_RPAsset.asset</c>)로 렌더링하는데,
    /// <c>Assets/_Project/Maps/Graybox/*.mat</c> 4개가 **빌트인 Standard 셰이더**
    /// (guid <c>933532a4fcc9baf4fa0491de14d08ed7</c>)를 가리키고 있었다. URP는 빌트인 셰이더를
    /// 지원하지 않으므로 해당 표면이 정상 조명을 받지 못한다.
    ///
    /// 이 머티리얼들은 원래 URP/Lit으로 작성된 것이 확실하다 — <c>_BaseColor</c>·<c>_Smoothness</c>·
    /// <c>_Surface</c> 같은 **URP 전용 속성값이 그대로 남아 있다**(§16.1 팔레트 색도 보존됨).
    /// 그래서 셰이더만 URP/Lit으로 되돌리면 색·매끄러움이 함께 복원되며, 값을 새로 쓸 필요가 없다.
    ///
    /// 멱등: 이미 URP 셰이더인 머티리얼은 건너뛴다.
    /// </summary>
    public static class GrayboxMaterialFixTool
    {
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        [MenuItem("Tools/MARCO/Fix Graybox Materials (URP 셰이더 복원)", priority = 205)]
        public static void Run()
        {
            Shader urpLit = Shader.Find(UrpLitShader);
            if (urpLit == null)
            {
                Debug.LogError($"[MaterialFix] '{UrpLitShader}' 셰이더를 찾지 못했습니다 — " +
                               "URP 패키지가 설치돼 있는지 확인하세요. 변경 없음.");
                return;
            }

            if (GraphicsSettings.currentRenderPipeline == null)
            {
                // 빌트인 파이프라인이라면 오히려 지금 셰이더가 맞다 — 잘못 고치지 않도록 멈춘다.
                Debug.LogWarning("[MaterialFix] 현재 렌더 파이프라인이 빌트인입니다 — " +
                                 "URP 셰이더로 바꾸면 오히려 깨집니다. 변경 없음.");
                return;
            }

            List<Material> broken = FindBuiltInMaterials();
            if (broken.Count == 0)
            {
                Debug.Log("[MaterialFix] 빌트인 셰이더를 쓰는 머티리얼이 없습니다 — 정리할 것이 없습니다(멱등).");
                return;
            }

            foreach (Material mat in broken)
            {
                string before = mat.shader.name;
                Undo.RecordObject(mat, "Fix material shader for URP");
                mat.shader = urpLit;
                EditorUtility.SetDirty(mat);

                // 셰이더가 실제로 바뀌었는지 확인해서 로그로 남긴다 — "고쳤다고 보고했는데
                // 실기에서 그대로"였던 이번 사례를 다시 만들지 않기 위한 확인이다.
                string color = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "(속성 없음)";
                Debug.Log($"[MaterialFix] '{mat.name}': {before} → {mat.shader.name} / _BaseColor {color}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[MaterialFix] 완료 — 머티리얼 {broken.Count}개 복원. " +
                      "Play로 그레이박스 바닥·벽이 정상 조명을 받는지 확인하세요(docs/수동검증_절차.md §20-6b).");
        }

        /// <summary>
        /// 빌트인(비 URP) 셰이더를 쓰는 프로젝트 머티리얼을 모은다(읽기 전용).
        ///
        /// **셰이더 이름으로 판정한다** — GUID 비교는 쓰지 않는다. 머티리얼 YAML에는 Standard의
        /// 원본 GUID(<c>933532a4…</c>)가 적혀 있지만, 로드된 <see cref="Shader"/> 객체의
        /// <c>AssetDatabase.GetAssetPath</c>는 빌트인 번들 경로(<c>Resources/unity_builtin_extra</c>)를
        /// 돌려주고 그 GUID는 <c>0000…f000…</c>이라 **원본 GUID와 절대 일치하지 않는다**.
        /// 첫 구현이 이 비교를 써서 대상이 0개로 나왔고, 진단까지 ✔로 잘못 보고했다.
        /// </summary>
        internal static List<Material> FindBuiltInMaterials()
        {
            var result = new List<Material>();

            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null)
                    continue;

                if (IsBuiltInShader(mat.shader.name))
                    result.Add(mat);
            }

            return result;
        }

        /// <summary>URP가 렌더링하지 못하는 빌트인 파이프라인 전용 셰이더인가.</summary>
        internal static bool IsBuiltInShader(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
                return false;

            return shaderName == "Standard"
                   || shaderName == "Standard (Specular setup)"
                   || shaderName == "Autodesk Interactive"
                   || shaderName.StartsWith("Legacy Shaders/")
                   || shaderName.StartsWith("Mobile/");
        }
    }
}
