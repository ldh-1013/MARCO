using System;
using System.Collections.Generic;

namespace Marco.Core.Net
{
    /// <summary>참가 주소 입력의 거절 사유(09-30).</summary>
    public enum JoinAddressError
    {
        None,

        /// <summary>빈 값(공백만 포함).</summary>
        Empty,

        /// <summary>IPv6 주소 — 이번 범위에서 지원하지 않는다.</summary>
        Ipv6NotSupported,

        /// <summary>호스트 부분이 localhost · IPv4 · 호스트 이름(DNS) 어느 것도 아니다.</summary>
        InvalidHost,

        /// <summary>포트가 숫자가 아니거나 비어 있다(":" 뒤가 빔).</summary>
        InvalidPort,

        /// <summary>포트가 1~65535 밖이다.</summary>
        PortOutOfRange,

        /// <summary>실행 인자 뒤에 값이 없다(예: "-join"이 마지막 인자).</summary>
        MissingValue,
    }

    /// <summary>파싱된 참가 주소 — 호스트(localhost · IPv4 · 호스트 이름)와 포트.</summary>
    public readonly struct JoinAddress : IEquatable<JoinAddress>
    {
        public readonly string Host;
        public readonly ushort Port;

        public JoinAddress(string host, ushort port)
        {
            Host = host;
            Port = port;
        }

        public bool Equals(JoinAddress other) =>
            string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase) && Port == other.Port;

        public override bool Equals(object obj) => obj is JoinAddress other && Equals(other);

        public override int GetHashCode() => (Host ?? string.Empty).ToLowerInvariant().GetHashCode() ^ Port;

        public override string ToString() => $"{Host}:{Port}";
    }

    /// <summary>
    /// 참가 주소 파서(09-30, 원격 친구와 직접 연결). 허용: <c>localhost</c> · IPv4(<c>1.2.3.4</c>) · 호스트 이름(DNS, 터널 서비스가 주는
    /// <c>이름.example.com</c> 포함), 각각 <c>:포트</c> 선택. 포트 생략 시 <see cref="DefaultPort"/>. 앞뒤 공백 제거. IPv6는 거절.
    /// 메인 메뉴 입력 · 로비 재시도 · 실행 인자 <c>-join</c>이 모두 이것을 쓴다.
    /// </summary>
    public static class JoinAddressParser
    {
        /// <summary>Tugboat 기본 포트(Lobby 씬 NetworkManager의 값과 같다).</summary>
        public const ushort DefaultPort = 7770;

        /// <summary>호스트 이름 최대 길이(DNS).</summary>
        public const int MaxHostLength = 253;

        /// <summary>호스트 이름 한 칸(label) 최대 길이(DNS).</summary>
        private const int MaxLabelLength = 63;

        public static bool TryParse(string input, out JoinAddress address, out JoinAddressError error)
        {
            address = default;
            string text = input?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                error = JoinAddressError.Empty;
                return false;
            }

            // URL 형태(http://…)는 주소가 아니다 — 스킴 없이 호스트만 넣어야 한다.
            if (text.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                error = JoinAddressError.InvalidHost;
                return false;
            }

            // IPv6: 대괄호 표기이거나 콜론이 둘 이상이면 IPv6로 본다(host:port는 콜론 하나).
            int firstColon = text.IndexOf(':');
            if (text[0] == '[' || (firstColon >= 0 && text.IndexOf(':', firstColon + 1) >= 0))
            {
                error = JoinAddressError.Ipv6NotSupported;
                return false;
            }

            string host = text;
            ushort port = DefaultPort;
            if (firstColon >= 0)
            {
                host = text.Substring(0, firstColon);
                if (!TryParsePort(text.Substring(firstColon + 1), out port, out error))
                {
                    // 포트가 비었으면(예: "1.2.3.4:") 형식 오류다 — 빈 입력 전체와 구분한다.
                    if (error == JoinAddressError.Empty)
                        error = JoinAddressError.InvalidPort;
                    return false;
                }
            }

            if (!IsValidHost(host))
            {
                error = JoinAddressError.InvalidHost;
                return false;
            }

            address = new JoinAddress(host, port);
            error = JoinAddressError.None;
            return true;
        }

        /// <summary>포트 문자열 하나(실행 인자 <c>-hostport</c>) — 1~65535.</summary>
        public static bool TryParsePort(string input, out ushort port, out JoinAddressError error)
        {
            port = 0;
            string text = input?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                error = JoinAddressError.Empty;
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    error = JoinAddressError.InvalidPort;
                    return false;
                }
            }

            // 숫자뿐인데 너무 길면(ulong도 넘치면) 범위 밖이다.
            if (text.Length > 10 || !ulong.TryParse(text, out ulong value) || value < 1 || value > 65535)
            {
                error = JoinAddressError.PortOutOfRange;
                return false;
            }

            port = (ushort)value;
            error = JoinAddressError.None;
            return true;
        }

        /// <summary>거절 사유를 화면 안내 문구로.</summary>
        public static string Describe(JoinAddressError error)
        {
            switch (error)
            {
                case JoinAddressError.Empty:
                    return "주소를 입력하세요 — 예: 192.168.0.10 · 이름.example.com:12345";
                case JoinAddressError.Ipv6NotSupported:
                    return "IPv6 주소는 아직 지원하지 않습니다 — IPv4(1.2.3.4) 또는 호스트 이름을 입력하세요";
                case JoinAddressError.InvalidHost:
                    return "주소 형식이 올바르지 않습니다 — localhost · 1.2.3.4 · 이름.example.com 중 하나(뒤에 :포트 선택, http:// 없이)";
                case JoinAddressError.InvalidPort:
                    return "포트는 숫자여야 합니다 — 예: 1.2.3.4:7770";
                case JoinAddressError.PortOutOfRange:
                    return "포트는 1~65535 사이여야 합니다";
                case JoinAddressError.MissingValue:
                    return "실행 인자 뒤에 값이 없습니다 — 예: -join 1.2.3.4:7770 · -hostport 7770";
                default:
                    return string.Empty;
            }
        }

        private static bool IsValidHost(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length > MaxHostLength)
                return false;

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] labels = host.Split('.');
            bool allNumeric = true;
            for (int i = 0; i < labels.Length; i++)
            {
                if (!IsAllDigits(labels[i]))
                {
                    allNumeric = false;
                    break;
                }
            }

            // 숫자로만 된 주소는 IPv4여야 한다(1.2.3 · 1.2.3.999 거절).
            if (allNumeric)
                return IsIpv4(labels);

            for (int i = 0; i < labels.Length; i++)
            {
                if (!IsValidLabel(labels[i]))
                    return false;
            }

            return true;
        }

        private static bool IsIpv4(string[] labels)
        {
            if (labels.Length != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (labels[i].Length == 0 || labels[i].Length > 3 || !int.TryParse(labels[i], out int part) || part > 255)
                    return false;
            }

            return true;
        }

        /// <summary>DNS 한 칸: 1~63자, 글자 · 숫자 · '-'(앞뒤 제외). 글자에는 한글 등 유니코드 글자를 포함한다(국제화 도메인).</summary>
        private static bool IsValidLabel(string label)
        {
            if (label.Length == 0 || label.Length > MaxLabelLength)
                return false;
            if (label[0] == '-' || label[label.Length - 1] == '-')
                return false;

            for (int i = 0; i < label.Length; i++)
            {
                char c = label[i];
                if (c == '-' || char.IsLetterOrDigit(c))
                    continue;
                return false;
            }

            return true;
        }

        private static bool IsAllDigits(string s)
        {
            if (s.Length == 0)
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                    return false;
            }

            return true;
        }
    }

    /// <summary>실행 인자(09-30) — <c>-join &lt;주소[:포트]&gt;</c> · <c>-hostport &lt;번호&gt;</c>. 이름은 대소문자를 가리지 않는다.</summary>
    public static class LaunchArguments
    {
        public const string JoinFlag = "-join";
        public const string HostPortFlag = "-hostport";

        /// <summary>
        /// 인자 목록에 <paramref name="flag"/>가 있는가(<paramref name="present"/>), 있으면 바로 뒤의 값. 값이 없거나 다음 인자가
        /// '-'로 시작하는 플래그면 false.
        /// </summary>
        public static bool TryGetValue(IReadOnlyList<string> args, string flag, out string value, out bool present)
        {
            value = null;
            present = false;
            if (args == null)
                return false;

            for (int i = 0; i < args.Count; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    continue;

                present = true;
                if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    return false;

                value = args[i + 1];
                return true;
            }

            return false;
        }

        /// <summary><c>-join</c>. 없으면 false · error None. 있는데 값이 틀리면 false · 사유.</summary>
        public static bool TryGetJoin(IReadOnlyList<string> args, out JoinAddress address, out JoinAddressError error)
        {
            address = default;
            if (!TryGetValue(args, JoinFlag, out string value, out bool present))
            {
                error = present ? JoinAddressError.MissingValue : JoinAddressError.None;
                return false;
            }

            return JoinAddressParser.TryParse(value, out address, out error);
        }

        /// <summary><c>-hostport</c>. 없으면 <see cref="JoinAddressParser.DefaultPort"/> · true. 있는데 틀리면 기본 포트 · false · 사유.</summary>
        public static bool TryGetHostPort(IReadOnlyList<string> args, out ushort port, out JoinAddressError error)
        {
            port = JoinAddressParser.DefaultPort;
            if (!TryGetValue(args, HostPortFlag, out string value, out bool present))
            {
                error = present ? JoinAddressError.MissingValue : JoinAddressError.None;
                return !present;
            }

            if (!JoinAddressParser.TryParsePort(value, out ushort parsed, out error))
                return false;

            port = parsed;
            return true;
        }
    }

    /// <summary>접속 안내 문구(09-30).</summary>
    public static class ConnectionMessages
    {
        /// <summary>참가 시도가 성립하지 못하고 끝났을 때 — 원인 후보 세 가지.</summary>
        public static string JoinFailed(JoinAddress target) =>
            $"{target}에 접속하지 못했습니다 — ① 주소 · 포트 오타 ② 호스트가 아직 방을 만들지 않음 ③ 호스트 쪽 방화벽 또는 터널(playit.gg 등) 미연결";

        /// <summary>접속됐다가 끊겼을 때.</summary>
        public static string Disconnected(JoinAddress target) => $"{target}와(과)의 연결이 끊겼습니다 — 호스트가 나갔거나 네트워크가 끊겼습니다";

        /// <summary>호스트 로비의 주소 줄 — 같은 네트워크용 LAN IP:포트. LAN IP를 못 찾으면 포트만.</summary>
        public static string HostAddressLine(string lanIpv4, ushort port) =>
            string.IsNullOrEmpty(lanIpv4)
                ? $"내 주소: (LAN IP를 찾지 못함) · 포트 {port}"
                : $"내 주소(같은 네트워크): {lanIpv4}:{port}";

        /// <summary>참가자 로비의 주소 줄.</summary>
        public static string ClientAddressLine(JoinAddress target) => $"접속 주소: {target}";
    }

    /// <summary>호스트의 LAN IPv4 고르기(09-30) — 사설 대역(10/8 · 172.16/12 · 192.168/16) 우선, 루프백 · 링크로컬(169.254) 제외.</summary>
    public static class LanAddress
    {
        public static bool IsPrivate(string ipv4)
        {
            if (!TryOctets(ipv4, out int a, out int b))
                return false;

            return a == 10 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168);
        }

        public static string PickPreferred(IEnumerable<string> ipv4Candidates)
        {
            if (ipv4Candidates == null)
                return null;

            string fallback = null;
            foreach (string ip in ipv4Candidates)
            {
                if (!TryOctets(ip, out int a, out int b) || a == 127 || (a == 169 && b == 254) || a == 0)
                    continue;

                if (IsPrivate(ip))
                    return ip;

                fallback ??= ip;
            }

            return fallback;
        }

        private static bool TryOctets(string ipv4, out int a, out int b)
        {
            a = b = 0;
            if (string.IsNullOrEmpty(ipv4))
                return false;

            string[] parts = ipv4.Split('.');
            if (parts.Length != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i], out int v) || v < 0 || v > 255)
                    return false;
                if (i == 0) a = v;
                if (i == 1) b = v;
            }

            return true;
        }
    }
}
