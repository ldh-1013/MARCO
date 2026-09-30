using Marco.Core.Net;
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

        /// <summary>참가 주소(§12.2 "코드 입장" — 주소 직결, GAP-28). 09-30부터 호스트 이름 · 포트 포함(입력 칸 · -join).</summary>
        public static JoinAddress PendingJoin { get; private set; }

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
        [Tooltip("J(코드 입장) 입력 칸에 미리 채우는 값. 09-30부터 입력 칸에서 바꿀 수 있다.")]
        [SerializeField] private string _joinAddress = "localhost";

        [Header("표시")]
        [SerializeField, Range(24, 96)] private int _titleFontSize = 64;
        [SerializeField, Range(12, 48)] private int _bodyFontSize = 24;

        [Tooltip("\"솔로 연습장 — 준비 중\" 줄을 보일지. 09-30 숨김(GAP-31 이월 — 줄과 문구 코드는 남긴다).")]
        [SerializeField] private bool _showSoloPracticeHint;

        private Text _titleText;
        private Text _menuText;
        private Text _hintText;
        private Text _statusText;
        private bool _transitioning;

        /// <summary>코드 입장 주소 입력 칸(09-30).</summary>
        private readonly AddressEntry _entry = new AddressEntry();

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
            _statusText = CreateText(canvasGo.transform, font, "Status", _bodyFontSize, new Vector2(0.5f, 0.42f));
            _statusText.color = new Color(0.62f, 0.62f, 0.62f);
            _statusText.text = string.Empty;

            _titleText.text = "마르코!";
            _titleText.color = new Color(0.91f, 0.91f, 0.91f);

            // §12.2 도식의 버튼 3종 — 솔로 연습장은 GAP-31로 이월. 09-30부터 그 안내 줄은 기본 숨김(_showSoloPracticeHint).
            _menuText.text = $"{_hostKey} — 방 만들기        {_joinKey} — 코드 입장";
            _menuText.color = new Color(0.21f, 0.94f, 0.82f);

            _hintText.text = "솔로 연습장 — 준비 중";
            _hintText.color = new Color(0.5f, 0.5f, 0.5f);

            // 09-30: 준비 중인 기능을 첫 화면에 내세우지 않는다 — 줄은 만들되 끈다(코드 삭제 금지 · 인스펙터에서 다시 켤 수 있다).
            _hintText.gameObject.SetActive(_showSoloPracticeHint);
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

        /// <summary>
        /// 실행 인자 <c>-join &lt;주소[:포트]&gt;</c>(09-30) — 값이 맞으면 메뉴를 거치지 않고 바로 참가한다. 틀리면 안내만 띄우고 메뉴에 남는다.
        /// 인자가 없으면(Run3P_QA.bat 포함) 아무것도 하지 않는다.
        /// </summary>
        private void Start()
        {
            if (LaunchArguments.TryGetJoin(System.Environment.GetCommandLineArgs(), out JoinAddress address, out JoinAddressError error))
            {
                Debug.Log($"[MainMenu] 실행 인자 -join {address} — 바로 참가한다.");
                Choose(Intent.Client, address);
                return;
            }

            if (error != JoinAddressError.None)
            {
                _statusText.text = "-join 인자를 쓸 수 없습니다 — " + JoinAddressParser.Describe(error);
                Debug.LogWarning("[MainMenu] " + _statusText.text);
            }
        }

        private void Update()
        {
            if (_transitioning)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // 코드 입장 — 주소 입력 칸이 열려 있는 동안은 H/J를 글자로 받는다.
            if (_entry.IsOpen)
            {
                AddressEntry.Result result = _entry.Tick(keyboard, out JoinAddress address);
                if (result == AddressEntry.Result.Submitted)
                {
                    Choose(Intent.Client, address);
                    return;
                }

                if (result == AddressEntry.Result.Cancelled)
                {
                    ShowMenu();
                    return;
                }

                _menuText.text = $"코드 입장 — {_entry.Render(Time.unscaledTime)}";
                _statusText.text = _entry.HintLine();
                _statusText.color = _entry.Error != null ? new Color(0.94f, 0.42f, 0.30f) : new Color(0.62f, 0.62f, 0.62f);
                return;
            }

            if (keyboard[_hostKey].wasPressedThisFrame)
                Choose(Intent.Host, default);
            else if (keyboard[_joinKey].wasPressedThisFrame)
                _entry.Open(_joinAddress);
        }

        private void ShowMenu()
        {
            _menuText.text = $"{_hostKey} — 방 만들기        {_joinKey} — 코드 입장";
            _statusText.text = string.Empty;
        }

        private void OnDisable() => _entry.Close();

        private void Choose(Intent intent, JoinAddress address)
        {
            _transitioning = true;
            _entry.Close();
            PendingIntent = intent;
            PendingJoin = address;

            Debug.Log($"[MainMenu] {(intent == Intent.Host ? "방 만들기(호스트)" : $"코드 입장(참가 {address})")} 선택 — " +
                      $"로비 씬 로드 후 접속한다(§12.2 → §15.1)");

            UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName,
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
