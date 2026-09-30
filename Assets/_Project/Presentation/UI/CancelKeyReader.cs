using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.UI
{
    /// <summary>취소 키를 어느 경로로 읽었는가 — 진단 로그용(10-01).</summary>
    [System.Flags]
    public enum CancelKeySource
    {
        None = 0,

        /// <summary>Input System 키 상태 — 물리 키 위치(스캔코드)로 판정한다.</summary>
        InputSystemKey = 1,

        /// <summary>OS 키 이벤트(IMGUI KeyDown) — 가상 키 코드로 판정한다.</summary>
        OsKeyEvent = 2,
    }

    /// <summary>
    /// 취소 · 뒤로 키(Esc) 읽기 — 입력 읽기 → 판정을 한곳에 둔다(10-01 실기 결함 수정). 접속 중 화면(Esc 취소)과 메인 메뉴
    /// 주소 입력 칸(Esc 뒤로, <see cref="AddressEntry"/>)이 쓴다. 설정 창은 따로 읽는다.
    ///
    /// <para>
    /// <b>실기에서 Esc가 먹지 않은 원인</b>: 전에는 Input System 키 상태(<c>keyboard[Key.Escape].wasPressedThisFrame</c>) 한 경로만
    /// 읽었다. Input System은 키를 물리 위치(스캔코드)로 판정하므로, 스캔코드 없이 가상 키 코드만 담긴 Esc — 키 리매퍼 · 키보드
    /// 제조사 유틸 · 원격 입력 도구가 보내는 형태 — 는 Key.Escape로 잡히지 않는다. 10-01 재현: 릴리스 빌드에 스캔코드 0인 Esc를
    /// 두 번 보냄 → 취소되지 않고 5.7초 뒤 자연 실패(실기 증상과 같다). 스캔코드가 있는 Esc는 같은 빌드에서 곧바로 취소됐다.
    /// </para>
    /// <para>
    /// 그래서 OS 키 이벤트(가상 키 코드 — IMGUI <see cref="Event"/> KeyDown)도 함께 읽는다. 둘 중 하나라도 잡히면 취소다.
    /// 한 번 누름이 두 경로에 모두 잡혀도 취소는 한 번이면 된다(취소 뒤 화면은 메뉴로 돌아간다).
    /// </para>
    /// </summary>
    public sealed class CancelKeyReader
    {
        private readonly Key _key;
        private readonly KeyCode _keyCode;
        private bool _osKeyDown;

        public CancelKeyReader(Key key, KeyCode keyCode)
        {
            _key = key;
            _keyCode = keyCode;
        }

        /// <summary>
        /// OS 키 이벤트를 넘긴다(<c>OnGUI</c>의 <see cref="Event.current"/>). 이 키의 KeyDown이면 다음 <see cref="Read"/>에서 한 번 쓴다.
        /// </summary>
        public void Observe(Event e)
        {
            if (e != null && e.type == EventType.KeyDown && e.keyCode == _keyCode)
                _osKeyDown = true;
        }

        /// <summary>
        /// 이번 프레임에 취소 키가 눌렸는가(프레임마다 한 번). <paramref name="blocked"/>(설정 창이 열려 있음)이면 그 키는 설정 창
        /// 몫이다 — 읽은 것을 버리고 None.
        /// </summary>
        public CancelKeySource Read(Keyboard keyboard, bool blocked)
        {
            CancelKeySource source = CancelKeySource.None;
            if (keyboard != null && keyboard[_key].wasPressedThisFrame)
                source |= CancelKeySource.InputSystemKey;
            if (_osKeyDown)
                source |= CancelKeySource.OsKeyEvent;

            _osKeyDown = false;
            return blocked ? CancelKeySource.None : source;
        }

        /// <summary>쌓인 OS 키 이벤트를 버린다 — 접속 중이 아닐 때 눌린 키가 다음 시도를 취소하지 않게.</summary>
        public void Clear() => _osKeyDown = false;

        /// <summary>진단 로그 문구 — 실기 로그에서 어느 경로로 읽혔는지(= 원인) 바로 보이게.</summary>
        public static string Describe(CancelKeySource source)
        {
            switch (source)
            {
                case CancelKeySource.InputSystemKey:
                    return "Input System 키";
                case CancelKeySource.OsKeyEvent:
                    return "OS 키 이벤트만(스캔코드 없는 입력 — 키 리매퍼 · 키보드 유틸 등. 수정 전에는 무시됐다)";
                case CancelKeySource.InputSystemKey | CancelKeySource.OsKeyEvent:
                    return "Input System 키 + OS 키 이벤트";
                default:
                    return "없음";
            }
        }
    }
}
