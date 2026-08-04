using Marco.Core.Settings;
using Marco.Core.Voice;
using Voice = Marco.Presentation.Voice;
using Marco.Presentation.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// §12.6 설정 화면(스프린트 23). 진입점은 §12.1 타이틀 화면의 "설정 ⚙"이며,
    /// 어디서든 열 수 있도록 키(<see cref="_toggleKey"/>)로도 연다.
    ///
    /// **스프린트 16~18 UI 패턴 그대로**: 런타임 uGUI 구축(씬 저장 누락 사고가 불가능),
    /// legacy <see cref="Text"/>(TMP Essentials 미설치), <c>ColorPalette</c> 폴백 색.
    ///
    /// **§12.6 4탭 중 지금 실제로 동작하는 항목만 조작 가능**하게 두고, 선행 시스템이 없는
    /// 항목은 도식 위치에 "준비 중"으로 남긴다(§12.2 "솔로 연습장 — 준비 중" 선례).
    /// 없는 기능을 조작 가능한 것처럼 보여주면 "설정했는데 아무 일도 안 일어나는" 상태가
    /// 되기 때문이다 — 상세는 GAP-42.
    ///
    /// 정식 마우스 위젯(드롭다운·슬라이더 드래그)은 §12.6 도식에 있으나 이 프로젝트에는
    /// 아직 포인터 UI 배선이 없어, 키보드 조작(↑↓ 항목 이동, ←→ 값 변경)으로 제공한다(GAP-44).
    /// </summary>
    public sealed class SettingsScreen : MonoBehaviour
    {
        private enum Row
        {
            Colorblind,
            MouseSensitivity,
            InvertY,
            FieldOfView,
            MasterVolume,
            ShowFrameRate,
            VoiceMode,
            InputGain,
            Calibrate,
            RestoreDefaults
        }

        private const int RowCount = 10;

        [Header("키 (정식 포인터 UI는 GAP-44)")]
        [SerializeField] private Key _toggleKey = Key.F1;
        [SerializeField] private Key _closeKey = Key.Escape;

        [Header("표시")]
        [SerializeField, Range(12, 48)] private int _bodyFontSize = 22;
        [SerializeField] private Palette.ColorPalette _palette;

        private GameObject _root;
        private Text _titleText;
        private Text[] _rowTexts;
        private Text _footerText;
        private Row _selected = Row.Colorblind;
        private bool _visible;

        private void Awake()
        {
            BuildUi();
            SetVisible(false);
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("Settings Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400; // 설정은 결과 화면(200)·메인 메뉴(300)보다 위

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Settings Root");
            _root.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var rootRect = _root.AddComponent<RectTransform>();
            StretchFull(rootRect);

            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(_root.transform, worldPositionStays: false);
            StretchFull(dimGo.AddComponent<RectTransform>());
            var dim = dimGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.88f);
            dim.raycastTarget = false;

            Font font = ResolveFont();
            _titleText = CreateText(font, "Title", _bodyFontSize + 10, new Vector2(0.5f, 0.86f));
            _titleText.text = "설정 (§12.6)";

            _rowTexts = new Text[RowCount];
            for (int i = 0; i < RowCount; i++)
                _rowTexts[i] = CreateText(font, $"Row{i}", _bodyFontSize, new Vector2(0.5f, 0.70f - i * 0.07f));

            _footerText = CreateText(font, "Footer", _bodyFontSize - 4, new Vector2(0.5f, 0.16f));
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_toggleKey].wasPressedThisFrame)
            {
                SetVisible(!_visible);
                return;
            }

            if (!_visible)
                return;

            if (keyboard[_closeKey].wasPressedThisFrame)
            {
                SetVisible(false);
                return;
            }

            HandleNavigation(keyboard);
            Redraw();
        }

        private void HandleNavigation(Keyboard keyboard)
        {
            if (keyboard.downArrowKey.wasPressedThisFrame)
                _selected = (Row)(((int)_selected + 1) % RowCount);
            else if (keyboard.upArrowKey.wasPressedThisFrame)
                _selected = (Row)(((int)_selected + RowCount - 1) % RowCount);

            bool left = keyboard.leftArrowKey.wasPressedThisFrame;
            bool right = keyboard.rightArrowKey.wasPressedThisFrame;
            bool activate = keyboard.enterKey.wasPressedThisFrame;

            if (!left && !right && !activate)
                return;

            GameSettings settings = SettingsStore.Current;
            float step = right ? 1f : -1f;

            switch (_selected)
            {
                case Row.Colorblind:
                    settings.ColorblindMode = !settings.ColorblindMode;
                    break;
                case Row.InvertY:
                    settings.InvertY = !settings.InvertY;
                    break;
                case Row.ShowFrameRate:
                    settings.ShowFrameRate = !settings.ShowFrameRate;
                    break;
                case Row.MouseSensitivity:
                    settings.MouseSensitivity += step * 0.02f;
                    break;
                case Row.FieldOfView:
                    settings.FieldOfView += step * 5f;
                    break;
                case Row.MasterVolume:
                    settings.MasterVolume += step * 0.05f;
                    break;
                case Row.VoiceMode:
                    // §5.8: PTT를 고르면 "핵심 긴장감이 줄어든다"는 안내를 노출해야 한다.
                    settings.VoiceMode = settings.VoiceMode == VoiceActivationMode.VoiceActivation
                        ? VoiceActivationMode.PushToTalk
                        : VoiceActivationMode.VoiceActivation;
                    break;
                case Row.InputGain:
                    settings.InputGainDb += step * 1f;
                    break;
                case Row.Calibrate:
                    // §12.6 "발화 감도 재보정 | 버튼 → 3초 프롬프트(5.2절) 재실행"
                    if (activate)
                    {
                        var pipeline = FindAnyObjectByType<Voice.LocalVoicePipeline>();
                        if (pipeline != null)
                            pipeline.BeginCalibration();
                        else
                            Debug.LogWarning("[Settings] 음성 파이프라인이 씬에 없어 재보정을 시작할 수 없습니다.");
                    }

                    return;
                case Row.RestoreDefaults:
                    if (activate)
                    {
                        SettingsStore.RestoreDefaults();
                        Debug.Log("[Settings] §12.6 기본값 복원");
                        return;
                    }

                    return;
            }

            SettingsStore.Apply(settings);
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null)
                _root.SetActive(visible);

            if (visible)
                Redraw();
        }

        private void Redraw()
        {
            GameSettings s = SettingsStore.Current;

            SetRow(Row.Colorblind, $"색맹 모드          {OnOff(s.ColorblindMode)}");
            SetRow(Row.MouseSensitivity, $"마우스 감도        {s.MouseSensitivity:0.00}");
            SetRow(Row.InvertY, $"Y축 반전           {OnOff(s.InvertY)}");
            SetRow(Row.FieldOfView, $"FOV                {s.FieldOfView:0}°");
            SetRow(Row.MasterVolume, $"마스터 볼륨        {s.MasterVolume * 100f:0}%");
            SetRow(Row.ShowFrameRate, $"프레임 표시        {OnOff(s.ShowFrameRate)}");
            SetRow(Row.VoiceMode, $"발화 방식          {(s.VoiceMode == VoiceActivationMode.VoiceActivation ? "VAD(기본)" : "PTT(대체)")}");
            SetRow(Row.InputGain, $"입력 게인          {s.InputGainDb:+0.#;-0.#;0} dB");
            SetRow(Row.Calibrate, CalibrationRowText(s));
            SetRow(Row.RestoreDefaults, "기본값 복원        [Enter]");

            _footerText.text =
                "↑↓ 항목 · ←→ 변경 · Enter 실행 · Esc 닫기\n" +
                "준비 중(선행 시스템 필요): 마이크 장치 선택 · 효과음/UI 개별 볼륨 · " +
                "밝기 · 키 리바인딩 · 파문 자막 · 오프닝 가이드" +
                (SettingsStore.Current.VoiceMode == VoiceActivationMode.PushToTalk
                    ? "\n⚠ PTT는 대체 방식입니다 — 말이 새어나가는 긴장감이 줄어듭니다(§5.8)."
                    : string.Empty);
            _footerText.color = new Color(0.5f, 0.5f, 0.5f);
        }

        private void SetRow(Row row, string text)
        {
            int index = (int)row;
            bool selected = row == _selected;

            _rowTexts[index].text = selected ? $"▶ {text}" : $"   {text}";
            _rowTexts[index].color = selected ? Highlight() : Neutral();
        }

        private static string OnOff(bool value) => value ? "켬" : "끔";

        /// <summary>§12.6 "발화 감도 재보정" 행. 측정 중이면 남은 초를, 아니면 저장된 보정값을 보여준다.</summary>
        private string CalibrationRowText(GameSettings s)
        {
            var pipeline = FindAnyObjectByType<Voice.LocalVoicePipeline>();
            if (pipeline != null && pipeline.IsCalibrating)
                return $"발화 감도 재보정    측정 중… {pipeline.CalibrationRemaining:0.0}초 (가장 작은 목소리로)";

            string current = Mathf.Approximately(s.VoiceCalibrationOffsetDb, 0f)
                ? "미보정"
                : $"{s.VoiceCalibrationOffsetDb:+0.0;-0.0} dB";

            return $"발화 감도 재보정    {current}  [Enter]";
        }

        private Color Highlight() =>
            _palette != null ? _palette.GetInteractable(SettingsStore.Current.ColorblindMode)
                             : new Color(1f, 0.72f, 0.30f);

        private Color Neutral() => new Color(0.91f, 0.91f, 0.91f);

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 24);

            return font;
        }

        private Text CreateText(Font font, string name, int fontSize, Vector2 anchor)
        {
            var go = new GameObject($"Settings_{name}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1600f, fontSize * 3f);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
