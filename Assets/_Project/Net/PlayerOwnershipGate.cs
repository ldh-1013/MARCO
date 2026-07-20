using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using Marco.Core.Net;

namespace Marco.Net
{
    /// <summary>
    /// 네트워크 소유권을 같은 오브젝트의 Presentation 컴포넌트에 전달한다.
    ///
    /// **어셈블리 경계 유지**: `Net`은 `Presentation`을 참조하지 않는다.
    /// 대신 양쪽이 공통으로 아는 <see cref="ILocalControlGate"/>(Core)를
    /// `GetComponent`로 찾아 호출한다 — 구체 타입(`FirstPersonController`)을
    /// 전혀 모르는 채로 연결되므로 §15.2 3분할이 깨지지 않는다.
    ///
    /// 소유권은 스폰 직후(<see cref="OnStartClient"/>)와 이후 소유권 이전
    /// (<see cref="OnOwnershipClient"/>) 양쪽에서 반영해야 한다. 전자만 처리하면
    /// 런타임 중 소유권이 바뀌는 경우를 놓친다.
    /// </summary>
    public sealed class PlayerOwnershipGate : NetworkBehaviour
    {
        private ILocalControlGate[] _gates;

        private void Awake()
        {
            // 인터페이스 기준으로 찾으므로 Presentation의 구체 타입을 알 필요가 없다.
            _gates = GetComponentsInChildren<ILocalControlGate>(includeInactive: true);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            PushOwnership();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            PushOwnership();
        }

        private void PushOwnership()
        {
            if (_gates == null)
                return;

            for (int i = 0; i < _gates.Length; i++)
                _gates[i]?.SetLocalControl(IsOwner);

            gameObject.name = IsOwner ? "Player (Local)" : "Player (Remote)";
        }
    }
}
