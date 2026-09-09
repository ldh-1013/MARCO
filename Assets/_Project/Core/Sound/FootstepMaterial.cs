namespace Marco.Core.Sound
{
    /// <summary>
    /// §5.9 재질별 발소리 배율의 지형 종류. 표에 있는 행을 그대로 옮겼으며 임의로 만든 값이 없다.
    /// </summary>
    public enum FootstepMaterial
    {
        /// <summary>§5.9 콘크리트 ×1.0 — 기계실·약품창고·물탱크실. **판정 실패 시의 기본값**이기도 하다.</summary>
        Concrete = 0,

        /// <summary>§5.9 목재 ×1.0 — 사우나.</summary>
        Wood,

        /// <summary>§5.9 금속 그레이팅 ×1.5 — 직원 통로·계단.</summary>
        MetalGrating,

        /// <summary>§5.9 타일 ×1.3 — 샤워장·풀사이드·유아풀 존·세탁실.</summary>
        Tile,

        /// <summary>§5.9 매트/카펫 ×0.7 — 라커룸·로비·관람석·라이프가드실.</summary>
        Mat,

        /// <summary>
        /// §5.9 나무 마루(폐극장 지하) ×1.2 — §11 폐극장 고유 지형.
        ///
        /// **표의 "상시 강제 발생(은신 불가)"는 아직 구현되지 않았다.** §11 맵 자체가 없어서
        /// 재현할 대상이 없고, "정지 중에도 소리가 난다"는 §5.1-1의 이동거리 규칙을 정면으로
        /// 뒤집는 예외라 별도 작업이 맞다. 여기서는 **배율만** 옮겼다.
        /// </summary>
        StageFloor,

        /// <summary>§5.9 물(수면 아래) — **파문이 발생하지 않는다.** 대신 숨 게이지를 소모한다(§5.9-1).</summary>
        Water
    }

    /// <summary>
    /// §5.9 재질 배율의 순수 규칙. UnityEngine.Physics를 모르므로 EditMode로 고정된다 —
    /// 실제 바닥을 찾아내는 일은 Presentation의 프로브가 맡는다
    /// (<c>PhysicsOcclusionProbe</c>가 Physics를, <c>SoundPulseResolver</c>가 규칙을 나눠 갖는 것과 같은 구조).
    ///
    /// **적용 대상은 발생 반경뿐이다(§5.1-1).**
    ///
    /// <code>
    /// | 대상                  | 재질 배율        |
    /// | 발생 간격(2m / 6m)    | 미적용 — 항상 고정 |
    /// | 발생 반경(들키는 범위) | 적용             |
    /// </code>
    ///
    /// 간격이 재질마다 흔들리면 파문 점멸 리듬이 구역마다 달라져 혼란스럽고,
    /// 재질 효과가 소음량에서 상쇄되어 사라지기 때문이다.
    /// </summary>
    public static class FootstepMaterialRules
    {
        /// <summary>§5.9 금속 그레이팅.</summary>
        public const float MetalGratingMultiplier = 1.5f;

        /// <summary>§5.9 타일.</summary>
        public const float TileMultiplier = 1.3f;

        /// <summary>§5.9 나무 마루(폐극장 지하, §11).</summary>
        public const float StageFloorMultiplier = 1.2f;

        /// <summary>§5.9 콘크리트·목재. 기준값이다.</summary>
        public const float NeutralMultiplier = 1f;

        /// <summary>§5.9 매트/카펫.</summary>
        public const float MatMultiplier = 0.7f;

        /// <summary>
        /// 재질이 판정되지 않았을 때 쓰는 값. §5.9 콘크리트(×1.0)와 같다 —
        /// **모르는 바닥이 플레이어에게 유리해서도 불리해서도 안 된다.**
        /// </summary>
        public const FootstepMaterial Default = FootstepMaterial.Concrete;

        /// <summary>
        /// §5.9 발생 반경에 곱할 배율. 물은 0 — 곱셈 결과가 반경 0이 되어
        /// "파문 발생 안 함"이 자연스럽게 성립한다. 다만 호출자는 곱하기 전에
        /// <see cref="EmitsPulse"/>로 먼저 걸러내는 편이 의도가 분명하다.
        /// </summary>
        public static float RadiusMultiplier(FootstepMaterial material)
        {
            switch (material)
            {
                case FootstepMaterial.MetalGrating: return MetalGratingMultiplier;
                case FootstepMaterial.Tile: return TileMultiplier;
                case FootstepMaterial.StageFloor: return StageFloorMultiplier;
                case FootstepMaterial.Mat: return MatMultiplier;
                case FootstepMaterial.Water: return 0f;
                default: return NeutralMultiplier; // 콘크리트·목재
            }
        }

        /// <summary>§5.9 "물(수면 아래) — 파문 발생 안 함".</summary>
        public static bool EmitsPulse(FootstepMaterial material)
        {
            return material != FootstepMaterial.Water;
        }

        /// <summary>
        /// §5.9를 적용한 최종 발생 반경. 음수 반경이 들어와도 0 아래로 내려가지 않는다.
        /// </summary>
        public static float ApplyToRadius(float baseRadius, FootstepMaterial material)
        {
            float radius = baseRadius * RadiusMultiplier(material);
            return radius > 0f ? radius : 0f;
        }

        /// <summary>
        /// 이 소리 종류가 §5.9 재질 배율의 대상인가.
        ///
        /// **발소리(걷기·질주)만 대상이다.** §5.9의 제목이 "재질별 <b>발소리</b> 배율"이고,
        /// 음성·밸브·노크는 바닥을 딛는 소리가 아니다 — 카펫 위에서 고함쳤다고 22m가
        /// 15.4m로 줄면 §5.1 등급 체계 자체가 무너진다.
        /// </summary>
        public static bool AppliesTo(SoundType type)
        {
            return type == SoundType.Walk || type == SoundType.Sprint;
        }
    }
}
