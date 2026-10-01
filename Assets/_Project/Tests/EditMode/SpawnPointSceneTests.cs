using System.Collections.Generic;
using System.Text;
using Marco.Core.GameFlow;
using Marco.Core.Objectives;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Objectives;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스폰 지점 씬 검증 — 러너 스폰 슬롯 전부(10-01부터 로비 남쪽 절반 4열 × 2행 격자, <c>MapSpawnSlots</c>) + 술래 격리 지점이
    /// <b>실제 Game 씬 지오메트리 위</b>에 안전하게 놓이는가, 그리고 출구 판정 반경 + 3m 밖인가.
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
                List<SceneGeometry.Aabb> solids = SceneGeometry.CollectSolidBoxes(game);
                Assert.Greater(solids.Count, 0, "Game 씬에 고체 BoxCollider가 없다");

                SpawnAnchor anchor = SceneGeometry.FindInScene<SpawnAnchor>(game);
                SeekerIsolationAnchor isolation = SceneGeometry.FindInScene<SeekerIsolationAnchor>(game);
                Assert.IsNotNull(anchor, "Game 씬에 SpawnAnchor가 없다");
                Assert.IsNotNull(isolation, "Game 씬에 SeekerIsolationAnchor가 없다");

                // PawnPhaseTeleporter와 같은 함수 — 러너 슬롯 = MapSpawnSlots(앵커, PlayerId % slots), 술래 = 격리 앵커.
                var anchorPose = new SpawnPose(anchor.transform.position, anchor.transform.rotation);
                var points = new List<(string Name, Vector3 Feet)>();
                for (int i = 0; i < config.Slots; i++)
                {
                    Vector3 p = MapSpawnSlots.GetPose(anchorPose, i, config.Slots).Position;
                    points.Add(($"러너 슬롯 {i}/{config.Slots}", p + Vector3.up * config.VerticalOffset));
                }

                points.Add(("술래 격리 지점", isolation.transform.position + Vector3.up * config.VerticalOffset));

                var failures = new List<string>();
                for (int i = 0; i < points.Count; i++)
                {
                    (string name, Vector3 feet) = points[i];
                    string where = $"{name} ({feet.x:0.00}, {feet.y:0.00}, {feet.z:0.00})";

                    // (a) 바닥 — 캡슐 반지름만큼 안쪽까지 XZ가 덮이고, 윗면이 발에서 0.5m 이내.
                    if (!SceneGeometry.HasFloorUnder(solids, feet, capsuleRadius, out string floorNote))
                        failures.Add($"(a) {where}: 발밑 바닥 없음 — 반지름 {capsuleRadius}m 안쪽까지 덮고 높이 차 ≤ {SceneGeometry.MaxFloorGap}m인 바닥이 없다. {floorNote}");

                    // (b) 벽 — 캡슐(발 위 바닥 윗면부터 머리까지)이 고체와 겹치지 않는다.
                    foreach (string hit in SceneGeometry.CapsuleOverlaps(solids, feet, capsuleRadius, capsuleBottom, capsuleTop))
                        failures.Add($"(b) {where}: 캡슐이 {hit}과 겹친다");

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

        /// <summary>
        /// 10-01 직접 탈출 규칙 — 어느 도망자 스폰 슬롯도 어느 출구에서든 (판정 반경 + 3m) 이상 떨어져 있다.
        /// 출구 반경 안에서 시작하면 게이트가 열리는 순간 걷지 않고 탈출이 확정된다(출구 트리거 0.5초 재시도) —
        /// 슬롯 2 (7, 0.1, 39)가 정문 1.34m였다. 거리는 탈출 판정과 같은 3D 거리(<see cref="EscapeRules.IsWithinExit"/>)로 잰다.
        /// 출구 · 앵커는 실제 Game 씬, 슬롯 수 · 높이 오프셋은 Lobby 씬 직렬화 값, 슬롯 계산은 PawnPhaseTeleporter와 같은 함수.
        /// 술래 격리 지점까지의 거리는 표에만 남긴다(술래는 탈출할 수 없다 — GAP-11).
        /// </summary>
        [Test]
        public void EveryRunnerSpawnSlot_IsAtLeastExitRadiusPlusThreeFromEveryExit()
        {
            const float Margin = 3f;
            float required = EscapeRules.ExitRadiusMeters + Margin;
            SpawnConfig config = ReadLobbySpawnConfig();

            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                SpawnAnchor anchor = SceneGeometry.FindInScene<SpawnAnchor>(game);
                SeekerIsolationAnchor isolation = SceneGeometry.FindInScene<SeekerIsolationAnchor>(game);
                List<EscapePointTrigger> exits = SceneGeometry.FindAllActive<EscapePointTrigger>(game);
                Assert.IsNotNull(anchor, "Game 씬에 SpawnAnchor가 없다");
                Assert.GreaterOrEqual(exits.Count, 2, "Game 씬 출구(EscapePointTrigger)가 2개 미만이다");

                var anchorPose = new SpawnPose(anchor.transform.position, anchor.transform.rotation);
                var points = new List<(string Name, Vector3 Feet, bool Runner)>();
                for (int i = 0; i < config.Slots; i++)
                    points.Add(($"러너 슬롯 {i}", MapSpawnSlots.GetPose(anchorPose, i, config.Slots).Position + Vector3.up * config.VerticalOffset, true));
                if (isolation != null)
                    points.Add(("술래 격리 지점", isolation.transform.position + Vector3.up * config.VerticalOffset, false));

                var table = new StringBuilder();
                table.AppendLine($"기준: 출구 판정 반경 {EscapeRules.ExitRadiusMeters}m + 여유 {Margin}m = {required}m (3D 거리, 괄호는 수평 거리)");
                table.Append("지점 (발 좌표)");
                foreach (EscapePointTrigger exit in exits)
                    table.Append($" | {exit.name} {exit.transform.position}");
                table.AppendLine();

                var failures = new List<string>();
                foreach ((string name, Vector3 feet, bool runner) in points)
                {
                    table.Append($"{name} ({feet.x:0.00}, {feet.y:0.00}, {feet.z:0.00})");
                    foreach (EscapePointTrigger exit in exits)
                    {
                        Vector3 e = exit.transform.position;
                        float d = Vector3.Distance(feet, e);
                        float h = Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(e.x, e.z));
                        string flag = d <= EscapeRules.ExitRadiusMeters ? " ★반경 안" : d < required ? " ▲여유 부족" : string.Empty;
                        table.Append($" | {d:0.00}m ({h:0.00}){flag}");
                        if (runner && d < required)
                            failures.Add($"{name} ({feet.x:0.00}, {feet.z:0.00}) → {exit.name}: {d:0.00}m < {required}m");
                    }

                    table.AppendLine();
                }

                TestContext.WriteLine(table.ToString());
                Assert.IsEmpty(failures, "출구에 너무 가까운 도망자 스폰 슬롯 — 게이트가 열리면 걷지 않고 탈출한다:\n  " +
                                         string.Join("\n  ", failures) + "\n" + table);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }

        // ── 도우미 ────────────────────────────────────────────────────

        private static SpawnConfig ReadLobbySpawnConfig()
        {
            Scene lobby = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                PawnPhaseTeleporter teleporter = SceneGeometry.FindInScene<PawnPhaseTeleporter>(lobby);
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
    }
}
