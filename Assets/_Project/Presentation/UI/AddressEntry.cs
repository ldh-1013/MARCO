using System.Text;
using Marco.Core.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// 참가 주소 입력 칸(09-30) — 메인 메뉴 "코드 입장"(J)과 로비 접속 전 패널이 같이 쓴다. uGUI InputField(EventSystem 필요) 대신
    /// Input System 키 입력을 직접 받는다 — 이 화면들이 모두 키 기반이라서다.
    ///
    /// <list type="bullet">
    /// <item>글자: <c>Keyboard.onTextInput</c>(제어 문자 제외). 처음 연 기본값(예: localhost)은 첫 입력 · 붙여넣기 · Backspace에서 통째로 바뀐다.</item>
    /// <item>Ctrl+V: 클립보드 붙여넣기(터널 주소를 복사해 오기 쉽게). 줄바꿈 · 공백은 지운다.</item>
    /// <item>Enter: <see cref="JoinAddressParser"/>로 검사 — 형식 오류면 접속 시도 없이 <see cref="Error"/>에 안내를 남기고 칸은 열린 채.</item>
    /// <item>Esc: 닫기.</item>
    /// </list>
    /// </summary>
    public sealed class AddressEntry
    {
        public enum Result
        {
            None,
            Submitted,
            Cancelled,
        }

        /// <summary>입력 길이 상한 — DNS 253 + ":65535".</summary>
        public const int MaxLength = 260;

        private readonly StringBuilder _text = new StringBuilder();
        private Keyboard _subscribed;
        private bool _replaceOnFirstInput;

        public bool IsOpen { get; private set; }

        public string Text => _text.ToString();

        /// <summary>직전 Enter의 거절 안내. 없으면 null.</summary>
        public string Error { get; private set; }

        /// <summary>칸을 연다. <paramref name="initial"/>은 미리 채워 두는 값(첫 입력에서 통째로 바뀐다).</summary>
        public void Open(string initial)
        {
            _text.Clear();
            _text.Append(initial ?? string.Empty);
            _replaceOnFirstInput = _text.Length > 0;
            Error = null;
            IsOpen = true;
            Subscribe(Keyboard.current);
        }

        public void Close()
        {
            IsOpen = false;
            Unsubscribe();
        }

        /// <summary>글자를 넣는다(키 입력 · 붙여넣기 공용). 제어 문자 · 공백은 버린다.</summary>
        public void Type(string text)
        {
            if (!IsOpen || string.IsNullOrEmpty(text))
                return;

            foreach (char c in text)
                TypeChar(c);
        }

        /// <summary>한 글자 지운다(처음 연 기본값이면 통째로).</summary>
        public void Backspace()
        {
            if (!IsOpen)
                return;

            if (_replaceOnFirstInput)
            {
                _text.Clear();
                _replaceOnFirstInput = false;
            }
            else if (_text.Length > 0)
            {
                _text.Length--;
            }

            Error = null;
        }

        /// <summary>Enter — 파싱해 통과하면 Submitted(칸은 닫힌다), 아니면 Error를 남기고 None.</summary>
        public Result Submit(out JoinAddress address)
        {
            if (JoinAddressParser.TryParse(Text, out address, out JoinAddressError error))
            {
                Close();
                return Result.Submitted;
            }

            Error = JoinAddressParser.Describe(error);
            return Result.None;
        }

        /// <summary>매 프레임 — Enter · Esc · Backspace · Ctrl+V를 처리한다(글자는 onTextInput이 넣는다).</summary>
        public Result Tick(Keyboard keyboard, out JoinAddress address)
        {
            address = default;
            if (!IsOpen || keyboard == null)
                return Result.None;

            if (!ReferenceEquals(_subscribed, keyboard))
                Subscribe(keyboard);

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return Result.Cancelled;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                return Submit(out address);

            if (keyboard.backspaceKey.wasPressedThisFrame)
                Backspace();

            if (keyboard.ctrlKey.isPressed && keyboard.vKey.wasPressedThisFrame)
                Type(GUIUtility.systemCopyBuffer);

            return Result.None;
        }

        /// <summary>표시 한 줄 — 커서(▌)는 0.5초마다 깜박인다.</summary>
        public string Render(float time)
        {
            bool cursor = ((int)(time * 2f) & 1) == 0;
            return $"주소: {Text}{(cursor ? "▌" : " ")}" + (_replaceOnFirstInput ? "  (입력하면 바뀝니다)" : string.Empty);
        }

        /// <summary>입력 칸 아래 안내 — 거절 사유가 있으면 그것, 아니면 조작법과 예시.</summary>
        public string HintLine() =>
            Error ?? "Enter — 접속 · Esc — 뒤로 · Ctrl+V — 붙여넣기   (예: 192.168.0.10 · 이름.example.com:12345)";

        private void TypeChar(char c)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
                return;

            if (_replaceOnFirstInput)
            {
                _text.Clear();
                _replaceOnFirstInput = false;
            }

            if (_text.Length < MaxLength)
                _text.Append(c);

            Error = null;
        }

        private void Subscribe(Keyboard keyboard)
        {
            Unsubscribe();
            if (keyboard == null)
                return;

            keyboard.onTextInput += TypeChar;
            _subscribed = keyboard;
        }

        private void Unsubscribe()
        {
            if (_subscribed != null)
                _subscribed.onTextInput -= TypeChar;
            _subscribed = null;
        }
    }
}
