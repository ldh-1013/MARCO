using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// §12.2 메인 메뉴(스프린트 18b). 접속 전 독립 화면이며, 선택 결과를 정적으로 남긴 뒤
    /// 로비(시스템) 씬으로 넘어간다 — 접속 자체는 로비 씬의 <c>LobbyEntry</c>가 시작한다.
    ///
    /// **왜 여기서 바로 접속하지 않는가**: 라운드 상태기계(<c>RoundNetworkSync</c>)는 로비 씬의
    /// **씬 NetworkObject**다. 서버가 시작되는 순간 그 씬이 이미 로드돼 있어야 스폰되므로,
    /// "로비 씬 로드 → 접속 시작" 순서를 지켜야 한다. 이 화면은 의도(호스트/참가)만 전달한다.
    ///
    /// 스프린트 16·17·18과 같은 런타임 uGUI 구축 패턴이다(TMP 미사용 사유도 동일).
    ///
    /// **솔로 연습장**(§12.2 세 번째 버튼)은 이번 스코프 밖이다 — GAP-31로 이월했다.
    /// </summary>
    public sealed class MainMenuScreen : MonoBehaviour
    {
        /// <summary>메인 메뉴에서 고른 접속 의도. 로비 씬이 로드된 뒤 소비된다.</summary>
        public enum Intent
        {
            None,
            Host,
            Client
        }

        /// <summary>
        /// 다음 씬으로 넘길 접속 의도(씬 전환을 넘어 살아남아야 해서 정적이다).
        /// 로비 씬의 진입 컴포넌트가 읽고 <see cref="Consume"/>로 비운다.
        /// </summary>
        public static Intent PendingIntent { get; private set; } = Intent.None;

        /// <summary>참가 주소(§12.2 "코드 입장" — MVP는 주소 직결, GAP-28).</summary>
        public static string PendingAddress { get; private set; }

        public static Intent Consume()
        {
            Intent intent = PendingIntent;
            PendingIntent = Intent.None;
            return intent;
        }

        [Header("씬 (§15.1)")]
        [SerializeField] private string _lobbySceneName = "Lobby";

        [Header("키 (정식 버튼 UI는 §12.6 이후)")]
        [SerializeField] private Key _hostKey = Key.H;
        [SerializeField] private Key _joinKey = Key.J;

        [Header("참가")]
        [SerializeField] private string _joinAddress = "localhost";

        [Header("표시")]
        [SerializeField, Range(24, 96)] private int _titleFontSize = 64;
        [SerializeField, Range(12, 48)] private int _bodyFontSize = 24;

        private Text _titleText;
        private Text _menuText;
        private Text _hintText;
        private bool _transitioning;

        private void Awake()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("MainMenu Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300; // 메인 메뉴는 어떤 것보다 위

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // §16.1 흑 배경.
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bg = bgGo.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = false;

            Font font = ResolveFont();
            _titleText = CreateText(canvasGo.transform, font, "Title", _titleFontSize, new Vector2(0.5f, 0.72f));
            _menuText = CreateText(canvasGo.transform, font, "Menu", _bodyFontSize, new Vector2(0.5f, 0.5f));
            _hintText = CreateText(canvasGo.transform, font, "Hint", _bodyFontSize, new Vector2(0.5f, 0.36f));

            _titleText.text = "마르코!";
            _titleText.color = new Color(0.91f, 0.91f, 0.91f);

            // §12.2 도식의 버튼 3종 — 솔로 연습장은 GAP-31로 이월임을 화면에도 밝힌다.
            _menuText.text = $"{_hostKey} — 방 만들기        {_joinKey} — 코드 입장";
            _menuText.color = new Color(0.21f, 0.94f, 0.82f);

            _hintText.text = "솔로 연습장 — 준비 중";
            _hintText.color = new Color(0.5f, 0.5f, 0.5f);
        }

        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 24);

            if (font == null)
                Debug.LogWarning("[MainMenu] 빌트인 폰트를 찾지 못했습니다 — 메뉴 텍스트가 보이지 않습니다.");

            return font;
        }

        private static Text CreateText(Transform parent, Font font, string name, int fontSize, Vector2 anchor)
        {
            var go = new GameObject($"MainMenu_{name}");
            go.transform.SetParent(parent, worldPositionStays: false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1600f, fontSize * 2f);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private void Update()
        {
            if (_transitioning)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_hostKey].wasPressedThisFrame)
                Choose(Intent.Host);
            else if (keyboard[_joinKey].wasPressedThisFrame)
                Choose(Intent.Client);
        }

        private void Choose(Intent intent)
        {
            _transitioning = true;
            PendingIntent = intent;
            PendingAddress = _joinAddress;

            Debug.Log($"[MainMenu] {(intent == Intent.Host ? "방 만들기(호스트)" : "코드 입장(참가)")} 선택 — " +
                      $"로비 씬 로드 후 접속한다(§12.2 → §15.1)");

            UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName,
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
