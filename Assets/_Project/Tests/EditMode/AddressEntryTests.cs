using Marco.Core.Net;
using Marco.Presentation.UI;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 주소 입력 칸(09-30) — 메인 메뉴 "코드 입장"과 로비 재시도가 같이 쓴다. Enter는 <see cref="JoinAddressParser"/>를 그대로 부르고,
    /// 형식 오류는 접속 시도 없이 칸에 안내를 남긴다.
    /// </summary>
    public class AddressEntryTests
    {
        [Test]
        public void Prefilled_ReplacedByFirstInput_ThenSubmitsParsedAddress()
        {
            var entry = new AddressEntry();
            entry.Open("localhost");
            entry.Type("abc.gl.at.ply.gg:48123");

            Assert.AreEqual("abc.gl.at.ply.gg:48123", entry.Text, "미리 채운 localhost는 첫 입력에서 통째로 바뀐다");
            Assert.AreEqual(AddressEntry.Result.Submitted, entry.Submit(out JoinAddress address));
            Assert.AreEqual(new JoinAddress("abc.gl.at.ply.gg", 48123), address);
            Assert.IsFalse(entry.IsOpen, "제출하면 닫힌다");
        }

        [Test]
        public void EnterWithPrefilledDefault_JoinsLocalhost7770()
        {
            var entry = new AddressEntry();
            entry.Open("localhost");

            Assert.AreEqual(AddressEntry.Result.Submitted, entry.Submit(out JoinAddress address));
            Assert.AreEqual(new JoinAddress("localhost", 7770), address);
        }

        [TestCase("::1", "IPv6")]
        [TestCase("1.2.3.4:70000", "1~65535")]
        [TestCase("http://1.2.3.4", "http://")]
        public void BadInput_NoConnectionAttempt_ShowsReason_StaysOpen(string input, string reasonContains)
        {
            var entry = new AddressEntry();
            entry.Open(string.Empty);
            entry.Type(input);

            Assert.AreEqual(AddressEntry.Result.None, entry.Submit(out _));
            Assert.IsTrue(entry.IsOpen, "고쳐서 다시 Enter할 수 있게 열어 둔다");
            StringAssert.Contains(reasonContains, entry.HintLine());
        }

        [Test]
        public void Empty_ShowsHowToEnter()
        {
            var entry = new AddressEntry();
            entry.Open("localhost");
            entry.Backspace(); // 미리 채운 값은 Backspace 한 번에 통째로 지운다

            Assert.AreEqual(string.Empty, entry.Text);
            Assert.AreEqual(AddressEntry.Result.None, entry.Submit(out _));
            StringAssert.Contains("주소를 입력", entry.HintLine());
        }

        [Test]
        public void Paste_StripsWhitespaceAndNewlines()
        {
            var entry = new AddressEntry();
            entry.Open(string.Empty);
            entry.Type("  abc.gl.at.ply.gg:48123\r\n");

            Assert.AreEqual("abc.gl.at.ply.gg:48123", entry.Text);
        }
    }
}
