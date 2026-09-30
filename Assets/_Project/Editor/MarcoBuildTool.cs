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
    ///
    /// <para>
    /// <b>QA 빌드다</b> — <see cref="QaBuildDefine"/>를 넣어 QA 전용 코드(<c>Marco.Presentation.QA.QaDebugOverlay</c>)를
    /// 포함한다. 심볼은 <see cref="BuildPlayerOptions.extraScriptingDefines"/>로 <b>이 빌드에만</b> 넘긴다 —
    /// PlayerSettings에 저장하지 않으므로 에디터 Play와 다른 빌드 경로(향후 릴리즈/스팀 빌드)에는 새지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>릴리스 빌드(09-30)</b> — <see cref="BuildReleaseFromMenu"/> · <see cref="BuildReleaseBatch"/>는 같은 씬 목록을
    /// <see cref="QaBuildDefine"/> <b>없이</b> <c>Builds/Release/MARCO.exe</c>에 만든다(친구에게 보내는 판). QA 오버레이 · F2~F7은
    /// 컴파일 단계에서 빠진다.
    /// </para>
    /// </summary>
    public static class MarcoBuildTool
    {
        private const string OutputPath = "Builds/Windows/MARCO.exe";

        /// <summary>릴리스 빌드 출력(09-30) — QA 빌드와 폴더를 나눠 섞이지 않게 한다.</summary>
        public const string ReleaseOutputPath = "Builds/Release/MARCO.exe";

        /// <summary>QA 전용 코드를 켜는 스크립팅 정의 심볼. 이 도구 밖에서 정의하지 않는다.</summary>
        public const string QaBuildDefine = "MARCO_QA_BUILD";

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

        [MenuItem("Tools/MARCO/Build Windows Release (배포용 — QA 제외)", priority = 301)]
        public static void BuildReleaseFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("빌드", "재생 모드에서는 빌드할 수 없습니다.", "확인");
                return;
            }

            BuildRelease();
        }

        /// <summary>배치모드 진입점(릴리스): <c>-executeMethod Marco.EditorTools.MarcoBuildTool.BuildReleaseBatch</c>.</summary>
        public static void BuildReleaseBatch()
        {
            bool ok = BuildRelease();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Build() => Build(OutputPath, new[] { QaBuildDefine },
            $"3인 테스트: 에디터 Play(호스트) + exe 2개 실행 → 로비 접속. " +
            $"{QaBuildDefine} 포함 — exe에서 F3 QA 오버레이 · F2 QA 라이트 · F4 QA 순간이동 · F5 QA 음성 파문 · F6/F7 음성 조명 배율.");

        private static bool BuildRelease() => Build(ReleaseOutputPath, new string[0],
            $"릴리스 — {QaBuildDefine} 없음(QA 오버레이 · F2~F7 제외). 보낼 것: Builds/Release 폴더 전체(단 *_BurstDebugInformation_DoNotShip 폴더는 빼고).");

        private static bool Build(string outputPath, string[] extraDefines, string note)
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
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = extraDefines,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            string sceneList = string.Join(", ", scenes);
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] 성공 — {outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0}초). " +
                          $"씬: {sceneList}. {note}");
                return true;
            }

            Debug.LogError($"[Build] 실패 — 결과 {summary.result}, 오류 {summary.totalErrors}개. 씬: {sceneList}");
            return false;
        }
    }
}
