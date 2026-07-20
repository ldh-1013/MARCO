namespace Marco.Core.Net
{
    /// <summary>
    /// 네트워크 소유권(ownership)을 Presentation에 전달하는 최소 계약.
    ///
    /// **왜 Core에 두는가**: §15.2 어셈블리 분리상 `Net`과 `Presentation`은 서로를
    /// 참조하지 않는다. 그런데 "내 캐릭터가 아니면 입력을 막아야 한다"는 판단은
    /// FishNet 개념(Net)이고, 입력 처리는 `FirstPersonController`(Presentation)에 있다.
    /// 두 어셈블리가 공통으로 참조하는 Core에 인터페이스만 두어, 어느 쪽도 상대를
    /// 직접 참조하지 않고 연결한다 — `IOcclusionProbe`가 Core와 Physics를 갈라놓은
    /// 것과 같은 원리다.
    ///
    /// Core는 FishNet도 UnityEngine도 모르므로 이 파일에는 어떤 using도 없다.
    /// </summary>
    public interface ILocalControlGate
    {
        /// <summary>
        /// 이 오브젝트를 로컬 플레이어가 조종하는지 알린다.
        ///
        /// <para><c>false</c>면 구현체는 입력 폴링·카메라·커서 잠금을 모두 중단하고,
        /// 오직 네트워크로 받은 Transform 값으로만 움직여야 한다.</para>
        ///
        /// <para>네트워크가 없는 로컬 단독 실행에서는 아무도 이 메서드를 호출하지
        /// 않는다. 따라서 구현체의 기본값은 <c>true</c>(로컬 조종)여야 스프린트 3~7의
        /// 로컬 스모크 리그가 그대로 동작한다.</para>
        /// </summary>
        void SetLocalControl(bool isLocallyControlled);
    }
}
