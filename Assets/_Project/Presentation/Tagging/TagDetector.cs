using UnityEngine;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Player;

namespace Marco.Presentation.Tagging
{
    /// <summary>
    /// §3.1 태그 판정. 술래 역할일 때만 동작하며, 1.2m 이내 도망자를 즉시 태그한다.
    ///
    /// 판정 규칙은 <see cref="TagRules"/>(순수)에, 집계와 승패는
    /// <see cref="RoundCoordinator"/>에 있다 — 스프린트 6에서 확립한
    /// **"판정 권한은 RoundCoordinator 단일"** 원칙을 유지해, 여기서는 승패를
    /// 계산하지 않고 태그 사실만 보고한다.
    ///
    /// §3.1 "1회 접촉 즉시 확정"이라 홀드도 쿨다운도 없다. 태그당한 대상은
    /// 즉시 메아리가 되어 다시 태그되지 않으므로 재태그 방지 장치도 필요 없다.
    /// </summary>
    public sealed class TagDetector : MonoBehaviour
    {
        [SerializeField] private FirstPersonController _player;
        [SerializeField] private RoundCoordinator _roundCoordinator;

        private TaggableRunner[] _runners;

        private void Awake()
        {
            if (_player == null)
                _player = GetComponentInParent<FirstPersonController>() ?? FindAnyObjectByType<FirstPersonController>();
            if (_roundCoordinator == null)
                _roundCoordinator = FindAnyObjectByType<RoundCoordinator>();

            _runners = FindObjectsByType<TaggableRunner>();

            if (_player == null || _roundCoordinator == null)
            {
                Debug.LogError("[Tag] Player 또는 RoundCoordinator를 찾지 못했습니다 — 비활성화합니다.");
                enabled = false;
                return;
            }

            if (_runners.Length == 0)
                Debug.LogWarning("[Tag] 씬에 TaggableRunner가 없습니다 — 태그할 대상이 없습니다.");
        }

        private void Update()
        {
            // 역할 조건은 TagRules가 최종 판단하지만, 술래가 아니면 순회 자체가 낭비다.
            if (_player.Role != Marco.Core.Role.RoleType.Seeker)
                return;

            Vector3 seekerPosition = _player.transform.position;

            for (int i = 0; i < _runners.Length; i++)
            {
                TaggableRunner runner = _runners[i];
                if (runner == null || runner.IsTagged)
                    continue;

                if (!TagRules.IsWithinTagRange(seekerPosition, runner.transform.position))
                    continue;

                if (_roundCoordinator.TryRegisterTag(_player.Role, runner.PlayerId, runner.Role))
                    runner.MarkTagged(); // §3.1: 태그 1회 → 메아리 즉시 전환
            }
        }
    }
}
