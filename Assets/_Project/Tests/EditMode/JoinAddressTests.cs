using Marco.Core.Net;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 참가 주소 파싱(09-30, 원격 친구와 직접 연결) — 메인 메뉴 입력 · 로비 재시도 · 실행 인자 <c>-join</c>이 같은 파서를 쓴다.
    /// ★ 터널 서비스(playit.gg 등)는 "호스트 이름 + 임의 포트"를 준다 — DNS 이름과 7770이 아닌 포트를 반드시 받아야 한다.
    /// </summary>
    public class JoinAddressTests
    {
        // ── 허용 형식 ──────────────────────────────────────────────────────

        [TestCase("localhost", "localhost", 7770)]
        [TestCase("1.2.3.4", "1.2.3.4", 7770)]
        [TestCase("1.2.3.4:7771", "1.2.3.4", 7771)]
        [TestCase("이름.example.com", "이름.example.com", 7770)]
        [TestCase("이름.example.com:12345", "이름.example.com", 12345)]
        [TestCase("  abc-12.gl.at.ply.gg:48123  ", "abc-12.gl.at.ply.gg", 48123)]
        [TestCase("LOCALHOST:1", "LOCALHOST", 1)]
        [TestCase("192.168.0.10:65535", "192.168.0.10", 65535)]
        [TestCase("mypc", "mypc", 7770)]
        public void Valid(string input, string host, int port)
        {
            Assert.IsTrue(JoinAddressParser.TryParse(input, out JoinAddress a, out JoinAddressError e), $"'{input}' — {e}");
            Assert.AreEqual(JoinAddressError.None, e);
            Assert.AreEqual(host, a.Host);
            Assert.AreEqual(port, a.Port);
        }

        [Test]
        public void DefaultPort_Is7770()
        {
            Assert.AreEqual(7770, JoinAddressParser.DefaultPort);
        }

        // ── 거절 — 접속 시도 없이 안내 ───────────────────────────────────────

        [TestCase(null, JoinAddressError.Empty)]
        [TestCase("", JoinAddressError.Empty)]
        [TestCase("   ", JoinAddressError.Empty)]
        [TestCase("::1", JoinAddressError.Ipv6NotSupported)]
        [TestCase("[::1]:7770", JoinAddressError.Ipv6NotSupported)]
        [TestCase("fe80::1", JoinAddressError.Ipv6NotSupported)]
        [TestCase("1.2.3.4:0", JoinAddressError.PortOutOfRange)]
        [TestCase("1.2.3.4:65536", JoinAddressError.PortOutOfRange)]
        [TestCase("host.example.com:99999999999", JoinAddressError.PortOutOfRange)]
        [TestCase("1.2.3.4:abc", JoinAddressError.InvalidPort)]
        [TestCase("1.2.3.4:", JoinAddressError.InvalidPort)]
        [TestCase("1.2.3.4:-5", JoinAddressError.InvalidPort)]
        [TestCase(":7770", JoinAddressError.InvalidHost)]
        [TestCase("1.2.3", JoinAddressError.InvalidHost)]
        [TestCase("1.2.3.999", JoinAddressError.InvalidHost)]
        [TestCase("a..b.com", JoinAddressError.InvalidHost)]
        [TestCase("-bad.com", JoinAddressError.InvalidHost)]
        [TestCase("bad-.com", JoinAddressError.InvalidHost)]
        [TestCase("has space.com", JoinAddressError.InvalidHost)]
        [TestCase("a_b.com", JoinAddressError.InvalidHost)]
        [TestCase("http://1.2.3.4", JoinAddressError.InvalidHost)]
        public void Rejected(string input, JoinAddressError expected)
        {
            Assert.IsFalse(JoinAddressParser.TryParse(input, out _, out JoinAddressError e), $"'{input}'는 거절돼야 한다");
            Assert.AreEqual(expected, e, $"'{input}'");
        }

        [Test]
        public void HostLongerThanDnsLimit_Rejected()
        {
            string longHost = new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 63) + ".com"; // 259자
            Assert.Greater(longHost.Length, JoinAddressParser.MaxHostLength);
            Assert.IsFalse(JoinAddressParser.TryParse(longHost, out _, out JoinAddressError e));
            Assert.AreEqual(JoinAddressError.InvalidHost, e);
        }

        [TestCase(JoinAddressError.Empty, "주소")]
        [TestCase(JoinAddressError.Ipv6NotSupported, "IPv6")]
        [TestCase(JoinAddressError.InvalidHost, "주소")]
        [TestCase(JoinAddressError.InvalidPort, "포트")]
        [TestCase(JoinAddressError.PortOutOfRange, "1~65535")]
        [TestCase(JoinAddressError.MissingValue, "값")]
        public void EveryError_HasAScreenMessage(JoinAddressError error, string mustContain)
        {
            string text = JoinAddressParser.Describe(error);
            Assert.IsNotEmpty(text);
            StringAssert.Contains(mustContain, text);
        }

        // ── 실행 인자 ────────────────────────────────────────────────────

        [Test]
        public void JoinArgument_UsesSameParser()
        {
            var args = new[] { "MARCO.exe", "-logFile", "x.log", "-join", "abc.gl.at.ply.gg:48123" };
            Assert.IsTrue(LaunchArguments.TryGetJoin(args, out JoinAddress a, out JoinAddressError e));
            Assert.AreEqual(JoinAddressError.None, e);
            Assert.AreEqual(new JoinAddress("abc.gl.at.ply.gg", 48123), a);

            Assert.IsTrue(LaunchArguments.TryGetJoin(new[] { "MARCO.exe", "-JOIN", "1.2.3.4" }, out a, out _), "대소문자 무관");
            Assert.AreEqual(7770, a.Port);
        }

        [Test]
        public void JoinArgument_Absent_IsNotAnError()
        {
            // Run3P_QA.bat처럼 -join 없이 실행 — 동작은 그대로(메인 메뉴에서 고른다).
            var args = new[] { "MARCO.exe", "-logFile", "P0.log", "-screen-fullscreen", "0" };
            Assert.IsFalse(LaunchArguments.TryGetJoin(args, out _, out JoinAddressError e));
            Assert.AreEqual(JoinAddressError.None, e);
        }

        [Test]
        public void JoinArgument_MissingOrBadValue_IsReported()
        {
            Assert.IsFalse(LaunchArguments.TryGetJoin(new[] { "MARCO.exe", "-join" }, out _, out JoinAddressError e));
            Assert.AreEqual(JoinAddressError.MissingValue, e);

            Assert.IsFalse(LaunchArguments.TryGetJoin(new[] { "MARCO.exe", "-join", "-hostport" }, out _, out e), "다음 인자가 플래그면 값이 아니다");
            Assert.AreEqual(JoinAddressError.MissingValue, e);

            Assert.IsFalse(LaunchArguments.TryGetJoin(new[] { "MARCO.exe", "-join", "::1" }, out _, out e));
            Assert.AreEqual(JoinAddressError.Ipv6NotSupported, e);
        }

        [Test]
        public void HostPortArgument()
        {
            Assert.IsTrue(LaunchArguments.TryGetHostPort(new[] { "MARCO.exe" }, out ushort p, out JoinAddressError e), "없으면 기본 7770");
            Assert.AreEqual(7770, p);
            Assert.AreEqual(JoinAddressError.None, e);

            Assert.IsTrue(LaunchArguments.TryGetHostPort(new[] { "MARCO.exe", "-hostport", "7780" }, out p, out e));
            Assert.AreEqual(7780, p);

            Assert.IsFalse(LaunchArguments.TryGetHostPort(new[] { "MARCO.exe", "-hostport", "0" }, out p, out e));
            Assert.AreEqual(JoinAddressError.PortOutOfRange, e);
            Assert.AreEqual(7770, p, "틀리면 기본 포트로 연다");

            Assert.IsFalse(LaunchArguments.TryGetHostPort(new[] { "MARCO.exe", "-hostport", "abc" }, out p, out e));
            Assert.AreEqual(JoinAddressError.InvalidPort, e);

            Assert.IsFalse(LaunchArguments.TryGetHostPort(new[] { "MARCO.exe", "-hostport" }, out p, out e));
            Assert.AreEqual(JoinAddressError.MissingValue, e);
        }

        // ── 안내 문구 ────────────────────────────────────────────────────

        [Test]
        public void JoinFailed_NamesThreeLikelyCauses_AndTheTarget()
        {
            string text = ConnectionMessages.JoinFailed(new JoinAddress("abc.gl.at.ply.gg", 48123));
            StringAssert.Contains("abc.gl.at.ply.gg:48123", text);
            StringAssert.Contains("오타", text);
            StringAssert.Contains("호스트", text);
            StringAssert.Contains("방화벽", text);
            StringAssert.Contains("터널", text);
        }

        [Test]
        public void AddressLines()
        {
            StringAssert.Contains("192.168.0.10:7770", ConnectionMessages.HostAddressLine("192.168.0.10", 7770));
            StringAssert.Contains("7780", ConnectionMessages.HostAddressLine(null, 7780), "LAN IP를 못 찾아도 포트는 보인다");
            StringAssert.Contains("abc.gl.at.ply.gg:48123", ConnectionMessages.ClientAddressLine(new JoinAddress("abc.gl.at.ply.gg", 48123)));
            StringAssert.Contains("abc.gl.at.ply.gg:48123", ConnectionMessages.Disconnected(new JoinAddress("abc.gl.at.ply.gg", 48123)));
        }

        // ── LAN IP ───────────────────────────────────────────────────────

        [TestCase("10.0.0.1", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("172.31.255.255", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("192.168.1.5", true)]
        [TestCase("203.0.113.5", false)]
        [TestCase("127.0.0.1", false)]
        [TestCase("169.254.3.4", false)]
        [TestCase("not-an-ip", false)]
        public void PrivateRanges(string ip, bool expected)
        {
            Assert.AreEqual(expected, LanAddress.IsPrivate(ip), ip);
        }

        [Test]
        public void PickPreferred_PrivateFirst_SkipsLoopbackAndLinkLocal()
        {
            Assert.AreEqual("192.168.0.10", LanAddress.PickPreferred(new[] { "127.0.0.1", "169.254.3.4", "203.0.113.5", "192.168.0.10" }));
            Assert.AreEqual("10.0.0.2", LanAddress.PickPreferred(new[] { "10.0.0.2", "172.20.1.1" }), "사설이 여럿이면 먼저 나온 것");
            Assert.AreEqual("203.0.113.5", LanAddress.PickPreferred(new[] { "127.0.0.1", "203.0.113.5" }), "사설이 없으면 공인");
            Assert.IsNull(LanAddress.PickPreferred(new[] { "127.0.0.1", "169.254.1.1" }));
            Assert.IsNull(LanAddress.PickPreferred(new string[0]));
        }
    }
}
