using NUnit.Framework;
using UnityEngine;
using Marco.Core.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 18b: 맵 스폰 앵커 레지스트리 계약을 고정한다.
    ///
    /// 이 레지스트리는 "맵(Game 씬)이 애디티브로 로드됐는가"의 신호로도 쓰이므로
    /// (§15.4 "InGame: 맵 로드"), 등록·해제가 정확해야 pawn 배치와 라운드 시작 게이트가 맞는다.
    ///
    /// GameObject를 만들지 않는다 — 이 프로젝트의 EditMode 테스트는 Unity 런타임 없이 돌아야 하고,
    /// 레지스트리가 Transform 대신 좌표 스냅샷 + 식별 토큰을 보관하도록 설계된 이유이기도 하다.
    /// </summary>
    public class SpawnAnchorRegistryTests
    {
        // Quaternion은 (x,y,z,w) 생성자로만 만든다 — Quaternion.Euler는 네이티브 호출이라
        // Unity 런타임 없이 도는 이 테스트 환경에서 쓸 수 없다.
        // (0,1,0,0) = Y축 180° 회전(기존 플레이어 스폰 회전과 동일).
        private static readonly SpawnPose PoseA =
            new SpawnPose(new Vector3(12f, 0.05f, 27f), new Quaternion(0f, 1f, 0f, 0f));

        private static readonly SpawnPose PoseB =
            new SpawnPose(new Vector3(-3f, 1f, 4f), new Quaternion(0f, 0f, 0f, 1f));

        [SetUp]
        public void SetUp() => SpawnAnchorRegistry.ResetForNewSession();

        [TearDown]
        public void TearDown() => SpawnAnchorRegistry.ResetForNewSession();

        [Test]
        public void Initially_NoAnchor()
        {
            // 맵 미로드 상태 — 라운드 시작 게이트가 열리지 않아야 하는 근거.
            Assert.IsFalse(SpawnAnchorRegistry.HasAnchor);
        }

        [Test]
        public void Register_ExposesPose()
        {
            var token = new object();
            SpawnAnchorRegistry.Register(token, PoseA);

            Assert.IsTrue(SpawnAnchorRegistry.HasAnchor);
            Assert.AreEqual(PoseA.Position, SpawnAnchorRegistry.Pose.Position);
            Assert.AreEqual(PoseA.Rotation, SpawnAnchorRegistry.Pose.Rotation);
        }

        [Test]
        public void Register_NullToken_IsIgnored()
        {
            var token = new object();
            SpawnAnchorRegistry.Register(token, PoseA);
            SpawnAnchorRegistry.Register(null, PoseB);

            Assert.IsTrue(SpawnAnchorRegistry.HasAnchor, "null 등록이 기존 앵커를 지우면 안 된다");
            Assert.AreEqual(PoseA.Position, SpawnAnchorRegistry.Pose.Position);
        }

        [Test]
        public void Unregister_MatchingToken_Clears()
        {
            // 맵 언로드 시나리오 — 앵커가 사라져야 pawn 재배치 래치가 풀린다.
            var token = new object();
            SpawnAnchorRegistry.Register(token, PoseA);
            SpawnAnchorRegistry.Unregister(token);

            Assert.IsFalse(SpawnAnchorRegistry.HasAnchor);
        }

        [Test]
        public void Unregister_StaleToken_DoesNotClearNewAnchor()
        {
            // 맵 교체 중 옛 앵커의 OnDisable이 새 앵커 등록보다 늦게 와도 새 것을 지우지 않아야 한다.
            var older = new object();
            var newer = new object();

            SpawnAnchorRegistry.Register(older, PoseA);
            SpawnAnchorRegistry.Register(newer, PoseB);
            SpawnAnchorRegistry.Unregister(older);

            Assert.IsTrue(SpawnAnchorRegistry.HasAnchor);
            Assert.AreEqual(PoseB.Position, SpawnAnchorRegistry.Pose.Position);
        }

        [Test]
        public void Unregister_Null_IsIgnored()
        {
            var token = new object();
            SpawnAnchorRegistry.Register(token, PoseA);
            SpawnAnchorRegistry.Unregister(null);

            Assert.IsTrue(SpawnAnchorRegistry.HasAnchor);
        }

        [Test]
        public void ResetForNewSession_Clears()
        {
            SpawnAnchorRegistry.Register(new object(), PoseA);
            SpawnAnchorRegistry.ResetForNewSession();

            Assert.IsFalse(SpawnAnchorRegistry.HasAnchor);
        }

        [Test]
        public void ReRegisterAfterUnload_RestoresAnchor()
        {
            // 부결 → 로비(맵 언로드) → 다시 준비 → 맵 재로드 흐름.
            var first = new object();
            SpawnAnchorRegistry.Register(first, PoseA);
            SpawnAnchorRegistry.Unregister(first);

            var second = new object();
            SpawnAnchorRegistry.Register(second, PoseA);

            Assert.IsTrue(SpawnAnchorRegistry.HasAnchor);
            Assert.AreEqual(PoseA.Position, SpawnAnchorRegistry.Pose.Position);
        }
    }
}
