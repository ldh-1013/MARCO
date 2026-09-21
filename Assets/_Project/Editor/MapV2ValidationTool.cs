using System.Collections.Generic;
using System.Text;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Sound;
using Marco.Presentation.Sound;
using UnityEditor;
using UnityEngine;

namespace Marco.EditorTools
{
    /// <summary>
    /// §10.2-1 밸브 배치 조건과 §6.5-3 배수구 조건을 <b>생성된 씬의 실제 콜라이더로</b> 판정하는
    /// <b>판정 정본</b>이다(블록 1-D §3.6).
    ///
    /// <para>
    /// <b>밸브 합격 조건은 하나다 — 동시 감시 지점 0개(§10.2-1 ②).</b>
    /// 경로 거리 30m 하한(구 조건①)은 폐지됐다. 경로는 계속 재서 표에 남기지만
    /// <b>판정에는 넣지 않고</b> 30m 미만을 "주의"로만 표시한다. 폐지 근거 세 가지는
    /// §10.2-1에 있으며, 그 중 핵심은 ②의 임계 <c>14.4 × (1 + 0.5ⁿ)</c>이 n을 아무리
    /// 키워도 14.4m 아래로 내려가지 않는다는 것이다 — <b>②를 통과했다는 사실 자체가
    /// "직선 14.4m 초과"를 증명</b>하므로 별도의 거리 하한이 할 일이 없다.
    /// </para>
    ///
    /// <para>
    /// <c>docs/tools/valve_layout_check.py</c>는 <b>좌표 후보 탐색용 보조</b>로 남는다 —
    /// 도면을 1m 격자로 근사하고, 수면 차폐를 모르고, 층간 바닥을 상수 2로 가정하고,
    /// 계단을 12m 페널티로 대신한다. 빠르게 후보를 훑는 데는 그게 맞지만
    /// <b>합격/불합격은 이 도구만이 판정한다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>파이썬 판과 의도적으로 다른 네 가지.</b>
    /// ① <b>수면 차폐</b>(§5.6 [v0.4])를 센다 — 수중 밸브 B·E가 관련된 쌍이 달라진다.
    /// ② <b>층간 바닥</b>을 상수가 아니라 실제 <c>FloorSlab</c> 태그 히트로 센다 —
    ///    계단 개구부를 지나는 선은 2장이 붙지 않는다.
    /// ③ <b>계단을 실제로 탄다.</b> 지상·2층 두 층 그래프를 계단 간선으로 잇고 Dijkstra로
    ///    최단 경로를 잰다. FIFO BFS는 가중 간선에서 최단거리를 보장하지 않는다.
    /// ④ <b>청취 지점에 2층과 경사로를 포함한다.</b> 계단이 생기면 술래가 실제로 2층에
    ///    올라갈 수 있고, 계단은 개구부라 층간 ×0.25를 우회하는 경로를 만든다.
    /// </para>
    /// </summary>
    public static class MapV2ValidationTool
    {
        // ── §10.2-1 판정 기준 (기획서 수치) ─────────────────────────────

        /// <summary>§5.1 밸브 회전 소음 12m × §5.7 술래 반경 배율 1.2 = 14.4m. 유도값이다.</summary>
        private static float SeekerHearing =>
            Valve.SoundRadiusMeters * SoundPulseResolver.RoleRadiusMultiplier(Core.Role.RoleType.Seeker);

        /// <summary>
        /// 경로 거리 <b>주의 기준</b>(m). §10.2-1 구 조건①의 값이지만 <b>판정이 아니다</b> —
        /// 이 값 미만인 쌍은 표에 "주의"로 남기고 불합격으로 세지 않는다.
        /// </summary>
        private const float PathNoticeMeters = 30f;

        /// <summary>§6.5-3 배수구 ↔ 육상 최단 거리 하한(태그 1.2m + 한 걸음 여유 1.8m).</summary>
        private const float MinLandMeters = 3f;

        /// <summary>배수구 ↔ 같은 구역 밸브 하한(상호작용 프롬프트 분리).</summary>
        private const float MinDrainValveMeters = 3f;

        /// <summary>§6.5-2 배수구 요구 수심.</summary>
        private const float DrainDepthMeters = 3.5f;

        /// <summary>
        /// 격자 한 칸(m).
        ///
        /// <para>
        /// <b>파이썬 판은 1m 격자를 쓰지만 여기서는 0.5m다.</b> 파이썬은 벽을 셀 단위로
        /// 근사하고 통행을 점으로 판정하지만, 이 도구는 두께 0.2m 콜라이더에 몸통 반경
        /// 0.35m를 대고 판정한다. 그 조합에서 1m 격자는 <b>폭 2m 문을 한 칸으로</b>
        /// 줄여 버려 경로가 실제보다 길게 나온다.
        /// </para>
        /// </summary>
        private const float Cell = 0.5f;

        /// <summary>보행 가능 판정에 쓰는 몸통 반경. §4.2 "캐릭터 콜라이더 반경 0.35m".</summary>
        private const float BodyRadius = 0.35f;

        /// <summary>술래 귀 높이 — <c>DiveRules</c>가 소유한 서 있을 때 머리 높이를 쓴다.</summary>
        private static float EarHeight => DiveRules.StandingHeadHeight;

        /// <summary>층 수. 0 = 지상, 1 = 상부 관람층.</summary>
        private const int Levels = 2;

        private static int GridW => Mathf.RoundToInt(MapV2Layout.MapWidth / Cell);
        private static int GridH => Mathf.RoundToInt(MapV2Layout.MapDepth / Cell);

        [MenuItem("Tools/MARCO/맵 v2 배치 검증 (§10.2-1 · §6.5-3)", priority = 41)]
        public static void RunFromMenu()
        {
            bool ok = RunAndReport();
            if (!MarcoSetupPipeline.Automated)
            {
                EditorUtility.DisplayDialog("맵 v2 배치 검증",
                    ok ? "전 항목 합격. Console에서 수치를 확인하세요."
                       : "불합격 항목이 있습니다. Console의 조정 순서를 확인하세요.",
                    "확인");
            }
        }

        public static bool RunAndReport()
        {
            var log = new StringBuilder();
            log.AppendLine("=== 맵 v2 배치 검증 (§10.2-1 · §6.5-3) — 판정 정본 ===");

            if (GameObject.Find(MapV2GeneratorTool.RootName) == null)
            {
                Debug.LogError($"[MapV2:검증] 씬에 '{MapV2GeneratorTool.RootName}' 루트가 없습니다 — " +
                               "Tools/MARCO/맵 v2 생성 을 먼저 실행하세요.");
                return false;
            }

            log.AppendLine($"술래 청취 기준 {SeekerHearing:0.0}m " +
                           $"(= 밸브 소음 {Valve.SoundRadiusMeters:0}m × 술래 배율 " +
                           $"{SoundPulseResolver.RoleRadiusMultiplier(Core.Role.RoleType.Seeker):0.0})");

            bool[][,] walk = BuildWalkGrids();
            log.AppendLine();
            LogStairs(log, walk);

            int bad = CheckValvePairs(walk, log);
            bad += CheckDrains(log);
            bad += CheckDivableDepths(log);

            log.AppendLine();
            if (bad == 0)
            {
                log.AppendLine("결과: **전 항목 합격** — 밸브 10쌍 + 배수구 2개 + 잠수 수심.");
                Debug.Log(log.ToString());
                return true;
            }

            log.AppendLine($"결과: **불합격 {bad}건.**");
            log.AppendLine("조정 순서: ①문 위치 ②칸막이 추가 ③밸브·배수구·계단 좌표 ④구역 배치 (§10.2-1)");
            log.AppendLine($"좌표는 {nameof(MapV2Layout)}에 모여 있다 — 고친 뒤 맵을 재생성하고 다시 검증할 것.");
            Debug.LogError(log.ToString());
            return false;
        }

        // ── 보행 격자 (실제 콜라이더에서 뽑는다) ────────────────────────

        /// <summary>
        /// 층별 보행 가능 격자.
        ///
        /// <para>
        /// <b>"§5.9 재질 태그가 붙은 콜라이더는 벽이 아니라 밟는 면"</b>이라는 규칙으로
        /// 바닥·계단 경사로를 벽과 구분한다. 생성기가 벽의 이동용 콜라이더에는 태그를
        /// 붙이지 않고(차폐용 자식만 `Wall`) 바닥·경사로에는 `Floor*`를 붙이므로,
        /// 이 규칙은 씬에서 이미 참이다. 이렇게 하지 않으면 계단 경사로가 허리 높이에서
        /// 벽으로 잡혀 <b>주변 통행을 가로막는다</b>.
        /// </para>
        ///
        /// <para>
        /// 2층은 허리 높이 검사에 더해 <b>발밑에 바닥이 있는지</b>를 하향 레이로 확인한다 —
        /// 그래야 관람석·물탱크실 바깥의 허공이 걸어갈 수 있는 곳으로 잡히지 않는다.
        /// </para>
        /// </summary>
        private static bool[][,] BuildWalkGrids()
        {
            var grids = new bool[Levels][,];
            for (int level = 0; level < Levels; level++)
                grids[level] = new bool[GridW, GridH];

            int mask = ~LayerMask.GetMask(PhysicsOcclusionProbe.SoundBlockingLayerName, "Water");
            var half = new Vector3(BodyRadius, 0.5f, BodyRadius);
            var hits = new Collider[16];

            for (int x = 0; x < GridW; x++)
            for (int y = 0; y < GridH; y++)
            {
                // ★ 셀 인덱스가 아니라 **월드 좌표**로 물어야 한다. 인덱스를 넘기면
                //   Cell < 1일 때 L자 컷이 맵 안쪽까지 잘라낸다.
                float wx = x * Cell, wz = y * Cell;
                if (MapV2Layout.IsOutsideMap(wx, wz))
                    continue;

                for (int level = 0; level < Levels; level++)
                {
                    float floorY = level == 0 ? 0f : MapV2Layout.UpperFloorY;

                    if (level > 0 && !HasFloorUnder(wx, wz, floorY))
                        continue;

                    int count = Physics.OverlapBoxNonAlloc(
                        new Vector3(wx, floorY + 1f, wz), half, hits, Quaternion.identity,
                        mask, QueryTriggerInteraction.Ignore);

                    bool blocked = false;
                    for (int i = 0; i < count; i++)
                    {
                        if (!IsWalkableSurface(hits[i]))
                        {
                            blocked = true;
                            break;
                        }
                    }

                    grids[level][x, y] = !blocked;
                }
            }

            return grids;
        }

        /// <summary>
        /// §5.9 재질 태그가 붙어 있으면 <b>밟는 면</b>(바닥·계단 경사로)이고, 없으면 벽이다.
        ///
        /// <para>
        /// <c>Classify</c>만으로는 판정할 수 없다 — 태그가 없을 때도 기본값(콘크리트)을
        /// 돌려주기 때문이다. 그래서 콘크리트만 태그를 직접 확인한다.
        /// </para>
        /// </summary>
        private static bool IsWalkableSurface(Collider collider)
        {
            if (collider == null)
                return false;

            if (collider.CompareTag(PhysicsFootstepMaterialProbe.ConcreteTag))
                return true;

            return PhysicsFootstepMaterialProbe.Classify(collider) != FootstepMaterialRules.Default;
        }

        /// <summary>이 지점 <paramref name="floorY"/> 바로 아래에 밟을 바닥이 있는가.</summary>
        private static bool HasFloorUnder(float wx, float wz, float floorY)
        {
            return Physics.Raycast(new Vector3(wx, floorY + 0.5f, wz), Vector3.down,
                       out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore)
                   && IsWalkableSurface(hit.collider);
        }

        // ── 계단 간선 + Dijkstra ────────────────────────────────────────

        private readonly struct Node
        {
            public readonly int X, Y, Level;

            public Node(int x, int y, int level)
            {
                X = x;
                Y = y;
                Level = level;
            }

            /// <summary>딕셔너리 키. 층 → 열 → 행 순으로 자리를 나눠 충돌이 없다.</summary>
            public int Key => (Level * GridW * GridH) + (X * GridH) + Y;
        }

        private static void LogStairs(StringBuilder log, bool[][,] walk)
        {
            log.AppendLine("계단 (§10.1 · 블록 1-D) — §5.6상 개구부이므로 차폐물이 아니다");
            for (int i = 0; i < MapV2Layout.Stairs.Length; i++)
            {
                MapV2Layout.Stair st = MapV2Layout.Stairs[i];
                Node b = ToNode(st.BottomAttach, 0);
                Node u = ToNode(st.UpperAttach, 1);
                bool bOk = InBounds(b) && walk[0][b.X, b.Y];
                bool uOk = InBounds(u) && walk[1][u.X, u.Y];

                log.AppendLine($"  {st.Name}: 수평 {st.Run:0.0}m 경사 {st.SlopeDegrees:0.00}° " +
                               $"경사면 {st.SlopeLength:0.00}m → 간선 {st.GraphCost:0.00}m " +
                               $"| 하단 접속 {(bOk ? "OK" : "막힘")} / 상단 접속 {(uOk ? "OK" : "막힘")}");

                if (!bOk || !uOk)
                {
                    log.AppendLine("      ↑ 접속점이 막혔다 — 이 계단으로는 층을 오갈 수 없고 " +
                                   "관련 밸브 쌍의 경로가 무한이 된다.");
                }

                // CharacterController.slopeLimit 기본 45°. 넘으면 2층이 통째로 접근 불가다.
                if (st.SlopeDegrees >= 45f)
                {
                    log.AppendLine($"      ↑ **경사 {st.SlopeDegrees:0.0}° ≥ 45°** — " +
                                   "CharacterController.slopeLimit을 넘어 등반 불가다.");
                }
            }
        }

        private static Node ToNode(Vector2 p, int level) =>
            new Node(Mathf.RoundToInt(p.x / Cell), Mathf.RoundToInt(p.y / Cell), level);

        private static bool InBounds(in Node n) =>
            n.X >= 0 && n.X < GridW && n.Y >= 0 && n.Y < GridH && n.Level >= 0 && n.Level < Levels;

        private static readonly (int Dx, int Dy, float Cost)[] Steps =
        {
            (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
            (1, 1, 1.414f), (1, -1, 1.414f), (-1, 1, 1.414f), (-1, -1, 1.414f),
        };

        /// <summary>
        /// 층을 넘나드는 최단 경로. <b>Dijkstra여야 한다</b> — 계단 간선이 6~7m라
        /// 균일하지 않고, FIFO 큐로는 최단거리가 보장되지 않는다.
        /// </summary>
        private static Dictionary<int, float> ShortestPaths(bool[][,] walk, in Node source)
        {
            var dist = new Dictionary<int, float>();
            var from = new Dictionary<int, Node>();
            var heap = new MinHeap(GridW * GridH);

            int sk = source.Key;
            dist[sk] = 0f;
            from[sk] = source;
            heap.Push(sk, 0f);

            // 계단 간선: 양방향.
            var stairEdges = new Dictionary<int, List<(Node To, float Cost)>>();
            for (int i = 0; i < MapV2Layout.Stairs.Length; i++)
            {
                MapV2Layout.Stair st = MapV2Layout.Stairs[i];
                Node a = ToNode(st.BottomAttach, 0), b = ToNode(st.UpperAttach, 1);
                if (!InBounds(a) || !InBounds(b) || !walk[0][a.X, a.Y] || !walk[1][b.X, b.Y])
                    continue;

                Add(a, b, st.GraphCost);
                Add(b, a, st.GraphCost);
            }

            void Add(Node k, Node v, float cost)
            {
                int key = k.Key;
                if (!stairEdges.TryGetValue(key, out List<(Node, float)> list))
                    stairEdges[key] = list = new List<(Node, float)>();

                list.Add((v, cost));
                from[key] = k;
            }

            while (heap.TryPop(out int key, out float d))
            {
                if (d > dist[key] + 1e-4f)
                    continue;

                Node cur = from[key];

                for (int i = 0; i < Steps.Length; i++)
                {
                    var next = new Node(cur.X + Steps[i].Dx, cur.Y + Steps[i].Dy, cur.Level);
                    if (!InBounds(next) || !walk[next.Level][next.X, next.Y])
                        continue;

                    Relax(next, d + Steps[i].Cost * Cell);
                }

                if (stairEdges.TryGetValue(key, out List<(Node To, float Cost)> edges))
                {
                    for (int i = 0; i < edges.Count; i++)
                        Relax(edges[i].To, d + edges[i].Cost);
                }
            }

            void Relax(in Node next, float nd)
            {
                int nk = next.Key;
                if (dist.TryGetValue(nk, out float old) && old <= nd + 1e-4f)
                    return;

                dist[nk] = nd;
                from[nk] = next;
                heap.Push(nk, nd);
            }

            return dist;
        }

        /// <summary>최소 이진 힙. Unity의 netstandard2.1에는 <c>PriorityQueue</c>가 없다.</summary>
        private sealed class MinHeap
        {
            private int[] _keys;
            private float[] _costs;
            private int _count;

            public MinHeap(int capacity)
            {
                _keys = new int[Mathf.Max(16, capacity)];
                _costs = new float[_keys.Length];
            }

            public void Push(int key, float cost)
            {
                if (_count == _keys.Length)
                {
                    System.Array.Resize(ref _keys, _count * 2);
                    System.Array.Resize(ref _costs, _count * 2);
                }

                int i = _count++;
                _keys[i] = key;
                _costs[i] = cost;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_costs[parent] <= _costs[i])
                        break;

                    Swap(parent, i);
                    i = parent;
                }
            }

            public bool TryPop(out int key, out float cost)
            {
                if (_count == 0)
                {
                    key = 0;
                    cost = 0f;
                    return false;
                }

                key = _keys[0];
                cost = _costs[0];
                _count--;
                if (_count > 0)
                {
                    _keys[0] = _keys[_count];
                    _costs[0] = _costs[_count];
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, best = i;
                        if (l < _count && _costs[l] < _costs[best]) best = l;
                        if (r < _count && _costs[r] < _costs[best]) best = r;
                        if (best == i)
                            break;

                        Swap(best, i);
                        i = best;
                    }
                }

                return true;
            }

            private void Swap(int a, int b)
            {
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
                (_costs[a], _costs[b]) = (_costs[b], _costs[a]);
            }
        }

        // ── ② 동시 감시(판정) + 경로 거리(기록) ─────────────────────────

        private static int CheckValvePairs(bool[][,] walk, StringBuilder log)
        {
            var probe = new PhysicsOcclusionProbe();
            MapV2Layout.ValvePoint[] valves = MapV2Layout.Valves;
            List<(Vector3 Pos, string Level)> ears = BuildEarPositions(walk);

            log.AppendLine();
            log.AppendLine($"청취 지점 {ears.Count}개 — " +
                           $"지상 {Count(ears, "지상")} / 2층 {Count(ears, "2층")} / 경사로 {Count(ears, "경사로")}");

            var paths = new Dictionary<string, Dictionary<int, float>>();
            for (int i = 0; i < valves.Length; i++)
            {
                Node n = ToNode(valves[i].Position, valves[i].UpperFloor ? 1 : 0);
                if (!InBounds(n) || !walk[n.Level][n.X, n.Y])
                {
                    log.AppendLine($"[경고] 밸브 {valves[i].Id} {valves[i].Position} " +
                                   $"(레벨 {n.Level})이 벽 또는 맵 바깥이다 — 경로가 무한이 된다.");
                }

                paths[valves[i].Id] = ShortestPaths(walk, n);
            }

            log.AppendLine();
            log.AppendLine($"{"쌍",-7}{"직선",7}{"경로",8}{"동시감시",9}{"(지상",7}{"2층",5}{"경사로)",8}  판정");
            log.AppendLine(new string('-', 62));

            int bad = 0;
            int notices = 0;
            for (int a = 0; a < valves.Length; a++)
            for (int b = a + 1; b < valves.Length; b++)
            {
                MapV2Layout.ValvePoint va = valves[a], vb = valves[b];
                string pair = $"{va.Id}↔{vb.Id}";

                float straight = Vector2.Distance(va.Position, vb.Position);
                Node target = ToNode(vb.Position, vb.UpperFloor ? 1 : 0);
                if (!paths[va.Id].TryGetValue(target.Key, out float path))
                    path = float.PositiveInfinity;

                (int g, int u, int r, string sample) spots = CoWatchSpots(ears, probe, va, vb);
                int total = spots.g + spots.u + spots.r;

                // **합격/불합격은 동시 감시 하나로 정한다**(§10.2-1). 경로는 기록만 한다.
                bool watchOk = total == 0;
                if (!watchOk)
                    bad++;

                bool pathNotice = path < PathNoticeMeters;
                if (pathNotice)
                    notices++;

                string verdict = watchOk ? (pathNotice ? "OK ⚠" : "OK") : "FAIL";
                log.AppendLine($"{pair,-7}{straight,7:0.0}{path,8:0.0}{total,9}{spots.g,7}{spots.u,5}{spots.r,8}" +
                               $"  {verdict}");

                if (float.IsInfinity(path))
                {
                    // 경로 무한은 판정이 아니지만 **거의 항상 맵 결함**이다 — 밸브에 갈 수 없다.
                    log.AppendLine("        ⚠ 경로가 없다 — 두 밸브가 서로 닿지 않는다. " +
                                   "문이 빠졌거나 계단 접속점이 막혔다(판정 아님, 그래도 확인할 것).");
                }
                else if (pathNotice)
                {
                    log.AppendLine($"        ⚠ 주의: 경로 {path:0.0}m < {PathNoticeMeters:0}m — " +
                                   "판정 아님(구 조건① 폐지). §20.3 관찰 항목 대상이다.");
                }

                if (!watchOk)
                    log.AppendLine($"        ↑ 동시 감시 {total}개 (§10.2-1 ②) — 예: {spots.sample}");
            }

            log.AppendLine();
            log.AppendLine($"밸브 불합격 {bad}쌍 / 총 10쌍 " +
                           $"(판정 기준: 동시 감시 0개 — §10.2-1 ②)");
            if (notices > 0)
            {
                log.AppendLine($"경로 30m 미만 \"주의\" {notices}쌍 — **불합격이 아니다.** " +
                               "구 조건①은 폐지됐고(§10.2-1 폐지 근거 3가지) " +
                               "이 쌍들은 §20.3 플레이테스트 관찰 항목이다.");
            }

            return bad;
        }

        private static int Count(List<(Vector3 Pos, string Level)> ears, string level)
        {
            int n = 0;
            for (int i = 0; i < ears.Count; i++)
                if (ears[i].Level == level)
                    n++;

            return n;
        }

        /// <summary>
        /// 술래가 설 수 있는 모든 지점. <b>지상 + 2층 + 경사로 위</b>다.
        ///
        /// <para>
        /// 경사로 위가 가장 위험하다 — 중간 높이라 수중 밸브와의 <b>수직 차이가 줄어든다</b>.
        /// 밸브 B는 Y −3.0이고 2층 귀는 Y 5.12라 수직 차이만 8.12m로 이미 청취 임계
        /// 7.2m(수면 1장)를 넘지만, 경사로 중간에서는 그 여유가 사라진다.
        /// </para>
        /// </summary>
        private static List<(Vector3 Pos, string Level)> BuildEarPositions(bool[][,] walk)
        {
            var ears = new List<(Vector3, string)>();

            for (int level = 0; level < Levels; level++)
            {
                float floorY = level == 0 ? 0f : MapV2Layout.UpperFloorY;
                string label = level == 0 ? "지상" : "2층";

                for (int x = 0; x < GridW; x++)
                for (int y = 0; y < GridH; y++)
                {
                    if (!walk[level][x, y])
                        continue;

                    ears.Add((new Vector3(x * Cell, floorY + EarHeight, y * Cell), label));
                }
            }

            for (int i = 0; i < MapV2Layout.Stairs.Length; i++)
            {
                MapV2Layout.Stair st = MapV2Layout.Stairs[i];
                int samples = Mathf.Max(2, Mathf.CeilToInt(st.Run / Cell));
                for (int s = 0; s <= samples; s++)
                {
                    float t = (float)s / samples;
                    Vector2 xz = Vector2.Lerp(st.Bottom, st.Top, t);
                    ears.Add((new Vector3(xz.x, MapV2Layout.UpperFloorY * t + EarHeight, xz.y), "경사로"));
                }
            }

            return ears;
        }

        /// <summary>
        /// 두 밸브를 동시에 감시 가능한 지점 수. <b>0이어야 합격</b>이다(§10.2-1 ②).
        /// 임계는 <c>14.4 × 0.5^(사이 차폐 가중치)</c>이며, 가중치는 씬의 실제
        /// <c>SoundBlocking</c> 콜라이더를 읽어 얻는다 — 층간 2, 수면 1이 자동 반영된다.
        /// </summary>
        private static (int, int, int, string) CoWatchSpots(List<(Vector3 Pos, string Level)> ears,
            PhysicsOcclusionProbe probe, in MapV2Layout.ValvePoint a, in MapV2Layout.ValvePoint b)
        {
            Vector3 pa = WorldOf(a), pb = WorldOf(b);
            float reach = SeekerHearing;
            int ground = 0, upper = 0, ramp = 0;
            var sample = new StringBuilder();

            for (int i = 0; i < ears.Count; i++)
            {
                Vector3 ear = ears[i].Pos;

                // 차폐 0이어도 못 듣는 거리면 즉시 탈락(§5.6 1차 컷과 같은 순서).
                if (Vector3.Distance(ear, pa) > reach || Vector3.Distance(ear, pb) > reach)
                    continue;

                if (!Audible(probe, ear, pa, reach) || !Audible(probe, ear, pb, reach))
                    continue;

                switch (ears[i].Level)
                {
                    case "지상": ground++; break;
                    case "2층": upper++; break;
                    default: ramp++; break;
                }

                if (sample.Length < 60)
                {
                    if (sample.Length > 0)
                        sample.Append(", ");

                    sample.Append($"{ears[i].Level}({ear.x:0.0},{ear.z:0.0})");
                }
            }

            return (ground, upper, ramp, sample.ToString());
        }

        /// <summary>§5.6 그대로 — 역할 배율 적용 후 반경에 벽 수만큼 ×0.5를 곱해 비교한다.</summary>
        private static bool Audible(PhysicsOcclusionProbe probe, Vector3 ear, Vector3 valve, float reach)
        {
            OcclusionResult occlusion = probe.Probe(valve, ear);
            if (occlusion.HasHardBlocker)
                return false;

            return Vector3.Distance(ear, valve) <= reach * Mathf.Pow(0.5f, occlusion.WallCount);
        }

        // ── ③④ 배수구 (§6.5-3) ─────────────────────────────────────────

        /// <summary>
        /// §6.5-3 수평 거리 제약. <b>동시 감시(②)는 적용하지 않는다</b> —
        /// 최후 생존자 페이즈에서 밸브는 얼어붙어 회전음이 발생하지 않으므로 감시할 대상이 없다.
        /// </summary>
        private static int CheckDrains(StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine($"{"배수구",-16}{"육상거리",10}{"밸브거리",10}{"수심",8}  판정");
            log.AppendLine(new string('-', 58));

            int bad = 0;
            for (int i = 0; i < MapV2Layout.Drains.Length; i++)
            {
                MapV2Layout.DrainPoint d = MapV2Layout.Drains[i];
                MapV2Layout.WaterArea water = WaterOf(d.Water);

                float land = LandDistance(d.Position, water.Area);
                float toValve = Vector2.Distance(d.Position, ValveOf(d.NearValve).Position);
                float depth = DepthAt(d.Position, water);

                bool ok = land >= MinLandMeters && toValve >= MinDrainValveMeters
                          && depth >= DrainDepthMeters;
                if (!ok)
                    bad++;

                float cap = Mathf.Min(water.Area.width, water.Area.height) * 0.5f;
                string note = Mathf.Abs(land - cap) < 1e-4f ? "  ← 수면 기하학적 상한" : "";

                log.AppendLine($"{d.Id + " " + d.Water,-16}{land,10:0.00}{toValve,10:0.00}{depth,8:0.0}" +
                               $"  {(ok ? "OK" : "FAIL")}{note}");

                if (land < MinLandMeters)
                    log.AppendLine($"        ↑ 육상 {land:0.00}m < {MinLandMeters:0.0}m — " +
                                   "술래가 마른 바닥에서 부상 지점을 태그한다(§6.5-3)");
                if (toValve < MinDrainValveMeters)
                    log.AppendLine($"        ↑ 밸브 {toValve:0.00}m < {MinDrainValveMeters:0.0}m — " +
                                   "상호작용 프롬프트가 겹친다");
                if (depth < DrainDepthMeters)
                    log.AppendLine($"        ↑ 수심 {depth:0.0}m < {DrainDepthMeters:0.0}m — " +
                                   "§6.5-2 위반. 유아풀이면 침강부(sump)가 빠졌다");
            }

            log.AppendLine();
            log.AppendLine($"배수구 불합격 {bad}개 / 총 {MapV2Layout.Drains.Length}개");
            if (bad > 0)
                log.AppendLine("→ 조정: ①배수구 좌표 ②수면 영역 확대 (§6.5-3, §10.1)");

            return bad;
        }

        /// <summary>
        /// 잠수가 실제로 가능한 수심인지. <b>파이썬 판에 없는 검사</b>이며,
        /// 블록 1-A③에서 드러난 제약이다 — 수심이 <c>DiveRules.MinDivableDepth</c> 이하면
        /// 잠수 키를 눌러도 머리가 잠기지 않아 수중 밸브·배수구가 <b>조용히</b> 동작하지 않는다.
        /// </summary>
        private static int CheckDivableDepths(StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine($"잠수 가능 수심 검사 (> {DiveRules.MinDivableDepth:0.00}m, " +
                           $"머리 {DiveRules.StandingHeadHeight:0.00} → 잠수 {DiveRules.SubmergedHeadHeight:0.00})");
            log.AppendLine(new string('-', 58));

            int bad = 0;
            for (int i = 0; i < MapV2Layout.Valves.Length; i++)
            {
                MapV2Layout.ValvePoint v = MapV2Layout.Valves[i];
                if (!v.Underwater)
                    continue;

                float depth = DepthAtAnyWater(v.Position);
                bool ok = MapV2GeneratorTool.IsDivable(depth);
                if (!ok)
                    bad++;

                log.AppendLine($"  수중 밸브 {v.Id} ({v.Zone}) 수심 {depth:0.00}m  {(ok ? "OK" : "FAIL")}");
                if (!ok)
                {
                    log.AppendLine("        ↑ 이 수심으로는 잠수해도 머리가 잠기지 않는다 — " +
                                   "§6.1 진입·부상 배분이 성립하지 않는다(GAP-75)");
                }
            }

            for (int i = 0; i < MapV2Layout.Drains.Length; i++)
            {
                MapV2Layout.DrainPoint d = MapV2Layout.Drains[i];
                float depth = DepthAtAnyWater(d.Position);
                bool ok = MapV2GeneratorTool.IsDivable(depth);
                if (!ok)
                    bad++;

                log.AppendLine($"  배수구 {d.Id} 수심 {depth:0.00}m  {(ok ? "OK" : "FAIL")}");
            }

            log.AppendLine($"잠수 수심 불합격 {bad}건");
            return bad;
        }

        // ── 좌표 헬퍼 ────────────────────────────────────────────────────

        /// <summary>밸브의 월드 좌표. 수중·2층 밸브는 높이가 다르다 — 그게 차폐에 그대로 반영된다.</summary>
        private static Vector3 WorldOf(in MapV2Layout.ValvePoint v)
        {
            float y = 1f;
            if (v.UpperFloor)
                y = MapV2Layout.UpperFloorY + 1f;
            else if (v.Underwater)
                y = -DepthAtAnyWater(v.Position) + 0.5f;

            return new Vector3(v.Position.x, y, v.Position.y);
        }

        private static MapV2Layout.WaterArea WaterOf(string name)
        {
            for (int i = 0; i < MapV2Layout.Waters.Length; i++)
                if (MapV2Layout.Waters[i].Name == name)
                    return MapV2Layout.Waters[i];

            return default;
        }

        private static MapV2Layout.ValvePoint ValveOf(string id)
        {
            for (int i = 0; i < MapV2Layout.Valves.Length; i++)
                if (MapV2Layout.Valves[i].Id == id)
                    return MapV2Layout.Valves[i];

            return default;
        }

        private static float DepthAt(Vector2 p, in MapV2Layout.WaterArea w)
        {
            if (w.SumpRadius > 0f && Vector2.Distance(p, w.SumpCenter) <= w.SumpRadius)
                return Mathf.Max(w.Depth, w.SumpDepth);

            return w.Depth;
        }

        private static float DepthAtAnyWater(Vector2 p)
        {
            for (int i = 0; i < MapV2Layout.Waters.Length; i++)
            {
                if (MapV2Layout.Waters[i].Area.Contains(p))
                    return DepthAt(p, MapV2Layout.Waters[i]);
            }

            return 0f;
        }

        /// <summary>배수구에서 물 밖까지의 최단 거리 — 수면 사각형 경계까지의 거리.</summary>
        private static float LandDistance(Vector2 p, Rect water)
        {
            return Mathf.Min(
                Mathf.Min(p.x - water.xMin, water.xMax - p.x),
                Mathf.Min(p.y - water.yMin, water.yMax - p.y));
        }
    }
}
