using UnityEngine;
using Marco.Core.Role;

namespace Marco.Core.Net
{
    /// <summary>
    /// "태그 대상이 될 수 있는 것"의 통일 계약(스프린트 11). 술래의 태그 판정
    /// (<c>TagDetector</c>, Presentation)이 로컬 대역(<c>TaggableRunner</c>)과 네트워크
    /// 실제 플레이어(<c>TagNetworkSync</c>, Net)를 <b>같은 방식으로</b> 다루게 한다.
    ///
    /// **왜 Core에 두는가**: 소비자는 Presentation(TagDetector)이고 구현체 중 하나는
    /// Net(TagNetworkSync)이다. 둘은 §15.2상 서로를 참조하지 않으므로, 공통 조상 Core에
    /// 계약을 둔다. 대상 열거는 <see cref="TagTargetRegistry"/>가 담당한다.
    ///
    /// **밸브와 다른 점**: 밸브는 한 오브젝트를 조작했지만, 태그는 "술래 → 대상"의 두
    /// 주체가 있다. 그래서 대상 쪽을 이 인터페이스로 추상화하고, 술래는 <see cref="RequestTag"/>로
    /// "이 대상을 태그하겠다"는 의사만 보낸다 — 실제 확정은 서버가 한다(네트워크 대상의 경우).
    /// </summary>
    public interface ITagTarget
    {
        /// <summary>대상의 안정적 ID(로컬 대역은 인스펙터 값, 네트워크는 OwnerId).</summary>
        ulong PlayerId { get; }

        /// <summary>대상의 현재 역할. 태그되면 §3.1대로 Echo가 된다.</summary>
        RoleType Role { get; }

        /// <summary>이미 태그됐는가(= 역할이 Echo). 태그된 대상은 재태그 불가.</summary>
        bool IsTagged { get; }

        /// <summary>대상의 월드 위치. 술래 쪽 클라이언트가 거리 사전 판정에 쓴다.</summary>
        Vector3 WorldPosition { get; }

        /// <summary>서버 권위(네트워크)로 관리되는 대상인가. false면 로컬 대역(즉시 확정).</summary>
        bool NetworkActive { get; }

        /// <summary>
        /// 술래가 이 대상을 태그하겠다는 의사를 전달한다.
        /// - 네트워크 대상: 서버에 요청(ServerRpc)만 하고, 거리·역할 재검증·확정은 서버가 한다.
        /// - 로컬 대역: 즉시 확정(호출자가 이미 거리·역할을 걸렀다).
        /// </summary>
        void RequestTag(ulong seekerId, RoleType seekerRole);
    }
}
