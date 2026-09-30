using System.Reflection;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 메인 메뉴 주소 입력 칸의 Esc(뒤로) — 입력 읽기 → 닫기(10-01). 접속 중 화면과 같은 결함: Input System 키 상태(스캔코드)만
    /// 읽어 스캔코드 없는 가상 키 Esc(키 리매퍼 · 키보드 유틸 등)를 놓쳤다. 접속 중 화면과 같은 <see cref="CancelKeyReader"/>로 통일.
    /// ★ 기존 <c>AddressEntryTests</c>는 Type · Backspace · Submit을 직접 불러 키 입력 경로(<c>Tick</c>)를 거치지 않았다.
    /// 여기서는 가상 키보드(Input System 테스트 런타임)의 키 누름과 OS 키 이벤트(<see cref="Event"/>)에서 출발한다.
    /// </summary>
    public class AddressEntryCancelInputTests : InputTestFixture
    {
        private static Event KeyDown(KeyCode code) => new Event { type = EventType.KeyDown, keyCode = code };

        private static void Lifecycle(Component component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(component, null);
        }

        private Keyboard _keyboard;
        private AddressEntry _entry;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            Assert.AreSame(_keyboard, Keyboard.current, "전제: 가상 키보드가 현재 키보드");
            MainMenuScreen.ResetSessionState();
            _entry = new AddressEntry();
        }

        public override void TearDown()
        {
            _entry.Close();
            MainMenuScreen.ResetSessionState();
            base.TearDown();
        }

        // ── 입력 칸 ──────────────────────────────────────────────────────

        [Test]
        public void Entry_InputSystemEsc_Closes()
        {
            _entry.Open("127.0.0.1:7999");

            Press(_keyboard.escapeKey);

            Assert.AreEqual(AddressEntry.Result.Cancelled, _entry.Tick(_keyboard, out _));
            Assert.IsFalse(_entry.IsOpen);
        }

        [Test]
        public void Entry_OsKeyEscWithoutScanCode_Closes()
        {
            _entry.Open("127.0.0.1:7999");

            _entry.ObserveKeyEvent(KeyDown(KeyCode.Escape)); // OnGUI가 넘기는 OS 키 이벤트 — Input System에는 Esc가 없다
            InputSystem.Update();
            Assert.IsFalse(_keyboard.escapeKey.isPressed, "전제: Input System 키 상태에는 Esc가 없다");

            Assert.AreEqual(AddressEntry.Result.Cancelled, _entry.Tick(_keyboard, out _), "수정 전: None — 칸이 닫히지 않는다");
            Assert.IsFalse(_entry.IsOpen);
        }

        [Test]
        public void Entry_EscWhileClosed_IsNotKeptForNextOpen()
        {
            // 메뉴에서(칸이 닫힌 채) Esc를 누른 뒤 J로 연 칸이 곧바로 닫히면 안 된다.
            _entry.ObserveKeyEvent(KeyDown(KeyCode.Escape));
            _entry.Open("localhost");

            Assert.AreEqual(AddressEntry.Result.None, _entry.Tick(_keyboard, out _));
            Assert.IsTrue(_entry.IsOpen);
        }

        [Test]
        public void Entry_PendingEsc_IsDroppedWhenClosedAnotherWay()
        {
            _entry.Open("127.0.0.1:7999");
            _entry.ObserveKeyEvent(KeyDown(KeyCode.Escape));
            _entry.Close(); // 같은 프레임에 Enter로 제출됐다고 치자
            _entry.Open("127.0.0.1:7999");

            Assert.AreEqual(AddressEntry.Result.None, _entry.Tick(_keyboard, out _), "닫히기 전에 쌓인 Esc가 다음에 연 칸을 닫지 않는다");
            Assert.IsTrue(_entry.IsOpen);
        }

        [Test]
        public void Entry_OtherOsKeys_DoNotClose()
        {
            _entry.Open("127.0.0.1:7999");

            _entry.ObserveKeyEvent(KeyDown(KeyCode.J));
            _entry.ObserveKeyEvent(new Event { type = EventType.KeyUp, keyCode = KeyCode.Escape });
            Press(_keyboard.jKey);

            Assert.AreEqual(AddressEntry.Result.None, _entry.Tick(_keyboard, out _));
            Assert.IsTrue(_entry.IsOpen);
        }

        [Test]
        public void Entry_EscCharFromTextInput_IsNotTyped()
        {
            _entry.Open(string.Empty);

            InputSystem.QueueTextEvent(_keyboard, '\u001b'); // Esc가 만드는 문자(0x1B)
            InputSystem.Update();

            Assert.AreEqual(string.Empty, _entry.Text, "Esc 문자가 주소에 들어가면 안 된다");
        }

        // ── 메인 메뉴: 입력 → 칸 닫기 → 메뉴 줄 ────────────────────────────

        private static AddressEntry MenuEntry(MainMenuScreen menu) =>
            (AddressEntry)typeof(MainMenuScreen).GetField("_entry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu);

        private void WithOpenMenuEntry(System.Action<MainMenuScreen, AddressEntry> body)
        {
            var go = new GameObject("MainMenu");
            try
            {
                var menu = go.AddComponent<MainMenuScreen>();
                Lifecycle(menu, "Awake");
                Lifecycle(menu, "Start");
                Lifecycle(menu, "OpenJoinEntry"); // J
                AddressEntry entry = MenuEntry(menu);
                Assert.IsTrue(entry.IsOpen, "전제: 주소 입력 칸이 열려 있다");
                body(menu, entry);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Menu_InputSystemEsc_ClosesEntry_BackToMenu()
        {
            WithOpenMenuEntry((menu, entry) =>
            {
                Press(_keyboard.escapeKey);
                Lifecycle(menu, "Update");

                Assert.IsFalse(entry.IsOpen, "Esc — 뒤로");
                StringAssert.Contains("방 만들기", MenuLine(menu));
            });
        }

        [Test]
        public void Menu_OsKeyEscWithoutScanCode_ClosesEntry_BackToMenu()
        {
            WithOpenMenuEntry((menu, entry) =>
            {
                menu.ObserveKeyEvent(KeyDown(KeyCode.Escape)); // OnGUI가 넘기는 것
                InputSystem.Update();
                Lifecycle(menu, "Update");

                Assert.IsFalse(entry.IsOpen, "수정 전: 칸이 열린 채 — 스캔코드 없는 Esc가 무시됐다");
                StringAssert.Contains("방 만들기", MenuLine(menu));
            });
        }

        private static string MenuLine(MainMenuScreen menu) =>
            ((Text)typeof(MainMenuScreen).GetField("_menuText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(menu)).text;
    }
}
