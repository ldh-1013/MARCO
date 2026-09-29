using System;
using System.Collections.Generic;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Water;
using Marco.Presentation.Objectives;
using Marco.Presentation.Water;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 승리 경로 씬 검증(1-5) — <c>SpawnPointSceneTests</c>와 같은 방식(프리뷰 씬 · BoxCollider 경계 · 물리 시뮬레이션 없음).
    /// 활성 출구가 정확히 2개이고, 각 출구 · 밸브마다 <b>실제로 서서 판정에 닿을 수 있는 지점</b>이 있는가.
    /// 찾은 지점은 테스트 출력(<c>[QA-POINT]</c>)으로 남긴다 — QA 순간이동(F4)이 이 좌표를 쓴다.
    /// </summary>
    public class ExitValveSceneTests
    {
        private const string GameScenePath = "Assets/Scenes/Game.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const float GridStep = 0.1f;

        /// <summary>이보다 낮은 천장 아래는 설 자리가 아니다(<see cref="SceneGeometry.HasLowCeiling"/>).</summary>
        private const float EnclosedCeiling = 3f;

        private struct Capsule
        {
            public float Radius, Bottom, Top;
        }

        private static Capsule ReadCapsule()
        {
            var cc = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<CharacterController>();
            Assert.IsNotNull(cc, "Player 프리팹에 CharacterController가 없다");
            return new Capsule { Radius = cc.radius, Bottom = cc.center.y - cc.height * 0.5f, Top = cc.center.y + cc.height * 0.5f };
        }

        private static float ReadInteractionRange()
        {
            var interactor = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponentInChildren<ValveInteractor>(true);
            Assert.IsNotNull(interactor, "Player 프리팹에 ValveInteractor가 없다");
            return new SerializedObject(interactor).FindProperty("_interactionRange").floatValue;
        }

        /// <summary>
        /// 대상 주변 격자에서 설 수 있는 지점을 찾는다(판정 반경 안 · 벽과 안 겹침 · 추가 조건). 바닥은 대상 높이 + 0.5 이하에서
        /// 가장 높은 윗면(2층 · 수중을 구분). 여럿이면 대상과의 <b>수평 거리가 <paramref name="preferredHorizontal"/>에 가장 가까운</b>
        /// 것 — QA 순간이동(F4)이 대상을 바라보고 설 자리다(밸브 모형 안에 서지 않게).
        /// </summary>
        private static bool TryFindStandPoint(List<SceneGeometry.Aabb> solids, Capsule capsule, Vector3 target, float reach,
            float preferredHorizontal, System.Func<Vector3, float> distance, System.Func<Vector3, bool> extra,
            out Vector3 best, out string reason) =>
            TryFindStandPoint(solids, capsule, target, target.y + 0.5f, reach, preferredHorizontal, distance, extra, out best, out reason);

        /// <param name="maxFloorTop">밟을 바닥 윗면의 상한 — 지상 · 2층 대상은 대상 높이 + 0.5, 수중 대상은 수면(사람은 수영 바닥에 선다).</param>
        private static bool TryFindStandPoint(List<SceneGeometry.Aabb> solids, Capsule capsule, Vector3 target, float maxFloorTop,
            float reach, float preferredHorizontal, System.Func<Vector3, float> distance, System.Func<Vector3, bool> extra,
            out Vector3 best, out string reason)
        {
            best = default;
            float bestKey = float.MaxValue;
            int standable = 0;

            for (float x = target.x - reach; x <= target.x + reach; x += GridStep)
            {
                for (float z = target.z - reach; z <= target.z + reach; z += GridStep)
                {
                    if (!SceneGeometry.TryStandTop(solids, x, z, capsule.Radius, maxFloorTop, out float top))
                        continue;

                    var feet = new Vector3(x, top, z);
                    float d = distance(feet);
                    if (d > reach)
                        continue;
                    if (SceneGeometry.HasLowCeiling(solids, feet, EnclosedCeiling))
                        continue; // 다른 바닥 아래 갇힌 틈 — 걸어서 닿을 수 없다
                    if (SceneGeometry.CapsuleOverlaps(solids, feet, capsule.Radius, capsule.Bottom, capsule.Top).Count > 0)
                        continue;

                    standable++;
                    if (extra != null && !extra(feet))
                        continue;

                    float key = Mathf.Abs(SceneGeometry.DistanceXZ(new SceneGeometry.Aabb(null, target, target), feet) - preferredHorizontal);
                    if (key < bestKey)
                    {
                        bestKey = key;
                        best = feet;
                    }
                }
            }

            reason = $"반경 {reach}m 안 설 수 있는 지점 {standable}개" + (extra != null ? " (추가 조건 전 기준)" : string.Empty);
            return bestKey < float.MaxValue;
        }

        /// <summary>QA 순간이동 지점 한 줄(QaDebugOverlay.QaPoints에 그대로 붙인다) — 발 높이 = 바닥 + 0.05, 대상을 바라보는 yaw.</summary>
        private static string QaPointLine(string name, Vector3 feet, Vector3 target)
        {
            float yaw = Mathf.Atan2(target.x - feet.x, target.z - feet.z) * Mathf.Rad2Deg;
            if (yaw < 0f)
                yaw += 360f;
            return FormattableString.Invariant(
                $"[QA-POINT] new QaPoint(\"{name}\", {feet.x:0.00}f, {feet.y + 0.05f:0.00}f, {feet.z:0.00}f, {Mathf.Round(yaw):0}f),");
        }

        // ── 출구 ─────────────────────────────────────────────────────────

        [Test]
        public void Game_HasExactlyTwoActiveExits_EachWithStandablePointInRadius()
        {
            Capsule capsule = ReadCapsule();
            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                List<EscapePointTrigger> exits = SceneGeometry.FindAllActive<EscapePointTrigger>(game);
                Assert.AreEqual(2, exits.Count,
                    $"활성 출구가 정확히 2개여야 한다 — [{string.Join(", ", exits.ConvertAll(e => e.name))}]");

                List<SceneGeometry.Aabb> solids = SceneGeometry.CollectSolidBoxes(game);
                var failures = new List<string>();
                foreach (EscapePointTrigger exit in exits)
                {
                    float radius = exit.EscapeRadius; // Core 상수(EscapeRules) — 트리거와 서버가 같이 쓴다
                    Vector3 at = exit.transform.position;

                    // EscapePointTrigger와 같은 판정 — 발(transform.position)과 출구 중심의 3D 거리. QA 지점은 중심에서 수평 0.8m(여유).
                    if (TryFindStandPoint(solids, capsule, at, radius, 0.8f, feet => Vector3.Distance(feet, at), null,
                            out Vector3 point, out string reason))
                        TestContext.Out.WriteLine(QaPointLine($"출구 {exit.name}", point, at) +
                                                  $" // 3D 거리 {Vector3.Distance(point, at):0.00} ≤ {radius}");
                    else
                        failures.Add($"출구 {exit.name} {at}: 판정 반경 {radius}m 안에 캡슐이 설 자리가 없다 — {reason}");
                }

                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }

        // ── 밸브 ─────────────────────────────────────────────────────────

        [Test]
        public void EveryMapValve_HasStandablePointWithinInteractionRange_UnderwaterCanWork()
        {
            Capsule capsule = ReadCapsule();
            float range = ReadInteractionRange();
            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            List<WaterVolumeBehaviour> water = SceneGeometry.FindAllActive<WaterVolumeBehaviour>(game);
            foreach (WaterVolumeBehaviour w in water)
                WaterVolumeRegistry.Register(w); // 실제 수면 판정(WaterVolumeRegistry.Sample)을 쓴다

            try
            {
                List<ValveBehaviour> valves = SceneGeometry.FindAllActive<ValveBehaviour>(game);
                Assert.AreEqual(5, valves.Count, $"맵 밸브 5개 — [{string.Join(", ", valves.ConvertAll(v => v.name))}]");
                Assert.Greater(water.Count, 0, "수면 영역이 없다");

                List<SceneGeometry.Aabb> solids = SceneGeometry.CollectSolidBoxes(game);
                var failures = new List<string>();
                foreach (ValveBehaviour valve in valves)
                {
                    Vector3 at = valve.transform.position;
                    bool underwater = ValveOccupancy.IsUnderwater(valve.ValveId);

                    // ValveInteractor와 같은 판정 — 수중 밸브는 수평 거리(GAP-88) + 이 자리에서 실제로 잠길 수 있음(CanWork).
                    System.Func<Vector3, bool> canWork = null;
                    if (underwater)
                        canWork = feet => UnderwaterWorkSession.CanWork(RoleType.Runner, true, WaterVolumeRegistry.Sample(feet), feet.y, true);

                    // 수중 밸브는 물 밖 · 수영 바닥에서 손을 뻗는다 — 바닥 상한은 수면(밸브 높이 + 0.5가 아니다: 그러면 수영 바닥 아래
                    // 풀 바닥에 갇힌 자리를 고른다). QA 지점은 밸브에서 수평 1.2m — 밸브를 바라보고 선다.
                    float maxFloorTop = underwater ? WaterVolumeRegistry.Sample(at).SurfaceY + 0.05f : at.y + 0.5f;
                    if (TryFindStandPoint(solids, capsule, at, maxFloorTop, range, 1.2f, feet => InteractionRules.DistanceTo(feet, at, underwater), canWork,
                            out Vector3 point, out string reason))
                        TestContext.Out.WriteLine(QaPointLine($"밸브 {valve.ValveId} — {ValveOccupancy.ZoneOf(valve.ValveId)}", point, at) +
                                                  $" // 판정 거리 {InteractionRules.DistanceTo(point, at, underwater):0.00} ≤ {range}");
                    else
                        failures.Add($"밸브 {valve.ValveId} {at}{(underwater ? "(수중)" : "")}: 상호작용 거리 {range}m 안에 설 자리가 없다 — {reason}");
                }

                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally
            {
                foreach (WaterVolumeBehaviour w in water)
                    WaterVolumeRegistry.Unregister(w);
                EditorSceneManager.ClosePreviewScene(game);
            }
        }
    }
}
