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

        /// <summary>
        /// §5.6 [v0.4] 층간 바닥(2층↔지상) 전용 태그. <b>표의 "Wall 2장 상당"을 태그 하나로
        /// 표현할 수 없어서</b> 새로 만든 것이다 — Unity 태그는 콜라이더당 1개다.
        ///
        /// **왜 콜라이더를 2장 겹치지 않았나**: 그 방식이면 한 장이 실수로 지워졌을 때
        /// 차폐가 조용히 ×0.25에서 ×0.5로 바뀐다. §10.2-1 C↔D 쌍은 여유가 0.32m뿐이라
        /// 벽 1장 차이로 배치 검증이 즉시 불합격하는데, 그것이 <b>런타임에 무경고로</b>
        /// 발생하는 것이 이 프로젝트에서 가장 피해야 할 실패 모드다(MaxHits 절단과 같은 성격).
        ///
        /// **계단에는 붙이지 않는다** — §5.6 "계단은 개구부라 미적용".
        /// </summary>
        public const string FloorSlabTag = "FloorSlab";

        /// <summary>§5.6 "일반 벽/문/락커 — 통과 시 반경 ×0.5". 수면도 같은 `Wall` 태그를 쓴다.</summary>
        public const int WallWeight = 1;

        /// <summary>§5.6 [v0.4] "층간 바닥 — Wall 2장 상당, 통과 시 반경 ×0.25".</summary>
        public const int FloorSlabWeight = 2;

        /// <summary>
        /// 레이캐스트 히트 버퍼 상한. 맵 v2는 구역 13개 + 2층 + 수면 콜라이더라
        /// 한 직선이 32개를 넘을 수 있고, <c>RaycastNonAlloc</c>은 버퍼가 차면
        /// <b>에러 없이 잘라서</b> 돌려준다 — 그 순간 벽 수가 실제보다 적게 세어져
        /// 차폐가 조용히 약해진다. 넉넉히 128로 두고, 그래도 차면 경고를 남긴다.
        /// </summary>
        private const int MaxHits = 128;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaxHits];

        /// <summary>
        /// 절단 경고를 이미 냈는가. 차폐 재판정은 초당 수십 회 도는 경로라(§5.6 성능 고려)
        /// 매번 로그를 남기면 콘솔이 잠기고 프레임이 떨어진다. <b>최초 1회만</b> 남긴다 —
        /// 절단은 맵 구조 문제이므로 한 번 알면 충분하고, 발생 여부 자체가 정보다.
        /// </summary>
        private static bool _truncationWarned;

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 위 플래그가 true로 남아,
        /// 두 번째 Play부터는 절단이 일어나도 <b>경고가 나오지 않는다</b>.
        /// 레지스트리들과 같은 방식으로 진입 시 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            _truncationWarned = false;
        }

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

            // 버퍼가 꽉 찼다 = 잘렸을 수 있다. RaycastNonAlloc은 잘렸는지 알려주지 않으므로
            // "정확히 상한과 같다"가 우리가 가진 유일한 신호다. 여기서 놓치면 벽 수가
            // 실제보다 적게 세어져 차폐가 조용히 약해진다(그래서 경고가 필요하다).
            if (hitCount >= MaxHits && !_truncationWarned)
            {
                _truncationWarned = true;
                Debug.LogWarning(
                    $"[PhysicsOcclusionProbe] 레이캐스트 히트가 상한 {MaxHits}에 도달했습니다 — " +
                    "차폐물이 잘려 벽 수가 실제보다 적게 세어질 수 있습니다(§5.6 감쇠가 약해짐). " +
                    $"구간 {from} → {to}, 거리 {distance:0.0}m. MaxHits를 올리거나 " +
                    $"'{SoundBlockingLayerName}' 레이어의 불필요한 콜라이더를 줄이세요. (이 경고는 1회만 출력됩니다)");
            }

            // Component.tag 게터는 호출마다 문자열을 새로 할당한다(Unity의 알려진 GC 원인).
            // 재판정이 초당 수십 회 도는 경로라 CompareTag로 태그 비교를 무할당 처리한다.
            var accumulator = default(OcclusionAccumulator);
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = HitBuffer[i].collider;
                accumulator.Add(
                    isHardBlocker: collider.CompareTag(HardBlockerTag),
                    wallWeight: WeightOf(collider));
            }

            return accumulator.ToResult();
        }

        /// <summary>
        /// §5.6 판정 규칙: HardBlocker가 하나라도 있으면 완전 차단, 그 외 차폐물은
        /// <b>가중치의 합</b>이 감쇠(×0.5^n)에 쓰인다. 감쇠 계산 자체는 SoundPulseResolver의
        /// 책임 — 여기서는 세기만 한다. Probe(무할당 경로)와 Classify(테스트 경로)가
        /// 같은 규칙을 쓰도록 이 구조체를 공유한다.
        ///
        /// **"개수"가 아니라 "가중치 합"이다** — 층간 바닥 1장이 Wall 2장으로 세어져야
        /// §5.6의 ×0.25가 나온다(v0.4).
        /// </summary>
        private struct OcclusionAccumulator
        {
            private bool _hasHardBlocker;
            private int _wallCount;

            public void Add(bool isHardBlocker, int wallWeight)
            {
                if (isHardBlocker)
                    _hasHardBlocker = true;
                else
                    _wallCount += wallWeight;
            }

            public OcclusionResult ToResult() => new OcclusionResult(_hasHardBlocker, _wallCount);
        }

        /// <summary>
        /// §5.6 차폐 태그 → Wall 환산 가중치. 차폐물이 아니면 0.
        /// <c>Component.tag</c> 문자열 할당을 피하려 <c>CompareTag</c>만 쓴다.
        /// </summary>
        private static int WeightOf(Collider collider)
        {
            if (collider.CompareTag(WallTag)) return WallWeight;
            if (collider.CompareTag(FloorSlabTag)) return FloorSlabWeight;

            // SoundBlocking 레이어에 있으면서 차폐 태그가 없는 콜라이더 — 감쇠에 세지 않는다.
            // (태그 미부착 벽이 조용히 무력화되는 문제는 맵 생성 시점 부착으로 막는다 — 블록 1-B)
            return 0;
        }

        /// <summary>태그 문자열로 같은 가중치 규칙을 적용한다(<see cref="Classify"/> 전용).</summary>
        private static int WeightOf(string tag)
        {
            if (tag == WallTag) return WallWeight;
            if (tag == FloorSlabTag) return FloorSlabWeight;
            return 0;
        }

        /// <summary>
        /// 태그 목록으로 같은 규칙을 적용하는 테스트용 진입점.
        /// 런타임 경로(<see cref="Probe"/>)는 문자열 할당을 피하려 CompareTag를 쓰지만,
        /// 판정 규칙 자체는 <see cref="OcclusionAccumulator"/>로 공유한다.
        /// </summary>
        public static OcclusionResult Classify(IReadOnlyList<string> hitTags)
        {
            var accumulator = default(OcclusionAccumulator);
            for (int i = 0; i < hitTags.Count; i++)
                accumulator.Add(hitTags[i] == HardBlockerTag, WeightOf(hitTags[i]));

            return accumulator.ToResult();
        }
    }
}
