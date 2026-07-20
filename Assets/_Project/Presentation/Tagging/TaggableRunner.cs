using UnityEngine;
using Marco.Core.Role;

namespace Marco.Presentation.Tagging
{
    /// <summary>
    /// 태그 대상이 되는 도망자 하나.
    ///
    /// **로컬 단독 실행용 대역(stand-in)이다.** 지금은 플레이어가 한 명뿐이라
    /// "술래가 도망자를 태그한다"를 확인할 상대가 없다. 그래서 그레이박스의 밸브
    /// 큐브가 실제 밸브를 대신하듯, 씬에 놓인 이 오브젝트가 도망자를 대신한다.
    /// 네트워크 스프린트에서 실제 원격 플레이어로 대체된다.
    ///
    /// §3.1 "태그 1회 → 메아리로 즉시 전환"을 그대로 구현한다 — 태그되면
    /// <see cref="Role"/>이 <see cref="RoleType.Echo"/>가 되고, 메아리는 §3.1상
    /// "태그 불가"이므로 이후 태그 시도가 자연히 걸러진다(별도 쿨다운 불필요).
    /// </summary>
    public sealed class TaggableRunner : MonoBehaviour
    {
        [Tooltip("역할 배정 시스템 배선 전 임시 식별자. 씬 안에서 고유해야 한다.")]
        [SerializeField] private ulong _playerId = 100;

        [SerializeField] private string _displayName = "도망자";

        public ulong PlayerId => _playerId;
        public string DisplayName => _displayName;

        /// <summary>태그 전에는 도망자, 태그 후에는 §3.1대로 메아리.</summary>
        public RoleType Role { get; private set; } = RoleType.Runner;

        public bool IsTagged => Role == RoleType.Echo;

        /// <summary>§3.1: 태그 1회로 즉시 메아리 전환.</summary>
        public void MarkTagged()
        {
            Role = RoleType.Echo;
        }
    }
}
