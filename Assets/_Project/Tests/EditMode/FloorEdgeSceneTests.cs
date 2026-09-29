using System.Collections.Generic;
using System.Reflection;
using Marco.Presentation.GameFlow;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 모든 바닥 가장자리가 벽이나 다른 바닥으로 막혀 있다(09-29) — 걸어서 맵 밖으로 떨어질 수 있는 가장자리가 없어야 한다.
    /// <c>SpawnPointSceneTests</c>와 같은 방식(프리뷰 씬 · BoxCollider 경계). 판정은 <see cref="SceneGeometry.OpenFloorEdges"/>:
    /// 가장자리 바로 바깥에 같은 높이(±0.5m)의 바닥 · 경사로가 이어지거나, 그 자리에 선 캡슐이 벽에 걸리면 막힌 것이다.
    /// 캡슐 치수는 Player 프리팹 값.
    /// </summary>
    public class FloorEdgeSceneTests
    {
        private const string GameScenePath = "Assets/Scenes/Game.unity";
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        /// <summary>바닥판 두께(<c>MapV2Layout.FloorThickness</c> 0.2) + 여유. 경사로는 회전해 경계 상자가 두꺼워 바닥으로 세지 않는다(연속 판정에는 쓰인다).</summary>
        private const float MaxFloorSlabThickness = 0.25f;

        private static void ReadCapsule(out float radius, out float height)
        {
            var cc = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<CharacterController>();
            Assert.IsNotNull(cc, "Player 프리팹에 CharacterController가 없다");
            radius = cc.radius;
            height = cc.center.y + cc.height * 0.5f;
        }

        private static List<int> FloorsOf(List<SceneGeometry.Aabb> solids)
        {
            var floors = new List<int>();
            for (int i = 0; i < solids.Count; i++)
            {
                Vector3 size = solids[i].Max - solids[i].Min;
                if (size.y > MaxFloorSlabThickness || size.x * size.z < 0.25f)
                    continue;

                // 다른 바닥 아래 갇힌 판(수영 바닥 아래 풀 바닥)은 걸어서 닿지 않는다 — 가장자리를 볼 대상이 아니다.
                Vector3 center = (solids[i].Min + solids[i].Max) * 0.5f;
                if (SceneGeometry.HasLowCeiling(solids, new Vector3(center.x, solids[i].Max.y, center.z), 3f))
                    continue;

                floors.Add(i);
            }

            return floors;
        }

        [Test]
        public void GameMap_EveryFloorEdge_IsWalledOrContinues()
        {
            ReadCapsule(out float radius, out float height);
            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                List<SceneGeometry.Aabb> solids = SceneGeometry.CollectSolidBoxes(game);
                List<int> floors = FloorsOf(solids);
                Assert.Greater(floors.Count, 0, "Game 씬에 바닥이 없다");

                List<string> open = SceneGeometry.OpenFloorEdges(solids, floors, radius, height);
                Assert.IsEmpty(open, $"바닥 {floors.Count}장 중 열린 가장자리 {open.Count}곳:\n  " + string.Join("\n  ", open));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }

        [Test]
        public void LobbyPlaceholder_EveryFloorEdge_IsWalled()
        {
            ReadCapsule(out float radius, out float height);
            Scene lobby = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                LobbyPlaceholderFloor placeholder = SceneGeometry.FindInScene<LobbyPlaceholderFloor>(lobby);
                Assert.IsNotNull(placeholder, "Lobby 씬에 LobbyPlaceholderFloor가 없다");

                // 런타임과 같은 순서 — Awake에서 부품(바닥 + 가장자리 벽)을 갖춘다. EditMode에서는 생명주기가 자동으로 돌지 않는다.
                placeholder.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(placeholder, null);

                List<SceneGeometry.Aabb> solids = SceneGeometry.CollectSolidBoxes(lobby);
                var floors = new List<int>();
                BoxCollider floorBox = placeholder.GetComponentInChildren<BoxCollider>(includeInactive: true);
                Assert.IsNotNull(floorBox, "임시 바닥에 BoxCollider가 없다");
                string floorPath = SceneGeometry.PathOf(floorBox.transform);
                for (int i = 0; i < solids.Count; i++)
                {
                    if (solids[i].Path == floorPath)
                        floors.Add(i);
                }

                Assert.AreEqual(1, floors.Count, "임시 바닥 콜라이더 하나");
                Vector3 size = solids[floors[0]].Max - solids[floors[0]].Min;
                TestContext.Out.WriteLine($"임시 바닥 {solids[floors[0]].Min} ~ {solids[floors[0]].Max} ({size.x:0.#} × {size.z:0.#})");

                List<string> open = SceneGeometry.OpenFloorEdges(solids, floors, radius, height);
                Assert.IsEmpty(open, $"로비 임시 바닥 열린 가장자리 {open.Count}곳:\n  " + string.Join("\n  ", open));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(lobby);
            }
        }
    }
}
