using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Marco.Core.Objectives;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Sound;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// §12.5 결과 화면(스프린트 17, 정식 UI 2단계). 라운드가 끝나면 화면 전체를 덮고
    /// 승패·사유를 보여주며, 다음 라운드를 시작할 수 있게 한다.
    ///
    /// **새 판정 로직이 없다** — 이미 서버 권위로 동기화되는 <see cref="RoundResult"/>를
    /// <see cref="RoundCoordinator"/>에서 읽어 표시하고, 재시작 요청도 그쪽으로 넘긴다
    /// (네트워크/로컬 경로 선택은 코디네이터가 이미 한다).
    ///
    /// **스프린트 16 HUD와 같은 방식**으로 uGUI 계층을 런타임에 구축한다(씬 YAML·에디터 툴을
    /// 거치지 않아 "툴 실행 후 씬 저장 누락" 사고가 불가능하다). legacy <see cref="Text"/>를
    /// 쓰는 이유도 같다 — TMP Essentials가 이 프로젝트에 없다.
    ///
    /// **이번 스코프에서 제외된 §12.5 요소**: 어워드 카드 3종(§8 집계 로직 필요), 사운드맵
    /// 타임랩스·클립 저장(**기획서가 "포스트 MVP"로 명시**), 리매치 투표 15초·과반(로비 3단계).
    /// 어워드 자리에는 다음 단계를 알리는 플레이스홀더 한 줄만 둔다.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        [Header("참조 (비우면 씬에서 찾는다)")]
        [SerializeField] private RoundCoordinator _round;
        [SerializeField] private Palette.ColorPalette _palette;

        [Tooltip("색맹 모드의 단일 진실 소스(§19). 비우면 씬에서 찾는다.")]
        [SerializeField] private PulseVisualRenderer _colorblindSource;
        [SerializeField] private bool _colorblindFallback;

        [Header("표시")]
        [SerializeField, Range(24, 96)] private int _bannerFontSize = 64;
        [SerializeField, Range(12, 48)] private int _bodyFontSize = 24;

        [Tooltip("배경 암막 알파. §16.1 흑 배경 컨셉에 맞춰 완전 불투명에 가깝게 둔다.")]
        [SerializeField, Range(0f, 1f)] private float _dimAlpha = 0.88f;

        [Header("재시작")]
        [Tooltip("다음 라운드 시작 키. 정식 리매치 투표(§12.5)가 생기면 버튼 UI로 대체된다.")]
        [SerializeField] private Key _restartKey = Key.Enter;

        [Tooltip("결과 화면이 뜬 뒤 이 시간(초)이 지나야 재시작을 받는다 — 오조작 방지.")]
        [SerializeField, Range(0f, 5f)] private float _restartLockSeconds = 1f;

        private Canvas _canvas;
        private GameObject _root;
        private Image _dim;
        private Text _bannerText;
        private Text _reasonText;
        private Text[] _awardCards;
        private Text _restartHintText;

        private bool _visible;
        private float _shownAt;

        private bool Colorblind =>
            _colorblindSource != null ? _colorblindSource.ColorblindMode : _colorblindFallback;

        private void Awake()
        {
            if (_round == null)
                _round = GetComponent<RoundCoordinator>() ?? FindAnyObjectByType<RoundCoordinator>();
            if (_colorblindSource == null)
                _colorblindSource = FindAnyObjectByType<PulseVisualRenderer>();

            BuildUi();
            SetVisible(false);
        }

        // ── UI 구축 (런타임, 스프린트 16과 같은 방식) ──────────────────────

        private void BuildUi()
        {
            var canvasGo = new GameObject("Result Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // HUD(100)보다 위에 덮어야 한다 — 결과 화면은 전체를 가린다.
            _canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Result Root");
            _root.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var rootRect = _root.AddComponent<RectTransform>();
            StretchFull(rootRect);

            // 암막: §16.1 흑 배경 위에 결과만 떠오르게 한다.
            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(_root.transform, worldPositionStays: false);
            StretchFull(dimGo.AddComponent<RectTransform>());
            _dim = dimGo.AddComponent<Image>();
            _dim.color = new Color(0f, 0f, 0f, _dimAlpha);
            _dim.raycastTarget = false;

            Font font = ResolveFont();

            _bannerText = CreateText(font, "Banner", _bannerFontSize, new Vector2(0.5f, 0.62f));
            _reasonText = CreateText(font, "Reason", _bodyFontSize, new Vector2(0.5f, 0.52f));
            _restartHintText = CreateText(font, "RestartHint", _bodyFontSize, new Vector2(0.5f, 0.28f));

            // §12.5 어워드 카드 3종(스프린트 22) — 도식대로 가로 3열로 배치한다.
            _awardCards = new Text[3];
            _awardCards[0] = CreateText(font, "AwardScream", _bodyFontSize, new Vector2(0.25f, 0.42f));
            _awardCards[1] = CreateText(font, "AwardSilent", _bodyFontSize, new Vector2(0.5f, 0.42f));
            _awardCards[2] = CreateText(font, "AwardLiar", _bodyFontSize, new Vector2(0.75f, 0.42f));
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>스프린트 16과 같은 3단 폰트 폴백(TMP 미사용 사유는 클래스 주석 참고).</summary>
        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 24);

            if (font == null)
                Debug.LogWarning("[Result] 빌트인 폰트를 찾지 못했습니다 — 결과 화면 텍스트가 보이지 않습니다.");

            return font;
        }

        private Text CreateText(Font font, string name, int fontSize, Vector2 anchor)
        {
            var go = new GameObject($"Result_{name}");
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

        // ── 표시·입력 ─────────────────────────────────────────────────────

        private void Update()
        {
            if (_round == null)
                return;

            RoundResult result = _round.Result;
            bool shouldShow = result != RoundResult.InProgress;

            if (shouldShow != _visible)
            {
                SetVisible(shouldShow);
                if (shouldShow)
                {
                    _shownAt = Time.time;
                    Refresh(result);
                }
            }
            else if (shouldShow)
            {
                // 색맹 토글·잠금 해제 안내가 실시간으로 반영되게 갱신한다.
                Refresh(result);
            }

            if (_visible)
                HandleRestartInput();
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null)
                _root.SetActive(visible);
        }

        private void Refresh(RoundResult result)
        {
            // §12.5 "승패 배너 | 승리 진영 색(도망자=시안 / 술래=레드, 색맹 모드는 §16.2 대체 팔레트)"
            Color winnerColor = result == RoundResult.SeekerWin ? SeekerColor() : RunnerColor();

            _bannerText.text = HudFormatter.FormatRoundResult(result);
            _bannerText.color = winnerColor;

            _reasonText.text = HudFormatter.FormatResultReason(result, _round.RemainingSeconds);
            _reasonText.color = NeutralColor();

            // §12.5 어워드 카드 3종 — §8 판정 결과(서버 확정)를 그대로 보여준다.
            Color awardColor = new Color(NeutralColor().r, NeutralColor().g, NeutralColor().b, 0.85f);
            _awardCards[0].text = HudFormatter.FormatAward("최다 비명상", _round.AwardLoudestScream);
            _awardCards[1].text = HudFormatter.FormatAward("무성 생존상", _round.AwardSilentSurvivor);
            _awardCards[2].text = HudFormatter.FormatAward("최고의 거짓말상", _round.AwardBestLiar);

            for (int i = 0; i < _awardCards.Length; i++)
                _awardCards[i].color = awardColor;

            // 스프린트 18: 네트워크면 §12.5 리매치 투표 상태(찬성 수·15초 창)를, 로컬이면
            // 기존 즉시 재시작 안내를 보여준다. Enter는 양쪽 모두 RequestRestart로 이어지며,
            // 네트워크에서는 "찬성 1표"를 의미한다(중복 투표는 서버가 멱등 처리).
            if (_round.IsNetworkActive)
            {
                _restartHintText.text = RestartUnlocked
                    ? $"{HudFormatter.FormatRematchVote(_round.RematchVotesFor, _round.RematchVotesNeeded, _round.RematchSecondsRemaining)}   ·   {_restartKey} — 찬성"
                    : HudFormatter.FormatRematchVote(_round.RematchVotesFor, _round.RematchVotesNeeded, _round.RematchSecondsRemaining);
            }
            else
            {
                _restartHintText.text = RestartUnlocked
                    ? $"{_restartKey} — 다음 라운드"
                    : "…";
            }
            _restartHintText.color = winnerColor;

            if (_dim != null)
                _dim.color = new Color(0f, 0f, 0f, _dimAlpha);
        }

        private bool RestartUnlocked => Time.time - _shownAt >= _restartLockSeconds;

        private void HandleRestartInput()
        {
            if (!RestartUnlocked)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_restartKey].wasPressedThisFrame)
                _round.RequestRestart();
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
