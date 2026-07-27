using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Marco.Core.GameFlow;
using Marco.Core.Net;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Sound;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// §12.2 메인 메뉴(접속) + §12.3 로비 화면(스프린트 18, 정식 UI 3단계).
    /// 스프린트 16·17과 같은 런타임 uGUI 구축 패턴이다.
    ///
    /// 세 상태를 하나의 화면이 순서대로 담당한다:
    /// 1. **접속 전**(§12.2): "방 만들기(H) / 코드 입장(J)" — <see cref="IConnectionService"/>(Core)를
    ///    통해 접속을 시작한다. 지금까지 이 역할을 하던 DebugTools H/J 키의 정식 대체 경로다.
    /// 2. **로비**(§12.3, 페이즈 Lobby): 방코드(GAP-28: 주소로 대체)·플레이어 목록(준비 상태)·
    ///    "준비완료 (n/m)"·R 키 안내. 플레이어 pawn은 이미 맵의 입구 로비(§10.1)에 스폰돼
    ///    자유 이동·파문 확인이 가능하다 — §12.3 "이 화면 자체가 튜토리얼" 컨셉의 구현이다(GAP-28).
    /// 3. **카운트다운**(페이즈 RoleAssign): §12.3 "3초 카운트다운" 대형 표시.
    /// InGame·RoundEnd에서는 완전히 숨는다(HUD·ResultScreen 담당).
    ///
    /// 준비 토글은 <see cref="ReadyStateRegistry"/>에서 자기 pawn(<see cref="IReadyState.IsLocalPlayer"/>)을
    /// 찾아 요청한다 — 서버 반영·전파는 Net(<c>ReadyNetworkSync</c>)이 담당한다.
    /// </summary>
    public sealed class LobbyScreen : MonoBehaviour
    {
        private const int MaxPlayerRows = 6; // §1 최대 인원

        [Header("참조 (비우면 씬에서 찾는다)")]
        [SerializeField] private RoundCoordinator _round;
        [SerializeField] private Palette.ColorPalette _palette;
        [SerializeField] private PulseVisualRenderer _colorblindSource;
        [SerializeField] private bool _colorblindFallback;

        [Header("키 (§12.2/§12.3 — 정식 버튼 UI는 §12.6 이후)")]
        [SerializeField] private Key _hostKey = Key.H;
        [SerializeField] private Key _joinKey = Key.J;
        [SerializeField] private Key _readyKey = Key.R;

        [Header("표시")]
        [SerializeField, Range(12, 48)] private int _bodyFontSize = 24;
        [SerializeField, Range(24, 96)] private int _countdownFontSize = 72;
        [SerializeField, Range(0f, 1f)] private float _dimAlpha = 0.55f;

        private Canvas _canvas;
        private GameObject _root;
        private Image _dim;
        private Text _titleText;
        private Text _roomCodeText;
        private Text[] _playerRows;
        private Text _readyCountText;
        private Text _hintText;
        private Text _countdownText;

        private bool _visible;

        private bool Colorblind =>
            _colorblindSource != null ? _colorblindSource.ColorblindMode : _colorblindFallback;

        private void Awake()
        {
            if (_round == null)
                _round = GetComponent<RoundCoordinator>() ?? FindAnyObjectByType<RoundCoordinator>();
            if (_colorblindSource == null)
                _colorblindSource = FindAnyObjectByType<PulseVisualRenderer>();

            BuildUi();
            SetVisible(true); // 시작 화면(접속 전 패널)부터 보인다.
        }

        // ── UI 구축 (스프린트 16·17과 같은 방식) ──────────────────────────

        private void BuildUi()
        {
            var canvasGo = new GameObject("Lobby Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // HUD(100)와 ResultScreen(200) 사이 — 로비 목록이 HUD 위, 결과 화면 아래.
            _canvas.sortingOrder = 150;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Lobby Root");
            _root.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // 옅은 암막 — 맵(튜토리얼 이동)이 비쳐 보여야 하므로 결과 화면(0.88)보다 훨씬 옅다.
            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(_root.transform, worldPositionStays: false);
            var dimRect = dimGo.AddComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            _dim = dimGo.AddComponent<Image>();
            _dim.color = new Color(0f, 0f, 0f, _dimAlpha);
            _dim.raycastTarget = false;

            Font font = ResolveFont();

            _titleText = CreateText(font, "Title", _bodyFontSize * 2, new Vector2(0.5f, 0.86f));
            _roomCodeText = CreateText(font, "RoomCode", _bodyFontSize, new Vector2(0.5f, 0.76f));

            _playerRows = new Text[MaxPlayerRows];
            for (int i = 0; i < MaxPlayerRows; i++)
                _playerRows[i] = CreateText(font, $"Player{i}", _bodyFontSize, new Vector2(0.5f, 0.66f - i * 0.06f));

            _readyCountText = CreateText(font, "ReadyCount", _bodyFontSize, new Vector2(0.5f, 0.24f));
            _hintText = CreateText(font, "Hint", _bodyFontSize, new Vector2(0.5f, 0.16f));

            _countdownText = CreateText(font, "Countdown", _countdownFontSize, new Vector2(0.5f, 0.5f));
        }

        /// <summary>스프린트 16과 같은 3단 폰트 폴백.</summary>
        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 24);

            if (font == null)
                Debug.LogWarning("[Lobby] 빌트인 폰트를 찾지 못했습니다 — 로비 텍스트가 보이지 않습니다.");

            return font;
        }

        private Text CreateText(Font font, string name, int fontSize, Vector2 anchor)
        {
            var go = new GameObject($"Lobby_{name}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);

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
            text.text = string.Empty;
            return text;
        }

        // ── 상태 갱신·입력 ────────────────────────────────────────────────

        private void Update()
        {
            IConnectionService connection = ConnectionServiceRegistry.Current;
            bool connectionStarted = connection != null && connection.HasStarted;

            if (!connectionStarted)
            {
                SetVisible(true);
                RefreshPreConnection(connection);
                HandleConnectionInput(connection);
                return;
            }

            // 접속 시작됨 — 서버 페이즈에 따라 로비/카운트다운만 그린다.
            GameFlowState phase = _round != null ? _round.CurrentPhase : GameFlowState.Boot;
            bool networkReady = _round != null && _round.IsNetworkActive;

            if (!networkReady)
            {
                SetVisible(true);
                ShowConnecting();
                return;
            }

            bool show = phase == GameFlowState.Lobby || phase == GameFlowState.RoleAssign;
            SetVisible(show);
            if (!show)
                return;

            if (phase == GameFlowState.Lobby)
            {
                RefreshLobby(connection);
                HandleReadyInput();
            }
            else
            {
                RefreshCountdown();
            }
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;

            _visible = visible;
            if (_root != null)
                _root.SetActive(visible);
        }

        // ── 접속 전 (§12.2) ──────────────────────────────────────────────

        private void RefreshPreConnection(IConnectionService connection)
        {
            _titleText.text = "마르코!";
            _titleText.color = NeutralColor();

            if (connection == null)
            {
                _roomCodeText.text = "접속 서비스를 찾지 못했습니다 — Diagnose Network Setup 확인";
                _roomCodeText.color = SeekerColor();
                _hintText.text = string.Empty;
            }
            else
            {
                _roomCodeText.text = HudFormatter.FormatRoomCode(connection.DefaultAddress);
                _roomCodeText.color = NeutralColor();
                _hintText.text = $"{_hostKey} — 방 만들기(호스트)   ·   {_joinKey} — 코드 입장(참가)";
                _hintText.color = RunnerColor();
            }

            ClearPlayerRows();
            _readyCountText.text = string.Empty;
            _countdownText.text = string.Empty;
        }

        private void HandleConnectionInput(IConnectionService connection)
        {
            if (connection == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_hostKey].wasPressedThisFrame)
                connection.StartHost();
            else if (keyboard[_joinKey].wasPressedThisFrame)
                connection.StartClient(connection.DefaultAddress);
        }

        private void ShowConnecting()
        {
            _titleText.text = "마르코!";
            _titleText.color = NeutralColor();
            _roomCodeText.text = "접속 중…";
            _roomCodeText.color = NeutralColor();
            _hintText.text = string.Empty;
            _readyCountText.text = string.Empty;
            _countdownText.text = string.Empty;
            ClearPlayerRows();
        }

        // ── 로비 (§12.3) ─────────────────────────────────────────────────

        private void RefreshLobby(IConnectionService connection)
        {
            _titleText.text = "로비";
            _titleText.color = NeutralColor();
            _countdownText.text = string.Empty;

            _roomCodeText.text = HudFormatter.FormatRoomCode(connection != null ? connection.DefaultAddress : null);
            _roomCodeText.color = NeutralColor();

            var states = ReadyStateRegistry.All;
            int ready = 0;

            for (int i = 0; i < _playerRows.Length; i++)
            {
                if (i < states.Count && states[i] != null)
                {
                    IReadyState state = states[i];
                    if (state.IsReady)
                        ready++;

                    _playerRows[i].text = HudFormatter.FormatPlayerRow(state.PlayerId, state.IsReady, state.IsLocalPlayer);
                    _playerRows[i].color = state.IsReady ? RunnerColor() : NeutralColor();
                }
                else
                {
                    _playerRows[i].text = string.Empty;
                }
            }

            _readyCountText.text = HudFormatter.FormatReadyCount(ready, states.Count);
            _readyCountText.color = NeutralColor();

            _hintText.text = $"{_readyKey} — 준비 토글   ·   맵을 걸어다니며 파문을 확인해 보세요(§12.3 튜토리얼)";
            _hintText.color = RunnerColor();
        }

        private void HandleReadyInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[_readyKey].wasPressedThisFrame)
                return;

            var states = ReadyStateRegistry.All;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] != null && states[i].IsLocalPlayer)
                {
                    states[i].RequestToggleReady();
                    return;
                }
            }

            Debug.Log("[Lobby] 준비 토글 실패 — 로컬 플레이어의 준비 상태 컴포넌트를 찾지 못했습니다(스폰 대기 중일 수 있음).");
        }

        // ── 카운트다운 (§12.3 "3초", 페이즈 RoleAssign) ───────────────────

        private void RefreshCountdown()
        {
            _titleText.text = string.Empty;
            _roomCodeText.text = string.Empty;
            _hintText.text = string.Empty;
            _readyCountText.text = string.Empty;
            ClearPlayerRows();

            _countdownText.text = HudFormatter.FormatLobbyCountdown(_round.CountdownRemaining);
            _countdownText.color = RunnerColor();
        }

        private void ClearPlayerRows()
        {
            for (int i = 0; i < _playerRows.Length; i++)
                _playerRows[i].text = string.Empty;
        }

        // ── 색상 (§16.2 팔레트 재사용) ────────────────────────────────────

        private Color RunnerColor() =>
            _palette != null ? _palette.GetRunner(Colorblind) : new Color(0.21f, 0.94f, 0.82f);

        private Color SeekerColor() =>
            _palette != null ? _palette.GetSeeker(Colorblind) : new Color(1f, 0.23f, 0.30f);

        private Color NeutralColor() =>
            _palette != null ? _palette.GetEnvironmentPulse(Colorblind) : new Color(0.91f, 0.91f, 0.91f);
    }
}
