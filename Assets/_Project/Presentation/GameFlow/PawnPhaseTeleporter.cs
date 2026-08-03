using UnityEngine;
using Marco.Core.GameFlow;
using Marco.Presentation.Player;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 맵이 로드되면 로컬 플레이어를 맵의 스폰 지점으로 옮긴다(스프린트 18b).
    ///
    /// **왜 필요한가**: 플레이어 pawn은 접속 직후(로비 페이즈) 스폰되지만, 그때 맵은
    /// 아직 로드되지 않았다(§15.4 "InGame: 맵 로드"). 그래서 pawn은 시스템 씬의 대기 위치에
    /// 있다가, 맵이 도착한 시점에 §10.1 입구 로비로 이동해야 한다.
    ///
    /// **각 클라이언트가 자기 pawn만 옮긴다**: NetworkTransform은 소유자 권위이므로
    /// (스프린트 8) 로컬 소유 pawn을 로컬에서 움직이면 그 값이 정상적으로 전파된다.
    /// 남의 pawn을 움직이려 하면 다음 동기화에 덮여 되돌아가므로 시도하지 않는다.
    ///
    /// <see cref="CharacterController"/>는 <c>transform.position</c> 직접 대입을 무시할 수 있어
    /// 이동 전에 잠시 비활성화한다(순간이동의 표준 처리).
    /// </summary>
    public sealed class PawnPhaseTeleporter : MonoBehaviour
    {
        [Tooltip("맵 스폰 지점 도착 시 이 높이만큼 띄워 배치한다(바닥에 파묻히는 것 방지).")]
        [SerializeField] private float _verticalOffset = 0.05f;

        [Header("스폰 분산 (실기 버그 수정)")]
        [Tooltip("앵커를 중심으로 이 반경의 원 위에 플레이어를 나눠 배치한다. 0이면 전원이 앵커 한 점에 " +
                 "모여 시작 즉시 태그가 성립한다(스프린트 18b 실기 버그).")]
        [SerializeField] private float _spawnRadius = SpawnRing.DefaultRadiusMeters;

        [Tooltip("원 위 슬롯 수. 플레이어 인덱스가 이 수를 넘으면 되돌아온다.")]
        [SerializeField] private int _spawnSlots = SpawnRing.DefaultSlots;

        private bool _placedForCurrentMap;
        private int _placedForRound = int.MinValue;
        private RoundCoordinator _round;
        private readonly SeekerIsolation _isolation = new SeekerIsolation();
        private bool _loggedAlive;
        private bool _loggedWaitingAnchor;
        private bool _loggedWaitingPlayer;

        private void Start()
        {
            // "새 코드가 도는가"를 로그 유무로 판별할 수 있게 하는 표식. 이 줄이 없으면
            // 이 컴포넌트가 씬에 없거나 어셈블리가 갱신되지 않은 것이다.
            Debug.Log($"[SpawnDiag] PawnPhaseTeleporter 시작 — 스폰 분산 활성(반경 {_spawnRadius}m, " +
                      $"슬롯 {_spawnSlots}, 인접 간격 {SpawnRing.AdjacentSpacing(_spawnRadius, _spawnSlots):F2}m).");
            _loggedAlive = true;
        }

        private void Update()
        {
            // §10.1 격리 대기는 배치 여부와 무관하게 매 프레임 흘러야 한다.
            TickIsolation();

            bool mapReady = SpawnAnchorRegistry.HasAnchor;

            // 맵이 언로드되면 다음 로드에서 다시 배치하도록 래치를 푼다(리매치·부결 복귀 대응).
            if (!mapReady)
            {
                if (_loggedAlive && !_loggedWaitingAnchor)
                {
                    _loggedWaitingAnchor = true;
                    Debug.Log("[SpawnDiag] 대기 중 — 맵의 SpawnAnchor가 아직 등록되지 않았습니다(맵 미로드).");
                }

                _placedForCurrentMap = false;
                return;
            }

            // 리매치 스폰 리셋(스프린트 22): 맵이 유지된 채 새 라운드가 시작되면(리매치 가결)
            // 라운드 번호가 바뀐다. 그 순간을 "다시 배치할 때"로 삼는다 — 맵 로드 신호만 보던
            // 기존 구조로는 리매치 때 아무도 움직이지 않아 직전 라운드 자리에서 재시작됐다.
            //
            // 순서 보장: 서버는 배정(EnsureRolesAssigned) **직후** 라운드 번호를 올리고 그 뒤에
            // 라운드를 시작하므로, 클라이언트가 새 번호를 관측한 시점에는 역할이 이미 확정돼 있다.
            // 스폰 슬롯 자체는 PlayerId로만 정해져 역할과 무관하다.
            int round = CurrentRoundNumber();
            if (_placedForCurrentMap && round == _placedForRound)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null)
            {
                if (!_loggedWaitingPlayer)
                {
                    _loggedWaitingPlayer = true;
                    Debug.Log("[SpawnDiag] 대기 중 — 맵은 준비됐으나 로컬 플레이어가 아직 스폰되지 않았습니다.");
                }

                return; // 아직 스폰 전 — 다음 프레임에 다시 시도한다.
            }

            // §10.1 술래 격리(스프린트 24): 술래는 도망자 스폰 지점이 아니라 격리 공간에서
            // 시작하고 3초 뒤에 움직일 수 있다. 러너·메아리는 종전대로 스폰 링에 배치된다.
            bool isolate = SeekerIsolation.AppliesTo(player.Role);
            if (isolate && IsolationAnchorRegistry.HasAnchor)
                PlaceExactly(player, IsolationAnchorRegistry.Pose);
            else
                PlaceAtAnchor(player, SpawnAnchorRegistry.Pose);

            _placedForCurrentMap = true;
            _placedForRound = round;

            // 격리 앵커가 없어도 대기 자체는 건다 — §10.1의 "3초 후 진입"은 위치가 아니라
            // 시간 규칙이고, 앵커 누락으로 규칙까지 사라지면 안 된다(GAP-45).
            _isolation.Begin(player.Role);
            player.SetMovementLocked(_isolation.IsHolding);

            if (isolate && !IsolationAnchorRegistry.HasAnchor)
            {
                Debug.LogWarning("[Isolation] 격리 앵커(SeekerIsolationAnchor)가 맵에 없어 술래를 " +
                                 "스폰 링에 배치했습니다 — 3초 대기는 그대로 적용됩니다. " +
                                 "Tools/MARCO/Scene Flow — 3. 맵 씬 정리로 앵커를 생성하세요.");
            }
            else if (isolate)
            {
                Debug.Log($"[Isolation] 술래를 격리 공간 {IsolationAnchorRegistry.Pose.Position}에 배치 — " +
                          $"{SeekerIsolation.IsolationSeconds:0}초 후 이동 가능(§10.1).");
            }
        }

        /// <summary>격리 공간은 한 지점이라 스폰 링 분산을 적용하지 않는다(술래는 1인 — §6.2).</summary>
        private void PlaceExactly(FirstPersonController player, SpawnPose pose)
        {
            Vector3 target = pose.Position + Vector3.up * _verticalOffset;

            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;

            player.transform.SetPositionAndRotation(target, pose.Rotation);

            if (wasEnabled)
                controller.enabled = true;
        }

        /// <summary>§10.1 격리 대기를 흘리고, 끝나면 이동을 풀어 준다.</summary>
        private void TickIsolation()
        {
            if (!_isolation.IsHolding)
                return;

            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null)
                return;

            _isolation.Tick(Time.deltaTime);

            if (_isolation.IsHolding)
            {
                player.SetMovementLocked(true);
                return;
            }

            player.SetMovementLocked(false);
            Debug.Log("[Isolation] 격리 해제 — 술래가 맵으로 진입합니다(§10.1).");
        }

        /// <summary>격리 대기의 남은 초(HUD 표시용). 대기 중이 아니면 0.</summary>
        public float IsolationRemaining => _isolation.Remaining;

        /// <summary>
        /// 현재 라운드 번호. 라운드 지휘부를 찾지 못하거나 로컬 단독 실행이면 0으로 고정되어
        /// 기존(맵 로드 1회 배치) 동작이 그대로 유지된다.
        /// </summary>
        private int CurrentRoundNumber()
        {
            if (_round == null)
                _round = FindAnyObjectByType<RoundCoordinator>();

            return _round != null ? _round.RoundNumber : 0;
        }

        private void PlaceAtAnchor(FirstPersonController player, SpawnPose anchor)
        {
            // 플레이어별로 다른 슬롯을 쓴다. PlayerId는 접속마다 다른 값이라(스프린트 8 이후 서버가
            // 배정) 각 클라이언트가 스스로 계산해도 서로 다른 자리에 선다 — 서버가 좌표를 나눠
            // 줄 필요가 없다. 앵커 한 점에 전원이 모이면 시작 즉시 태그가 성립한다(§실기 버그).
            int slot = (int)(player.PlayerId % (ulong)Mathf.Max(1, _spawnSlots));
            SpawnPose spread = SpawnRing.GetPose(anchor, slot, _spawnRadius, _spawnSlots);

            Vector3 target = spread.Position + Vector3.up * _verticalOffset;

            // CharacterController가 활성인 상태에서는 위치 대입이 무시될 수 있다.
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;

            player.transform.SetPositionAndRotation(target, spread.Rotation);

            if (wasEnabled)
                controller.enabled = true;

            Debug.Log($"[SpawnDiag] 맵 로드 확인 — 로컬 플레이어(PlayerId={player.PlayerId})를 " +
                      $"스폰 슬롯 {slot}/{_spawnSlots} = {target} 으로 이동(§10.1 입구 로비). " +
                      $"인접 슬롯 간격 {SpawnRing.AdjacentSpacing(_spawnRadius, _spawnSlots):F2}m " +
                      $"(태그 반경보다 커야 시작 즉시 태그되지 않는다).");
        }
    }
}
