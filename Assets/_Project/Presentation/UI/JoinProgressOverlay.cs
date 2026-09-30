using Marco.Core.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// 접속 중 화면 · 메뉴 복귀(09-30 실기 결함 수정).
    ///
    /// <para>
    /// <b>왜 따로 있는가</b>: 로비 화면(<see cref="LobbyScreen"/>)은 NetworkObject가 붙은 PulseSystem에 있다. FishNet은 로비 씬이 시작될 때
    /// (아직 연결 전) 씬 NetworkObject를 끄고(<c>NetworkObject.TryStartDeactivation</c>) 서버가 스폰해 줄 때만 다시 켠다 — 그래서 참가가
    /// 실패하면 로비 화면 전체가 꺼진 채 빈 월드만 남았고, 실패 안내 · Esc 취소 · 메뉴 복귀가 모두 없었다(09-30 로그: 처음부터 끝까지
    /// <c>PulseSystem=꺼짐</c>). 이 화면은 NetworkObject가 없는 SceneFlow에 <see cref="GameFlow.LobbyEntry"/>가 붙인다.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>시도 중: "127.0.0.1:7999에 접속 중… (N초)" · "Esc — 취소"(설정 창이 열려 있으면 Esc는 설정 창 몫). Esc는
    ///   <see cref="CancelKeyReader"/>가 Input System 키와 OS 키 이벤트 두 경로로 읽는다(10-01 — 스캔코드 없는 Esc가 무시됐다).</item>
    /// <item>실패(Tugboat 포기 · 10초 초과 · 연결 뒤 끊김) · 취소: 네트워크를 정리하고(<see cref="IConnectionService.StopAll"/>) 메인 메뉴로.
    ///   메뉴에 빨간 원인 안내(취소는 회색)를 띄우고 J에 직전 주소를 채운다(<see cref="MainMenuScreen.PrepareReturnFromLobby"/>).</item>
    /// <item>연결됨: 숨는다 — 로비 화면이 이어받는다.</item>
    /// </list>
    /// </summary>
    public sealed class JoinProgressOverlay : MonoBehaviour
    {
        [Tooltip("실패 · 취소 뒤 돌아갈 메인 메뉴 씬 이름(빌드 설정).")]
        [SerializeField] private string _menuSceneName = "MainMenu";

        [SerializeField] private Key _cancelKey = Key.Escape;

        [Tooltip("OS 키 이벤트(가상 키 코드)로 읽을 취소 키 — _cancelKey와 같은 키여야 한다.")]
        [SerializeField] private KeyCode _cancelKeyCode = KeyCode.Escape;

        [SerializeField, Range(12, 48)] private int _fontSize = 28;

        private GameObject _root;
        private Text _progressText;
        private Text _hintText;
        private bool _returning;
        private CancelKeyReader _cancelInput;

        /// <summary>화면이 보이는가(시도 중).</summary>
        public bool IsShowing => _root != null && _root.activeSelf;

        public string ProgressText => _progressText != null ? _progressText.text : string.Empty;

        public string HintText => _hintText != null ? _hintText.text : string.Empty;

        /// <summary>메뉴로 돌아가기 시작했는가(한 번만).</summary>
        public bool ReturnedToMenu => _returning;

        private void Awake()
        {
            _cancelInput = new CancelKeyReader(_cancelKey, _cancelKeyCode);
            useGUILayout = false; // OnGUI는 키 이벤트만 읽는다 — 레이아웃 패스가 필요 없다
            BuildUi();
            SetShowing(false);
        }

        private void Update()
        {
            IConnectionService connection = ConnectionServiceRegistry.Current;
            JoinAttempt attempt = connection?.Attempt;
            if (_returning || attempt == null)
            {
                _cancelInput.Clear();
                SetShowing(false);
                return;
            }

            switch (attempt.State)
            {
                case JoinAttemptState.Connecting:
                    SetShowing(true);
                    _progressText.text = attempt.ProgressLine(Time.realtimeSinceStartup);
                    _hintText.text = JoinAttempt.CancelHint;

                    CancelKeySource pressed = _cancelInput.Read(Keyboard.current, SettingsScreen.IsAnyOpen);
                    if (pressed != CancelKeySource.None)
                    {
                        Debug.Log($"[JoinOverlay] 취소 키 감지 — {CancelKeyReader.Describe(pressed)} · " +
                                  $"시도 후 {attempt.Elapsed(Time.realtimeSinceStartup):0.0}초 → 접속 취소");
                        connection.Cancel();
                        if (attempt.State == JoinAttemptState.Connecting)
                            Debug.LogWarning("[JoinOverlay] 취소를 불렀지만 아직 접속 중 — [Connection] 줄에서 이유를 확인");
                    }
                    break;

                case JoinAttemptState.Failed:
                case JoinAttemptState.Cancelled:
                    ReturnToMenu(connection, attempt);
                    break;

                default:
                    _cancelInput.Clear();
                    SetShowing(false);
                    break;
            }
        }

        /// <summary>OS 키 이벤트 — 가상 키 코드만 담긴 Esc도 여기로 온다(Input System 키 상태에는 안 잡힌다).</summary>
        private void OnGUI()
        {
            ObserveKeyEvent(Event.current);
        }

        /// <summary>
        /// OS 키 이벤트를 취소 키 읽기에 넘긴다(<see cref="OnGUI"/>). 접속 중일 때만 — 그 밖의 때 눌린 키는 쌓아 두지 않는다.
        /// </summary>
        public void ObserveKeyEvent(Event e)
        {
            if (!_returning && ConnectionServiceRegistry.Current?.Attempt?.State == JoinAttemptState.Connecting)
                _cancelInput.Observe(e);
        }

        /// <summary>진단(10-01): 접속 중에 창 포커스가 바뀌면 남긴다 — 포커스가 없으면 키 입력이 이 창에 오지 않는다.</summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            JoinAttempt attempt = ConnectionServiceRegistry.Current?.Attempt;
            if (_returning || attempt == null || attempt.State != JoinAttemptState.Connecting)
                return;

            Debug.Log($"[JoinOverlay] 접속 중 창 포커스 {(hasFocus ? "얻음" : "잃음 — 키 입력이 이 창에 오지 않는다")} · " +
                      $"시도 후 {attempt.Elapsed(Time.realtimeSinceStartup):0.0}초");
        }

        /// <summary>네트워크를 정리하고 메인 메뉴로 — 안내 · 직전 주소를 넘긴다. 한 번만.</summary>
        private void ReturnToMenu(IConnectionService connection, JoinAttempt attempt)
        {
            _returning = true;
            SetShowing(false);

            attempt.GetMenuNotice(out string notice, out bool isError);
            connection.StopAll(); // 실패 · 취소 뒤 전송 계층이 반쯤 남아 있으면 다시 H/J가 되지 않는다
            MainMenuScreen.PrepareReturnFromLobby(notice, isError, attempt.RetryText);

            Debug.Log($"[JoinOverlay] 메인 메뉴로 복귀 — {attempt.State}{(attempt.TimedOut ? "(시간 초과)" : string.Empty)} · " +
                      $"안내 \"{notice}\" · 다시 입력할 주소 {attempt.RetryText ?? "-"}");

            // EditMode 테스트에서는 씬을 바꾸지 않는다(재생 중에만).
            if (Application.isPlaying)
                SceneManager.LoadScene(_menuSceneName, LoadSceneMode.Single);
        }

        private void SetShowing(bool showing)
        {
            if (_root != null && _root.activeSelf != showing)
                _root.SetActive(showing);
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("JoinProgress Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250; // 로비(150) · 결과(200) 위, 메인 메뉴(300) 아래

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("JoinProgress Root");
            _root.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // 뒤의 빈 월드가 비치지 않게 거의 검게 덮는다 — 아직 아무것도 할 수 없는 화면이다.
            var dim = _root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.9f);
            dim.raycastTarget = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", _fontSize);

            _progressText = CreateText(font, "Progress", _fontSize, new Vector2(0.5f, 0.54f), new Color(0.91f, 0.91f, 0.91f));
            _hintText = CreateText(font, "Hint", Mathf.RoundToInt(_fontSize * 0.8f), new Vector2(0.5f, 0.44f), new Color(0.21f, 0.94f, 0.82f));
        }

        private Text CreateText(Font font, string name, int fontSize, Vector2 anchor, Color color)
        {
            var go = new GameObject($"JoinProgress_{name}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1800f, fontSize * 2f);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = color;
            text.text = string.Empty;
            return text;
        }
    }
}
