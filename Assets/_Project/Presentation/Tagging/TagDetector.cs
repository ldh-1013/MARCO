using System.Collections.Generic;
using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Tagging;
using Marco.Presentation.Player;

namespace Marco.Presentation.Tagging
{
    /// <summary>
    /// §3.1 태그 판정. 술래 역할일 때만 동작하며, 1.2m 이내 도망자를 즉시 태그 요청한다.
    ///
    /// 판정 규칙은 <see cref="TagRules"/>(Core, 순수)에, 집계와 승패는
    /// <see cref="Marco.Presentation.GameFlow.RoundCoordinator"/>에 있다.
    ///
    /// **스프린트 11(서버 권위 동기화)**: 더 이상 이 컴포넌트가 직접 대상의 역할을 바꾸거나
    /// RoundCoordinator에 등록하지 않는다. 대상(<see cref="ITagTarget"/>)에게 "태그하겠다"는
    /// 의사만 <see cref="ITagTarget.RequestTag"/>로 보낸다 —
    /// - 네트워크 플레이어(<c>TagNetworkSync</c>): 서버에 요청 → 서버가 거리·역할 재검증·확정.
    /// - 로컬 대역(<c>TaggableRunner</c>): 즉시 확정.
    /// 확정 결과의 라운드 집계는 <see cref="TagTargetRegistry.TargetTagged"/>를 구독하는
    /// RoundCoordinator가 처리한다(로컬/네트워크 공통 경로).
    ///
    /// 대상 열거도 씬 스캔이 아니라 <see cref="TagTargetRegistry"/>로 한다 — 로컬 대역과
    /// 네트워크 플레이어를 어셈블리 경계 넘어 한 목록으로 다루기 위함.
    /// </summary>
    public sealed class TagDetector : MonoBehaviour
    {
        [SerializeField] private FirstPersonController _player;

        [Tooltip("같은 대상에게 태그 요청을 다시 보내기까지의 최소 간격(초). 기획서에 수치가 없어 둔 값이다(GAP-58).")]
        [SerializeField, Range(0.1f, 2f)] private float _retryIntervalSeconds = 0.5f;

        [Header("진단 (실기 태그 디버그용 — 확인 끝나면 꺼도 됨)")]
        [Tooltip("술래 시점에서 2초마다 등록 대상/태그가능 러너/사거리 내 러너 수를 로그로 찍는다. " +
                 "원격 플레이어가 등록·인식되는지 이등분 확인용.")]
        [SerializeField] private bool _logTargetDiagnostics = true;
        private float _nextDiagTime;

        /// <summary>
        /// 대상별 다음 요청 허용 시각. 밸브(<c>ValveNetworkSync</c>의 송신 디듀프)와 같은 목적이다 —
        /// 매 프레임 같은 요청을 보내지 않는다.
        ///
        /// <b>왜 필요한가</b>: 이 컴포넌트는 사거리 안에 러너가 있는 동안 <see cref="Update"/>마다
        /// <see cref="ITagTarget.RequestTag"/>를 불렀다. 태그가 확정되면 <c>IsTagged</c>로 멈추지만,
        /// 확정까지의 왕복 시간 동안 프레임 수만큼 중복 ServerRpc가 나갔다. 더 나쁜 경우는
        /// <b>서버가 거부할 때</b>다 — 서버가 역할을 재검증하게 된 뒤로는(2/3 B항목) 클라이언트가
        /// 자기를 술래로 오인하면 사거리 안에 서 있는 내내 초당 수십 건이 무한 전송된다.
        ///
        /// 사거리를 벗어나면 항목을 지워, 다시 접근했을 때는 즉시 시도할 수 있게 한다
        /// (§3.1 "1회 접촉 즉시 확정"의 체감을 해치지 않기 위함).
        /// </summary>
        private readonly Dictionary<ulong, float> _nextRequestAt = new Dictionary<ulong, float>();

        private void Awake()
        {
            if (_player == null)
                _player = GetComponentInParent<FirstPersonController>() ?? FindAnyObjectByType<FirstPersonController>();

            if (_player == null)
            {
                Debug.LogError("[Tag] Player를 찾지 못했습니다 — 비활성화합니다.");
                enabled = false;
            }
        }

        private void Update()
        {
            // 스프린트 11: 네트워크 모드에서 이 컴포넌트는 원격 플레이어 프록시에도 존재한다.
            // 로컬 조종 술래만 판정·요청해야 원격 프록시가 내 키보드로 남을 태그하지 않는다
            // (스프린트 10에서 ValveInteractor에 넣은 것과 같은 소유권 가드).
            if (!_player.IsLocallyControlled)
                return;

            // 술래만 태그한다(§3.1). 술래가 아니면 순회 자체가 낭비다.
            if (_player.Role != RoleType.Seeker)
                return;

            Vector3 seekerPos = _player.transform.position;
            ulong seekerId = _player.PlayerId;
            RoleType seekerRole = _player.Role;

            IReadOnlyList<ITagTarget> targets = TagTargetRegistry.Targets;

            // [진단] 술래 시점에서 원격 러너가 레지스트리에 보이는지·사거리 안인지 이등분 확인.
            // - 등록대상에 원격 러너가 안 잡히면(네트워크러너=0): 등록/역할 문제 (또는 컴포넌트 미스폰)
            // - 잡히는데 사거리내=0인데 붙어 있다면: NetworkTransform 프록시 위치/거리 문제
            // - 잡히고 사거리내≥1인데 태그 안 되면: [TagNet:Server] 확정/거부 로그를 볼 것(서버 재검증)
            if (_logTargetDiagnostics && Time.time >= _nextDiagTime)
            {
                _nextDiagTime = Time.time + 2f;
                int runnerCount = 0, networkRunners = 0, inRangeRunners = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    ITagTarget t = targets[i];
                    if (t == null || t.IsTagged || t.Role != RoleType.Runner)
                        continue;
                    runnerCount++;
                    if (t.NetworkActive) networkRunners++;
                    if (TagRules.IsWithinTagRange(seekerPos, t.WorldPosition)) inRangeRunners++;
                }
                Debug.Log($"[Tag:Diag] 술래 시점 — 등록대상={targets.Count}, 태그가능러너={runnerCount} " +
                          $"(네트워크러너={networkRunners}), 사거리(1.2m)내러너={inRangeRunners}");
            }

            float now = Time.time;

            for (int i = 0; i < targets.Count; i++)
            {
                ITagTarget target = targets[i];
                if (target == null)
                    continue;

                if (target.IsTagged || target.Role != RoleType.Runner)
                {
                    // 확정됐거나 대상이 아니게 됐으면 재시도 상태를 남겨 둘 이유가 없다.
                    _nextRequestAt.Remove(target.PlayerId);
                    continue;
                }

                // 거리 사전 판정(§3.1 1.2m). 네트워크 대상은 서버가 다시 검증한다(§5.3).
                if (!TagRules.IsWithinTagRange(seekerPos, target.WorldPosition))
                {
                    // 사거리를 벗어나면 쿨다운을 지운다 — 다시 붙었을 때 즉시 시도한다.
                    _nextRequestAt.Remove(target.PlayerId);
                    continue;
                }

                // 이미 보낸 요청이 처리되기를 기다리는 중이면 재전송하지 않는다.
                if (_nextRequestAt.TryGetValue(target.PlayerId, out float allowedAt) && now < allowedAt)
                    continue;

                _nextRequestAt[target.PlayerId] = now + _retryIntervalSeconds;
                target.RequestTag(seekerId, seekerRole);
            }
        }
    }
}
