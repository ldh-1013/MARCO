using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Marco.Core.GameFlow;
using Marco.Core.Net;
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
        private Text _phaseText;             // §6.5 최후 생존자 페이즈 · 배수구 [블록 4]
        private Text _itemText;              // §12.4 아이템 슬롯(우하단) — 찰칵이 [블록 6]
        private Text _staminaText;           // §3.1 질주 스태미나(기능 표시) [블록 5·7]
        private Text[] _compassTexts;        // §3.2-2 메아리 8방위 [블록 6]
        private Text _scoreText;             // §12.4 승리조건 점수판(탈출 ●○ / 요구) [블록 7]
        private Text _breathText;            // §12.4 숨 게이지(잠수 중, 12초) [블록 7]
        private Text _guideText;             // §12.4 첫 20초 오프닝 가이드 [블록 7]
        private Image _overlay;              // 연출.md §4.2 태그 플래시·암전 [블록 7]

        // §12.4 로비 브리핑 평면도 [블록 7]
        private RectTransform _briefingRoot;
        private RectTransform _briefingPlan;
        private Text _briefingTitle;
        private MapPlanData _plan;
        private bool _briefingBuilt;
        private readonly List<Image> _briefingDots = new List<Image>();
        private readonly List<Text> _briefingDotLabels = new List<Text>();
        private Font _font;

        // 연출.md §4.2 태그 타임라인
        private float _tagFxStart = -1f;
        private bool _tagFxSelf;
        private const float TagFxSeekerPeak = 0.15f;   // 연출.md §4.2 "0.15 플래시 최대"
        private const float TagFxSeekerEnd = 0.5f;     // "0.50 플래시 소멸"
        private const float TagFxRunnerDark = 0.5f;    // "0.50 완전 암전"
        private const float SeekerFlashAlpha = 0.6f;   // 표시값(규칙 수치 아님) — 화면 전체를 덮되 시야는 남긴다
        private static readonly Color TagFlashRed = new Color(0xEF / 255f, 0x6A / 255f, 0x4C / 255f, 1f); // §16.1 #EF6A4C

        // §12.4 오프닝 가이드
        private GameFlowState _lastPhase = GameFlowState.Boot;
        private float _roundStartedAt = -1f;
        private int _guideStep;
        private float _guideDoneAt = -1f;

        private static readonly string[] CompassLabels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>
        /// §3.2-2 "아주 흐린 8방위". 투명도는 표시값이다(규칙 수치 아님) — 화면 가장자리에서
        /// 방향만 읽히고 시야를 가리지 않는 정도.
        /// </summary>
        private const float CompassAlpha = 0.18f;
        private Text _resultText;
        private Text _voiceText;              // 스프린트 26a 음성 등급(개발용 표시)
        private Voice.LocalVoicePipeline _voice; // 지연 탐색 — 씬에 없으면 표시하지 않는다
        private Image[] _valvePips;

        /// <summary>
        /// 핍 개수 = §6.1-0 <b>배치</b> 수(5). v0.3의 "맵당 최대 3개"였던 상수가 남아 있어
        /// 밸브 D·E가 HUD에 영영 안 그려졌다(블록 2의 enum 파급 누락 — 블록 4에서 발견).
        /// 활성 3~4개만이 아니라 잠금 밸브도 그려야 "어느 것이 잠겼나"가 보인다.
        /// </summary>
        private static int MaxValvePips => ValveRoster.PlacedCount;

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

            // §12.4 "아이템 슬롯 — 우하단, 1슬롯, 상시 — 찰칵이 보유 시 아이콘 노출".
            _itemText = CreateText(font, "item", TextAnchor.LowerRight, new Vector2(1f, 0f),
                new Vector2(-_screenMargin.x, _screenMargin.y));

            // §3.1 스태미나 — 도망자 전용 기능 표시(하단 중앙 숨 게이지 자리 바로 위, GAP-26 배치).
            _staminaText = CreateText(font, "stamina", TextAnchor.LowerCenter, new Vector2(0.5f, 0f),
                new Vector2(0f, _screenMargin.y + _fontSize * 1.4f));

            // §3.2-2 메아리 8방위 — 화면 가장자리 원주. 위치는 매 프레임 카메라 yaw로 돌린다.
            _compassTexts = new Text[CompassLabels.Length];
            for (int i = 0; i < CompassLabels.Length; i++)
            {
                _compassTexts[i] = CreateText(font, "compass" + CompassLabels[i], TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), Vector2.zero);
                _compassTexts[i].text = CompassLabels[i];
                _compassTexts[i].enabled = false;
            }

            // §6.5 페이즈 줄 — 타이머 바로 아래. 페이즈 밖에서는 빈 문자열이라 자리만 차지하지 않는다.
            _phaseText = CreateText(font, "phase", TextAnchor.UpperCenter, new Vector2(0.5f, 1f),
                new Vector2(0f, -_screenMargin.y - _fontSize * 1.4f));

            // 결과 배너: 정식 결과 화면(§12.5)은 다음 단계라, 화면 중앙에 최소 문구만.
            _resultText = CreateText(font, "result", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
            _resultText.fontSize = _fontSize * 2;

            // 음성 등급(스프린트 26a 진단): §12.4 도식에 없는 **개발용 표시**다. 빈 좌하단에 두고
            // 네트워크 연결 여부와 무관하게 항상 갱신한다 — 에디터 단독 Play로 마이크를 확인하려는 것이다.
            _voiceText = CreateText(font, "voice", TextAnchor.LowerLeft, new Vector2(0f, 0f),
                new Vector2(_screenMargin.x, _screenMargin.y));

            // ── [블록 7] ─────────────────────────────────────────────────
            _font = font;

            // §12.4 승리조건 점수판 — 게이트 안내 아래.
            _scoreText = CreateText(font, "score", TextAnchor.UpperLeft, new Vector2(0f, 1f),
                new Vector2(_screenMargin.x, -_screenMargin.y - _fontSize * 4.2f));

            // §12.4 "숨 게이지 — 하단 중앙".
            _breathText = CreateText(font, "breath", TextAnchor.LowerCenter, new Vector2(0.5f, 0f),
                new Vector2(0f, _screenMargin.y));

            // §12.4 오프닝 가이드 — 화면 중앙 조금 아래(시야 중앙을 가리지 않게).
            _guideText = CreateText(font, "guide", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.3f), Vector2.zero);

            // 연출.md §4.2 태그 오버레이 — 마지막에 만들어 모든 HUD 위에 그린다.
            var overlayGo = new GameObject("HUD_tagOverlay");
            overlayGo.transform.SetParent(_canvas.transform, worldPositionStays: false);
            var overlayRect = overlayGo.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            _overlay = overlayGo.AddComponent<Image>();
            _overlay.raycastTarget = false;
            _overlay.color = Color.clear;
            _overlay.enabled = false;
        }

        private void OnEnable()
        {
            TagTargetRegistry.TargetTagged += OnTargetTagged;
            SelfPulseFeed.Emitted += OnSelfPulse;
        }

        private void OnDisable()
        {
            TagTargetRegistry.TargetTagged -= OnTargetTagged;
            SelfPulseFeed.Emitted -= OnSelfPulse;
        }

        /// <summary>
        /// 연출.md §4.2 — 태그가 확정됐다(각 피어에서 한 번). 내가 잡혔으면 백색 플래시 → 암전(3초),
        /// 내가 술래면 적색 플래시 + FOV −5° punch + §3.1-1 경직 1.0초(이동 — GAP-85).
        /// "다른 생존자"는 태그 파문(서버)만 받는다 — 여기서 아무것도 하지 않는다.
        /// </summary>
        private void OnTargetTagged(ITagTarget target)
        {
            FirstPersonController local = LocalPlayerRegistry.Current;
            if (local == null || target == null || !local.IsLocallyControlled)
                return;

            if (target.PlayerId == local.PlayerId)
            {
                _tagFxStart = Time.time;
                _tagFxSelf = true;
                return;
            }

            if (local.Role == RoleType.Seeker)
            {
                _tagFxStart = Time.time;
                _tagFxSelf = false;
                local.PunchFov(-5f, 1f);                                 // 연출.md §4.2 "FOV −5° punch-in", 1.00 복귀
                local.StunFor(Core.Tagging.TagAftermath.SeekerStunSeconds); // §3.1-1 1.0초
            }
        }

        private void UpdateTagOverlay()
        {
            if (_overlay == null)
                return;

            if (_tagFxStart < 0f)
            {
                _overlay.enabled = false;
                return;
            }

            float t = Time.time - _tagFxStart;
            Color c;
            if (_tagFxSelf)
            {
                // 0 백색 플래시 → 0.15 암전 시작 → 0.50 완전 암전 → 3.00 소나 시야 복귀(§3.1-1 암전 3.0초).
                float blackout = Core.Tagging.TagAftermath.RunnerBlackoutSeconds;
                if (t >= blackout)
                {
                    _tagFxStart = -1f;
                    _overlay.enabled = false;
                    return;
                }

                if (t < TagFxSeekerPeak)
                    c = Color.white;
                else if (t < TagFxRunnerDark)
                    c = Color.Lerp(Color.white, Color.black, (t - TagFxSeekerPeak) / (TagFxRunnerDark - TagFxSeekerPeak));
                else
                    c = Color.black;
                c.a = 1f;
            }
            else
            {
                if (t >= TagFxSeekerEnd)
                {
                    _tagFxStart = -1f;
                    _overlay.enabled = false;
                    return;
                }

                c = TagFlashRed;
                c.a = t < TagFxSeekerPeak
                    ? SeekerFlashAlpha * (t / TagFxSeekerPeak)
                    : SeekerFlashAlpha * (1f - (t - TagFxSeekerPeak) / (TagFxSeekerEnd - TagFxSeekerPeak));
            }

            _overlay.enabled = true;
            _overlay.color = c;
        }

        /// <summary>§12.4 오프닝 가이드 단계 진행 — 자기 발소리 → 자기 목소리.</summary>
        private void OnSelfPulse(Core.Sound.SoundType type, float radius, float duration, Vector3 position)
        {
            if (_roundStartedAt < 0f)
                return;

            bool footstep = type == Core.Sound.SoundType.Walk || type == Core.Sound.SoundType.Sprint;
            bool voice = type == Core.Sound.SoundType.Whisper || type == Core.Sound.SoundType.Talk ||
                         type == Core.Sound.SoundType.Shout;

            if (_guideStep == 0 && footstep)
                _guideStep = 1;
            else if (_guideStep == 1 && voice)
            {
                _guideStep = 2;
                _guideDoneAt = Time.time;
            }
        }

        private void UpdateGuide()
        {
            if (_guideText == null || _round == null)
                return;

            GameFlowState phase = _round.CurrentPhase;
            if (phase != _lastPhase)
            {
                if (phase == GameFlowState.InGame)
                {
                    _roundStartedAt = Time.time;
                    _guideStep = 0;
                    _guideDoneAt = -1f;
                }
                else
                {
                    _roundStartedAt = -1f;
                }

                _lastPhase = phase;
            }

            FirstPersonController player = LocalPlayerRegistry.Current;
            bool eligible = player != null && player.Role != RoleType.Echo;
            float elapsed = _roundStartedAt >= 0f ? Time.time - _roundStartedAt : float.MaxValue;
            bool doneShown = _guideDoneAt >= 0f && Time.time - _guideDoneAt > GuideDoneHoldSeconds;

            if (!eligible || elapsed > BriefingConfig.OpeningGuideSeconds || doneShown)
            {
                _guideText.text = string.Empty;
                return;
            }

            _guideText.text = HudFormatter.FormatOpeningGuide(_guideStep);
            _guideText.color = PulseColor();
        }

        /// <summary>가이드 마지막 문구 유지 시간(표시값).</summary>
        private const float GuideDoneHoldSeconds = 2f;

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
            UpdatePhase();
            UpdateItem();
            UpdateStamina();
            UpdateCompass();
            UpdateScore();
            UpdateBreath();
            UpdateGuide();
            UpdateBriefing();
            UpdateTagOverlay();
            UpdateValves();
            UpdateRole();
            UpdateGateHint();
            UpdateResult();
            UpdateVoice();
        }

        /// <summary>
        /// 음성 등급 실시간 표시(스프린트 26a). **네트워크 연결과 무관하게 항상 갱신**한다 —
        /// 에디터 단독 Play(접속 없음)로 마이크를 확인할 수 있어야 하기 때문이다.
        /// 씬에 <c>LocalVoicePipeline</c>이 없으면 줄 자체를 비운다(§12.4 도식에 없는 개발용 표시라
        /// 없을 때 자리를 차지하면 안 된다).
        /// </summary>
        private void UpdateVoice()
        {
            if (_voiceText == null)
                return;

            if (_voice == null)
                _voice = FindAnyObjectByType<Voice.LocalVoicePipeline>();

            if (_voice == null)
            {
                _voiceText.text = string.Empty;
                return;
            }

            if (!_voice.CaptureActive)
            {
                _voiceText.text = "🎤 마이크 없음";
                _voiceText.color = PulseColor();
                return;
            }

            _voiceText.text = $"🎤 {_voice.CurrentGrade}  {_voice.CurrentDbfs:0.0} dBFS";

            // 등급이 올라갈수록 눈에 띄게 — 색맹 모드에서도 팔레트를 그대로 따른다(§16.2).
            _voiceText.color = _voice.CurrentGrade switch
            {
                Core.Voice.VoiceGrade.Shout => SeekerColor(),
                Core.Voice.VoiceGrade.Talk => RunnerColor(),
                Core.Voice.VoiceGrade.Whisper => PulseColor(),
                _ => new Color(0.5f, 0.5f, 0.5f)
            };
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

        /// <summary>
        /// §6.5 페이즈 줄. ★ 배수구 진행도는 밸브와 같은 규칙이므로(§6.5-2) <b>작업 중과 감쇠 중을
        /// 다른 색으로</b> 그린다 — 밸브 핍과 같은 색 두 개를 쓴다(§12.4).
        /// </summary>
        private void UpdatePhase()
        {
            if (_phaseText == null)
                return;

            if (_round == null || !_round.LastSurvivorPhaseActive)
            {
                _phaseText.text = string.Empty;
                return;
            }

            _phaseText.text = HudFormatter.FormatLastSurvivorPhase(
                true, _round.LastSurvivorSecondsRemaining, _round.ActiveDrain, _round.DrainProgress01);

            // §12.4 "마지막 한 명 + 활성 배수구 방향"(양 진영 공통) — 카메라 기준 8방위 화살표.
            FirstPersonController viewer = LocalPlayerRegistry.Current;
            Camera cam = viewer != null ? viewer.GetComponentInChildren<Camera>() : null;
            if (_round.ActiveDrain != 0 && cam != null &&
                DrainRegistry.TryGetPosition((DrainId)_round.ActiveDrain, out Vector3 drainPos))
            {
                Vector3 d = drainPos - cam.transform.position;
                float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                _phaseText.text += " " + HudFormatter.DirectionArrow(bearing - cam.transform.eulerAngles.y);
            }
            _phaseText.color = _round.DrainDecaying ? DecayColor : InteractableColor();
        }

        /// <summary><paramref name="index"/>번째 활성 밸브(없으면 null). 배열 순서를 유지한다.</summary>
        private static ValveBehaviour ActiveValveAt(ValveBehaviour[] valves, int index)
        {
            int seen = 0;
            for (int i = 0; i < valves.Length; i++)
            {
                ValveBehaviour v = valves[i];
                if (v == null || !v.IsActiveThisRound)
                    continue;
                if (seen == index)
                    return v;
                seen++;
            }

            return null;
        }

        /// <summary>
        /// §12.4 승리조건 점수판 — "탈출 ●● / 요구 ●●". 칸 수는 §6.2 표에서 유도한다(상수 금지).
        /// 도망자 수 = 총원 − 술래(§6.2). 인원을 모르면(로컬) 표시하지 않는다.
        /// </summary>
        private void UpdateScore()
        {
            if (_scoreText == null || _round == null)
                return;

            int total = RoundStateRegistry.TotalPlayers;
            if (total <= 0 || _round.CurrentPhase != GameFlowState.InGame)
            {
                _scoreText.text = string.Empty;
                return;
            }

            int runners = Core.Role.RoleAssigner.RunnersFor(total);
            _scoreText.text = HudFormatter.FormatEscapeBoard(_round.EscapedCount, ValveRoster.EscapeRequirement(runners));
            _scoreText.color = RunnerColor();
        }

        /// <summary>
        /// §12.4 숨 게이지 — 도망자, 잠수 중이거나 덜 찼을 때만. 값은 서버가 소유자에게 보낸 것이다(GAP-76 해소).
        /// </summary>
        private void UpdateBreath()
        {
            if (_breathText == null)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            bool show = player != null && player.Role == RoleType.Runner &&
                        (Core.Breath.BreathClientState.Submerged ||
                         Core.Breath.BreathClientState.Remaining < Core.Breath.BreathConfig.TotalSeconds);
            if (!show)
            {
                _breathText.text = string.Empty;
                return;
            }

            _breathText.text = HudFormatter.FormatBreath(Core.Breath.BreathClientState.Remaining);
            _breathText.color = Core.Breath.BreathClientState.Remaining < Core.Breath.BreathConfig.SuppressionCost
                ? DecayColor   // §3.5 비명 억제(-4.5) 불가 구간 — 색으로 알린다
                : PulseColor();
        }

        // ── §12.4 로비 브리핑 평면도 ───────────────────────────────────

        /// <summary>
        /// §12.4 "라운드 시작 전 맵 평면도를 30초간 표시한다. 라운드 시작 시 사라진다." 평면도 한 장 +
        /// 활성 밸브 점(잠긴 밸브는 ✕) — 사용자 지시 범위. <b>술래와 도망자가 같은 화면을 본다.</b>
        /// 인게임 중에는 어떤 지도도 보이지 않는다(브리핑 잔여 0이면 숨김).
        /// </summary>
        private void UpdateBriefing()
        {
            float remaining = _round != null ? _round.BriefingSecondsRemaining : 0f;
            if (remaining <= 0f)
            {
                if (_briefingRoot != null)
                    _briefingRoot.gameObject.SetActive(false);
                return;
            }

            if (_plan == null)
                _plan = FindAnyObjectByType<MapPlanData>();

            if (!_briefingBuilt)
                BuildBriefing();

            if (_briefingRoot == null)
                return;

            _briefingRoot.gameObject.SetActive(true);

            ValveBehaviour[] valves = _valves != null ? _valves.Valves : System.Array.Empty<ValveBehaviour>();
            int active = 0;
            for (int i = 0; i < valves.Length; i++)
            {
                if (valves[i] != null && valves[i].IsActiveThisRound)
                    active++;
            }

            int required = _valves != null ? _valves.RequiredOpenCount : 0;
            _briefingTitle.text = HudFormatter.FormatBriefingTitle(active, required, remaining);

            // 밸브 점 — 씬의 실제 밸브 위치(도면 좌표 x, z).
            EnsureDots(valves.Length);
            Rect bounds = _plan != null ? _plan.Bounds : new Rect(0f, 0f, 50f, 42f);
            for (int i = 0; i < _briefingDots.Count; i++)
            {
                bool has = i < valves.Length && valves[i] != null;
                _briefingDots[i].enabled = has;
                _briefingDotLabels[i].enabled = has;
                if (!has)
                    continue;

                ValveBehaviour v = valves[i];
                Vector3 p = v.transform.position;
                Vector2 n = MapPlanData.Normalize(bounds, new Vector2(p.x, p.z));
                Vector2 pos = PlanPoint(n);
                _briefingDots[i].rectTransform.anchoredPosition = pos;
                _briefingDotLabels[i].rectTransform.anchoredPosition = pos + new Vector2(0f, 18f);

                bool on = v.IsActiveThisRound;
                _briefingDots[i].color = on ? InteractableColor() : new Color(0.35f, 0.35f, 0.35f, 0.9f);
                _briefingDotLabels[i].text = on ? v.ValveId.ToString() : v.ValveId + " ✕";
                _briefingDotLabels[i].color = _briefingDots[i].color;
            }
        }

        private const float PlanWidth = 1000f;
        private const float PlanHeight = 840f;

        private Vector2 PlanPoint(Vector2 normalized)
        {
            Rect bounds = _plan != null ? _plan.Bounds : new Rect(0f, 0f, 50f, 42f);
            float scale = Mathf.Min(PlanWidth / Mathf.Max(1f, bounds.width), PlanHeight / Mathf.Max(1f, bounds.height));
            return new Vector2((normalized.x - 0.5f) * bounds.width * scale, (normalized.y - 0.5f) * bounds.height * scale);
        }

        private void BuildBriefing()
        {
            _briefingBuilt = true;

            var rootGo = new GameObject("HUD_briefing");
            rootGo.transform.SetParent(_canvas.transform, worldPositionStays: false);
            _briefingRoot = rootGo.AddComponent<RectTransform>();
            _briefingRoot.anchorMin = Vector2.zero;
            _briefingRoot.anchorMax = Vector2.one;
            _briefingRoot.offsetMin = Vector2.zero;
            _briefingRoot.offsetMax = Vector2.zero;
            var dim = rootGo.AddComponent<Image>();
            dim.raycastTarget = false;
            dim.color = new Color(0f, 0f, 0f, 0.92f);

            var planGo = new GameObject("Plan");
            planGo.transform.SetParent(_briefingRoot, worldPositionStays: false);
            _briefingPlan = planGo.AddComponent<RectTransform>();
            _briefingPlan.anchorMin = new Vector2(0.5f, 0.5f);
            _briefingPlan.anchorMax = new Vector2(0.5f, 0.5f);
            _briefingPlan.sizeDelta = new Vector2(PlanWidth, PlanHeight);
            _briefingPlan.anchoredPosition = new Vector2(0f, -30f);

            _briefingTitle = CreateText(_font, "briefingTitle", TextAnchor.UpperCenter, new Vector2(0.5f, 1f),
                new Vector2(0f, -_screenMargin.y));
            _briefingTitle.transform.SetParent(_briefingRoot, worldPositionStays: true);
            _briefingTitle.color = PulseColor();

            if (_plan == null)
                return; // 맵 v2가 아니면 구역 없이 밸브 점만 그린다

            Rect bounds = _plan.Bounds;
            float scale = Mathf.Min(PlanWidth / Mathf.Max(1f, bounds.width), PlanHeight / Mathf.Max(1f, bounds.height));
            Color env = PulseColor();

            MapPlanData.Area[] areas = _plan.Areas;
            for (int i = 0; i < areas.Length; i++)
            {
                MapPlanData.Area a = areas[i];
                var go = new GameObject("Area_" + a.Name);
                go.transform.SetParent(_briefingPlan, worldPositionStays: false);
                var rect = go.AddComponent<RectTransform>();
                Vector2 center = PlanPoint(MapPlanData.Normalize(bounds, a.Rect.center));
                rect.anchoredPosition = center;
                rect.sizeDelta = new Vector2(a.Rect.width * scale, a.Rect.height * scale);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                Color c = env;
                c.a = a.Water ? 0.06f : a.UpperFloor ? 0.2f : 0.12f; // 표시값 — 물은 옅게, 2층은 진하게
                img.color = c;

                Text label = CreateText(_font, "AreaLabel_" + a.Name, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
                label.transform.SetParent(_briefingPlan, worldPositionStays: false);
                label.rectTransform.anchoredPosition = center;
                label.fontSize = Mathf.Max(10, _fontSize - 8);
                label.text = a.UpperFloor ? a.Name + " (2층)" : a.Name;
                Color lc = env;
                lc.a = 0.55f;
                label.color = lc;
            }
        }

        private void EnsureDots(int count)
        {
            while (_briefingDots.Count < count && _briefingPlan != null)
            {
                var go = new GameObject("ValveDot");
                go.transform.SetParent(_briefingPlan, worldPositionStays: false);
                var rect = go.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(18f, 18f);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                _briefingDots.Add(img);

                Text label = CreateText(_font, "ValveDotLabel", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
                label.transform.SetParent(_briefingPlan, worldPositionStays: false);
                _briefingDotLabels.Add(label);
            }
        }

        /// <summary>§12.4 아이템 슬롯 — 찰칵이 보유 시만. 1회용 · 소지 1개(§7).</summary>
        private void UpdateItem()
        {
            if (_itemText == null)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            bool show = player != null && player.Role == RoleType.Runner && Core.Items.ClickerClientEvents.LocalHolding;
            _itemText.text = show ? "▮ 찰칵이  [Q / 우클릭]" : string.Empty;
            _itemText.color = InteractableColor();
        }

        /// <summary>§3.1 질주 스태미나 — 도망자만. 소진 페널티 중에는 감쇠 색으로 구분한다.</summary>
        private void UpdateStamina()
        {
            if (_staminaText == null)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null || player.Role != RoleType.Runner)
            {
                _staminaText.text = string.Empty;
                return;
            }

            float s = Mathf.Clamp01(player.Stamina01);
            if (s >= 1f && !player.IsStaminaExhausted)
            {
                _staminaText.text = string.Empty; // 가득 차 있으면 표시하지 않는다(기능 위주 — 필요할 때만)
                return;
            }

            _staminaText.text = HudFormatter.FormatStamina(s, player.IsStaminaExhausted);
            _staminaText.color = player.IsStaminaExhausted ? DecayColor : RunnerColor();
        }

        /// <summary>
        /// §3.2-2 메아리 8방위 — <b>바라보는 방향만</b>. 위치·지형·러너는 알려주지 않는다.
        /// 메아리가 아니면 숨긴다.
        /// </summary>
        private void UpdateCompass()
        {
            if (_compassTexts == null)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            Camera cam = player != null ? player.GetComponentInChildren<Camera>() : null;
            bool show = player != null && player.Role == RoleType.Echo && cam != null;

            float yaw = show ? cam.transform.eulerAngles.y : 0f;
            RectTransform canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
            float rx = canvasRect != null ? canvasRect.rect.width * 0.46f : 880f;
            float ry = canvasRect != null ? canvasRect.rect.height * 0.44f : 470f;

            for (int i = 0; i < _compassTexts.Length; i++)
            {
                Text t = _compassTexts[i];
                if (t == null)
                    continue;

                t.enabled = show;
                if (!show)
                    continue;

                // 월드 기준 방위(N=+z, 시계방향) → 카메라 기준 상대각(§3.4 방위 인디케이터와 같은 계산).
                float rel = (i * 45f - yaw) * Mathf.Deg2Rad;
                t.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(rel) * rx, Mathf.Cos(rel) * ry);
                Color c = PulseColor();
                c.a = CompassAlpha;
                t.color = c;
            }
        }

        /// <summary>§12.4 감쇠 색. 밸브 핍과 배수구 줄이 같은 값을 쓴다.</summary>
        private static readonly Color DecayColor = new Color(1f, 0.45f, 0.1f, 0.95f);

        private Color InteractableColor() =>
            _palette != null ? _palette.GetInteractable(Colorblind) : new Color(1f, 0.72f, 0.3f);

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
                // ★ §12.4 "동시 개방 수 / 요구 수" — 분모가 활성 수가 아니라 **요구 수**다.
                //   역류·감쇠로 분자가 **줄어들 수 있다**(증가만 하는 값이 아니다).
                _valveText.text = HudFormatter.FormatValveCount(
                    _valves.OpenedCount, _valves.RequiredOpenCount);
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

            Color accent = InteractableColor();
            var closed = new Color(0.25f, 0.25f, 0.25f, 0.85f);

            // ★ §12.4 감쇠 색은 회전 색(accent)과 반드시 달라야 한다.
            Color decay = DecayColor;

            // §6.1-2 역류 경고 색(§16.2 팔레트의 술래 적색).
            var warn = new Color(0.94f, 0.42f, 0.30f, 1f);

            // §6.1-0 비활성(잠금) 색.
            var locked = new Color(0.16f, 0.16f, 0.2f, 0.7f);

            for (int i = 0; i < _valvePips.Length; i++)
            {
                Image pip = _valvePips[i];
                if (pip == null)
                    continue;

                // 스프린트 18b: 밸브 목록은 맵 로드·언로드에 따라 바뀌므로 매 프레임 집계기에서
                // 최신 배열을 읽는다(집계기가 씬 이벤트로 재스캔한다). 맵이 없으면 길이 0이라
                // 모든 핍이 숨는다.
                // §12.4 "밸브 상태 슬롯 — 활성 개수만큼(3~4칸)". 잠긴 밸브는 슬롯을 차지하지 않는다
                // (잠금은 §12.4 브리핑 평면도가 보여준다).
                ValveBehaviour[] valves = _valves.Valves;
                ValveBehaviour valve = ActiveValveAt(valves, i);
                if (valve == null)
                {
                    pip.enabled = false;
                    continue;
                }

                pip.enabled = true;

                // §6.1-0 비활성 밸브는 잠금 표시 — 상호작용이 거부되므로 진행도를 안 보인다.
                if (!valve.IsActiveThisRound)
                {
                    pip.color = locked;
                    continue;
                }

                switch (valve.State)
                {
                    case ValveState.Open:
                        pip.color = accent;
                        break;

                    case ValveState.Rotating:
                        pip.color = Color.Lerp(closed, accent, Mathf.Clamp01(valve.Progress01));
                        break;

                    case ValveState.Reflowing:
                        // §6.1-2 "HUD 밸브 아이콘 점멸 — 역류 시작 시점부터 30초간"
                        pip.color = Mathf.Repeat(Time.time, 1f) < 0.5f ? warn : accent;
                        break;

                    default:
                        // ★ §12.4 감쇠 중에는 **회전 중과 다른 색**으로 같은 진행도를 그린다.
                        //   이건 연출이 아니라 정보다 — 구분이 안 되면 "지금 뺄까, 1초 더
                        //   돌릴까" 판단 자체가 불가능해진다(v0.4의 핵심).
                        if (valve.Progress01 > 0f)
                        {
                            pip.color = Color.Lerp(closed,
                                valve.IsDecaying ? decay : accent,
                                Mathf.Clamp01(valve.Progress01));
                        }
                        else
                        {
                            pip.color = closed;
                        }

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
