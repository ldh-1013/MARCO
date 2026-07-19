using UnityEngine;

namespace Marco.Presentation.Palette
{
    /// <summary>
    /// §16.2 컬러 팔레트. 기본/색맹 모드 두 세트를 하나의 에셋에 담아
    /// 런타임에 토글만으로 전환할 수 있게 한다(§12.6 접근성 탭, §19).
    /// </summary>
    [CreateAssetMenu(fileName = "ColorPalette", menuName = "Marco/Color Palette")]
    public sealed class ColorPalette : ScriptableObject
    {
        [Header("배경")]
        public Color background = HexToColor("#000000");
        public Color backgroundColorblind = HexToColor("#000000"); // 변경 없음(§16.2)

        [Header("환경 파문")]
        public Color environmentPulse = HexToColor("#E8E8E8"); // 흰빛 계열, 투명 감쇠
        public Color environmentPulseColorblind = HexToColor("#E8E8E8"); // 변경 없음(§16.2)

        [Header("러너(플레이어)")]
        public Color runner = HexToColor("#35F0D0");
        public Color runnerColorblind = HexToColor("#F2E205");

        [Header("술래(레드)")]
        public Color seeker = HexToColor("#FF3B4D");
        public Color seekerColorblind = HexToColor("#3357FF");

        [Header("상호작용 오브젝트(주변)")]
        public Color interactable = HexToColor("#FFB84D");
        public Color interactableColorblind = HexToColor("#C98A2E");

        public Color GetRunner(bool colorblindMode) => colorblindMode ? runnerColorblind : runner;
        public Color GetSeeker(bool colorblindMode) => colorblindMode ? seekerColorblind : seeker;
        public Color GetInteractable(bool colorblindMode) => colorblindMode ? interactableColorblind : interactable;
        public Color GetEnvironmentPulse(bool colorblindMode) => colorblindMode ? environmentPulseColorblind : environmentPulse;
        public Color GetBackground(bool colorblindMode) => colorblindMode ? backgroundColorblind : background;

        private static Color HexToColor(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color color);
            return color;
        }
    }
}
