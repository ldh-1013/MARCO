using Marco.Core.Settings;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 23 §12.6 설정 값. 순수 구조체라 Unity 없이 그대로 검증된다.
    /// </summary>
    public class GameSettingsTests
    {
        [Test]
        public void Defaults_MatchDesignDoc()
        {
            GameSettings d = GameSettings.Default;

            // §12.6 비디오 탭: FOV 기본 90°, 색맹 모드 "끔", 프레임 표시 "끔".
            Assert.AreEqual(90f, d.FieldOfView, 0.001f);
            Assert.IsFalse(d.ColorblindMode);
            Assert.IsFalse(d.ShowFrameRate);

            // §12.6 오디오 탭: 마스터 볼륨 80%.
            Assert.AreEqual(0.8f, d.MasterVolume, 0.001f);
        }

        [Test]
        public void FieldOfViewRange_MatchesDesignDoc()
        {
            // §12.6 "FOV | 슬라이더(75°~100°)".
            Assert.AreEqual(75f, GameSettings.MinFieldOfView, 0.001f);
            Assert.AreEqual(100f, GameSettings.MaxFieldOfView, 0.001f);
        }

        [Test]
        public void DefaultSensitivity_PreservesExistingFeel()
        {
            // 설정 화면이 생겼다는 이유만으로 조작감이 달라지면 안 된다
            // (FirstPersonController가 쓰던 값 그대로).
            Assert.AreEqual(0.12f, GameSettings.DefaultMouseSensitivity, 0.0001f);
        }

        [Test]
        public void Clamped_PullsValuesIntoRange()
        {
            var settings = new GameSettings
            {
                FieldOfView = 400f,
                MouseSensitivity = 99f,
                MasterVolume = 5f
            };

            GameSettings clamped = settings.Clamped();

            Assert.AreEqual(GameSettings.MaxFieldOfView, clamped.FieldOfView, 0.001f);
            Assert.AreEqual(GameSettings.MaxMouseSensitivity, clamped.MouseSensitivity, 0.001f);
            Assert.AreEqual(1f, clamped.MasterVolume, 0.001f);
        }

        [Test]
        public void Clamped_RaisesValuesBelowMinimum()
        {
            var settings = new GameSettings
            {
                FieldOfView = 10f,
                MouseSensitivity = 0f,
                MasterVolume = -3f
            };

            GameSettings clamped = settings.Clamped();

            Assert.AreEqual(GameSettings.MinFieldOfView, clamped.FieldOfView, 0.001f);
            Assert.AreEqual(GameSettings.MinMouseSensitivity, clamped.MouseSensitivity, 0.001f);
            Assert.AreEqual(0f, clamped.MasterVolume, 0.001f);
        }

        [Test]
        public void Clamped_RecoversFromCorruptedValues()
        {
            // PlayerPrefs가 손상되면 NaN이 들어올 수 있다 — 기본값으로 되돌려야 한다.
            var settings = new GameSettings
            {
                FieldOfView = float.NaN,
                MouseSensitivity = float.PositiveInfinity,
                MasterVolume = float.NaN
            };

            GameSettings clamped = settings.Clamped();

            Assert.AreEqual(GameSettings.DefaultFieldOfView, clamped.FieldOfView, 0.001f);
            Assert.AreEqual(GameSettings.DefaultMouseSensitivity, clamped.MouseSensitivity, 0.001f);
            Assert.AreEqual(GameSettings.DefaultMasterVolume, clamped.MasterVolume, 0.001f);
        }

        [Test]
        public void Clamped_LeavesValidValuesUntouched()
        {
            var settings = new GameSettings
            {
                ColorblindMode = true,
                InvertY = true,
                ShowFrameRate = true,
                FieldOfView = 85f,
                MouseSensitivity = 0.2f,
                MasterVolume = 0.5f
            };

            GameSettings clamped = settings.Clamped();

            Assert.AreEqual(85f, clamped.FieldOfView, 0.001f);
            Assert.AreEqual(0.2f, clamped.MouseSensitivity, 0.001f);
            Assert.AreEqual(0.5f, clamped.MasterVolume, 0.001f);
            Assert.IsTrue(clamped.ColorblindMode);
            Assert.IsTrue(clamped.InvertY);
            Assert.IsTrue(clamped.ShowFrameRate);
        }

        [Test]
        public void Default_IsAlreadyWithinRange()
        {
            GameSettings d = GameSettings.Default;
            GameSettings clamped = d.Clamped();

            Assert.AreEqual(d.FieldOfView, clamped.FieldOfView, 0.001f);
            Assert.AreEqual(d.MouseSensitivity, clamped.MouseSensitivity, 0.001f);
            Assert.AreEqual(d.MasterVolume, clamped.MasterVolume, 0.001f);
        }

        [Test]
        public void SensitivityRange_IsUsable()
        {
            // 최소가 최대보다 작고, 기본값이 그 안에 들어와야 슬라이더가 성립한다.
            Assert.Less(GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity);
            Assert.GreaterOrEqual(GameSettings.DefaultMouseSensitivity, GameSettings.MinMouseSensitivity);
            Assert.LessOrEqual(GameSettings.DefaultMouseSensitivity, GameSettings.MaxMouseSensitivity);
        }
    }
}
