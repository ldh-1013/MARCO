using System.Collections.Generic;
using System.Text;
using Marco.Core.GameFlow;
using Marco.Presentation.GameFlow;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스폰 지점 씬 검증 — 러너 스폰 링 슬롯 전부 + 술래 격리 지점이 <b>실제 Game 씬 지오메트리 위</b>에 안전하게 놓이는가.
    ///
    /// <para>
    /// <b>왜 필요한가(09-28 회귀)</b>: 스폰 링 반지름(4m, Lobby 씬 <c>PawnPhaseTeleporter</c> 직렬화)과 앵커 위치(로비 중심,
    /// Game 씬 — 937ebe8에서 이동)가 따로 놀아 슬롯 2가 로비 북벽 = 맵 북쪽 끝(z 40)에 걸렸고, 그 슬롯의 러너가 맵 밖으로
    /// 떨어졌다. 두 값을 각각 고쳐도 서로를 확인하는 곳이 없었다 — 이 테스트가 그 자리다.
    /// </para>
    ///
    /// <para>
    /// 물리 시뮬레이션 없이 판정한다 — Game 씬 콜라이더는 전부 BoxCollider라 크기 · 트랜스폼으로 경계를 정확히 계산한다.
    /// 두 씬은 <b>프리뷰로</b> 연다(에디터에 열린 씬은 건드리지 않는다). 반지름 · 슬롯 수 · 높이 오프셋은 Lobby 씬의 직렬화 값,
    /// 캡슐 치수는 Player 프리팹의 CharacterController 값을 읽는다 — 코드 기본값이 아니라 실제로 쓰이는 값이다.
    /// </para>
    /// </summary>
    public class SpawnPointSceneTests
    {
        private const string GameScenePath = "Assets/Scenes/Game.unity";
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        /// <summary>발밑 바닥 윗면과 발 높이의 허용 차(요청 기준 ≤ 0.5m).</summary>
        private const float MaxFloorGap = 0.5f;

        /// <summary>바닥에 발이 닿는 자리(윗면)는 벽 겹침 판정에서 뺀다 — 이 높이 아래의 콜라이더는 바닥이다.</summary>
        private const float FloorClearance = 0.02f;

        private readonly struct Aabb
        {
            public readonly string Path;
            public readonly Vector3 Min, Max;

            public Aabb(string path, Vector3 min, Vector3 max)
            {
                Path = path;
                Min = min;
                Max = max;
            }
        }

        private struct SpawnConfig
        {
            public float Radius;
            public int Slots;
            public float VerticalOffset;
        }

        [Test]
        public void EverySpawnPoint_StandsOnFloor_ClearOfWallsAndEachOther()
        {
            SpawnConfig config = ReadLobbySpawnConfig();
            var cc = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<CharacterController>();
            Assert.IsNotNull(cc, "Player 프리팹에 CharacterController가 없다");
            float capsuleRadius = cc.radius;
            float capsuleBottom = cc.center.y - cc.height * 0.5f; // 발 기준 캡슐 바닥(0)
            float capsuleTop = cc.center.y + cc.height * 0.5f;

            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                List<Aabb> solids = CollectSolidBoxes(game);
                Assert.Greater(solids.Count, 0, "Game 씬에 고체 BoxCollider가 없다");

                SpawnAnchor anchor = FindInScene<SpawnAnchor>(game);
                SeekerIsolationAnchor isolation = FindInScene<SeekerIsolationAnchor>(game);
                Assert.IsNotNull(anchor, "Game 씬에 SpawnAnchor가 없다");
                Assert.IsNotNull(isolation, "Game 씬에 SeekerIsolationAnchor가 없다");

                // PawnPhaseTeleporter와 같은 계산 — 러너 슬롯 = SpawnRing(앵커, PlayerId % slots), 술래 = 격리 앵커.
                var anchorPose = new SpawnPose(anchor.transform.position, anchor.transform.rotation);
                var points = new List<(string Name, Vector3 Feet)>();
                for (int i = 0; i < config.Slots; i++)
                {
                    Vector3 p = SpawnRing.GetPose(anchorPose, i, config.Radius, config.Slots).Position;
                    points.Add(($"러너 슬롯 {i}/{config.Slots}", p + Vector3.up * config.VerticalOffset));
                }

                points.Add(("술래 격리 지점", isolation.transform.position + Vector3.up * config.VerticalOffset));

                var failures = new List<string>();
                for (int i = 0; i < points.Count; i++)
                {
                    (string name, Vector3 feet) = points[i];
                    string where = $"{name} ({feet.x:0.00}, {feet.y:0.00}, {feet.z:0.00})";

                    // (a) 바닥 — 캡슐 반지름만큼 안쪽까지 XZ가 덮이고, 윗면이 발에서 0.5m 이내.
                    if (!HasFloorUnder(solids, feet, capsuleRadius, out string floorNote))
                        failures.Add($"(a) {where}: 발밑 바닥 없음 — 반지름 {capsuleRadius}m 안쪽까지 덮고 높이 차 ≤ {MaxFloorGap}m인 바닥이 없다. {floorNote}");

                    // (b) 벽 — 캡슐(발 위 바닥 윗면부터 머리까지)이 고체와 겹치지 않는다.
                    foreach (Aabb box in solids)
                    {
                        if (box.Max.y <= feet.y + FloorClearance)
                            continue; // 발밑 바닥
                        if (box.Min.y >= feet.y + capsuleTop || box.Max.y <= feet.y + capsuleBottom)
                            continue; // 캡슐 높이 밖
                        float d = DistanceXZ(box, feet);
                        if (d < capsuleRadius)
                            failures.Add($"(b) {where}: 캡슐이 '{box.Path}'과 겹친다 — 수평 거리 {d:0.00}m < 반지름 {capsuleRadius}m " +
                                         $"(x {box.Min.x:0.##}~{box.Max.x:0.##}, z {box.Min.z:0.##}~{box.Max.z:0.##}, y {box.Min.y:0.##}~{box.Max.y:0.##})");
                    }

                    // (c) 슬롯끼리 — 캡슐이 서로 겹치지 않는다(술래 격리 지점 포함).
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        Vector3 other = points[j].Feet;
                        float d = Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(other.x, other.z));
                        if (d < capsuleRadius * 2f && Mathf.Abs(feet.y - other.y) < cc.height)
                            failures.Add($"(c) {where} ↔ {points[j].Name}: 캡슐이 겹친다 — 거리 {d:0.00}m < {capsuleRadius * 2f:0.00}m");
                    }
                }

                var report = new StringBuilder();
                report.AppendLine($"스폰 반지름 {config.Radius}m · 슬롯 {config.Slots} · 오프셋 {config.VerticalOffset}m · " +
                                  $"앵커 {anchor.transform.position} · 캡슐 반지름 {capsuleRadius} 높이 {cc.height} — 문제 {failures.Count}건:");
                foreach (string f in failures)
                    report.AppendLine("  " + f);

                Assert.IsEmpty(failures, report.ToString());
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }

        /// <summary>
        /// 코드 기본값(<see cref="SpawnRing.DefaultRadiusMeters"/>)과 Lobby 씬 직렬화 값이 같은가 — 두 값이 따로 놀아 생긴 회귀라,
        /// 한쪽만 바꾸면 여기서 드러난다.
        /// </summary>
        [Test]
        public void LobbySpawnRadius_MatchesCodeDefault()
        {
            SpawnConfig config = ReadLobbySpawnConfig();
            Assert.AreEqual(SpawnRing.DefaultRadiusMeters, config.Radius, 1e-4f,
                "Lobby.unity의 PawnPhaseTeleporter._spawnRadius와 SpawnRing.DefaultRadiusMeters가 다르다");
            Assert.AreEqual(SpawnRing.DefaultSlots, config.Slots, "슬롯 수가 코드 기본값과 다르다");
        }

        // ── 도우미 ────────────────────────────────────────────────────

        private static SpawnConfig ReadLobbySpawnConfig()
        {
            Scene lobby = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                PawnPhaseTeleporter teleporter = FindInScene<PawnPhaseTeleporter>(lobby);
                Assert.IsNotNull(teleporter, "Lobby 씬에 PawnPhaseTeleporter가 없다");
                var so = new SerializedObject(teleporter);
                return new SpawnConfig
                {
                    Radius = so.FindProperty("_spawnRadius").floatValue,
                    Slots = so.FindProperty("_spawnSlots").intValue,
                    VerticalOffset = so.FindProperty("_verticalOffset").floatValue,
                };
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(lobby);
            }
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(includeInactive: false);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>활성 · 고체(트리거 아님) BoxCollider의 월드 AABB. 캐릭터 컨트롤러는 트리거와 부딪히지 않는다.</summary>
        private static List<Aabb> CollectSolidBoxes(Scene scene)
        {
            var result = new List<Aabb>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(includeInactive: false))
                {
                    if (!box.enabled || box.isTrigger)
                        continue;

                    Matrix4x4 m = box.transform.localToWorldMatrix;
                    Vector3 h = box.size * 0.5f;
                    Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 local = box.center + new Vector3(
                            (i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                        Vector3 p = m.MultiplyPoint3x4(local);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }

                    result.Add(new Aabb(PathOf(box.transform), min, max));
                }
            }

            return result;
        }

        private static bool HasFloorUnder(List<Aabb> solids, Vector3 feet, float inset, out string note)
        {
            string nearest = "바닥 후보 없음";
            foreach (Aabb box in solids)
            {
                float gap = feet.y - box.Max.y;
                if (gap < -FloorClearance || gap > MaxFloorGap)
                    continue; // 발보다 위(벽 등)이거나 너무 아래

                bool inside = feet.x >= box.Min.x + inset && feet.x <= box.Max.x - inset
                           && feet.z >= box.Min.z + inset && feet.z <= box.Max.z - inset;
                if (inside)
                {
                    note = null;
                    return true;
                }

                if (feet.x >= box.Min.x && feet.x <= box.Max.x && feet.z >= box.Min.z && feet.z <= box.Max.z)
                    nearest = $"'{box.Path}'(x {box.Min.x:0.##}~{box.Max.x:0.##}, z {box.Min.z:0.##}~{box.Max.z:0.##}) 가장자리에 걸림";
            }

            note = nearest;
            return false;
        }

        private static float DistanceXZ(in Aabb box, Vector3 p)
        {
            float dx = Mathf.Max(box.Min.x - p.x, 0f, p.x - box.Max.x);
            float dz = Mathf.Max(box.Min.z - p.z, 0f, p.z - box.Max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string PathOf(Transform t)
        {
            string s = t.name;
            for (int i = 0; i < 2 && t.parent != null; i++)
            {
                t = t.parent;
                s = t.name + "/" + s;
            }

            return s;
        }
    }
}
