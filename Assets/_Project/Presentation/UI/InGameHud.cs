using UnityEngine;
using UnityEngine.UI;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Objectives;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// §12.4 인게임 HUD. 지금까지 Console 로그·IMGUI 임시 표시로만 확인하던 정보를 화면 UI로 옮긴다
    /// (스프린트 16, 정식 UI 1단계).
    ///
    /// **새 게임플레이 로직이 없다** — 이미 서버 권위로 동기화되고 있는 값(라운드 타이머·밸브 상태·
    /// 배정된 역할)을 <b>읽어서 그리기만</b> 한다. 네트워크/로컬 경로 선택도 각 소유자
    /// (<see cref="RoundCoordinator"/>·<see cref="ValveBehaviour"/>)가 이미 하고 있으므로 여기서
    /// 다시 판단하지 않는다.
    ///
    /// **uGUI를 코드로 구축하는 이유**: Canvas·RectTransform·Font 참조가 얽힌 UI 계층을 씬 YAML로
    /// 직접 편집하는 것은 이 프로젝트에서 금지된 방식이고(에디터 생성값 손상 위험), 에디터 도구로
    /// 만들면 "툴 실행 후 씬 저장"이 필요해 스프린트 14 실기 실패와 같은 사고가 재발할 수 있다.
    /// 그래서 이 컴포넌트가 <see cref="Awake"/>에서 자기 UI 계층을 만든다 — 씬에는 이 컴포넌트
    /// 하나만 있으면 되고, 레이아웃·색은 인스펙터로 조정할 수 있다. 아이콘·폰트 자산이 들어오는
    /// 정식 확장(§12.5 결과 화면·§12.1 로비) 시점에 프리팹 기반으로 옮기는 것이 자연스럽다.
    ///
    /// **폰트**: TextMeshPro는 이 프로젝트에 Essentials가 임포트되지 않아 기본 폰트 자산이 없다
    /// (런타임 실패 위험). 그래서 빌트인 폰트를 쓰는 legacy <see cref="Text"/>를 사용한다.
    ///
    /// **색상**: §16.2 <see cref="ColorPalette"/>를 재사용하고, 색맹 모드는 T8 렌더러의 토글
    /// (`C` 키)을 단일 진실 소스로 삼아 함께 전환된다(§19).
    /// </summary>
    public sealed class InGameHud : MonoBehaviour
    {
        [Header("참조 (비우면 씬에서 찾는다)")]
        [SerializeField] private RoundCoordinator _round;
        [SerializeField] private ValveObjectiveTracker _valves;
        [SerializeField] private Palette.ColorPalette _palette;

        [Tooltip("색맹 모드의 단일 진실 소스. 비우면 씬에서 찾고, 없으면 아래 폴백 값을 쓴다.")]
        [SerializeField] private PulseVisualRenderer _colorblindSource;
        [SerializeField] private bool _colorblindFallback;

        [Header("레이아웃 (§12.4)")]
        [Tooltip("§16.1 암전 아트에 맞춘 기본 글자 크기.")]
        [SerializeField, Range(10, 48)] private int _fontSize = 22;
        [SerializeField] private Vector2 _screenMargin = new Vector2(24f, 18f);
        [SerializeField] private float _valvePipSize = 14f;
        [SerializeField] private float _valvePipSpacing = 6f;

        [Header("표시 토글")]
        [SerializeField] private bool _showTimer = true;
        [SerializeField] private bool _showValves = true;
        [SerializeField] private bool _showRole = true;
        [SerializeField] private bool _showGateHint = true;

        [Tooltip("스프린트 16의 최소 결과 배너. 스프린트 17에서 정식 결과 화면(ResultScreen, §12.5)이 " +
                 "생겼으므로 씬에서는 꺼 둔다 — 켜면 같은 내용이 두 번 표시된다. 결과 화면 없이 " +
                 "HUD만으로 확인하고 싶을 때만 켠다.")]
        [SerializeField] private bool _showResultBanner;

        // 런타임에 만든 UI 요소들.
        private Canvas _canvas;
        private Text _timerText;
        private Text _valveText;
        private Text _roleText;
        private Text _gateHintText;
        private Text _resultText;
        private Image[] _valvePips;

        /// <summary>§6.2상 맵당 밸브는 최대 3개다. 맵이 오갈 때를 대비해 이만큼 미리 만들어 둔다.</summary>
        private const int MaxValvePips = 3;

        private bool Colorblind =>
            _colorblindSource != null ? _colorblindSource.ColorblindMode : _colorblindFallback;

        private void Awake()
        {
            if (_round == null)
                _round = GetComponent<RoundCoordinator>() ?? FindAnyObjectByType<RoundCoordinator>();
            if (_valves == null)
                _valves = GetComponent<ValveObjectiveTracker>() ?? FindAnyObjectByType<ValveObjectiveTracker>();
            if (_colorblindSource == null)
                _colorblindSource = FindAnyObjectByType<PulseVisualRenderer>();

            BuildHud();
        }

        // ── UI 구축 (런타임) ─────────────────────────────────────────────

        private void BuildHud()
        {
            var canvasGo = new GameObject("HUD Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 파문 링(월드스페이스)보다 위에 그려지도록 넉넉한 정렬 순서를 준다.
            _canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // 입력을 받는 UI가 없으므로 GraphicRaycaster를 붙이지 않는다 —
            // 붙이면 매 프레임 레이캐스트 비용만 생기고 조작을 가로챌 수 있다.

            Font font = ResolveFont();

            // §12.4: 밸브 카운트 = 좌상단 소형. 역할·게이트 안내를 그 아래로 쌓는다(GAP-26).
            _valveText = CreateText(font, "valve", TextAnchor.UpperLeft, new Vector2(0f, 1f),
                new Vector2(_screenMargin.x, -_screenMargin.y));
            _valvePips = CreateValvePips();
            _roleText = CreateText(font, "role", TextAnchor.UpperLeft, new Vector2(0f, 1f),
                new Vector2(_screenMargin.x, -_screenMargin.y - _fontSize * 1.4f));
            _gateHintText = CreateText(font, "gate", TextAnchor.UpperLeft, new Vector2(0f, 1f),
                new Vector2(_screenMargin.x, -_screenMargin.y - _fontSize * 2.8f));

            // 타이머: §12.4가 좌상단(밸브)·우하단(아이템)·하단중앙(숨)·가장자리(방향)를 이미
            // 점유하므로, 비어 있는 상단 중앙에 둔다(GAP-26).
            _timerText = CreateText(font, "timer", TextAnchor.UpperCenter, new Vector2(0.5f, 1f),
                new Vector2(0f, -_screenMargin.y));

            // 결과 배너: 정식 결과 화면(§12.5)은 다음 단계라, 화면 중앙에 최소 문구만.
            _resultText = CreateText(font, "result", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
            _resultText.fontSize = _fontSize * 2;
        }

        /// <summary>
        /// 빌트인 폰트를 단계적으로 시도한다. Unity 2022.2+에서 Arial이 LegacyRuntime으로 바뀌었고
        /// 버전에 따라 이름이 달라, 실패 시 OS 폰트까지 폴백한 뒤 그래도 없으면 경고를 남긴다
        /// (폰트가 null이면 글자가 아예 안 보여서 원인 파악이 어렵다).
        /// </summary>
        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 22);

            if (font == null)
                Debug.LogWarning("[HUD] 빌트인 폰트를 찾지 못했습니다 — HUD 텍스트가 보이지 않습니다.");

            return font;
        }

        private Text CreateText(Font font, string name, TextAnchor alignment, Vector2 anchor, Vector2 offset)
        {
            var go = new GameObject($"HUD_{name}");
            go.transform.SetParent(_canvas.transform, worldPositionStays: false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(600f, _fontSize * 1.6f);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = _fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false; // 조작을 가로채지 않는다
            text.text = string.Empty;
            return text;
        }

        /// <summary>
        /// 밸브 표시용 사각형을 만든다(개방/회전중/닫힘을 색으로 구분).
        ///
        /// 스프린트 18b: 맵이 애디티브로 오가면서 밸브 수가 0 ↔ N으로 변하므로, HUD 구축 시점의
        /// 개수에 맞춰 만들 수 없다. §6.2 최대치(맵당 3개)만큼 미리 만들어 두고 실제 밸브 수에 따라
        /// 보이거나 숨긴다 — 런타임에 UI 오브젝트를 만들고 없애는 것보다 단순하고 할당도 없다.
        /// </summary>
        private Image[] CreateValvePips()
        {
            int count = MaxValvePips;
            var pips = new Image[count];

            // 밸브 카운트 텍스트("⚙ 0/3") 오른쪽에 나란히 배치한다.
            float startX = _screenMargin.x + _fontSize * 4f;

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"HUD_valvePip{i}");
                go.transform.SetParent(_canvas.transform, worldPositionStays: false);

                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(
                    startX + i * (_valvePipSize + _valvePipSpacing),
                    -_screenMargin.y - (_fontSize - _valvePipSize) * 0.5f);
                rect.sizeDelta = new Vector2(_valvePipSize, _valvePipSize);

                var image = go.AddComponent<Image>();
                image.raycastTarget = false;
                pips[i] = image;
            }

            return pips;
        }

        // ── 값 갱신 ──────────────────────────────────────────────────────

        /// <summary>
        /// 매 프레임 폴링한다. SyncVar <c>OnChange</c> 구독이 이 프로젝트의 관례지만, HUD가 읽는
        /// 값들은 <b>여러 소유자에 흩어져 있고</b>(라운드 브릿지·밸브 3개·로컬 플레이어 역할) 그중
        /// 타이머는 매 프레임 변하므로, 값마다 콜백을 다는 것보다 한곳에서 읽는 폴링이 단순하고
        /// 실수 여지가 적다. 표시 대상이 5개뿐이라 비용도 무의미하다.
        /// </summary>
        private void Update()
        {
            UpdateTimer();
            UpdateValves();
            UpdateRole();
            UpdateGateHint();
            UpdateResult();
        }

        private void UpdateTimer()
        {
            if (_timerText == null)
                return;

            if (!_showTimer || _round == null)
            {
                _timerText.text = string.Empty;
                return;
            }

            _timerText.text = HudFormatter.FormatRemainingTime(_round.RemainingSeconds);
            _timerText.color = PulseColor(); // 암전 배경 위 기본 발광색(§16.1)
        }

        private void UpdateValves()
        {
            if (!_showValves || _valves == null)
            {
                if (_valveText != null)
                    _valveText.text = string.Empty;
                SetPipsVisible(false);
                return;
            }

            if (_valveText != null)
            {
                _valveText.text = HudFormatter.FormatValveCount(_valves.OpenedCount, _valves.TotalValves);
                _valveText.color = PulseColor();
            }

            UpdateValvePips();
        }

        /// <summary>
        /// 밸브별 상태를 색으로 표시한다 — 닫힘=어두운 회색, 회전 중=진행률만큼 밝아지는 포인트 컬러,
        /// 개방=포인트 컬러 최대. 상태·진행률은 <see cref="ValveBehaviour"/>가 이미 네트워크/로컬을
        /// 골라 노출하는 값을 그대로 읽는다(스프린트 10 후속과 같은 소스).
        /// </summary>
        private void UpdateValvePips()
        {
            if (_valvePips == null)
                return;

            Color accent = _palette != null ? _palette.GetInteractable(Colorblind) : new Color(1f, 0.72f, 0.3f);
            var closed = new Color(0.25f, 0.25f, 0.25f, 0.85f);

            for (int i = 0; i < _valvePips.Length; i++)
            {
                Image pip = _valvePips[i];
                if (pip == null)
                    continue;

                // 스프린트 18b: 밸브 목록은 맵 로드·언로드에 따라 바뀌므로 매 프레임 집계기에서
                // 최신 배열을 읽는다(집계기가 씬 이벤트로 재스캔한다). 맵이 없으면 길이 0이라
                // 모든 핍이 숨는다.
                ValveBehaviour[] valves = _valves.Valves;
                ValveBehaviour valve = i < valves.Length ? valves[i] : null;
                if (valve == null)
                {
                    pip.enabled = false;
                    continue;
                }

                pip.enabled = true;

                switch (valve.State)
                {
                    case ValveState.Open:
                        pip.color = accent;
                        break;
                    case ValveState.Rotating:
                        pip.color = Color.Lerp(closed, accent, Mathf.Clamp01(valve.Progress01));
                        break;
                    default:
                        pip.color = closed;
                        break;
                }
            }
        }

        private void SetPipsVisible(bool visible)
        {
            if (_valvePips == null)
                return;

            for (int i = 0; i < _valvePips.Length; i++)
            {
                if (_valvePips[i] != null)
                    _valvePips[i].enabled = visible;
            }
        }

        private void UpdateRole()
        {
            if (_roleText == null)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            if (!_showRole || player == null)
            {
                _roleText.text = string.Empty;
                return;
            }

            RoleType role = player.Role;
            _roleText.text = HudFormatter.FormatRole(role);
            _roleText.color = RoleColor(role);
        }

        private void UpdateGateHint()
        {
            if (_gateHintText == null)
                return;

            if (!_showGateHint || _round == null)
            {
                _gateHintText.text = string.Empty;
                return;
            }

            _gateHintText.text = HudFormatter.FormatGateHint(_round.IsEscapeGateOpen);
            _gateHintText.color = _palette != null ? _palette.GetInteractable(Colorblind) : Color.yellow;
        }

        private void UpdateResult()
        {
            if (_resultText == null)
                return;

            if (!_showResultBanner || _round == null)
            {
                _resultText.text = string.Empty;
                return;
            }

            RoundResult result = _round.Result;
            _resultText.text = HudFormatter.FormatRoundResult(result);
            // §12.5 승패 배너 색 규칙(도망자=시안 / 술래=레드)을 팔레트에서 가져온다.
            _resultText.color = result == RoundResult.SeekerWin ? SeekerColor() : RunnerColor();
        }

        // ── 색상 (§16.2 팔레트 재사용) ────────────────────────────────────

        private Color PulseColor() =>
            _palette != null ? _palette.GetEnvironmentPulse(Colorblind) : new Color(0.91f, 0.91f, 0.91f);

        private Color RunnerColor() =>
            _palette != null ? _palette.GetRunner(Colorblind) : new Color(0.21f, 0.94f, 0.82f);

        private Color SeekerColor() =>
            _palette != null ? _palette.GetSeeker(Colorblind) : new Color(1f, 0.23f, 0.30f);

        private Color RoleColor(RoleType role)
        {
            switch (role)
            {
                case RoleType.Seeker: return SeekerColor();
                case RoleType.Echo: return PulseColor();
                default: return RunnerColor();
            }
        }
    }
}
