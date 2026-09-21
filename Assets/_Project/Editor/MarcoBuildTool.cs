using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Marco.EditorTools
{
    /// <summary>
    /// [블록 7-G] 3인 실기 테스트용 Windows 빌드 — 호스트(에디터) 1 + exe 2.
    ///
    /// <para>
    /// 씬 목록은 <c>Scene Flow — 4. Boot·MainMenu + 빌드 설정</c>이 정한 <see cref="EditorBuildSettings.scenes"/>를
    /// 그대로 쓴다(여기서 다시 정하지 않는다 — 두 곳에 적으면 어긋난다). 출력은 프로젝트 루트의
    /// <c>Builds/Windows/MARCO.exe</c>. 같은 exe를 두 번 실행하면 클라이언트 2개가 된다.
    /// </para>
    ///
    /// <para>
    /// 에디터가 프로젝트를 열고 있으면 배치모드(<c>-executeMethod</c>) 빌드는 잠금 때문에 실패한다 —
    /// 그때는 이 메뉴를 쓴다.
    /// </para>
    /// </summary>
    public static class MarcoBuildTool
    {
        private const string OutputPath = "Builds/Windows/MARCO.exe";

        [MenuItem("Tools/MARCO/Build Windows (3인 테스트)", priority = 300)]
        public static void BuildFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("빌드", "재생 모드에서는 빌드할 수 없습니다.", "확인");
                return;
            }

            Build();
        }

        /// <summary>배치모드 진입점: <c>Unity.exe -batchmode -quit -projectPath . -executeMethod Marco.EditorTools.MarcoBuildTool.BuildBatch</c>.</summary>
        public static void BuildBatch()
        {
            bool ok = Build();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Build()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] 빌드 설정에 켜진 씬이 없습니다 — Scene Flow 4를 먼저 실행하세요.");
                return false;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            string sceneList = string.Join(", ", scenes);
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] 성공 — {OutputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}초). " +
                          $"씬: {sceneList}. 3인 테스트: 에디터 Play(호스트) + exe 2개 실행 → 로비 접속.");
                return true;
            }

            Debug.LogError($"[Build] 실패 — 결과 {summary.result}, 오류 {summary.totalErrors}개. 씬: {sceneList}");
            return false;
        }
    }
}
