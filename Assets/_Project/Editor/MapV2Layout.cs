using System.Collections.Generic;
using Marco.Core.Sound;
using UnityEngine;

namespace Marco.EditorTools
{
    /// <summary>
    /// 맵 v2의 **모든 좌표를 한 곳에** 모은 데이터. §10.1 / §10.2 / §10.5 / §6.5-2에서
    /// 그대로 옮긴 값이며, <c>docs/tools/valve_layout_check.py</c>의
    /// <c>ROOMS·DOORS·VALVES·WATER·DRAINS</c>와 <b>같은 숫자</b>다.
    ///
    /// <para>
    /// <b>왜 데이터를 생성기에서 분리했나.</b> 같은 좌표를 생성기(1-B)와 배치 검증기(1-C)가
    /// 둘 다 알아야 한다. 각자 들고 있으면 한쪽만 고쳤을 때 <b>검증이 통과하는데 씬은 틀린</b>
    /// 상태가 된다 — 이 프로젝트에서 가장 비싼 실패 유형이다.
    /// </para>
    ///
    /// <para>
    /// <b>기획서에 숫자가 없는 것</b>(벽 높이·두께·계단 위치·찰칵이 리스폰 지점)은
    /// 프로토타입 잠정값이며 각 필드 주석에 GAP 번호를 적었다. 확정값처럼 쓰지 말 것.
    /// </para>
    /// </summary>
    public static class MapV2Layout
    {
        // ── 맵 전체 (§10.1) ──────────────────────────────────────────────

        /// <summary>§10.1 "크기 52m × 40m (L자형)".</summary>
        public const float MapWidth = 52f;
        public const float MapDepth = 40f;

        /// <summary>§10.1 "L자 컷: x ≥ 40 AND y ≥ 28 영역(12×12m)은 맵 바깥이다".</summary>
        public const float CutX = 40f;
        public const float CutY = 28f;

        /// <summary>§10.1 "2층은 Y +3.5m". 그래서 지상층 벽 높이도 3.5m다(유도값, 창작 아님).</summary>
        public const float UpperFloorY = 3.5f;

        /// <summary>벽 높이 = 층 간격. 따로 적지 않고 <see cref="UpperFloorY"/>에서 유도한다.</summary>
        public static float WallHeight => UpperFloorY;

        /// <summary>그레이박스 벽 두께. 기획서에 수치가 없다 — 잠정값(GAP-77).</summary>
        public const float WallThickness = 0.2f;

        /// <summary>바닥판 두께. 렌더·충돌용이며 판정에 영향이 없다.</summary>
        public const float FloorThickness = 0.2f;

        /// <summary>
        /// 구역 바닥과 기본(복도) 바닥의 높이차. <b>0이면 안 된다</b> —
        /// 두 바닥이 정확히 같은 높이면 §5.9 재질 하향 레이가 어느 쪽을 맞출지 정해지지 않고,
        /// 복도의 콘크리트가 구역의 타일·카펫을 조용히 덮어쓴다.
        /// </summary>
        public const float BaseFloorDrop = 0.01f;

        // ── 구역 (§10.1 구역 구성표) ─────────────────────────────────────

        public readonly struct Zone
        {
            public readonly string Name;
            public readonly Rect Area;                 // x, y = 남서 귀퉁이 / width, height
            public readonly FootstepMaterial Material; // §5.9
            public readonly bool IsUpperFloor;

            public Zone(string name, float x0, float y0, float x1, float y1,
                FootstepMaterial material, bool upper = false)
            {
                Name = name;
                Area = new Rect(x0, y0, x1 - x0, y1 - y0);
                Material = material;
                IsUpperFloor = upper;
            }
        }

        /// <summary>§10.1 구역 13개 — 지상 11 + 상부 관람층 2.</summary>
        public static readonly Zone[] Zones =
        {
            // 지상층
            new Zone("로비",        2f, 32f, 12f, 40f, FootstepMaterial.Mat),          // 카펫 ×0.7
            new Zone("약품창고",   16f, 33f, 22f, 38f, FootstepMaterial.Concrete),     // ×1.0
            new Zone("세탁실",     26f, 33f, 33f, 38f, FootstepMaterial.Tile),         // ×1.3
            new Zone("라커룸",      2f, 24f, 14f, 31f, FootstepMaterial.Mat),          // 매트 ×0.7
            new Zone("메인풀홀",   18f, 15f, 38f, 27f, FootstepMaterial.Tile),         // ×1.3
            new Zone("라이프가드",  2f, 17f,  7f, 21f, FootstepMaterial.Mat),          // 카펫 ×0.7
            new Zone("유아풀존",    2f,  5f, 14f, 14f, FootstepMaterial.Tile),         // ×1.3
            new Zone("사우나",     18f,  9f, 22f, 13f, FootstepMaterial.Wood),         // 목재 ×1.0
            new Zone("샤워장",     26f,  7f, 34f, 12f, FootstepMaterial.Tile),         // ×1.3
            new Zone("기계실",     41f,  5f, 49f, 12f, FootstepMaterial.Concrete),     // ×1.0
            new Zone("직원통로",   14f,  1f, 48f, 3.5f, FootstepMaterial.MetalGrating),// ×1.5

            // 상부 관람층 (Y +3.5m)
            new Zone("관람석",     18f, 15f, 36f, 21f, FootstepMaterial.Mat, upper: true),      // 카펫 ×0.7
            new Zone("물탱크실",    4f, 24f, 10f, 29f, FootstepMaterial.Concrete, upper: true), // ×1.0
        };

        // ── 문 (§10.1 개구부 + valve_layout_check.py DOORS) ──────────────

        public readonly struct Door
        {
            public readonly string Zone;
            public readonly Vector2 Center; // (x, y) — 구역 경계선 위의 점
            public readonly float Width;

            public Door(string zone, float x, float y, float width = 2f)
            {
                Zone = zone;
                Center = new Vector2(x, y);
                Width = width;
            }
        }

        /// <summary>
        /// §10.1 "문 — 폭 2m. 이 위치가 배치 검증 합격의 조건이다".
        /// 지상층 18개는 <c>valve_layout_check.py</c>의 <c>DOORS</c>와 순서까지 같다.
        ///
        /// <para>
        /// <b>직원통로 (14, 3.5)는 구역의 북서 <i>모서리</i>다.</b> 서쪽 변(x=14)과 북쪽
        /// 변(y=3.5)에 동시에 걸려 양쪽에 개구부가 난다 — 버그가 아니라 "통로 서쪽 끝이
        /// 열려 있다"는 뜻이고, 파이썬 판도 문마다 십자로 셀을 뚫어 같은 결과를 낸다.
        /// </para>
        ///
        /// <para>
        /// <b>2층 문 2개는 기획서에 없다 — 잠정값(GAP-80).</b> §10.1은 상부 관람층 두 구역의
        /// 좌표만 주고 개구부를 적지 않았는데, 문이 없으면 두 방이 <b>사방이 막힌 상자</b>가
        /// 되어 밸브 C에 걸어서 갈 수 없다. 이 두 문은 지상층 보행 격자(허리 높이 판정)에
        /// 걸리지 않으므로 <b>10쌍 경로 거리를 바꾸지 않는다.</b>
        /// </para>
        ///
        /// <para>
        /// <b>그러나 조건 ②(동시 감시)는 바꾼다.</b> 2층 문은 밸브 C의 회전음이 층간 바닥
        /// ×0.25를 <b>우회해</b> 새 나가는 구멍이라, 위치에 따라 §10.2-1 ②가 깨진다.
        /// 후보를 전수 판정한 결과:
        /// <list type="bullet">
        /// <item>동벽 (10,26) → <b>C↔D 동시 감시 9지점 — 불합격.</b> 소리가 문을 지나
        ///       차폐판 바깥(x&gt;10)의 빈 공간으로 내려와 약품창고 남쪽에서 D와 겹친다</item>
        /// <item>남벽 (7,24) → <b>C↔E 동시 감시 24지점 — 불합격</b></item>
        /// <item>북벽 (7,29) · 서벽 (4,26) · <b>동벽 (10,28)</b> → 전부 0지점, 합격</item>
        /// </list>
        /// 합격 후보 중 서 계단 (12,26)에 가장 가까운 <b>동벽 (10,28)</b>을 택했다.
        /// <b>이 문을 옮기면 배치 검증을 다시 돌려야 한다.</b>
        /// </para>
        /// </summary>
        public static readonly Door[] Doors =
        {
            new Door("로비", 12f, 36f), new Door("로비", 7f, 32f),
            new Door("약품창고", 16f, 35f), new Door("세탁실", 29f, 33f),
            new Door("라커룸", 14f, 27f), new Door("라커룸", 8f, 24f),
            new Door("메인풀홀", 18f, 20f), new Door("메인풀홀", 34f, 27f), new Door("메인풀홀", 38f, 24f),
            new Door("라이프가드", 7f, 19f),
            new Door("유아풀존", 14f, 10f), new Door("유아풀존", 8f, 14f),
            new Door("사우나", 20f, 13f), new Door("샤워장", 30f, 12f),
            new Door("기계실", 45f, 5f),   // ★ 남쪽 1개뿐 — §6.1 "퇴로 없음". 추가 금지
            new Door("직원통로", 14f, 3.5f), new Door("직원통로", 30f, 3.5f), new Door("직원통로", 44f, 3.5f),

            // 상부 관람층 — GAP-80 잠정값. 없으면 2층이 봉해진다.
            // ★ 이 좌표는 취향이 아니라 §10.2-1 ②가 고른 것이다(아래 주석 참조).
            new Door("물탱크실", 10f, 28f),  // 동벽 — 서 계단 상단 (10,28)과 같은 지점
            new Door("관람석", 36f, 18f),    // 동벽 — 동 계단 상단 (36.5,18) 정면
        };

        /// <summary>
        /// §10.1 "기계실–풀 홀 칸막이: x=40, y=13~20". 권장 요소(제거해도 10쌍 검증 통과).
        /// 여유 확보용이며, 이것이 A↔B 경로를 늘리는 장치와 함께 작동한다.
        /// </summary>
        public static readonly Rect Partition = new Rect(40f, 13f, 0f, 7f); // x=40, y 13~20

        // ── 수면 (§10.1 수면 영역표) ─────────────────────────────────────

        public readonly struct WaterArea
        {
            public readonly string Name;
            public readonly Rect Area;
            public readonly float Depth;
            public readonly Vector2 SumpCenter;
            public readonly float SumpRadius;
            public readonly float SumpDepth;

            public WaterArea(string name, float x0, float y0, float x1, float y1, float depth,
                Vector2 sumpCenter = default, float sumpRadius = 0f, float sumpDepth = 0f)
            {
                Name = name;
                Area = new Rect(x0, y0, x1 - x0, y1 - y0);
                Depth = depth;
                SumpCenter = sumpCenter;
                SumpRadius = sumpRadius;
                SumpDepth = sumpDepth;
            }
        }

        /// <summary>§6.5-2 침강부 반지름. 기획서에 수치가 없다 — 잠정값(GAP-77).</summary>
        public const float SumpRadius = 1.2f;

        /// <summary>
        /// §10.1 수면 영역. <b>줄이지 마라</b> — §6.5-3 배수구 육상 거리 3.0m가 즉시 깨진다.
        /// 유아풀은 여유가 0이다(수면 폭 6m의 절반이 정확히 3.0m).
        ///
        /// <para>
        /// 유아풀 본체 수심은 기획서가 "얕음"이라고만 적어 숫자가 없다 — <b>GAP-75</b>.
        /// 0.9m는 잠정값이며, <c>DiveRules.MinDivableDepth</c>(0.5m)보다 깊고
        /// <c>StandingHeadHeight</c>(1.62m)보다 얕아 "서서 잠수로만 잠기는" 구간에 든다.
        /// 그 구간이라야 §6.1 밸브 E(진입 0.5초)가 성립한다.
        /// </para>
        /// </summary>
        public static readonly WaterArea[] Waters =
        {
            new WaterArea("메인풀", 21f, 17f, 35f, 25f, depth: 3.5f),
            new WaterArea("유아풀", 3.5f, 6.5f, 12.5f, 12.5f, depth: 0.9f,
                sumpCenter: new Vector2(8f, 9.5f), sumpRadius: SumpRadius, sumpDepth: 3.5f),
        };

        // ── 밸브 (§10.2) ─────────────────────────────────────────────────

        public readonly struct ValvePoint
        {
            public readonly string Id;
            public readonly string Zone;
            public readonly Vector2 Position;
            public readonly bool Underwater;
            public readonly bool UpperFloor;

            public ValvePoint(string id, string zone, float x, float y,
                bool underwater = false, bool upperFloor = false)
            {
                Id = id;
                Zone = zone;
                Position = new Vector2(x, y);
                Underwater = underwater;
                UpperFloor = upperFloor;
            }
        }

        /// <summary>§10.2 밸브 5개. 회전시간 배분은 블록 2가 소유한다 — 여기는 좌표만.</summary>
        public static readonly ValvePoint[] Valves =
        {
            new ValvePoint("A", "기계실",   46f,  8f),
            new ValvePoint("B", "메인풀홀", 34f, 22f, underwater: true),
            new ValvePoint("C", "물탱크실",  7f, 26f, upperFloor: true),
            new ValvePoint("D", "약품창고", 19f, 36f),
            new ValvePoint("E", "유아풀존",  5f,  9f, underwater: true),
        };

        // ── 배수구 (§6.5-2 / §10.5) ──────────────────────────────────────

        public readonly struct DrainPoint
        {
            public readonly string Id;
            public readonly string Water;
            public readonly Vector2 Position;
            public readonly string NearValve;

            public DrainPoint(string id, string water, float x, float y, string nearValve)
            {
                Id = id;
                Water = water;
                Position = new Vector2(x, y);
                NearValve = nearValve;
            }
        }

        /// <summary>§6.5-2 배수구 2개소. 둘 다 수심 3.5m — 배수구 2는 국소 침강부다.</summary>
        public static readonly DrainPoint[] Drains =
        {
            new DrainPoint("1", "메인풀", 31f, 21f, "B"),
            new DrainPoint("2", "유아풀",  8f, 9.5f, "E"),
        };

        // ── 출구 (§10.5) ─────────────────────────────────────────────────

        public static readonly (string Name, Vector2 Position)[] Exits =
        {
            ("출구1_배수로", new Vector2(46f, 2f)),  // 직원 통로 동쪽 끝. 금속 ×1.5 경유
            ("출구2_정문",   new Vector2(7f, 40f)),  // 로비 북쪽. 카펫 ×0.7 경유
        };

        // ── 계단 (§10.1 "계단 서·동 2개" / 블록 1-D) ─────────────────────

        /// <summary>
        /// 계단 하나. §5.6상 <b>개구부</b>이므로 차폐물이 아니고, 층간 차폐판에서 발자국만큼
        /// 구멍을 낸다. 경사·길이는 좌표에서 <b>유도</b>하며 따로 적지 않는다.
        /// </summary>
        public readonly struct Stair
        {
            public readonly string Name;

            /// <summary>평면 점유(월드 XZ). 층간 차폐판에서 이만큼 뺀다.</summary>
            public readonly Rect Footprint;

            /// <summary>경사로 하단 중심 — 지상 높이(Y = 0).</summary>
            public readonly Vector2 Bottom;

            /// <summary>경사로 상단 중심 — 2층 높이(Y = <see cref="UpperFloorY"/>). 2층 문 앞이다.</summary>
            public readonly Vector2 Top;

            /// <summary>
            /// 지상에서 경사로에 <b>올라서는</b> 지점. 경사로 하단과 다를 수 있다 —
            /// 서 계단 하단(x=14)은 라커룸 동벽의 몸통 반경 안이라(문 (14,27)은 y 26~28만
            /// 열려 있고 y 28~31은 벽) 방 안쪽으로 물려야 실제로 밟을 수 있다.
            /// </summary>
            public readonly Vector2 BottomAttach;

            /// <summary>2층에서 경사로 상단으로 <b>내려서는</b> 지점(방 안쪽).</summary>
            public readonly Vector2 UpperAttach;

            public Stair(string name, Rect footprint, Vector2 bottom, Vector2 top,
                Vector2 bottomAttach, Vector2 upperAttach)
            {
                Name = name;
                Footprint = footprint;
                Bottom = bottom;
                Top = top;
                BottomAttach = bottomAttach;
                UpperAttach = upperAttach;
            }

            /// <summary>수평 주행 거리(m).</summary>
            public float Run => Vector2.Distance(Bottom, Top);

            /// <summary>경사면 실제 길이(m). 유도값이다.</summary>
            public float SlopeLength => Mathf.Sqrt(Run * Run + UpperFloorY * UpperFloorY);

            /// <summary>
            /// 경사각(°). <c>CharacterController.slopeLimit</c>(프리팹 45°)보다 작아야
            /// 등반 가능하다. <b>넘으면 2층이 통째로 접근 불가가 된다.</b>
            /// </summary>
            public float SlopeDegrees => Mathf.Atan2(UpperFloorY, Run) * Mathf.Rad2Deg;

            /// <summary>
            /// 배치 검증 그래프에서 이 계단 간선의 비용(m) — 경사면 + 양쪽 접속 보정.
            /// §10.2절 경로 거리 표에 그대로 들어간다(기록용 — 판정은 동시 감시 단독이다).
            /// </summary>
            public float GraphCost => SlopeLength
                + Vector2.Distance(Top, UpperAttach)
                + Vector2.Distance(Bottom, BottomAttach);
        }

        /// <summary>
        /// §10.1은 "계단 서·동 2개"라고만 적고 <b>좌표가 없다</b> — 잠정값(GAP-78).
        /// 서쪽은 물탱크실(4,24)~(10,29) 동쪽 복도, 동쪽은 관람석(…,36)과 풀 홀 동벽(38) 사이.
        ///
        /// <para>
        /// <b>§5.6 "계단은 개구부라 미적용"</b> — 이 지점에서는 층간 바닥 차폐판에 구멍을
        /// 낸다. 구멍을 안 내면 계단으로 새는 소리가 ×0.25로 깎여 2층 청취 우위(§10.1
        /// 관람석 "풀 홀 청취 우위")가 사라진다. 현재 두 좌표는 차폐판 바깥에 있어
        /// 구멍이 실제로 뚫리지는 않는다 — 결과는 같고(계단에서 소리가 안 깎인다),
        /// 구멍 로직은 계단을 옮겼을 때를 위한 보험이다.
        /// </para>
        ///
        /// <para>
        /// <b>[블록 1-D] 좌표 확정.</b> 둘 다 직선 경사로이며 경사각이 프리팹
        /// <c>slopeLimit</c> 45°보다 작다(서 41.19° / 동 37.87°).
        /// 동 계단은 메인 풀 수면(x ≤ 35)의 <b>동쪽 덱</b>에 있어 물 위를 건너지 않는다.
        /// </para>
        ///
        /// <para>
        /// <b>★ 이 좌표는 §10.2절 경로 거리를 크게 바꾼다.</b> 계단이 없을 때 배치 검증은
        /// 계단을 12m 페널티로 <i>해석적으로</i> 근사했는데, 실제 계단은 편도 6.3m라
        /// C↔D 경로가 30.5m에서 <b>21.8m로 줄었다</b>. 경로는 판정 대상이 아니므로
        /// (§10.2-1 구 조건① 폐지) 합격에는 영향이 없지만, 그 짧은 경로가 플레이에서
        /// 문제를 만드는지는 <b>§20.3 관찰 항목</b>으로 등록돼 있다 —
        /// 유효하다고 판단되면 서 계단을 스위치백(간선 14.5m)으로 되돌린다.
        /// 계단을 옮기거나 길이를 바꾸면 반드시 배치 검증을 다시 돌릴 것.
        /// </para>
        /// </summary>
        public static readonly Stair[] Stairs =
        {
            new Stair("계단_서", Rect.MinMaxRect(10f, 27f, 14f, 29f),
                bottom: new Vector2(14f, 28f), top: new Vector2(10f, 28f),
                bottomAttach: new Vector2(13.5f, 28f), upperAttach: new Vector2(9.5f, 28f)),

            new Stair("계단_동", Rect.MinMaxRect(35.5f, 18f, 37.5f, 22.5f),
                bottom: new Vector2(36.5f, 22.5f), top: new Vector2(36.5f, 18f),
                bottomAttach: new Vector2(36.5f, 22.5f), upperAttach: new Vector2(35f, 18f)),
        };

        // ── 찰칵이 리스폰 (§7) ───────────────────────────────────────────

        /// <summary>
        /// §7 "맵당 4곳, 120초". <b>좌표는 기획서에 없다</b> — 잠정값(GAP-79).
        /// 사분면에 하나씩, 물 밖·밸브에서 떨어진 구역 중앙으로 잡았다.
        /// 블록 6이 실제 아이템 로직을 붙인다.
        /// </summary>
        public static readonly (string Name, Vector2 Position, bool Upper)[] ClickerSpawns =
        {
            ("찰칵이_라커룸", new Vector2(8f, 27.5f), false),
            ("찰칵이_세탁실", new Vector2(29.5f, 35.5f), false),
            ("찰칵이_샤워장", new Vector2(30f, 9.5f), false),
            ("찰칵이_관람석", new Vector2(27f, 18f), true),
        };

        // ── 순수 헬퍼 (생성기·검증기가 공유) ─────────────────────────────

        /// <summary>이 (x, y)가 §10.1 L자 컷으로 잘려나간 맵 바깥인가.</summary>
        public static bool IsOutsideMap(float x, float y)
        {
            return x >= CutX && y >= CutY;
        }

        /// <summary>구역의 §5.9 재질 태그 이름. <c>PhysicsFootstepMaterialProbe</c>의 상수를 쓴다.</summary>
        public static string MaterialTag(FootstepMaterial material)
        {
            switch (material)
            {
                case FootstepMaterial.MetalGrating: return Presentation.Sound.PhysicsFootstepMaterialProbe.MetalGratingTag;
                case FootstepMaterial.Tile: return Presentation.Sound.PhysicsFootstepMaterialProbe.TileTag;
                case FootstepMaterial.Mat: return Presentation.Sound.PhysicsFootstepMaterialProbe.MatTag;
                case FootstepMaterial.Wood: return Presentation.Sound.PhysicsFootstepMaterialProbe.WoodTag;
                case FootstepMaterial.StageFloor: return Presentation.Sound.PhysicsFootstepMaterialProbe.StageFloorTag;
                case FootstepMaterial.Water: return Presentation.Sound.PhysicsFootstepMaterialProbe.WaterTag;
                default: return Presentation.Sound.PhysicsFootstepMaterialProbe.ConcreteTag;
            }
        }

        /// <summary>벽 한 변을 문 개구부로 끊어 만든 선분 하나(월드 XZ).</summary>
        public readonly struct WallSegment
        {
            public readonly Vector2 From;
            public readonly Vector2 To;

            public WallSegment(Vector2 from, Vector2 to)
            {
                From = from;
                To = to;
            }

            public float Length => Vector2.Distance(From, To);
        }

        /// <summary>
        /// 구역 외곽 네 변을 문 위치에서 끊어 벽 선분 목록으로 만든다.
        ///
        /// <para>
        /// 문은 "경계선 위의 점 + 폭"으로만 주어져 있어, 어느 변에 속하는지는
        /// 좌표를 보고 판정한다 — 그래서 문 목록에 변 이름을 적어 둘 필요가 없고,
        /// 구역을 옮겨도 문 데이터를 다시 분류하지 않아도 된다.
        /// </para>
        /// </summary>
        public static List<WallSegment> WallSegmentsOf(in Zone zone, Door[] doors)
        {
            var result = new List<WallSegment>();
            float x0 = zone.Area.xMin, x1 = zone.Area.xMax;
            float y0 = zone.Area.yMin, y1 = zone.Area.yMax;

            // 남·북 변은 x축으로, 서·동 변은 y축으로 뻗는다.
            AddEdge(result, doors, zone.Name, horizontal: true, fixedCoord: y0, from: x0, to: x1);
            AddEdge(result, doors, zone.Name, horizontal: true, fixedCoord: y1, from: x0, to: x1);
            AddEdge(result, doors, zone.Name, horizontal: false, fixedCoord: x0, from: y0, to: y1);
            AddEdge(result, doors, zone.Name, horizontal: false, fixedCoord: x1, from: y0, to: y1);

            return result;
        }

        private static void AddEdge(List<WallSegment> into, Door[] doors, string zoneName,
            bool horizontal, float fixedCoord, float from, float to)
        {
            // 이 변에 놓인 문들의 개구부 구간을 모은다.
            var gaps = new List<(float Start, float End)>();
            for (int i = 0; i < doors.Length; i++)
            {
                Door d = doors[i];
                if (d.Zone != zoneName)
                    continue;

                float onEdge = horizontal ? d.Center.y : d.Center.x;
                float along = horizontal ? d.Center.x : d.Center.y;
                if (!Mathf.Approximately(onEdge, fixedCoord))
                    continue;
                if (along < from || along > to)
                    continue;

                gaps.Add((along - d.Width * 0.5f, along + d.Width * 0.5f));
            }

            gaps.Sort((a, b) => a.Start.CompareTo(b.Start));

            float cursor = from;
            for (int i = 0; i < gaps.Count; i++)
            {
                if (gaps[i].Start > cursor)
                    into.Add(Make(horizontal, fixedCoord, cursor, gaps[i].Start));

                cursor = Mathf.Max(cursor, gaps[i].End);
            }

            if (cursor < to)
                into.Add(Make(horizontal, fixedCoord, cursor, to));
        }

        private static WallSegment Make(bool horizontal, float fixedCoord, float a, float b)
        {
            return horizontal
                ? new WallSegment(new Vector2(a, fixedCoord), new Vector2(b, fixedCoord))
                : new WallSegment(new Vector2(fixedCoord, a), new Vector2(fixedCoord, b));
        }
    }
}
