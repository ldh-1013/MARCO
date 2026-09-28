using Marco.Core.GameFlow;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 맵 밖으로 떨어진 **로컬 플레이어**를 되돌린다(스프린트 20 · 09-29 개정).
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
    /// <para>
    /// <b>[09-29 개정]</b> ① 맵이 올라오거나 내려가면 안전 지점을 비운다 — 09-28 기계실 루프의 원인은 판이 끝나 맵이
    /// 내려간 뒤에도 맵 위 안전 지점(48.47, 11.52)으로 되돌려, 바닥 없는 그 자리에서 다음 맵 로드까지 매초 다시 떨어진 것이다.
    /// ② 안전 지점은 접지 + <b>발밑 레이가 바닥을 맞힐 때만</b> 기록한다. ③ 직전 복구 뒤 10초 안에 또 떨어지면 배정 스폰
    /// 지점으로 보낸다(<see cref="FallRecovery.Recover"/>). ④ 발동마다 <c>Debug.LogError</c> — 복구가 원인을 가리지 않게.
    /// </para>
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

        /// <summary>발밑 레이 — 발 위 0.5m에서 아래로 1m(접지한 발 기준 ±0.5m). 발소리 재질 프로브와 같은 출발 높이.</summary>
        private const float FloorProbeStartHeight = 0.5f;
        private const float FloorProbeLength = 1f;

        /// <summary>바닥으로 치는 면의 기울기 한계(법선 y). 캐릭터가 설 수 없는 가파른 면 · 벽 옆면은 바닥이 아니다.</summary>
        private const float MinFloorNormalY = 0.5f;

        private FallRecovery _recovery;
        private FirstPersonController _tracked;
        private PawnPhaseTeleporter _teleporter;
        private bool _mapPresent;
        private int _floorMask = ~0;

        private void Awake()
        {
            _recovery = new FallRecovery(_killPlaneY, _sampleIntervalSeconds);
            _mapPresent = SpawnAnchorRegistry.HasAnchor;

            // 벽 차폐 콜라이더(SoundBlocking)는 바닥이 아니다 — 발소리 재질 프로브와 같은 마스크.
            int soundBlocking = LayerMask.GetMask(PhysicsOcclusionProbe.SoundBlockingLayerName);
            _floorMask = soundBlocking == 0 ? ~0 : ~soundBlocking;
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

            // 맵이 올라오거나 내려가면 이전 지오메트리의 안전 지점은 무효다(09-28 기계실 루프의 근본 원인).
            bool mapPresent = SpawnAnchorRegistry.HasAnchor;
            if (mapPresent != _mapPresent)
            {
                _mapPresent = mapPresent;
                _recovery.Reset();
            }

            var controller = player.GetComponent<CharacterController>();
            bool grounded = controller != null && controller.isGrounded;
            Vector3 position = player.transform.position;

            _recovery.Sample(Time.deltaTime, position, grounded, grounded && HasFloorBelow(position));

            if (_recovery.HasFallen(position))
                Recover(player, controller, position);
        }

        /// <summary>발밑 레이가 바닥(설 수 있는 면)을 맞히는가. 벽 모서리 · 틈 가장자리에 걸친 순간을 안전 지점에서 뺀다.</summary>
        private bool HasFloorBelow(Vector3 feet)
        {
            return Physics.Raycast(feet + Vector3.up * FloorProbeStartHeight, Vector3.down, out RaycastHit hit,
                       FloorProbeLength, _floorMask, QueryTriggerInteraction.Ignore)
                   && hit.normal.y >= MinFloorNormalY;
        }

        private void Recover(FirstPersonController player, CharacterController controller, Vector3 fallPosition)
        {
            // 배정 스폰 지점 — 맵이 있으면 맵 로드 배치와 같은 계산, 없으면 로비 임시 바닥 위 같은 슬롯. 둘 다 없으면 제자리 위.
            if (_teleporter == null)
                _teleporter = FindAnyObjectByType<PawnPhaseTeleporter>();

            Vector3 assignedSpawn;
            if (_teleporter == null || !_teleporter.TryGetAssignedSpawn(player, out assignedSpawn))
                assignedSpawn = new Vector3(fallPosition.x, _verticalOffset, fallPosition.z);

            Vector3 safePoint = _recovery.SafePoint;
            Vector3 target = _recovery.Recover(Time.time, assignedSpawn, out FallRecoveryKind kind);
            if (kind == FallRecoveryKind.SafePoint)
                target += Vector3.up * _verticalOffset; // 배정 스폰은 이미 오프셋을 포함한다

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
                string kindText = kind switch
                {
                    FallRecoveryKind.SafePoint => "마지막 안전 지점",
                    FallRecoveryKind.AssignedSpawnRepeatedFall =>
                        $"배정 스폰 — {FallRecovery.DefaultRepeatWindowSeconds:0}초 안 반복 낙하(안전 지점 {Fmt(safePoint)} 포기)",
                    _ => "배정 스폰 — 안전 지점 없음",
                };

                // LogError — 복구가 원인을 가리지 않도록(09-29 결정). 역할 · 태그 상태는 건드리지 않는다.
                Debug.LogError($"[FallRecovery] 낙하 감지(y < {_killPlaneY}) — 발생 {Fmt(fallPosition)} → 복구 {Fmt(target)} " +
                               $"[{kindText}] · 씬 {SceneManager.GetActiveScene().name} · 맵 {(_mapPresent ? "있음" : "없음")} · " +
                               $"역할 {player.Role} · PlayerId {player.PlayerId}");
            }
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
    }
}
