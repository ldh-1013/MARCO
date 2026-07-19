using System.Collections.Generic;
using UnityEngine;
using Marco.Core.Sound;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// §5.6 차폐 판정의 첫 Unity Physics 구현체. T2 그레이박스에 배선된
    /// SoundBlocking 레이어(벽 49개, Wall/HardBlocker 태그)를 상대로 발생원→청취점
    /// 직선상의 차폐물을 세어 OcclusionResult로 돌려준다.
    ///
    /// - MonoBehaviour가 아니다 — LocalPulsePipeline 생성 시 주입되는 순수 어댑터.
    /// - GAP-3의 재판정 주기는 ActivePulseTracker가 관리하므로 여기서는
    ///   "한 시점의 차폐 여부"만 답한다(주기 로직 중복 구현 금지 — 스프린트 지시 §3 Step 1).
    /// - Physics 호출(Probe)과 순수 판정(Classify)을 분리해, 판정 규칙 자체는
    ///   Physics 없이 EditMode 테스트로 고정한다(스프린트 지시 §3 Step 5).
    /// </summary>
    public sealed class PhysicsOcclusionProbe : IOcclusionProbe
    {
        public const string WallTag = "Wall";
        public const string HardBlockerTag = "HardBlocker";
        public const string SoundBlockingLayerName = "SoundBlocking";

        /// <summary>그레이박스 기준 여유치. 45×35m 맵에서 한 직선이 벽 32개를 넘을 일은 없다.</summary>
        private const int MaxHits = 32;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaxHits];
        private readonly List<string> _hitTags = new List<string>(MaxHits);
        private readonly int _layerMask;

        public PhysicsOcclusionProbe()
        {
            _layerMask = LayerMask.GetMask(SoundBlockingLayerName);
            if (_layerMask == 0)
                Debug.LogWarning($"[PhysicsOcclusionProbe] '{SoundBlockingLayerName}' 레이어가 없습니다 — 차폐 판정이 항상 '벽 0개'로 나옵니다. TagManager 설정을 확인하세요.");
        }

        public OcclusionResult Probe(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= Mathf.Epsilon)
                return new OcclusionResult(false, 0);

            int hitCount = Physics.RaycastNonAlloc(
                new Ray(from, delta / distance), HitBuffer, distance, _layerMask, QueryTriggerInteraction.Ignore);

            _hitTags.Clear();
            for (int i = 0; i < hitCount; i++)
                _hitTags.Add(HitBuffer[i].collider.tag);

            return Classify(_hitTags);
        }

        /// <summary>
        /// §5.6 순수 판정부: 직선상에서 맞은 콜라이더 태그 목록 → 차폐 결과.
        /// HardBlocker가 하나라도 있으면 완전 차단, Wall은 개수만큼 감쇠(×0.5^n)에 쓰인다.
        /// 감쇠 계산 자체는 SoundPulseResolver의 책임 — 여기서는 세기만 한다.
        /// </summary>
        public static OcclusionResult Classify(IReadOnlyList<string> hitTags)
        {
            bool hasHardBlocker = false;
            int wallCount = 0;

            for (int i = 0; i < hitTags.Count; i++)
            {
                if (hitTags[i] == HardBlockerTag)
                    hasHardBlocker = true;
                else if (hitTags[i] == WallTag)
                    wallCount++;
            }

            return new OcclusionResult(hasHardBlocker, wallCount);
        }
    }
}
