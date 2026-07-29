using UnityEngine;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// §15.1 첫 씬(Boot)의 최소 진행자(스프린트 18b).
    /// §15.4 Boot 행: "마이크 권한 확인 → (완료) → MainMenu".
    ///
    /// 마이크 권한 확인은 음성 파이프라인(§5.2) 스코프라 아직 없다 — 지금은 한 프레임 뒤
    /// 메인 메뉴로 넘기는 자리표시자이며, 권한 처리가 생기면 그 완료 콜백에서
    /// <see cref="Advance"/>를 부르도록 바꾸면 된다(구조를 미리 맞춰 둔 것).
    /// </summary>
    public sealed class BootFlow : MonoBehaviour
    {
        [SerializeField] private string _mainMenuSceneName = "MainMenu";

        [Tooltip("자동 진행 지연(초). 0이면 다음 프레임에 넘어간다.")]
        [SerializeField, Range(0f, 3f)] private float _delaySeconds;

        private float _elapsed;
        private bool _advanced;

        private void Update()
        {
            if (_advanced)
                return;

            _elapsed += Time.deltaTime;
            if (_elapsed >= _delaySeconds)
                Advance();
        }

        /// <summary>메인 메뉴로 진행한다(§15.4 Boot 종료 조건 = 권한 처리 완료).</summary>
        public void Advance()
        {
            if (_advanced)
                return;

            _advanced = true;
            Debug.Log("[Boot] 초기화 완료 — 메인 메뉴로 진행(§15.1/§15.4)");
            UnityEngine.SceneManagement.SceneManager.LoadScene(_mainMenuSceneName,
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
