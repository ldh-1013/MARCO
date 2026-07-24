using System;
using System.Collections.Generic;
using UnityEngine;

namespace Marco.Core.Net
{
    /// <summary>
    /// 씬에 존재하는 태그 대상(<see cref="ITagTarget"/>)들의 중앙 등록소(스프린트 11).
    ///
    /// **왜 필요한가**: 술래의 <c>TagDetector</c>(Presentation)가 로컬 대역(<c>TaggableRunner</c>,
    /// Presentation)과 네트워크 플레이어(<c>TagNetworkSync</c>, Net)를 한 목록으로 순회해야
    /// 하는데, 어셈블리 경계상 서로의 구체 타입을 <c>FindObjectsByType&lt;T&gt;()</c>로 찾을 수
    /// 없다. 각 대상이 스스로 여기 등록하면, 소비자는 Core 인터페이스 목록만 본다 —
    /// <c>LocalPlayerRegistry</c>(스프린트 8)와 같은 지연 바인딩 패턴을 Core에 둔 것이다.
    ///
    /// **태그 확정 통지**: 대상이 실제로 태그되면(로컬 즉시 / 서버 SyncVar 반영) <see cref="NotifyTagged"/>로
    /// 알린다. <see cref="TargetTagged"/> 구독자(예: <c>RoundCoordinator</c>)가 전원 태그 판정에
    /// 반영한다. SyncVar 변경은 모든 피어에서 발생하므로, 각 피어의 라운드 집계가 <b>서버가
    /// 확정한 같은 태그 집합</b>을 보게 된다.
    /// </summary>
    public static class TagTargetRegistry
    {
        private static readonly List<ITagTarget> _targets = new List<ITagTarget>();

        /// <summary>현재 등록된 태그 대상들(읽기 전용).</summary>
        public static IReadOnlyList<ITagTarget> Targets => _targets;

        /// <summary>대상이 태그 확정됐을 때 발생. 인자는 확정된 대상.</summary>
        public static event Action<ITagTarget> TargetTagged;

        public static void Register(ITagTarget target)
        {
            if (target == null || _targets.Contains(target))
                return;
            _targets.Add(target);
        }

        public static void Unregister(ITagTarget target)
        {
            if (target != null)
                _targets.Remove(target);
        }

        /// <summary>대상이 태그 확정됐음을 알린다(로컬 대역/네트워크 공통 진입점).</summary>
        public static void NotifyTagged(ITagTarget target)
        {
            if (target != null)
                TargetTagged?.Invoke(target);
        }

        /// <summary>
        /// 도메인 리로드를 끈 상태로 Play를 반복하면 static 상태가 남는다.
        /// 이전 판의 파괴된 대상·구독자를 물지 않도록 진입 시 초기화한다
        /// (<c>LocalPlayerRegistry</c>와 동일한 안전장치).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            _targets.Clear();
            TargetTagged = null;
        }
    }
}
