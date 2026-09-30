using System.Reflection;
using Marco.Core.Net;
using Marco.Presentation.GameFlow;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 접속 중 화면 · 메뉴 복귀(09-30 실기 결함). 원인: 로비 UI(<c>LobbyScreen</c>)가 NetworkObject(PulseSystem)에 붙어 있어 FishNet이
    /// 연결 전에 끄고, 연결이 실패하면 다시 켜지지 않았다 — 실패 안내 · Esc · 메뉴 복귀가 전부 없었다. 실제 컴포넌트를 부른다.
    /// </summary>
    public class JoinProgressOverlayTests
    {
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private static readonly JoinAddress Target = new JoinAddress("127.0.0.1", 7999);

        private sealed class FakeConnection : IConnectionService
        {
            public bool HasStarted { get; set; }
            public int StopAllCalls { get; private set; }
            public JoinAttempt Attempt { get; } = new JoinAttempt();
            public string DefaultAddress => "localhost";
            public string AddressLine => string.Empty;
            public string LastFailure => Attempt.FailureMessage;
            public JoinAddress RetryAddress => Target;
            public void StartHost() { }
            public void StartClient(JoinAddress address) { }
            public void Cancel() => Attempt.Cancel();
            public void StopAll() => StopAllCalls++;
        }

        private static void Lifecycle(Component component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(component, null);
        }

        private FakeConnection _fake;
        private GameObject _go;
        private JoinProgressOverlay _overlay;

        [SetUp]
        public void SetUp()
        {
            MainMenuScreen.ResetSessionState();
            _fake = new FakeConnection();
            ConnectionServiceRegistry.Register(_fake);
            _go = new GameObject("SceneFlow");
            _overlay = _go.AddComponent<JoinProgressOverlay>();
            Lifecycle(_overlay, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            ConnectionServiceRegistry.Unregister(_fake);
            Object.DestroyImmediate(_go);
            MainMenuScreen.ResetSessionState();
        }

        [Test]
        public void LobbyEntry_CreatesConnectingOverlay_OutsideNetworkObjects()
        {
            Scene lobby = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                LobbyEntry entry = SceneGeometry.FindInScene<LobbyEntry>(lobby);
                Assert.IsNotNull(entry, "Lobby 씬에 LobbyEntry가 없다");
                Lifecycle(entry, "Awake");

                var overlay = entry.GetComponent<JoinProgressOverlay>();
                Assert.IsNotNull(overlay, "로비 진입이 접속 중 화면을 만든다(수정 전: 없음 — 접속 중 표시는 PulseSystem의 LobbyScreen뿐이었다)");
                foreach (Component c in overlay.GetComponentsInParent<Component>(includeInactive: true))
                    Assert.AreNotEqual("NetworkObject", c.GetType().Name,
                        $"접속 중 화면이 NetworkObject('{c.name}') 아래에 있으면 FishNet이 연결 전에 꺼 버린다");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(lobby);
            }
        }

        [Test]
        public void Connecting_ShowsElapsedSecondsAndEscHint()
        {
            _fake.Attempt.BeginJoin(Target, Time.realtimeSinceStartup - 3.2f);
            Lifecycle(_overlay, "Update");

            Assert.IsTrue(_overlay.IsShowing, "접속 중에는 화면이 보인다");
            StringAssert.Contains("127.0.0.1:7999에 접속 중… (3초)", _overlay.ProgressText);
            Assert.AreEqual("Esc — 취소", _overlay.HintText);
            Assert.IsFalse(_overlay.ReturnedToMenu);
        }

        [Test]
        public void Failure_ReturnsToMenu_WithRedNotice_AndLastAddress_AfterStoppingNetwork()
        {
            _fake.Attempt.BeginJoin(Target, Time.realtimeSinceStartup - 6f);
            _fake.Attempt.OnTransportStopped(); // 로그로 확인한 실제 전이: Starting → Stopped(5.9초)
            Lifecycle(_overlay, "Update");

            Assert.IsTrue(_overlay.ReturnedToMenu, "실패하면 메인 메뉴로 돌아간다(수정 전: 빈 월드에 남았다)");
            Assert.AreEqual(1, _fake.StopAllCalls, "돌아가기 전에 네트워크를 정리한다(다시 H/J가 되게)");
            StringAssert.Contains("오타", MainMenuScreen.PendingNotice);
            StringAssert.Contains("방화벽", MainMenuScreen.PendingNotice);
            Assert.IsTrue(MainMenuScreen.PendingNoticeIsError, "빨간 안내");
            Assert.AreEqual("127.0.0.1:7999", MainMenuScreen.RetryJoinText);

            Lifecycle(_overlay, "Update");
            Assert.AreEqual(1, _fake.StopAllCalls, "한 번만 돌아간다");
        }

        [Test]
        public void Cancel_ReturnsToMenu_WithGrayNotice()
        {
            _fake.Attempt.BeginJoin(Target, Time.realtimeSinceStartup);
            _fake.Cancel(); // 취소 호출 이후만 본다 — Esc 입력 읽기 → 취소 호출은 JoinCancelInputTests(10-01)
            Lifecycle(_overlay, "Update");

            Assert.IsTrue(_overlay.ReturnedToMenu);
            StringAssert.Contains("취소", MainMenuScreen.PendingNotice);
            Assert.IsFalse(MainMenuScreen.PendingNoticeIsError);
            Assert.AreEqual("127.0.0.1:7999", MainMenuScreen.RetryJoinText);
        }

        [Test]
        public void Connected_HidesOverlay_StaysInLobby()
        {
            _fake.Attempt.BeginJoin(Target, Time.realtimeSinceStartup);
            _fake.Attempt.OnTransportStarted();
            Lifecycle(_overlay, "Update");

            Assert.IsFalse(_overlay.IsShowing, "연결되면 로비 화면이 이어받는다");
            Assert.IsFalse(_overlay.ReturnedToMenu);
        }

        // ── 메인 메뉴 ────────────────────────────────────────────────────

        [Test]
        public void MainMenu_AfterReturn_ShowsRedNotice_AndJPrefillsLastAddress()
        {
            MainMenuScreen.PrepareReturnFromLobby("127.0.0.1:7999에 접속하지 못했습니다 — ① 주소 · 포트 오타", true, "127.0.0.1:7999");

            var menuGo = new GameObject("MainMenu");
            try
            {
                var menu = menuGo.AddComponent<MainMenuScreen>();
                Lifecycle(menu, "Awake");
                Lifecycle(menu, "Start");

                Text status = null;
                foreach (Text t in menuGo.GetComponentsInChildren<Text>(includeInactive: true))
                    if (t.name == "MainMenu_Status")
                        status = t;

                Assert.IsNotNull(status);
                StringAssert.Contains("접속하지 못했습니다", status.text, "메뉴에 빨간 안내(수정 전: 메뉴로 돌아오지 못했다)");
                Assert.Greater(status.color.r, status.color.g + 0.2f, "빨강 계열");
                Assert.IsNull(MainMenuScreen.PendingNotice, "안내는 한 번 보여 주고 비운다");

                Lifecycle(menu, "OpenJoinEntry"); // J
                var entry = (AddressEntry)typeof(MainMenuScreen).GetField("_entry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);
                Assert.AreEqual("127.0.0.1:7999", entry.Text, "J — 직전 주소가 채워져 있다");
                entry.Close();
            }
            finally
            {
                Object.DestroyImmediate(menuGo);
            }
        }

        [Test]
        public void MainMenu_JoinArgument_IsUsedOnlyOncePerRun()
        {
            var args = new[] { "MARCO.exe", "-join", "127.0.0.1:7999" };

            Assert.IsTrue(MainMenuScreen.TryConsumeJoinArgument(args, out JoinAddress first, out _));
            Assert.AreEqual(Target, first);
            Assert.IsFalse(MainMenuScreen.TryConsumeJoinArgument(args, out _, out JoinAddressError error),
                "실패해서 메뉴로 돌아왔을 때 -join으로 다시 자동 참가하면 무한 반복이 된다");
            Assert.AreEqual(JoinAddressError.None, error);
        }
    }
}
