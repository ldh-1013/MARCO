namespace Marco.Core.Settings
{
    /// <summary>
    /// §12.6 설정 화면이 다루는 값들(스프린트 23). 저장·적용은 Presentation이 하고,
    /// 여기서는 **기본값과 유효 범위만** 관리한다 — Unity 없이 검증할 수 있게 하려는 것이다.
    ///
    /// **기획서에 기본값·범위가 명시된 항목만 상수로 못박았다**:
    /// FOV 75°~100°(기본 90°), 마스터 볼륨 80%, 색맹 모드 끔, 프레임 표시 끔.
    /// 마우스 감도는 범위·기본값 명시가 없어(GAP-43) 현재 코드값을 기본으로 삼고
    /// 조작 가능한 범위를 정했다.
    ///
    /// §12.6의 나머지 항목(마이크 장치·입력 게인·발화 감도 재보정·효과음 개별 볼륨·
    /// 밝기·키 리바인딩·VAD/PTT·파문 자막·오프닝 가이드)은 선행 시스템이 없어 값 자체를
    /// 두지 않았다 — 만들어 두면 "설정은 있는데 아무 효과가 없는" 상태가 되기 때문이다(GAP-42).
    /// </summary>
    public struct GameSettings
    {
        // §12.6 비디오 탭 — 원문에 명시된 값.
        public const float MinFieldOfView = 75f;
        public const float MaxFieldOfView = 100f;
        public const float DefaultFieldOfView = 90f;

        /// <summary>§12.6 "마스터 볼륨 | 슬라이더 | 80%".</summary>
        public const float DefaultMasterVolume = 0.8f;

        /// <summary>
        /// 마우스 감도. §12.6은 슬라이더라고만 하고 범위·기본값이 없다(GAP-43).
        /// 기본값은 <c>FirstPersonController</c>가 쓰던 값(0.12)을 그대로 옮겨, 설정 화면이
        /// 생겼다는 이유만으로 조작감이 달라지지 않게 했다.
        /// </summary>
        public const float DefaultMouseSensitivity = 0.12f;
        public const float MinMouseSensitivity = 0.02f;
        public const float MaxMouseSensitivity = 0.60f;

        public bool ColorblindMode;
        public bool InvertY;
        public bool ShowFrameRate;
        public float MouseSensitivity;
        public float FieldOfView;
        public float MasterVolume;

        /// <summary>§12.6 "기본값 복원" 버튼이 되돌릴 상태.</summary>
        public static GameSettings Default => new GameSettings
        {
            ColorblindMode = false,      // §12.6 색맹 모드 기본 "끔"
            InvertY = false,             // §4.3 "Y축 반전 옵션" — 기본 끔(명시 없음)
            ShowFrameRate = false,       // §12.6 프레임 표시 기본 "끔"
            MouseSensitivity = DefaultMouseSensitivity,
            FieldOfView = DefaultFieldOfView,
            MasterVolume = DefaultMasterVolume
        };

        /// <summary>저장값이 손상됐거나 범위를 벗어나도 안전한 값으로 되돌린다.</summary>
        public GameSettings Clamped()
        {
            GameSettings result = this;
            result.MouseSensitivity = Clamp(MouseSensitivity, MinMouseSensitivity, MaxMouseSensitivity,
                                            DefaultMouseSensitivity);
            result.FieldOfView = Clamp(FieldOfView, MinFieldOfView, MaxFieldOfView, DefaultFieldOfView);
            result.MasterVolume = Clamp(MasterVolume, 0f, 1f, DefaultMasterVolume);
            return result;
        }

        /// <summary>NaN·무한대는 기본값으로 되돌린다(손상된 PlayerPrefs 방어).</summary>
        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;

            if (value < min)
                return min;

            return value > max ? max : value;
        }
    }
}
