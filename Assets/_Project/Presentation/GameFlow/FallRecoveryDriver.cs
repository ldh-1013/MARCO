using Marco.Core.GameFlow;
using Marco.Presentation.Player;
using UnityEngine;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 맵 밖으로 떨어진 **로컬 플레이어**를 마지막 안전 지점으로 되돌린다(스프린트 20).
    ///
    /// **왜 로컬(소유자) 처리인가**: pawn의 Transform은 스프린트 8부터 **소유자 권위**
    /// (<c>NetworkTransform</c>)다. 즉 위치를 정하는 권한은 이미 소유 클라이언트에 있고,
    /// 여기서 옮긴 좌표는 기존 동기화 경로로 상대에게 그대로 전파된다. 태그·탈출·밸브를
    /// 서버 권위로 둔 이유는 그것들이 **라운드 결과를 바꾸기** 때문인데, 리스폰은 결과 판정에
    /// 개입하지 않으며 서버 RPC를 거쳐도 소유자가 이미 가진 위치 권한을 회수하지 못한다.
    /// 왕복 지연만 늘고 얻는 보증이 없어 소유자 처리로 둔다.
    /// (이동 자체가 서버 권위로 바뀌면 이 판정도 함께 옮겨야 한다 — GAP-33.)
    ///
    /// **역할 상태는 건드리지 않는다**: 이 컴포넌트는 Transform과 수직 속도만 만진다.
    /// <c>IRoleState</c>/<c>RoleNetworkSync</c>/태그 상태에 접근하지 않으므로, 메아리로 바뀐
    /// 뒤에 떨어져도 역할은 그대로 유지된다(§0 요구사항).
    ///
    /// 판정 규칙은 Core의 <see cref="FallRecovery"/>가 갖고, 여기서는 Unity 조작만 한다.
    /// </summary>
    public sealed class FallRecoveryDriver : MonoBehaviour
    {
        [Tooltip("이 높이 아래로 내려가면 떨어진 것으로 보고 되돌린다.")]
        [SerializeField] private float _killPlaneY = FallRecovery.DefaultKillPlaneY;

        [Tooltip("접지 상태에서 안전 지점을 기록하는 주기(초).")]
        [SerializeField] private float _sampleIntervalSeconds = FallRecovery.DefaultSampleIntervalSeconds;

        [Tooltip("되돌릴 때 지면에서 살짝 띄우는 높이(바닥에 파묻히는 것 방지).")]
        [SerializeField] private float _verticalOffset = 0.05f;

        [SerializeField] private bool _logRecovery = true;

        private FallRecovery _recovery;
        private FirstPersonController _tracked;
        private bool _hasMapFallback;
        private Vector3 _mapFallback;

        private void Awake()
        {
            _recovery = new FallRecovery(_killPlaneY, _sampleIntervalSeconds);
        }

        private void Update()
        {
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null)
                return;

            // 플레이어가 교체되면(재접속·재스폰) 이전 판의 좌표를 물고 있지 않도록 비운다.
            if (!ReferenceEquals(player, _tracked))
            {
                _tracked = player;
                _recovery.Reset();
            }

            // 맵이 로드돼 있으면 그 스폰 지점을 최후의 폴백으로 쓴다(안전 지점 기록 전에 떨어진 경우).
            if (SpawnAnchorRegistry.HasAnchor)
            {
                _hasMapFallback = true;
                _mapFallback = SpawnAnchorRegistry.Pose.Position;
            }

            var controller = player.GetComponent<CharacterController>();
            bool grounded = controller != null && controller.isGrounded;
            Vector3 position = player.transform.position;

            _recovery.Sample(Time.deltaTime, position, grounded);

            if (_recovery.HasFallen(position))
                Recover(player, controller);
        }

        private void Recover(FirstPersonController player, CharacterController controller)
        {
            // 안전 지점이 없으면 맵 스폰 지점, 그것도 없으면 제자리 위(로비 임시 바닥 위)로 올린다.
            Vector3 fallback = _hasMapFallback
                ? _mapFallback
                : new Vector3(player.transform.position.x, 0f, player.transform.position.z);

            Vector3 target = _recovery.GetRecoveryPoint(fallback) + Vector3.up * _verticalOffset;

            // CharacterController가 활성인 동안에는 위치 대입이 무시될 수 있다(순간이동 표준 처리).
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;

            player.transform.position = target;

            if (wasEnabled)
                controller.enabled = true;

            // 누적된 낙하 속도를 지우지 않으면 다음 프레임에 바닥을 뚫고 다시 떨어진다.
            player.ResetVerticalVelocity();

            if (_logRecovery)
            {
                Debug.Log($"[FallRecovery] 낙하 감지(y < {_killPlaneY}) — " +
                          $"{(_recovery.HasSafePoint ? "마지막 안전 지점" : "폴백 지점")} {target} 으로 복구했습니다. " +
                          "역할·태그 상태는 그대로 유지됩니다.");
            }
        }
    }
}
