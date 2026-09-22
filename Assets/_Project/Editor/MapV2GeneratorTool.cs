using System.Collections.Generic;
using System.Text;
using Marco.Core.Locomotion;
using Marco.Core.Sound;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Objectives;
using Marco.Presentation.Sound;
using Marco.Presentation.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.EditorTools
{
    /// <summary>
    /// §10.1 맵 v2를 <b>버튼 한 번으로</b> 생성한다. 좌표는 전부 <see cref="MapV2Layout"/>에 있다.
    ///
    /// <para>
    /// <b>왜 자동 생성인가.</b> ProBuilder 수작업은 며칠이 걸리고, 무엇보다 §10.2-1 배치 검증이
    /// 여유 0.32m(C↔D)까지 계산해 둔 좌표를 사람 손으로 맞출 수 없다. 한 구역만 0.5m
    /// 어긋나도 검증이 무효가 되는데 그것을 눈으로 알아챌 방법이 없다.
    /// </para>
    ///
    /// <para>
    /// <b>태그·레이어는 생성하는 그 자리에서 붙인다.</b> 나중에 일괄로 붙이면 반드시 빠지고,
    /// 빠진 벽은 §5.6 감쇠에서 <b>조용히</b> 무력화된다(<c>PhysicsOcclusionProbe.WeightOf</c>가
    /// 태그 없는 콜라이더를 0으로 세기 때문에 에러도 경고도 없다).
    /// </para>
    ///
    /// <para>
    /// <b>멱등</b>: 기존 <c>MapV2</c> 루트를 먼저 지우고 다시 만든다. 몇 번이든 재실행 가능하다.
    /// </para>
    ///
    /// <para>
    /// <b>콜라이더가 두 장인 이유</b>: §5.6이 *"SoundBlocking 레이어는 렌더 메시와 분리된 전용
    /// 콜라이더 레이어로 둬서 다른 물리 연산(플레이어 이동 충돌 등)과 섞이지 않게 한다"* 고
    /// 못박았다. 그래서 벽·바닥마다 ①이동·렌더용(Default) ②차폐용(SoundBlocking) 두 개를 만든다.
    /// 히트 수가 늘어나는 것은 블록 1-A①에서 <c>MaxHits</c>를 128로 올린 이유다.
    /// </para>
    /// </summary>
    public static class MapV2GeneratorTool
    {
        public const string RootName = "MapV2";

        private const string SoundBlockingLayer = PhysicsOcclusionProbe.SoundBlockingLayerName;
        private const string WaterLayer = "Water";

        [MenuItem("Tools/MARCO/맵 v2 생성 (Generate Map v2)", priority = 40)]
        public static void GenerateFromMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("맵 v2 생성", "재생 모드에서는 실행할 수 없습니다.", "확인");
                return;
            }

            if (!MarcoSetupPipeline.Automated && !EditorUtility.DisplayDialog(
                    "맵 v2 생성 (§10.1)",
                    $"활성 씬에 '{RootName}' 루트를 만들고 아래를 생성합니다:\n\n" +
                    $"  · 구역 {MapV2Layout.Zones.Length}개 (지상 + 관람층) — 바닥 재질 태그 포함\n" +
                    $"  · 문 {MapV2Layout.Doors.Length}개를 반영한 벽 + 차폐 콜라이더\n" +
                    $"  · 수면 {MapV2Layout.Waters.Length}개 (침강부 포함) + 수면 차폐판\n" +
                    $"  · 밸브 {MapV2Layout.Valves.Length}개 · 배수구 {MapV2Layout.Drains.Length}개 마커\n" +
                    $"  · 출구 {MapV2Layout.Exits.Length}개 · 계단 경사로 {MapV2Layout.Stairs.Length}개 · " +
                    $"찰칵이 리스폰 {MapV2Layout.ClickerSpawns.Length}곳\n" +
                    $"  · §16.1 암전 — 환경광 흑 · 스카이박스 없음 · Directional Light 끔\n\n" +
                    $"⚠ 기존 '{RootName}' 루트가 있으면 지우고 다시 만듭니다.\n\n계속하시겠습니까?",
                    "생성", "취소"))
            {
                Debug.Log("[MapV2] 사용자가 취소했습니다 — 변경 없음.");
                return;
            }

            Generate();
        }

        /// <summary>파이프라인·검증 도구가 부르는 실제 생성 진입점. 대화상자를 띄우지 않는다.</summary>
        public static GameObject Generate()
        {
            var log = new StringBuilder();
            log.AppendLine("=== 맵 v2 생성 (§10.1) ===");

            if (!VerifyTagsAndLayers(log))
            {
                Debug.LogError(log.ToString());
                return null;
            }

            // 멱등: 이전 판을 먼저 지운다.
            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
                log.AppendLine("기존 루트 제거 — 재생성합니다.");
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate Map v2");

            var counts = new Dictionary<string, int>();
            BuildBaseFloor(root, counts);
            BuildZones(root, counts);
            BuildPartition(root, counts);
            BuildStairs(root, counts);
            BuildWater(root, counts);
            BuildMarkers(root, counts);
            AttachPlanData(root);
            PlaceAnchors(log);
            DisableLegacyMap(log);
            ApplyDarkness(log);

            foreach (KeyValuePair<string, int> kv in counts)
                log.AppendLine($"  {kv.Key}: {kv.Value}개");

            EditorSceneManager.MarkSceneDirty(root.scene);
            log.AppendLine();
            log.AppendLine("생성 완료. 씬을 저장(Ctrl+S)한 뒤 " +
                           "Tools/MARCO/맵 v2 배치 검증 을 실행하세요(§10.2-1 · §6.5-3).");
            Debug.Log(log.ToString());
            return root;
        }

        // ── 사전 점검 ────────────────────────────────────────────────────

        /// <summary>
        /// 태그·레이어가 없으면 <b>생성 자체를 중단한다.</b>
        ///
        /// 없는 태그로 <c>gameObject.tag = ...</c> 를 하면 Unity가 예외를 던지고, 그 시점까지
        /// 만든 절반짜리 맵이 씬에 남는다. 더 나쁜 것은 레이어가 없을 때인데 —
        /// <c>LayerMask.GetMask</c>가 0을 돌려주고 차폐 판정이 <b>항상 "벽 0개"</b>가 되어
        /// 맵은 멀쩡해 보이는데 §5.6 전체가 죽는다.
        /// </summary>
        private static bool VerifyTagsAndLayers(StringBuilder log)
        {
            var missingTags = new List<string>();
            var required = new List<string>
            {
                PhysicsOcclusionProbe.WallTag,
                PhysicsOcclusionProbe.FloorSlabTag,
            };
            for (int i = 0; i < MapV2Layout.Zones.Length; i++)
                required.Add(MapV2Layout.MaterialTag(MapV2Layout.Zones[i].Material));
            required.Add(MapV2Layout.MaterialTag(FootstepMaterial.Concrete));

            foreach (string tag in required)
            {
                if (!TagExists(tag) && !missingTags.Contains(tag))
                    missingTags.Add(tag);
            }

            bool ok = true;
            if (missingTags.Count > 0)
            {
                ok = false;
                log.AppendLine($"[중단] TagManager에 없는 태그: {string.Join(", ", missingTags)}");
                log.AppendLine("       Project Settings > Tags and Layers 에 추가한 뒤 다시 실행하세요.");
            }

            if (LayerMask.NameToLayer(SoundBlockingLayer) < 0)
            {
                ok = false;
                log.AppendLine($"[중단] '{SoundBlockingLayer}' 레이어가 없습니다 — " +
                               "차폐 판정이 항상 '벽 0개'가 되어 §5.6 전체가 무력화됩니다.");
            }

            if (LayerMask.NameToLayer(WaterLayer) < 0)
            {
                ok = false;
                log.AppendLine($"[중단] '{WaterLayer}' 레이어가 없습니다 — 물 볼륨을 만들 수 없습니다.");
            }

            return ok;
        }

        private static bool TagExists(string tag)
        {
            string[] tags = UnityEditorInternal.InternalEditorUtility.tags;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i] == tag)
                    return true;

            return false;
        }

        // ── 바닥 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 복도(구역 밖)를 걸을 수 있게 하는 기본 바닥. §10.1 L자 컷을 반영해 두 장으로 만든다.
        /// 재질 태그는 §5.9 기본값(콘크리트 ×1.0)이다 — §10.1에 복도 재질 항목이 없다.
        ///
        /// 구역 바닥보다 <see cref="MapV2Layout.BaseFloorDrop"/>만큼 낮다.
        /// </summary>
        private static void BuildBaseFloor(GameObject root, Dictionary<string, int> counts)
        {
            var group = Child(root, "BaseFloor");
            string tag = MapV2Layout.MaterialTag(FootstepMaterial.Concrete);

            // L자: (0,0)~(40,40) + (40,0)~(52,28). 수면 사각형은 뚫는다(풀 뚜껑 방지).
            int made = MakeFloorAroundWater(group, "BaseFloor_West", new Rect(0f, 0f, MapV2Layout.CutX, MapV2Layout.MapDepth),
                -MapV2Layout.BaseFloorDrop, tag);
            made += MakeFloorAroundWater(group, "BaseFloor_EastWing",
                new Rect(MapV2Layout.CutX, 0f, MapV2Layout.MapWidth - MapV2Layout.CutX, MapV2Layout.CutY),
                -MapV2Layout.BaseFloorDrop, tag);

            Bump(counts, "기본 바닥", made);
        }

        private static void BuildZones(GameObject root, Dictionary<string, int> counts)
        {
            var ground = Child(root, "Ground");
            var upper = Child(root, "Upper");

            for (int i = 0; i < MapV2Layout.Zones.Length; i++)
            {
                MapV2Layout.Zone zone = MapV2Layout.Zones[i];
                GameObject parent = Child(zone.IsUpperFloor ? upper : ground, "Zone_" + zone.Name);
                float floorY = zone.IsUpperFloor ? MapV2Layout.UpperFloorY : 0f;

                // ① 밟는 바닥 — §5.9 재질 태그. 하향 레이가 읽는다. 지상 바닥은 수면 사각형을 뚫는다.
                string floorTag = MapV2Layout.MaterialTag(zone.Material);
                if (zone.IsUpperFloor)
                {
                    MakeFloor(parent, "Floor", zone.Area, floorY, floorTag);
                    Bump(counts, "구역 바닥", 1);
                }
                else
                {
                    Bump(counts, "구역 바닥", MakeFloorAroundWater(parent, "Floor", zone.Area, floorY, floorTag));
                }

                // ② 2층 바닥은 §5.6 층간 차폐판(Wall 2장 상당)을 겸한다.
                //    계단 개구부에는 구멍을 낸다 — §5.6 "계단은 개구부라 미적용".
                if (zone.IsUpperFloor)
                    Bump(counts, "층간 차폐판", MakeFloorSlab(parent, zone.Area, floorY));

                // ③ 벽 — 문 위치에서 끊는다.
                List<MapV2Layout.WallSegment> segments =
                    MapV2Layout.WallSegmentsOf(zone, MapV2Layout.Doors);
                for (int s = 0; s < segments.Count; s++)
                {
                    if (segments[s].Length < 0.05f)
                        continue;

                    MakeWall(parent, $"Wall_{s}", segments[s], floorY);
                    Bump(counts, "벽(+차폐)", 1);
                }
            }
        }

        /// <summary>
        /// §10.1 "기계실–풀 홀 칸막이: x=40, y=13~20". 권장 요소지만 §10.2-1의 여유를 만든다.
        /// </summary>
        private static void BuildPartition(GameObject root, Dictionary<string, int> counts)
        {
            var seg = new MapV2Layout.WallSegment(
                new Vector2(MapV2Layout.Partition.xMin, MapV2Layout.Partition.yMin),
                new Vector2(MapV2Layout.Partition.xMin, MapV2Layout.Partition.yMax));

            MakeWall(Child(root, "Partition"), "Partition_x40", seg, 0f);
            Bump(counts, "칸막이(+차폐)", 1);
        }

        // ── 수면 ─────────────────────────────────────────────────────────

        /// <summary>
        /// §10.1 수면 영역. 오브젝트가 <b>네 종류</b> 나온다 —
        /// ① 판정용 <see cref="WaterVolumeBehaviour"/>(콜라이더 없음, 좌표만)
        /// ② §5.6 수면 차폐판(SoundBlocking, `Wall` 태그 — 표가 지정한 태그 그대로)
        /// ③ 풀 바닥(Default, 재질 `FloorWater` — 물속 발소리는 §5.9 ×0으로 무음)
        /// ④ <b>풀 측벽</b>(SoundBlocking, `Wall`) — 아래 설명 참조
        ///
        /// <para>
        /// <b>④가 빠지면 §6.5-3이 무너진다.</b> 수면 차폐판만 깔면 수중 밸브의 소리가
        /// <b>수면을 지나지 않고 옆으로</b> 빠져나온다 — 풀이 "흙을 파낸 공백"이 아니라
        /// 얇은 판 두 장(덱과 바닥) 사이의 열린 틈이 되기 때문이다. 배치 검증을 돌려
        /// 실제로 A↔B 28지점·B↔D 2지점의 동시 감시가 생기는 것을 확인했고,
        /// 측벽을 넣자 둘 다 0이 됐다. 이 측벽이 <b>"수중 밸브 12m가 수면을 지나 술래
        /// 청취 7.2m"</b>(§5.6 [v0.4])를 성립시키는 물리적 조건이다.
        /// </para>
        ///
        /// <para>
        /// ①이 트리거가 아닌 이유는 <c>WaterVolumeBehaviour</c> 주석에 있다(상태를 들고 있지
        /// 않아야 리스폰·페이즈 전환으로 어긋나지 않는다).
        /// </para>
        /// </summary>
        private static void BuildWater(GameObject root, Dictionary<string, int> counts)
        {
            var group = Child(root, "Water");

            for (int i = 0; i < MapV2Layout.Waters.Length; i++)
            {
                MapV2Layout.WaterArea w = MapV2Layout.Waters[i];
                GameObject parent = Child(group, "Water_" + w.Name);
                parent.layer = LayerMask.NameToLayer(WaterLayer);

                var volume = parent.AddComponent<WaterVolumeBehaviour>();
                volume.Configure(
                    new Vector2(w.Area.xMin, w.Area.yMin),
                    new Vector2(w.Area.xMax, w.Area.yMax),
                    surfaceY: 0f, depth: w.Depth,
                    sumpCenter: w.SumpCenter, sumpRadius: w.SumpRadius, sumpDepth: w.SumpDepth);

                // ② 수면 차폐판 — §5.6 [v0.4] 표: 수면(물 표면) | `Wall` | 반경 ×0.5.
                //    별도 태그를 만들지 않았다(효과가 벽 1장과 같고 표가 `Wall`로 지정했다).
                GameObject occluder = MakeCube(parent, "SurfaceOccluder",
                    new Vector3(w.Area.center.x, -MapV2Layout.WallThickness * 0.5f, w.Area.center.y),
                    new Vector3(w.Area.width, MapV2Layout.WallThickness, w.Area.height));
                StripRenderer(occluder);
                occluder.layer = LayerMask.NameToLayer(SoundBlockingLayer);
                occluder.tag = PhysicsOcclusionProbe.WallTag;

                // [블록 7 · GAP-90] 수면판은 **트리거**다 — 고체면 플레이어가 물 위를 걸어 잠수가 아예
                // 성립하지 않았다(발이 수면 높이라 "덱"으로 판정). 차폐 레이는 트리거도 센다
                // (PhysicsOcclusionProbe — QueryTriggerInteraction.Collide). 풀 측벽(④)도 같은 이유로 트리거다.
                // 수면 위를 덮던 구역 바닥·기본 바닥은 수면 사각형만큼 뚫는다(MakeFloorAroundWater).
                occluder.GetComponent<Collider>().isTrigger = true;

                // [블록 7 · GAP-90] 수영 바닥 — 발이 수면 아래 SwimFloorDepth에 머문다(떠 있는 높이).
                //   소리를 막지 않는다(Default 레이어). 재질 태그는 물(§5.9 발소리 파문 없음).
                GameObject swim = MakeCube(parent, "SwimFloor",
                    new Vector3(w.Area.center.x, -MapV2Layout.SwimFloorDepth - MapV2Layout.FloorThickness * 0.5f, w.Area.center.y),
                    new Vector3(w.Area.width, MapV2Layout.FloorThickness, w.Area.height));
                StripRenderer(swim);
                swim.tag = MapV2Layout.MaterialTag(FootstepMaterial.Water);

                // [블록 7 · GAP-102] 가장자리 경사로 — 물에서 덱으로 걸어 나온다.
                BuildPoolRamps(parent, w.Area);
                Bump(counts, "풀 경사로", 4);

                // ③ 풀 바닥 — 기본 수심. 침강부는 아래에서 한 칸 더 판다.
                MakeFloor(parent, "PoolBed", w.Area, -w.Depth,
                    MapV2Layout.MaterialTag(FootstepMaterial.Water));

                // ④ 풀 측벽 — 수면부터 가장 깊은 바닥까지, 수면 사각형 네 변을 감싼다.
                //    깊이는 침강부를 포함한 최대 수심이어야 한다(침강부 옆이 뚫리면 같은 누출).
                float wallDepth = Mathf.Max(w.Depth, w.SumpRadius > 0f ? w.SumpDepth : 0f);
                BuildPoolWalls(parent, w.Area, wallDepth);
                Bump(counts, "풀 측벽", 4);

                if (w.SumpRadius > 0f)
                {
                    // §6.5-2 국소 침강부 — 이 지점만 3.5m. 얕게 만들면 §6.5-3이 무너진다.
                    var sumpRect = new Rect(
                        w.SumpCenter.x - w.SumpRadius, w.SumpCenter.y - w.SumpRadius,
                        w.SumpRadius * 2f, w.SumpRadius * 2f);
                    MakeFloor(parent, "PoolBed_Sump", sumpRect, -w.SumpDepth,
                        MapV2Layout.MaterialTag(FootstepMaterial.Water));
                    Bump(counts, "침강부", 1);
                }

                Bump(counts, "수면(+차폐판+바닥)", 1);
            }
        }

        /// <summary>
        /// §10.1 "계단 서·동 2개" — [블록 1-D] 직선 경사로.
        ///
        /// <para>
        /// <b>SoundBlocking 콜라이더를 만들지 않는다.</b> §5.6이 *"계단은 개구부이므로 차폐를
        /// 적용하지 않는다"* 고 못박았다. 층간 차폐판에서 발자국만큼 구멍을 내는 것도 같은 규칙의
        /// 다른 절반이다(<see cref="MakeFloorSlab"/>).
        /// </para>
        ///
        /// <para>
        /// <b>§5.9 재질 태그는 붙인다</b>(콘크리트 ×1.0 기본값 — §10.1에 계단 재질 항목이 없다).
        /// 태그가 붙어 있어야 배치 검증의 보행 판정이 이 경사로를 <b>벽이 아니라 바닥</b>으로
        /// 본다 — 그것이 "재질 태그가 있으면 밟을 수 있는 면"이라는 규칙이다.
        /// </para>
        ///
        /// <para>
        /// 경사각은 좌표에서 유도된다(서 41.19° / 동 37.87°). 프리팹
        /// <c>CharacterController.slopeLimit</c>이 45°라 둘 다 등반 가능하지만
        /// <b>여유가 3.81°뿐</b>이므로, 경사로를 조금이라도 눕히지 말 것.
        /// </para>
        /// </summary>
        private static void BuildStairs(GameObject root, Dictionary<string, int> counts)
        {
            var group = Child(root, "Stairs");
            string tag = MapV2Layout.MaterialTag(FootstepMaterial.Concrete);

            for (int i = 0; i < MapV2Layout.Stairs.Length; i++)
            {
                MapV2Layout.Stair st = MapV2Layout.Stairs[i];

                var bottom = new Vector3(st.Bottom.x, 0f, st.Bottom.y);
                var top = new Vector3(st.Top.x, MapV2Layout.UpperFloorY, st.Top.y);
                Vector3 slope = top - bottom;

                // 발자국의 짧은 변이 계단 폭이다(주행 방향이 긴 변).
                bool runsAlongX = Mathf.Abs(st.Top.x - st.Bottom.x) > Mathf.Abs(st.Top.y - st.Bottom.y);
                float width = runsAlongX ? st.Footprint.height : st.Footprint.width;

                Quaternion rotation = Quaternion.LookRotation(slope.normalized, Vector3.up);

                // 박스 중심을 경사면 법선 방향으로 반 두께 내려, **윗면**이 양 끝점을 지나게 한다.
                Vector3 surfaceMid = (bottom + top) * 0.5f;
                Vector3 center = surfaceMid - rotation * Vector3.up * (MapV2Layout.FloorThickness * 0.5f);

                GameObject ramp = MakeCube(group, st.Name,
                    center, new Vector3(width, MapV2Layout.FloorThickness, st.SlopeLength));
                ramp.transform.rotation = rotation;
                ramp.tag = tag;
            }

            Bump(counts, "계단 경사로", MapV2Layout.Stairs.Length);
        }

        /// <summary>
        /// [블록 7 · GAP-102] 수면 사각형 네 변 안쪽에 덱(0) → 수영 바닥(−SwimFloorDepth) 경사로.
        /// 윗면이 경사선 위에 오도록 두께만큼 법선 반대로 민다. 렌더러 없음, 소리 차폐 없음.
        /// </summary>
        private static void BuildPoolRamps(GameObject parent, Rect area)
        {
            float depth = MapV2Layout.SwimFloorDepth;
            float run = MapV2Layout.PoolRampRun;
            float length = Mathf.Sqrt(run * run + depth * depth);
            float t = MapV2Layout.FloorThickness;

            // (가장자리 중점, 안쪽 방향, 가장자리 길이)
            var edges = new (Vector3 Mid, Vector3 Inward, float Length, string Name)[]
            {
                (new Vector3(area.center.x, 0f, area.yMin), Vector3.forward, area.width, "S"),
                (new Vector3(area.center.x, 0f, area.yMax), Vector3.back, area.width, "N"),
                (new Vector3(area.xMin, 0f, area.center.y), Vector3.right, area.height, "W"),
                (new Vector3(area.xMax, 0f, area.center.y), Vector3.left, area.height, "E"),
            };

            for (int i = 0; i < edges.Length; i++)
            {
                (Vector3 mid, Vector3 inward, float edgeLength, string name) = edges[i];
                Vector3 down = (inward * run + Vector3.down * depth) / length;   // 경사 방향
                Vector3 normal = (inward * depth + Vector3.up * run) / length;   // 윗면 법선(위쪽)
                Vector3 surfaceCenter = mid + inward * (run * 0.5f) + Vector3.down * (depth * 0.5f);

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "PoolRamp_" + name;
                go.transform.SetParent(parent.transform, worldPositionStays: false);
                go.transform.position = surfaceCenter - normal * (t * 0.5f);
                go.transform.rotation = Quaternion.LookRotation(down, normal);
                go.transform.localScale = new Vector3(edgeLength, t, length);
                StripRenderer(go);
                go.tag = MapV2Layout.MaterialTag(FootstepMaterial.Water);
            }
        }

        /// <summary>
        /// 수면 사각형 네 변을 감싸는 차폐 측벽. 렌더러는 없다 — 지면 메시는 블록 7이 만든다.
        /// 모서리에서 두 장이 겹치지만, 모서리를 정확히 지나는 선은 실질적으로 없어
        /// 감쇠가 과하게 계산되는 경우가 생기지 않는다(겹침을 피하려 변마다 두께를
        /// 빼면 반대로 모서리에 <b>구멍</b>이 생겨 소리가 새는 쪽이 문제다).
        /// </summary>
        private static void BuildPoolWalls(GameObject parent, Rect area, float depth)
        {
            float t = MapV2Layout.WallThickness;
            float cy = -depth * 0.5f;

            Make("PoolWall_W", new Vector3(area.xMin - t * 0.5f, cy, area.center.y),
                new Vector3(t, depth, area.height + t * 2f));
            Make("PoolWall_E", new Vector3(area.xMax + t * 0.5f, cy, area.center.y),
                new Vector3(t, depth, area.height + t * 2f));
            Make("PoolWall_S", new Vector3(area.center.x, cy, area.yMin - t * 0.5f),
                new Vector3(area.width + t * 2f, depth, t));
            Make("PoolWall_N", new Vector3(area.center.x, cy, area.yMax + t * 0.5f),
                new Vector3(area.width + t * 2f, depth, t));

            void Make(string name, Vector3 center, Vector3 size)
            {
                GameObject go = MakeCube(parent, name, center, size);
                StripRenderer(go);
                go.layer = LayerMask.NameToLayer(SoundBlockingLayer);
                go.tag = PhysicsOcclusionProbe.WallTag;

                // [버그 수정 1] 측벽은 **트리거**다 — 소리만 막고 몸은 막지 않는다(수면판·밸브 마커와 같은 패턴).
                // 레이어 충돌 행렬상 SoundBlocking도 플레이어와 부딪히므로 고체로 두면 보이지 않는 벽이 된다.
                // 차폐 레이는 트리거도 센다(PhysicsOcclusionProbe — QueryTriggerInteraction.Collide).
                go.GetComponent<Collider>().isTrigger = true;
            }
        }

        // ── 마커 (블록 2·4·6이 로직을 붙인다) ───────────────────────────

        private static void BuildMarkers(GameObject root, Dictionary<string, int> counts)
        {
            var group = Child(root, "Markers");

            var valves = Child(group, "Valves");
            for (int i = 0; i < MapV2Layout.Valves.Length; i++)
            {
                MapV2Layout.ValvePoint v = MapV2Layout.Valves[i];
                float y = v.UpperFloor ? MapV2Layout.UpperFloorY + 1f : 1f;
                if (v.Underwater)
                    y = -DepthAt(v.Position) + 0.5f; // 수중 밸브는 바닥 근처

                // [블록 7] 실물 밸브 — 큐브 + ValveBehaviour(§10.2 식별자) + 상태 색 표시.
                //   NetworkObject · ValveNetworkSync는 파이프라인의 Setup Network Valves가 붙인다(생성 후 실행).
                GameObject valve = MakeCube(valves, $"Valve_{v.Id}_{v.Zone}",
                    new Vector3(v.Position.x, y, v.Position.y), Vector3.one * ValveCubeSize);
                valve.AddComponent<ValveBehaviour>().Configure(ParseValveId(v.Id));
                valve.AddComponent<ValveVisualIndicator>();

                // 밸브는 몸을 막지 않는다 — 트리거로 둔다. 고체면 §10.2-1 배치 검증(태그 없는 콜라이더 = 벽)이
                // 밸브 자리 셀을 막아 경로 끝점이 사라지고, 상호작용은 거리 판정이라 콜라이더가 필요 없다.
                valve.GetComponent<Collider>().isTrigger = true;
            }
            Bump(counts, "밸브 마커", MapV2Layout.Valves.Length);

            var drains = Child(group, "Drains");
            for (int i = 0; i < MapV2Layout.Drains.Length; i++)
            {
                MapV2Layout.DrainPoint d = MapV2Layout.Drains[i];
                // §6.5-2 "양쪽 모두 3.5m" — 배수구는 바닥에 있다.
                GameObject marker = MakeMarker(drains, $"Drain_{d.Id}_{d.Water}",
                    new Vector3(d.Position.x, -DepthAt(d.Position), d.Position.y));

                // [블록 4] 배수구 로직 — DrainRegistry 자가 등록 + E 홀드 전달.
                //   식별자는 MapV2Layout의 "1"/"2"를 DrainId(1 메인풀 / 2 유아풀)로 옮긴다.
                marker.AddComponent<Marco.Presentation.Objectives.DrainPoint>()
                    .Configure((Marco.Core.Objectives.DrainId)int.Parse(d.Id));
            }
            Bump(counts, "배수구 마커", MapV2Layout.Drains.Length);

            var exits = Child(group, "Exits");
            for (int i = 0; i < MapV2Layout.Exits.Length; i++)
            {
                (string name, Vector2 p) = MapV2Layout.Exits[i];
                GameObject exit = MakeMarker(exits, name, new Vector3(p.x, 1f, p.y));

                // [블록 7] §10.5 출구 — 탈출 판정 + 종반 출구 파문 위치 등록(EscapePointRegistry).
                exit.AddComponent<EscapePointTrigger>();
            }
            Bump(counts, "출구 마커", MapV2Layout.Exits.Length);

            var clickers = Child(group, "ClickerSpawns");
            for (int i = 0; i < MapV2Layout.ClickerSpawns.Length; i++)
            {
                (string name, Vector2 p, bool upper) = MapV2Layout.ClickerSpawns[i];
                float y = (upper ? MapV2Layout.UpperFloorY : 0f) + 0.5f;
                GameObject marker = MakeMarker(clickers, name, new Vector3(p.x, y, p.y));

                // [블록 6] §7 찰칵이 리스폰 지점 — 레지스트리 자가 등록 + E 줍기.
                marker.AddComponent<Marco.Presentation.Objectives.ClickerSpawnPoint>().Configure(i);
            }
            Bump(counts, "찰칵이 리스폰", MapV2Layout.ClickerSpawns.Length);
        }

        /// <summary>밸브 큐브 한 변(m). 표시·상호작용 대상 크기 — 규칙 수치 아님(구 그레이박스와 같은 0.6).</summary>
        private const float ValveCubeSize = 0.6f;

        private static Marco.Core.Objectives.ValveId ParseValveId(string id) =>
            (Marco.Core.Objectives.ValveId)System.Enum.Parse(typeof(Marco.Core.Objectives.ValveId), id);

        /// <summary>
        /// [블록 7] §12.4 로비 브리핑 평면도의 원천 — §10.1 구역 13개 + 수면 2개를 그대로 복사한다.
        /// 런타임은 에디터 어셈블리(MapV2Layout)를 읽을 수 없어서다. 새 좌표는 없다.
        /// </summary>
        private static void AttachPlanData(GameObject root)
        {
            var areas = new List<MapPlanData.Area>();
            for (int i = 0; i < MapV2Layout.Zones.Length; i++)
            {
                MapV2Layout.Zone z = MapV2Layout.Zones[i];
                areas.Add(new MapPlanData.Area { Name = z.Name, Rect = z.Area, UpperFloor = z.IsUpperFloor, Water = false });
            }

            for (int i = 0; i < MapV2Layout.Waters.Length; i++)
            {
                MapV2Layout.WaterArea w = MapV2Layout.Waters[i];
                areas.Add(new MapPlanData.Area { Name = w.Name, Rect = w.Area, UpperFloor = false, Water = true });
            }

            root.AddComponent<MapPlanData>().Configure(
                new Rect(0f, 0f, MapV2Layout.MapWidth, MapV2Layout.MapDepth), areas.ToArray());
        }

        /// <summary>
        /// [블록 7] 스폰 앵커를 맵 v2 좌표로 옮긴다. §10.1 "로비 (2,32)~(12,40) — 도망자 스폰" →
        /// 로비 중심. 술래 격리 앵커는 기존 좌표(구 그레이박스 GAP-45 잠정)를 유지한다 — 맵 v2에서
        /// 구역 사이 통로 위라 막히지 않는다(실기 확인 항목).
        /// </summary>
        private static void PlaceAnchors(StringBuilder log)
        {
            // §10.1 도망자 스폰 — 로비 중심, 남쪽(맵 안쪽)을 본다.
            Vector2 spawn = MapV2Layout.RunnerSpawn;
            PlaceAnchor<SpawnAnchor>("SpawnAnchor", new Vector3(spawn.x, 0.05f, spawn.y),
                Quaternion.Euler(0f, 180f, 0f), "§10.1 도망자 스폰(로비 중심)", log);

            // [커밋 전 수정 2 · GAP-101] 술래 격리 — 직원통로(기계실 바깥). 구 좌표 (12,17)은 v0.3 맵 기준이었다.
            Vector2 iso = MapV2Layout.SeekerIsolation;
            PlaceAnchor<SeekerIsolationAnchor>("SeekerIsolationAnchor", new Vector3(iso.x, 0.05f, iso.y),
                Quaternion.identity, "§10.1 술래 격리(직원통로, 통로 문을 본다)", log);
        }

        /// <summary>
        /// 앵커를 있으면 옮기고 없으면 만든다 — 파이프라인 순서(1b 생성 → 5 맵 정리)와 무관하게 맵 v2 좌표가 된다.
        /// </summary>
        private static void PlaceAnchor<T>(string name, Vector3 position, Quaternion rotation, string reason,
            StringBuilder log) where T : Component
        {
            T anchor = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (anchor == null)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                anchor = go.AddComponent<T>();
            }
            else
            {
                Undo.RecordObject(anchor.transform, "Place " + name + " (Map v2)");
            }

            anchor.transform.SetPositionAndRotation(position, rotation);
            log.AppendLine($"{name} → {position} ({reason})");
        }

        /// <summary>
        /// [블록 7] 구 그레이박스를 <b>끈다</b>(지우지 않는다 — 되돌릴 수 있게). 켜 두면 밸브 3개(식별자가
        /// 전부 A)가 네트워크 셋업에 함께 잡혀 §6.1-0 활성 선택이 깨지고, 구 출구가 탈출을 받는다.
        /// </summary>
        private static void DisableLegacyMap(StringBuilder log)
        {
            string[] legacy = { "Graybox", "EscapePoint" };
            for (int i = 0; i < legacy.Length; i++)
            {
                GameObject go = GameObject.Find(legacy[i]);
                if (go == null)
                    continue;

                Undo.RecordObject(go, "Disable legacy map");
                go.SetActive(false);
                log.AppendLine($"구 맵 '{legacy[i]}' 비활성화(삭제 아님)");
            }
        }

        /// <summary>
        /// [버그 수정 2] §16.1 "완전한 흑 배경 위에 발광 라인과 파문만으로" · §16.2 배경 `#000000` — 맵 씬의
        /// 환경광 · 스카이박스 · 태양광을 끈다. <b>씬을 손으로 고치지 않고 여기서 박는다</b> — 정본이 둘이 되면
        /// 재생성 때 조용히 되돌아간다(§10.1 좌표 유실과 같은 함정).
        ///
        /// <para>
        /// <c>RenderSettings</c>는 <b>활성 씬</b>의 값이다. 생성기는 맵 루트를 만드는 그 활성 씬(파이프라인 1b: Game)에
        /// 쓴다. 런타임에는 FishNet이 맵(전역 씬)을 로드한 뒤 그 씬을 활성으로 바꾸므로 라운드 동안 이 값이 적용된다.
        /// 태양광은 구 그레이박스와 같이 <b>끄되 지우지 않는다</b>.
        /// </para>
        /// </summary>
        private static void ApplyDarkness(StringBuilder log)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.skybox = null;

            // 스카이박스를 지워도 기본 환경 반사가 남으면 Lit 표면이 희미하게 보인다 — §16.1 "라이팅 사용량 0".
            RenderSettings.reflectionIntensity = 0f;

            log.AppendLine("§16.1 암전 — 환경광 Flat 흑 · 스카이박스 없음 · 환경 반사 0");

            GameObject sun = GameObject.Find("Directional Light");
            if (sun != null)
            {
                Undo.RecordObject(sun, "Disable directional light (§16.1)");
                sun.SetActive(false);
                log.AppendLine("§16.1 'Directional Light' 비활성화(삭제 아님)");
            }
        }

        /// <summary>이 (x, z) 지점의 수심. 침강부를 반영한다 — 없으면 0.</summary>
        private static float DepthAt(Vector2 p)
        {
            for (int i = 0; i < MapV2Layout.Waters.Length; i++)
            {
                MapV2Layout.WaterArea w = MapV2Layout.Waters[i];
                if (!w.Area.Contains(p))
                    continue;

                if (w.SumpRadius > 0f && Vector2.Distance(p, w.SumpCenter) <= w.SumpRadius)
                    return Mathf.Max(w.Depth, w.SumpDepth);

                return w.Depth;
            }

            return 0f;
        }

        // ── 프리미티브 헬퍼 ──────────────────────────────────────────────

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }

        private static GameObject MakeCube(GameObject parent, string name, Vector3 center, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.position = center;
            go.transform.localScale = size;
            return go;
        }

        private static void StripRenderer(GameObject go)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                Object.DestroyImmediate(renderer);
        }

        /// <summary>바닥판 하나. 윗면이 <paramref name="topY"/>에 오도록 배치한다.</summary>
        private static void MakeFloor(GameObject parent, string name, Rect area, float topY, string tag)
        {
            GameObject go = MakeCube(parent, name,
                new Vector3(area.center.x, topY - MapV2Layout.FloorThickness * 0.5f, area.center.y),
                new Vector3(area.width, MapV2Layout.FloorThickness, area.height));
            go.tag = tag;
        }

        /// <summary>
        /// §5.6 층간 차폐판. 계단 개구부를 피해 <b>최대 네 조각</b>으로 잘라 만든다 —
        /// 통째로 깔면 계단으로 새는 소리까지 ×0.25가 되어 §10.1 관람석의 "풀 홀 청취 우위"가
        /// 사라진다. 반환값은 실제로 만든 조각 수다.
        /// </summary>
        private static int MakeFloorSlab(GameObject parent, Rect area, float floorY)
        {
            // 구멍은 계단의 **실제 발자국**이다. 점 중심 정사각형으로 뚫으면 경사로가
            // 차폐판을 비스듬히 가로지르는 구간이 남아 §5.6 "계단은 개구부라 미적용"이
            // 부분적으로만 성립한다.
            var holes = new List<Rect>();
            for (int i = 0; i < MapV2Layout.Stairs.Length; i++)
                holes.Add(MapV2Layout.Stairs[i].Footprint);

            List<Rect> pieces = SubtractAll(area, holes);

            int made = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].width < 0.05f || pieces[i].height < 0.05f)
                    continue;

                GameObject slab = MakeCube(parent, $"FloorSlab_{made}",
                    new Vector3(pieces[i].center.x, floorY - MapV2Layout.FloorThickness * 0.5f, pieces[i].center.y),
                    new Vector3(pieces[i].width, MapV2Layout.FloorThickness, pieces[i].height));
                StripRenderer(slab);
                slab.layer = LayerMask.NameToLayer(SoundBlockingLayer);
                slab.tag = PhysicsOcclusionProbe.FloorSlabTag;
                made++;
            }

            return made;
        }

        /// <summary>
        /// [버그 수정] 지상 바닥판 — 수면 사각형만큼 뚫어 여러 조각으로 만든다. 반환값은 만든 조각 수다.
        ///
        /// <para>
        /// 구역 바닥(y 0)과 기본 바닥(y −0.01)이 수면을 통째로 덮고 있었다 — 풀 위가 고체 뚜껑이라 물에
        /// 들어갈 수 없었다(GAP-90의 수면판 트리거화만으로는 풀리지 않았다). 뚫는 영역은 §10.1 수면 사각형
        /// 그대로다(새 좌표 없음). 가장자리는 풀 경사로(GAP-102) 윗변과 만난다. 2층 바닥은 뚫지 않는다(관람석은 풀 위 발코니다).
        /// </para>
        /// </summary>
        private static int MakeFloorAroundWater(GameObject parent, string name, Rect area, float topY, string tag)
        {
            var holes = new List<Rect>();
            for (int i = 0; i < MapV2Layout.Waters.Length; i++)
                holes.Add(MapV2Layout.Waters[i].Area);

            List<Rect> pieces = SubtractAll(area, holes);

            int made = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].width < 0.05f || pieces[i].height < 0.05f)
                    continue;

                MakeFloor(parent, pieces.Count == 1 ? name : $"{name}_{made}", pieces[i], topY, tag);
                made++;
            }

            return made;
        }

        /// <summary>사각형에서 구멍 여러 개를 차례로 뺀 조각들(겹치지 않는 구멍은 무시된다).</summary>
        private static List<Rect> SubtractAll(Rect from, List<Rect> holes)
        {
            var pieces = new List<Rect> { from };
            for (int i = 0; i < holes.Count; i++)
            {
                var next = new List<Rect>();
                for (int p = 0; p < pieces.Count; p++)
                    next.AddRange(Subtract(pieces[p], holes[i]));

                pieces = next;
            }

            return pieces;
        }

        /// <summary>사각형에서 사각형을 빼 최대 4조각으로 만든다(겹치지 않으면 원본 그대로).</summary>
        private static List<Rect> Subtract(Rect from, Rect hole)
        {
            var result = new List<Rect>();
            if (!from.Overlaps(hole))
            {
                result.Add(from);
                return result;
            }

            float x0 = Mathf.Max(from.xMin, hole.xMin), x1 = Mathf.Min(from.xMax, hole.xMax);
            float y0 = Mathf.Max(from.yMin, hole.yMin), y1 = Mathf.Min(from.yMax, hole.yMax);

            if (from.yMin < y0) result.Add(Rect.MinMaxRect(from.xMin, from.yMin, from.xMax, y0));
            if (y1 < from.yMax) result.Add(Rect.MinMaxRect(from.xMin, y1, from.xMax, from.yMax));
            if (from.xMin < x0) result.Add(Rect.MinMaxRect(from.xMin, y0, x0, y1));
            if (x1 < from.xMax) result.Add(Rect.MinMaxRect(x1, y0, from.xMax, y1));

            return result;
        }

        /// <summary>
        /// 벽 하나 — 이동·렌더용(Default)과 차폐용(SoundBlocking, `Wall`) 콜라이더 두 장.
        /// 차폐 콜라이더를 자식으로 두면 벽을 옮길 때 같이 따라가므로 어긋날 수 없다.
        /// </summary>
        private static void MakeWall(GameObject parent, string name,
            in MapV2Layout.WallSegment segment, float floorY)
        {
            Vector2 mid = (segment.From + segment.To) * 0.5f;
            bool horizontal = Mathf.Approximately(segment.From.y, segment.To.y);
            var size = horizontal
                ? new Vector3(segment.Length, MapV2Layout.WallHeight, MapV2Layout.WallThickness)
                : new Vector3(MapV2Layout.WallThickness, MapV2Layout.WallHeight, segment.Length);

            var center = new Vector3(mid.x, floorY + MapV2Layout.WallHeight * 0.5f, mid.y);

            GameObject wall = MakeCube(parent, name, center, size);

            GameObject sound = MakeCube(wall, "Sound", center, size);
            // [버그 수정] MakeCube는 localScale에 size를 쓴다 — 부모(벽)가 이미 size로 늘어나 있어 자식의 실제
            // 크기가 size²가 됐다(길이 18m 벽 → 324m, 높이 3.5m → 12.25m). 플레이어와 충돌하는 SoundBlocking이라
            // 맵을 가로지르는 보이지 않는 벽이 됐고, 문 개구부도 옆 벽의 연장선이 막았다. 1이어야 벽과 같은 크기다.
            sound.transform.localScale = Vector3.one;
            StripRenderer(sound);
            sound.layer = LayerMask.NameToLayer(SoundBlockingLayer);
            sound.tag = PhysicsOcclusionProbe.WallTag;
        }

        private static GameObject MakeMarker(GameObject parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.position = position;
            return go;
        }

        private static void Bump(Dictionary<string, int> counts, string key, int delta)
        {
            counts.TryGetValue(key, out int current);
            counts[key] = current + delta;
        }

        /// <summary>
        /// §6.5-2 / §10.1 수심이 <c>DiveRules</c>의 잠수 가능 최소 수심을 만족하는지.
        /// 배치 검증(1-C)이 부르지만, 값이 여기(맵 데이터)에 있으므로 판정도 여기 둔다.
        /// </summary>
        public static bool IsDivable(float depth) => depth > DiveRules.MinDivableDepth;
    }
}
