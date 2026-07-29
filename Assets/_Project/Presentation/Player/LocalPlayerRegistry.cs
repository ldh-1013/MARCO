using System;
using UnityEngine;

namespace Marco.Presentation.Player
{
    /// <summary>
    /// 로컬 플레이어가 언제 등장하는지 씬 컴포넌트들에게 알리는 지연 바인딩 지점.
    ///
    /// **왜 필요한가**: 스프린트 8에서 플레이어가 씬 고정 오브젝트에서 네트워크
    /// 스폰 프리팹으로 바뀌었다. 그러면 씬에 미리 놓인 컴포넌트들
    /// (`LocalPulsePipelineBehaviour`·`EscapePointTrigger`·`PulseVisualRenderer`)이
    /// 인스펙터로 걸어둔 플레이어 참조를 잃는다 — 프리팹 인스턴스는 편집 시점에
    /// 존재하지 않기 때문이다. `Awake`에서 `FindAnyObjectByType`으로 찾는 기존
    /// 폴백도 스폰 전이라 실패한다.
    ///
    /// 그래서 로컬 플레이어가 스폰되는 순간 여기에 등록하고, 필요한 쪽은
    /// <see cref="WhenReady"/>로 구독한다. 이미 등록돼 있으면 즉시 콜백되므로
    /// 등록 순서를 신경 쓰지 않아도 된다.
    ///
    /// 네트워크 없이 씬에 플레이어를 직접 놓고 실행하는 기존 방식도 그대로 동작한다 —
    /// 그 경우 플레이어가 자기 Awake에서 스스로 등록한다.
    /// </summary>
    public static class LocalPlayerRegistry
    {
        private static FirstPersonController _current;
        private static Action<FirstPersonController> _pending;

        /// <summary>현재 로컬 플레이어. 아직 스폰 전이면 null.</summary>
        public static FirstPersonController Current => _current;

        /// <summary>로컬 플레이어가 준비되면 호출된다. 이미 준비됐으면 즉시 호출.</summary>
        public static void WhenReady(Action<FirstPersonController> callback)
        {
            if (callback == null)
                return;

            if (_current != null)
            {
                callback(_current);
                return;
            }

            _pending += callback;
        }

        public static void StopWaiting(Action<FirstPersonController> callback)
        {
            if (callback != null)
                _pending -= callback;
        }

        /// <summary>로컬 플레이어가 스스로 등록한다(소유권 확정 후).</summary>
        public static void Register(FirstPersonController player)
        {
            if (player == null || _current == player)
                return;

            // 진단(실기): 원격 pawn도 OnEnable 시점에는 IsLocallyControlled 기본값(true)이라
            // 여기로 들어와 _current를 **덮어쓴다**. 그 뒤 소유권이 확정돼 Unregister되면
            // _current가 null이 되고, 이미 true였던 로컬 pawn은 SetLocalControl(true)가
            // 조기 반환해 재등록되지 않는다 — 그 흐름을 눈으로 확인하기 위한 로그다.
            Debug.Log($"[PlayerReg] Register('{player.gameObject.name}', 로컬조종={player.IsLocallyControlled}) — " +
                      $"이전 Current={(_current == null ? "없음" : _current.gameObject.name)}");

            _current = player;

            Action<FirstPersonController> waiting = _pending;
            _pending = null;
            waiting?.Invoke(player);
        }

        /// <summary>
        /// 등록을 해제한다. 해제 대상이 현재 등록자면 비운 뒤 **실제 로컬 pawn을 다시 찾아 복구한다**.
        ///
        /// **왜 복구가 필요한가**(실기에서 확정된 버그): 모든 pawn은 <c>OnEnable</c> 시점에
        /// <c>IsLocallyControlled</c> 기본값이 true라 원격 pawn도 자기를 등록해 <see cref="Current"/>를
        /// 덮어쓴다. 그 뒤 소유권이 확정돼 원격이 해제되면 <see cref="Current"/>가 null이 되는데,
        /// 내 pawn은 이미 true라 <c>SetLocalControl(true)</c>가 조기 반환해 재등록되지 않는다.
        /// 특히 호스트에서는 내 pawn의 소유권 확정이 **상대 접속보다 먼저** 끝나 있어, 재발행만으로는
        /// 순서상 복구되지 않는다. 그래서 해제 시점에 살아 있는 로컬 pawn을 직접 찾아 되살린다.
        ///
        /// 탐색 비용은 문제되지 않는다 — 접속·이탈 때만 도는 경로다.
        /// </summary>
        public static void Unregister(FirstPersonController player)
        {
            if (_current != player)
            {
                if (player != null)
                    Debug.Log($"[PlayerReg] Unregister('{player.gameObject.name}') — 현재 등록자가 아니라 무시(정상).");
                return;
            }

            _current = null;

            FirstPersonController local = FindLocalPlayer(excluding: player);
            if (local != null)
            {
                _current = local;
                Debug.Log($"[PlayerReg] Unregister('{player.gameObject.name}') 후 실제 로컬 pawn " +
                          $"'{local.gameObject.name}'으로 복구했습니다.");
                return;
            }

            Debug.Log($"[PlayerReg] Unregister('{player.gameObject.name}') — Current를 비웠고 " +
                      "대체할 로컬 pawn이 없습니다(아직 스폰 전이거나 이탈).");
        }

        /// <summary>살아 있는 로컬 조종 pawn을 찾는다(없으면 null).</summary>
        private static FirstPersonController FindLocalPlayer(FirstPersonController excluding)
        {
            foreach (FirstPersonController candidate in
                     UnityEngine.Object.FindObjectsByType<FirstPersonController>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || candidate == excluding)
                    continue;

                if (candidate.IsLocallyControlled && candidate.isActiveAndEnabled)
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// 도메인 리로드를 끈 상태에서 Play를 반복하면 static 상태가 남는다.
        /// 이전 판의 파괴된 플레이어를 물고 있지 않도록 진입 시 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            _current = null;
            _pending = null;
        }
    }
}
