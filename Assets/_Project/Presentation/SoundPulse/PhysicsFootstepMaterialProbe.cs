using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// §5.9 재질 판정의 Unity Physics 구현체. 발생 지점에서 <b>아래로 짧은 레이를 쏴</b>
    /// 밟고 있는 바닥 콜라이더의 태그를 읽는다.
    ///
    /// **왜 태그인가**: <see cref="PhysicsOcclusionProbe"/>가 이미 벽을 태그(`Wall`/`HardBlocker`)로
    /// 구분하고 있어, 같은 방식이 이 프로젝트에서 가장 자연스럽다. 새 컴포넌트를 바닥마다
    /// 붙이게 하면 그레이박스 정리 작업이 늘고, 레이어는 이미 차폐용으로 쓰여 의미가 겹친다.
    ///
    /// **MonoBehaviour가 아니다** — <c>PhysicsOcclusionProbe</c>와 같이 순수 어댑터이며,
    /// 등록·해제는 이미 프로브를 등록하고 있는 <c>LocalPulsePipelineBehaviour</c>가 맡는다.
    /// 새 씬 오브젝트도 새 수명주기도 만들지 않는다.
    ///
    /// **태그가 없거나 바닥을 못 찾으면 콘크리트(×1.0)** — 미배선 맵에서 발소리가 사라지거나
    /// 증폭되지 않아야 한다(<see cref="FootstepMaterialRules.Default"/>).
    /// </summary>
    public sealed class PhysicsFootstepMaterialProbe : IFootstepMaterialProbe
    {
        // §5.9 표의 지형 이름을 그대로 태그로 쓴다. TagManager에 없는 태그는
        // CompareTag가 예외 없이 false를 돌려주므로, 미설정 맵은 자동으로 기본값이 된다.
        public const string MetalGratingTag = "FloorMetalGrating";
        public const string TileTag = "FloorTile";
        public const string ConcreteTag = "FloorConcrete";
        public const string WoodTag = "FloorWood";
        public const string MatTag = "FloorMat";
        public const string StageFloorTag = "FloorStage";
        public const string WaterTag = "FloorWater";

        /// <summary>발 아래를 찾기 시작할 높이(m). 캡슐 중심보다 위에서 시작해 발밑까지 훑는다.</summary>
        private const float RayStartHeight = 0.5f;

        /// <summary>레이 길이(m). 지면에 서 있는 캐릭터의 발밑을 넉넉히 덮되 아래층까지 뚫지 않는 값.</summary>
        private const float RayLength = 2f;

        public FootstepMaterial Sample(Vector3 worldPosition)
        {
            Vector3 origin = worldPosition + Vector3.up * RayStartHeight;

            // 발소리는 초당 1~3회 나는 저빈도 경로라 단일 Raycast로 충분하다
            // (차폐 재판정처럼 초당 수십 회 도는 경로가 아니다).
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, RayLength,
                    ~0, QueryTriggerInteraction.Ignore))
                return FootstepMaterialRules.Default;

            return Classify(hit.collider);
        }

        /// <summary>
        /// 콜라이더 태그 → §5.9 재질. Physics 호출(<see cref="Sample"/>)과 분리해 둔 것은
        /// <c>PhysicsOcclusionProbe</c>가 Probe/Classify를 나눈 이유와 같다 — 매핑 규칙 자체를
        /// Physics 없이 검사할 수 있게 하기 위함이다.
        ///
        /// <c>Component.tag</c> 게터는 호출마다 문자열을 할당하므로 <c>CompareTag</c>만 쓴다.
        /// </summary>
        public static FootstepMaterial Classify(Collider collider)
        {
            if (collider == null)
                return FootstepMaterialRules.Default;

            if (collider.CompareTag(MetalGratingTag)) return FootstepMaterial.MetalGrating;
            if (collider.CompareTag(TileTag)) return FootstepMaterial.Tile;
            if (collider.CompareTag(MatTag)) return FootstepMaterial.Mat;
            if (collider.CompareTag(WaterTag)) return FootstepMaterial.Water;
            if (collider.CompareTag(WoodTag)) return FootstepMaterial.Wood;
            if (collider.CompareTag(StageFloorTag)) return FootstepMaterial.StageFloor;
            if (collider.CompareTag(ConcreteTag)) return FootstepMaterial.Concrete;

            // 태그가 없는 그레이박스 바닥 — §5.9 콘크리트와 같은 ×1.0으로 본다.
            return FootstepMaterialRules.Default;
        }
    }
}
