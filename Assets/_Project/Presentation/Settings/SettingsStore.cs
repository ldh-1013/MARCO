using Marco.Core.Settings;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;
using UnityEngine;

namespace Marco.Presentation.Settings
{
    /// <summary>
    /// §12.6 설정의 저장·불러오기·적용(스프린트 23).
    ///
    /// **왜 정적인가**: 설정은 씬을 넘나들며(§15.1 Boot→MainMenu→Lobby→Game) 유지돼야 하고,
    /// 적용 대상(플레이어 pawn·파문 렌더러)은 네트워크 스폰이라 시점이 제각각이다. 다른
    /// 레지스트리들과 같은 정적 지연 바인딩 패턴을 쓴다.
    ///
    /// **값의 규칙은 Core**(<see cref="GameSettings"/>)가 갖고, 여기서는 <see cref="PlayerPrefs"/>
    /// 입출력과 Unity 객체 적용만 한다 — 그래야 규칙을 Unity 없이 테스트할 수 있다.
    /// </summary>
    public static class SettingsStore
    {
        private const string KeyColorblind = "marco.settings.colorblind";
        private const string KeyInvertY = "marco.settings.invertY";
        private const string KeyShowFps = "marco.settings.showFps";
        private const string KeySensitivity = "marco.settings.mouseSensitivity";
        private const string KeyFov = "marco.settings.fov";
        private const string KeyMasterVolume = "marco.settings.masterVolume";
        private const string KeyVoiceMode = "marco.settings.voiceMode";      // §12.6 발화 방식(VAD/PTT)
        private const string KeyInputGain = "marco.settings.inputGainDb";    // §12.6 입력 게인
        private const string KeyVoiceCalibration = "marco.settings.voiceCalibrationDb"; // §5.2-3 개인 보정

        private static GameSettings _current = GameSettings.Default;
        private static bool _loaded;

        /// <summary>현재 설정. 처음 접근하면 저장된 값을 불러온다.</summary>
        public static GameSettings Current
        {
            get
            {
                if (!_loaded)
                    Load();

                return _current;
            }
        }

        /// <summary>저장된 설정을 읽어 온다(손상된 값은 <see cref="GameSettings.Clamped"/>가 되돌린다).</summary>
        public static void Load()
        {
            GameSettings defaults = GameSettings.Default;

            var loaded = new GameSettings
            {
                ColorblindMode = GetBool(KeyColorblind, defaults.ColorblindMode),
                InvertY = GetBool(KeyInvertY, defaults.InvertY),
                ShowFrameRate = GetBool(KeyShowFps, defaults.ShowFrameRate),
                MouseSensitivity = PlayerPrefs.GetFloat(KeySensitivity, defaults.MouseSensitivity),
                FieldOfView = PlayerPrefs.GetFloat(KeyFov, defaults.FieldOfView),
                MasterVolume = PlayerPrefs.GetFloat(KeyMasterVolume, defaults.MasterVolume),
                VoiceMode = (Marco.Core.Voice.VoiceActivationMode)PlayerPrefs.GetInt(
                    KeyVoiceMode, (int)defaults.VoiceMode),
                InputGainDb = PlayerPrefs.GetFloat(KeyInputGain, defaults.InputGainDb),
                VoiceCalibrationOffsetDb = PlayerPrefs.GetFloat(KeyVoiceCalibration,
                                                                defaults.VoiceCalibrationOffsetDb)
            };

            _current = loaded.Clamped();
            _loaded = true;
        }

        /// <summary>설정을 바꾸고 즉시 저장·적용한다.</summary>
        public static void Apply(GameSettings settings)
        {
            _current = settings.Clamped();
            _loaded = true;

            PlayerPrefs.SetInt(KeyColorblind, _current.ColorblindMode ? 1 : 0);
            PlayerPrefs.SetInt(KeyInvertY, _current.InvertY ? 1 : 0);
            PlayerPrefs.SetInt(KeyShowFps, _current.ShowFrameRate ? 1 : 0);
            PlayerPrefs.SetFloat(KeySensitivity, _current.MouseSensitivity);
            PlayerPrefs.SetFloat(KeyFov, _current.FieldOfView);
            PlayerPrefs.SetFloat(KeyMasterVolume, _current.MasterVolume);
            PlayerPrefs.SetInt(KeyVoiceMode, (int)_current.VoiceMode);
            PlayerPrefs.SetFloat(KeyInputGain, _current.InputGainDb);
            PlayerPrefs.SetFloat(KeyVoiceCalibration, _current.VoiceCalibrationOffsetDb);
            PlayerPrefs.Save();

            ApplyToScene();
        }

        /// <summary>§12.6 "기본값 복원".</summary>
        public static void RestoreDefaults() => Apply(GameSettings.Default);

        /// <summary>
        /// 현재 설정을 살아 있는 오브젝트에 반영한다. 플레이어가 나중에 스폰돼도 맞도록
        /// 설정 변경 시점과 플레이어 준비 시점 **양쪽**에서 호출된다.
        /// </summary>
        public static void ApplyToScene()
        {
            GameSettings settings = Current;

            // §12.6 오디오 탭 "마스터 볼륨". 개별 효과음/UI 볼륨은 AudioMixer가 없어 미구현(GAP-42).
            AudioListener.volume = settings.MasterVolume;

            // §12.6 비디오 탭 "색맹 모드"(§16.2) — C 키 토글과 같은 상태를 공유한다.
            var visuals = Object.FindAnyObjectByType<PulseVisualRenderer>();
            if (visuals != null)
                visuals.SetColorblindMode(settings.ColorblindMode);

            // §12.6 조작 탭 "마우스 감도"·"Y축 반전", 비디오 탭 "FOV".
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player != null)
            {
                player.ApplyLookSettings(settings.MouseSensitivity, settings.InvertY);

                Camera cam = player.GetComponentInChildren<Camera>(includeInactive: true);
                if (cam != null)
                    cam.fieldOfView = settings.FieldOfView;
            }
        }

        /// <summary>
        /// 로컬 플레이어가 스폰되면 설정을 다시 적용하도록 예약한다. 씬 진입 컴포넌트가 한 번 부른다.
        /// </summary>
        public static void ApplyWhenPlayerReady()
        {
            ApplyToScene();
            LocalPlayerRegistry.WhenReady(_ => ApplyToScene());
        }

        private static bool GetBool(string key, bool fallback) =>
            PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0;

        /// <summary>도메인 리로드를 끈 상태에서 Play를 반복해도 저장값을 다시 읽도록 초기화한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            _loaded = false;
            _current = GameSettings.Default;
        }
    }
}
