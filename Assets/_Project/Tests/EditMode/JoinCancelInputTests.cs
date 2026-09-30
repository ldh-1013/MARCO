using System.Reflection;
using Marco.Core.Net;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 접속 중 Esc 취소 — 입력 읽기 → 취소 호출(10-01 실기 결함). 실기: "접속 중… / Esc — 취소"에서 Esc를 눌러도 취소되지 않고
    /// 약 6초 뒤 자연 실패로 메뉴 복귀. 원인: Input System 키 상태(물리 키 위치 = 스캔코드) 한 경로만 읽어, 스캔코드 없이 가상 키
    /// 코드만 담긴 Esc(키 리매퍼 · 키보드 유틸 등이 보내는 형태)를 놓쳤다 — 릴리스 빌드에 스캔코드 0인 Esc를 보내 같은 증상을 재현했다.
    ///
    /// ★ 09-30 테스트(<c>JoinProgressOverlayTests.Cancel_ReturnsToMenu_WithGrayNotice</c>)는 <c>Cancel()</c>을 직접 불러 입력 경로를
    /// 거치지 않았다. 여기서는 가상 키보드(Input System 테스트 런타임)의 키 누름과 OS 키 이벤트(<see cref="Event"/>)에서 출발해
    /// 실제 <see cref="JoinProgressOverlay"/>가 취소를 부르는지 본다.
    /// </summary>
    public class JoinCancelInputTests : InputTestFixture
    {
        private static readonly JoinAddress Target = new JoinAddress("127.0.0.1", 7999);

        private sealed class FakeConnection : IConnectionService
        {
            public bool HasStarted { get; set; }
            public int CancelCalls { get; private set; }
            public int StopAllCalls { get; private set; }
            public JoinAttempt Attempt { get; } = new JoinAttempt();
            public string AddressLine => string.Empty;
            public void StartHost() { }
            public void StartClient(JoinAddress address) { }

            public void Cancel()
            {
                CancelCalls++;
                Attempt.Cancel();
            }

            public void StopAll() => StopAllCalls++;
        }

        private static void Lifecycle(Component component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(component, null);
        }

        private static Event KeyDown(KeyCode code) => new Event { type = EventType.KeyDown, keyCode = code };

        private static Event KeyUp(KeyCode code) => new Event { type = EventType.KeyUp, keyCode = code };

        private Keyboard _keyboard;
        private CancelKeyReader _reader;
        private FakeConnection _fake;
        private GameObject _go;
        private JoinProgressOverlay _overlay;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Assert.AreSame(_keyboard, Keyboard.current, "전제: 가상 키보드가 현재 키보드");
            _reader = new CancelKeyReader(Key.Escape, KeyCode.Escape);

            MainMenuScreen.ResetSessionState();
            _fake = new FakeConnection();
            ConnectionServiceRegistry.Register(_fake);
            _go = new GameObject("SceneFlow");
            _overlay = _go.AddComponent<JoinProgressOverlay>();
            Lifecycle(_overlay, "Awake");
        }

        public override void TearDown()
        {
            ConnectionServiceRegistry.Unregister(_fake);
            Object.DestroyImmediate(_go);
            MainMenuScreen.ResetSessionState();
            base.TearDown();
        }

        private void BeginConnecting()
        {
            _fake.HasStarted = true;
            _fake.Attempt.BeginJoin(Target, Time.realtimeSinceStartup);
            Lifecycle(_overlay, "Update");
            Assert.IsTrue(_overlay.IsShowing, "전제: 접속 중 화면");
        }

        // ── 읽기 단위 ────────────────────────────────────────────────────

        [Test]
        public void Reader_InputSystemEsc_IsRead()
        {
            Press(_keyboard.escapeKey);

            Assert.AreEqual(CancelKeySource.InputSystemKey, _reader.Read(_keyboard, blocked: false));
        }

        [Test]
        public void Reader_OsKeyEscWithoutScanCode_IsRead()
        {
            // 스캔코드 없는 가상 키 Esc — Input System의 Key.Escape는 눌리지 않고 OS 키 이벤트만 온다.
            _reader.Observe(KeyDown(KeyCode.Escape));
            InputSystem.Update();
            Assert.IsFalse(_keyboard.escapeKey.isPressed, "전제: Input System 키 상태에는 Esc가 없다");

            Assert.AreEqual(CancelKeySource.OsKeyEvent, _reader.Read(_keyboard, blocked: false),
                "수정 전: None — 실기에서 Esc가 먹지 않은 경로");
        }

        [Test]
        public void Reader_EachPress_IsReadOnce()
        {
            _reader.Observe(KeyDown(KeyCode.Escape));
            Assert.AreEqual(CancelKeySource.OsKeyEvent, _reader.Read(_keyboard, blocked: false));
            Assert.AreEqual(CancelKeySource.None, _reader.Read(_keyboard, blocked: false), "OS 키 이벤트는 한 번만 쓴다");

            Press(_keyboard.escapeKey);
            Assert.AreEqual(CancelKeySource.InputSystemKey, _reader.Read(_keyboard, blocked: false));
            InputSystem.Update(); // 다음 프레임 — 아직 누르고 있다(0.3초 누름)
            Assert.IsTrue(_keyboard.escapeKey.isPressed);
            Assert.AreEqual(CancelKeySource.None, _reader.Read(_keyboard, blocked: false), "누르고 있는 동안 매 프레임 다시 읽지 않는다");
        }

        [Test]
        public void Reader_OtherKeysAndKeyUp_AreIgnored()
        {
            _reader.Observe(KeyDown(KeyCode.J));
            _reader.Observe(KeyUp(KeyCode.Escape));
            _reader.Observe(new Event { type = EventType.Repaint });
            _reader.Observe(null);
            Press(_keyboard.jKey);

            Assert.AreEqual(CancelKeySource.None, _reader.Read(_keyboard, blocked: false));
        }

        [Test]
        public void Reader_SettingsOpen_EscBelongsToSettings_AndIsNotKept()
        {
            _reader.Observe(KeyDown(KeyCode.Escape));
            Press(_keyboard.escapeKey);
            Assert.AreEqual(CancelKeySource.None, _reader.Read(_keyboard, blocked: true), "설정 창이 열려 있으면 Esc는 설정 창을 닫는다");

            InputSystem.Update();
            Assert.AreEqual(CancelKeySource.None, _reader.Read(_keyboard, blocked: false), "설정 창이 가져간 Esc가 나중에 취소로 남지 않는다");
        }

        [Test]
        public void Reader_NoKeyboard_StillReadsOsKeyEvent()
        {
            _reader.Observe(KeyDown(KeyCode.Escape));

            Assert.AreEqual(CancelKeySource.OsKeyEvent, _reader.Read(null, blocked: false), "Input System 키보드가 없어도 OS 키 이벤트로 취소");
        }

        [Test]
        public void Describe_NamesThePath()
        {
            StringAssert.Contains("Input System", CancelKeyReader.Describe(CancelKeySource.InputSystemKey));
            StringAssert.Contains("OS 키 이벤트", CancelKeyReader.Describe(CancelKeySource.OsKeyEvent));
            StringAssert.Contains("스캔코드", CancelKeyReader.Describe(CancelKeySource.OsKeyEvent), "실기 로그에서 원인을 바로 알 수 있게");
        }

        // ── 접속 중 화면: 입력 → 취소 호출 → 네트워크 정리 → 메뉴(회색) ─────────────

        [Test]
        public void Overlay_InputSystemEsc_Cancels_ThenReturnsToMenuGray()
        {
            BeginConnecting();

            Press(_keyboard.escapeKey);
            Lifecycle(_overlay, "Update");
            Assert.AreEqual(1, _fake.CancelCalls, "Esc → 취소 호출");
            Assert.AreEqual(JoinAttemptState.Cancelled, _fake.Attempt.State);

            Lifecycle(_overlay, "Update");
            Assert.IsTrue(_overlay.ReturnedToMenu, "즉시 메뉴로");
            Assert.AreEqual(1, _fake.StopAllCalls, "돌아가기 전에 네트워크를 정리한다(다시 H/J가 되게)");
            Assert.AreEqual("접속을 취소했습니다", MainMenuScreen.PendingNotice);
            Assert.IsFalse(MainMenuScreen.PendingNoticeIsError, "회색");
            Assert.AreEqual("127.0.0.1:7999", MainMenuScreen.RetryJoinText, "J에 직전 주소");
        }

        [Test]
        public void Overlay_OsKeyEscWithoutScanCode_Cancels_ThenReturnsToMenuGray()
        {
            BeginConnecting();

            _overlay.ObserveKeyEvent(KeyDown(KeyCode.Escape)); // OnGUI가 넘기는 OS 키 이벤트 — Input System에는 Esc가 없다
            InputSystem.Update();
            Lifecycle(_overlay, "Update");
            Assert.AreEqual(1, _fake.CancelCalls, "수정 전: 0 — 취소되지 않고 약 6초 뒤 자연 실패(실기 증상)");

            Lifecycle(_overlay, "Update");
            Assert.IsTrue(_overlay.ReturnedToMenu);
            Assert.AreEqual(1, _fake.StopAllCalls);
            Assert.AreEqual("접속을 취소했습니다", MainMenuScreen.PendingNotice);
            Assert.IsFalse(MainMenuScreen.PendingNoticeIsError);
            Assert.AreEqual("127.0.0.1:7999", MainMenuScreen.RetryJoinText);
        }

        [Test]
        public void Overlay_EscBeforeConnecting_DoesNotCancelTheAttempt()
        {
            // 메뉴 입력 칸을 Esc로 닫은 직후처럼 — 접속 중이 아닐 때 눌린 키는 쌓아 두지 않는다.
            _overlay.ObserveKeyEvent(KeyDown(KeyCode.Escape));
            Lifecycle(_overlay, "Update");

            BeginConnecting();
            Lifecycle(_overlay, "Update");

            Assert.AreEqual(0, _fake.CancelCalls);
            Assert.IsTrue(_overlay.IsShowing);
        }

        [Test]
        public void Overlay_NoEsc_KeepsConnecting()
        {
            BeginConnecting();

            Press(_keyboard.jKey);
            _overlay.ObserveKeyEvent(KeyDown(KeyCode.J));
            Lifecycle(_overlay, "Update");

            Assert.AreEqual(0, _fake.CancelCalls);
            Assert.AreEqual(JoinAttemptState.Connecting, _fake.Attempt.State);
            Assert.IsFalse(_overlay.ReturnedToMenu);
        }
    }
}
