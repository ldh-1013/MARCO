using UnityEngine;
using FishNet.Managing;
using Marco.Core.GameFlow;

namespace Marco.Net
{
    /// <summary>
    /// FishNet NetworkManager를 감싸는 격리 계층(§15.3). 이 클래스만 FishNet 타입을
    /// 직접 참조하며, Core는 이 클래스의 존재를 모른다. §14.3 이벤트 테이블의
    /// 실제 RPC/SyncVar 배선은 Phase 3(빌드 단계)에서 채운다 — 여기서는
    /// GameFlowManager를 감싸는 뼈대와 시작 상태 조회만 제공한다.
    /// </summary>
    public class NetworkBridge : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        private readonly GameFlowManager _gameFlow = new GameFlowManager();
        public GameFlowManager GameFlow => _gameFlow;

        public bool IsServerStarted => _networkManager != null && _networkManager.ServerManager.Started;
        public bool IsClientStarted => _networkManager != null && _networkManager.ClientManager.Started;

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = GetComponent<NetworkManager>();
        }
    }
}
