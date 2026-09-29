using Marco.Core.GameFlow;
using Marco.Core.Role;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 로비 복귀 · 새 판 배치(09-29). T4는 실제 컴포넌트(<see cref="LobbyPlaceholderFloor"/> · <see cref="PawnPhaseTeleporter"/> ·
    /// <see cref="FirstPersonController"/>)로, 새 판 배치는 텔레포터가 호출하는 규칙(<see cref="SpawnPlacementRules"/>)으로 검증한다.
    /// </summary>
    public class LobbyReturnTests
    {
        // ── T4: 맵이 내려가는 같은 호출 안에서 로비 슬롯 배치 ─────────────────

        [Test]
        public void T4_MapUnload_PlacesLocalPawnOnLobbySlot_InSameCall()
        {
            Assert.IsFalse(SpawnAnchorRegistry.HasAnchor, "전제: 맵 앵커가 등록돼 있지 않다(EditMode)");

            // 로비 임시 바닥 — Lobby 씬과 같은 크기 · 위치(중심 (12, -0.5, 27), 20×1×20).
            GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.transform.position = new Vector3(12f, -0.5f, 27f);
            floorGo.transform.localScale = new Vector3(20f, 1f, 20f);
            var floor = floorGo.AddComponent<LobbyPlaceholderFloor>();

            var teleporter = new GameObject("Teleporter").AddComponent<PawnPhaseTeleporter>();
            var so = new SerializedObject(floor);
            so.FindProperty("_teleporter").objectReferenceValue = teleporter;
            so.FindProperty("_logTransitions").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            var pawnGo = new GameObject("Pawn");
            var pawn = pawnGo.AddComponent<FirstPersonController>(); // CharacterController는 RequireComponent로 붙는다
            pawnGo.transform.position = new Vector3(45f, 0.05f, 8.5f); // 판이 끝난 자리(기계실 — 임시 바닥 밖)
            LocalPlayerRegistry.Register(pawn);

            try
            {
                floor.ApplyMapPresence(true);     // 판 중: 맵 있음 → 임시 바닥 꺼짐
                Assert.AreEqual(new Vector3(45f, 0.05f, 8.5f), pawnGo.transform.position, "맵이 있을 때는 옮기지 않는다");

                floor.ApplyMapPresence(false);    // 맵이 내려감 — 이 호출 하나로 바닥이 켜지고 pawn이 놓여야 한다

                Assert.IsTrue(floorGo.GetComponent<BoxCollider>().enabled, "임시 바닥이 켜졌다");

                var anchor = new SpawnPose(new Vector3(12f, 0f, 27f), Quaternion.identity);
                int slot = (int)(pawn.PlayerId % (ulong)SpawnRing.DefaultSlots);
                Vector3 expected = SpawnRing.GetPose(anchor, slot, SpawnRing.DefaultRadiusMeters, SpawnRing.DefaultSlots).Position
                                   + Vector3.up * 0.05f;
                Vector3 actual = pawnGo.transform.position;
                Assert.AreEqual(expected.x, actual.x, 1e-3f, $"로비 슬롯 {slot} x — 실제 {actual}");
                Assert.AreEqual(expected.y, actual.y, 1e-3f, $"로비 슬롯 {slot} y — 실제 {actual}");
                Assert.AreEqual(expected.z, actual.z, 1e-3f, $"로비 슬롯 {slot} z — 실제 {actual}");
            }
            finally
            {
                LocalPlayerRegistry.Unregister(pawn);
                Object.DestroyImmediate(pawnGo);
                Object.DestroyImmediate(teleporter.gameObject);
                Object.DestroyImmediate(floorGo);
            }
        }

        // ── 5번: 모든 새 판은 스폰 슬롯(술래는 격리 지점)에서 시작 ─────────────

        [Test]
        public void NewRound_Rematch_MapKept_RoundChanges_PlacesAgain()
        {
            // 리매치 가결: 맵은 내려가지 않고(placedForCurrentMap 유지) 라운드 번호만 바뀐다.
            Assert.IsTrue(SpawnPlacementRules.ShouldPlaceOnMap(mapReady: true, placedForCurrentMap: true, placedForRound: 0, currentRound: 1),
                "리매치 — 라운드 번호가 바뀌면 다시 배치한다");
            Assert.IsFalse(SpawnPlacementRules.ShouldPlaceOnMap(true, true, 1, 1), "같은 판에서는 다시 배치하지 않는다");
        }

        [Test]
        public void NewRound_MapReloaded_PlacesAgain_AndNoMap_NeverPlacesOnMap()
        {
            Assert.IsTrue(SpawnPlacementRules.ShouldPlaceOnMap(true, placedForCurrentMap: false, placedForRound: 5, currentRound: 5),
                "부결 → 로비 → 새 맵 로드 — 맵이 새로 올라오면 배치");
            Assert.IsFalse(SpawnPlacementRules.ShouldPlaceOnMap(false, false, 0, 1), "맵이 없으면 맵 배치는 없다(로비 배치는 T4)");
        }

        [Test]
        public void NewRound_SeekerAtIsolation_EveryoneElseOnSpawnRing()
        {
            Assert.IsTrue(SpawnPlacementRules.UsesIsolationAnchor(RoleType.Seeker, hasIsolationAnchor: true), "술래 → 격리 지점");
            Assert.IsFalse(SpawnPlacementRules.UsesIsolationAnchor(RoleType.Runner, true), "러너 → 스폰 링");
            Assert.IsFalse(SpawnPlacementRules.UsesIsolationAnchor(RoleType.Echo, true), "메아리(이론상) → 스폰 링");
            Assert.IsFalse(SpawnPlacementRules.UsesIsolationAnchor(RoleType.Seeker, false), "격리 앵커가 없으면 술래도 스폰 링(GAP-45)");
        }
    }
}
