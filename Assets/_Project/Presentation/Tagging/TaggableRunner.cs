using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Role;

namespace Marco.Presentation.Tagging
{
    /// <summary>
    /// 태그 대상이 되는 도망자 하나.
    ///
    /// **로컬 단독 실행용 대역(stand-in)이다.** 지금은 플레이어가 한 명뿐이라
    /// "술래가 도망자를 태그한다"를 확인할 상대가 없다. 그래서 그레이박스의 밸브
    /// 큐브가 실제 밸브를 대신하듯, 씬에 놓인 이 오브젝트가 도망자를 대신한다.
    /// 네트워크 스프린트에서 실제 원격 플레이어(<c>TagNetworkSync</c>)로 대체된다.
    ///
    /// §3.1 "태그 1회 → 메아리로 즉시 전환"을 그대로 구현한다 — 태그되면
    /// <see cref="Role"/>이 <see cref="RoleType.Echo"/>가 되고, 메아리는 §3.1상
    /// "태그 불가"이므로 이후 태그 시도가 자연히 걸러진다(별도 쿨다운 불필요).
    ///
    /// 스프린트 11: <see cref="ITagTarget"/>을 구현해 네트워크 플레이어와 <b>같은 방식</b>으로
    /// <see cref="TagDetector"/>에 노출된다. 다만 <see cref="NetworkActive"/>는 항상 false라
    /// 서버를 거치지 않고 즉시 확정한다(로컬 대역이므로 다른 클라이언트에 동기화되지 않음).
    /// </summary>
    public sealed class TaggableRunner : MonoBehaviour, ITagTarget
    {
        [Tooltip("대역 도망자의 안정적 로컬 ID. 씬 안에서 고유해야 한다(현재 101~103). " +
                 "실제 원격 러너로 교체되는 네트워크 2단계에서는 OwnerId로 대체된다. " +
                 "로컬 플레이어(FirstPersonController) ID 1과 겹치지 않도록 100번대를 쓴다.")]
        [SerializeField] private ulong _playerId = 100;

        [SerializeField] private string _displayName = "도망자";

        public ulong PlayerId => _playerId;
        public string DisplayName => _displayName;

        /// <summary>태그 전에는 도망자, 태그 후에는 §3.1대로 메아리.</summary>
        public RoleType Role { get; private set; } = RoleType.Runner;

        public bool IsTagged => Role == RoleType.Echo;

        public Vector3 WorldPosition => transform.position;

        /// <summary>로컬 대역이라 서버 권위 대상이 아니다 — 즉시 확정한다.</summary>
        public bool NetworkActive => false;

        private void OnEnable() => TagTargetRegistry.Register(this);
        private void OnDisable() => TagTargetRegistry.Unregister(this);

        /// <summary>
        /// 술래의 태그 의사. 거리·역할은 <see cref="TagDetector"/>가 이미 걸렀으므로,
        /// 로컬 대역은 즉시 확정하고 라운드 집계에 통지한다(네트워크 왕복 없음).
        /// </summary>
        public void RequestTag(ulong seekerId, RoleType seekerRole)
        {
            if (IsTagged)
                return;

            MarkTagged();
            TagTargetRegistry.NotifyTagged(this);
        }

        /// <summary>§3.1: 태그 1회로 즉시 메아리 전환.</summary>
        public void MarkTagged()
        {
            Role = RoleType.Echo;
        }
    }
}
