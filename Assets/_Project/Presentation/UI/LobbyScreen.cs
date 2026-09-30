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
    /// 접속 전에는 그리지 않는다(10-01 옛 접속 전 패널 삭제). 이 화면은 PulseSystem(NetworkObject)에 있어 FishNet이
    /// 접속 전에 꺼 두므로 H/J 패널은 원래 보이지 않았다. 접속 시작은 메인 메뉴(H/J) → <see cref="LobbyEntry"/>,
    /// 접속 중 · 실패 · 취소 표시는 SceneFlow의 <see cref="JoinProgressOverlay"/>가 맡는다.
    ///
    /// 접속이 시작된 뒤 세 상태를 순서대로 담당한다:
    /// 1. **접속 대기**(라운드 동기화 전): "접속 중…" · Esc 취소.
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

        [Header("키 (§12.3 — 정식 버튼 UI는 §12.6 이후)")]
        [SerializeField] private Key _readyKey = Key.R;

        [Tooltip("접속 대기 중 빠져나오는 키(§12.2 재시도). 접속에 실패해도 화면이 멈추지 않도록 하는 유일한 출구다.")]
        [SerializeField] private Key _cancelKey = Key.Escape;

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

            // 접속 전 — 그리지 않는다(10-01 옛 접속 전 패널 삭제, 위 클래스 설명). 끊김 직후 한두 프레임 여기로 올 수 있어
            // 숨기기만 한다(그 뒤는 JoinProgressOverlay가 메뉴로 돌려보낸다).
            if (!connectionStarted)
            {
                SetVisible(false);
                return;
            }

            // 접속 시작됨 — 서버 페이즈에 따라 로비/카운트다운만 그린다.
            GameFlowState phase = _round != null ? _round.CurrentPhase : GameFlowState.Boot;
            bool networkReady = _round != null && _round.IsNetworkActive;

            if (!networkReady)
            {
                // 접속이 성립하지 않으면 여기서 계속 머문다 — 취소로 빠져나갈 수 있어야 한다.
                // (타임아웃은 두지 않는다: 적정 대기 시간이 회선·환경마다 달라 값을 만들 수 없다 — GAP-57)
                SetVisible(true);
                ShowConnecting();
                HandleCancelInput(connection);
                return;
            }

            // 버그(09-24): RoleAssign은 §12.3 카운트다운(3초)뿐 아니라 맵 로드 대기 · §12.4 브리핑
            // 30초까지 포함하는 페이즈다(RoundStartSequencer). CountdownRemaining은 카운트다운
            // 하위 구간에서만 흐르고 그 뒤로는 0에 멈춰 있는데, 예전 조건은 phase == RoleAssign이면
            // 무조건 이 화면을 띄워 "시작까지 0초"가 맵 로드 · 브리핑 30초 내내 얼어붙은 채 보였다
            // (실제 남은 시간과 표시가 어긋남). 카운트다운이 실제로 도는 동안만 보이게 하고, 0이 되면
            // 숨겨서 InGameHud의 §12.4 브리핑 표시(실제 남은 초를 그대로 반영)가 이어받게 한다.
            bool show = phase == GameFlowState.Lobby
                || (phase == GameFlowState.RoleAssign && _round.CountdownRemaining > 0f);
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

        // ── 접속 대기 (§12.2 — 접속은 시작됐고 라운드 동기화 전) ──────────────

        private void ShowConnecting()
        {
            _titleText.text = "마르코!";
            _titleText.color = NeutralColor();
            _roomCodeText.text = "접속 중…";
            _roomCodeText.color = NeutralColor();
            _hintText.text = $"{_cancelKey} — 취소하고 돌아가기";
            _hintText.color = RunnerColor();
            _readyCountText.text = string.Empty;
            _countdownText.text = string.Empty;
            ClearPlayerRows();
        }

        /// <summary>
        /// 접속 대기 중 취소 입력을 받는다(§12.2 재시도 경로).
        ///
        /// 이 경로가 없으면 호스트가 없는 주소로 참가를 시도한 순간 화면이 "접속 중…"에
        /// 영구히 머문다 — 접속 입력을 받는 분기는 <c>HasStarted == false</c>일 때만 도는데,
        /// 그 플래그는 실패해도 되돌아오지 않았기 때문이다(<c>ConnectionService</c>에서 함께 해소).
        /// </summary>
        private void HandleCancelInput(IConnectionService connection)
        {
            if (connection == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_cancelKey].wasPressedThisFrame)
                connection.Cancel();
        }

        // ── 로비 (§12.3) ─────────────────────────────────────────────────

        private void RefreshLobby(IConnectionService connection)
        {
            _titleText.text = "로비";
            _titleText.color = NeutralColor();
            _countdownText.text = string.Empty;

            // 09-30 — 호스트는 내 LAN IP:포트(같은 네트워크용), 참가자는 접속한 주소.
            _roomCodeText.text = connection != null ? connection.AddressLine : HudFormatter.FormatRoomCode(null);
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

        private Color NeutralColor() =>
            _palette != null ? _palette.GetEnvironmentPulse(Colorblind) : new Color(0.91f, 0.91f, 0.91f);
    }
}
