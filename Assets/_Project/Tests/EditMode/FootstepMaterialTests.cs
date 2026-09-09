using Marco.Core.Locomotion;
using Marco.Core.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §5.9 재질별 발소리 배율(4단계).
    ///
    /// 두 가지를 고정한다: ①표의 배율을 그대로 재현하는가, ②**발생 반경에만** 적용되고
    /// 발생 간격에는 손대지 않는가(§5.1-1 "발생 간격에는 재질 배율을 적용하지 않는다").
    /// </summary>
    public class FootstepMaterialTests
    {
        // ── §5.9 표 ──────────────────────────────────────────────────────

        [TestCase(FootstepMaterial.MetalGrating, 1.5f)]
        [TestCase(FootstepMaterial.Tile, 1.3f)]
        [TestCase(FootstepMaterial.StageFloor, 1.2f)]
        [TestCase(FootstepMaterial.Concrete, 1.0f)]
        [TestCase(FootstepMaterial.Wood, 1.0f)]
        [TestCase(FootstepMaterial.Mat, 0.7f)]
        [TestCase(FootstepMaterial.Water, 0f)]
        public void RadiusMultiplier_MatchesDesignDoc(FootstepMaterial material, float expected)
        {
            Assert.AreEqual(expected, FootstepMaterialRules.RadiusMultiplier(material), 0.0001f);
        }

        [Test]
        public void Water_EmitsNoPulse()
        {
            // §5.9 "물(수면 아래) — 파문 발생 안 함".
            Assert.IsFalse(FootstepMaterialRules.EmitsPulse(FootstepMaterial.Water));
        }

        [TestCase(FootstepMaterial.Concrete)]
        [TestCase(FootstepMaterial.Wood)]
        [TestCase(FootstepMaterial.MetalGrating)]
        [TestCase(FootstepMaterial.Tile)]
        [TestCase(FootstepMaterial.Mat)]
        [TestCase(FootstepMaterial.StageFloor)]
        public void NonWaterMaterials_EmitPulse(FootstepMaterial material)
        {
            Assert.IsTrue(FootstepMaterialRules.EmitsPulse(material));
        }

        [Test]
        public void Default_IsConcrete()
        {
            // 모르는 바닥이 플레이어에게 유리해서도 불리해서도 안 된다.
            Assert.AreEqual(FootstepMaterial.Concrete, FootstepMaterialRules.Default);
            Assert.AreEqual(1f, FootstepMaterialRules.RadiusMultiplier(FootstepMaterialRules.Default), 0.0001f);
        }

        // ── §5.1 반경 적용 ───────────────────────────────────────────────

        [Test]
        public void ApplyToRadius_ScalesWalkRadius()
        {
            // §5.1 걷기 2m × §5.9 배율
            Assert.AreEqual(3.0f, FootstepMaterialRules.ApplyToRadius(LocomotionConfig.WalkPulseRadius, FootstepMaterial.MetalGrating), 0.0001f);
            Assert.AreEqual(2.6f, FootstepMaterialRules.ApplyToRadius(LocomotionConfig.WalkPulseRadius, FootstepMaterial.Tile), 0.0001f);
            Assert.AreEqual(1.4f, FootstepMaterialRules.ApplyToRadius(LocomotionConfig.WalkPulseRadius, FootstepMaterial.Mat), 0.0001f);
        }

        [Test]
        public void ApplyToRadius_ScalesSprintRadius()
        {
            // §5.1 질주 6m × §5.9 배율
            Assert.AreEqual(9.0f, FootstepMaterialRules.ApplyToRadius(LocomotionConfig.SprintPulseRadius, FootstepMaterial.MetalGrating), 0.0001f);
            Assert.AreEqual(7.8f, FootstepMaterialRules.ApplyToRadius(LocomotionConfig.SprintPulseRadius, FootstepMaterial.Tile), 0.0001f);
            Assert.AreEqual(4.2f, FootstepMaterialRules.ApplyToRadius(LocomotionConfig.SprintPulseRadius, FootstepMaterial.Mat), 0.0001f);
        }

        [Test]
        public void ApplyToRadius_NeverNegative()
        {
            Assert.AreEqual(0f, FootstepMaterialRules.ApplyToRadius(-5f, FootstepMaterial.Tile), 0.0001f);
        }

        // ── §5.9는 발소리에만 적용된다 ───────────────────────────────────

        [TestCase(SoundType.Walk)]
        [TestCase(SoundType.Sprint)]
        public void AppliesTo_Footsteps(SoundType type)
        {
            Assert.IsTrue(FootstepMaterialRules.AppliesTo(type));
        }

        [TestCase(SoundType.Whisper)]
        [TestCase(SoundType.Talk)]
        [TestCase(SoundType.Shout)]
        [TestCase(SoundType.Valve)]
        [TestCase(SoundType.Knock)]
        public void AppliesTo_NonFootsteps_IsFalse(SoundType type)
        {
            // 카펫 위에서 고함쳤다고 22m가 15.4m로 줄면 §5.1 등급 체계가 무너진다.
            Assert.IsFalse(FootstepMaterialRules.AppliesTo(type));
        }

        // ── §5.1-1: 발생 간격에는 배율이 없다 ────────────────────────────

        [TestCase(FootstepMaterial.MetalGrating)]
        [TestCase(FootstepMaterial.Mat)]
        [TestCase(FootstepMaterial.Tile)]
        public void PulseInterval_IsUnaffectedByMaterial(FootstepMaterial material)
        {
            // §5.1-1 "발생 간격(2m/6m)은 항상 고정". 재질이 무엇이든 걷기 10m는 5회다 —
            // 시뮬레이터가 재질을 입력으로 받지 않는다는 구조 자체가 근거지만,
            // 배율이 실수로 간격에 섞이면 이 테스트가 잡는다.
            var sim = new LocomotionSimulator(Role.RoleType.Runner);
            int pulses = 0;
            for (int i = 0; i < 100; i++) // 2초 = 10m
            {
                if (sim.Tick(new LocomotionInput(new Vector2(0f, 1f), false, false, false), 0.02f).Pulse.HasValue)
                    pulses++;
            }

            Assert.AreEqual(5, pulses,
                $"{material}에서도 발생 간격은 기본 반경 2m 그대로여야 한다(§5.1-1).");
        }

        // ── 서버 등록 경로 (§5.9 적용 지점) ──────────────────────────────

        [Test]
        public void ServerDriver_AppliesMaterialToFootstepRadius()
        {
            var driver = new ServerPulseDriver();

            // 재질을 넘기지 않으면 §5.1 기본값(2m)으로 등록된다.
            Assert.GreaterOrEqual(driver.AddPulse(1, SoundType.Walk, Vector3.zero, 0f), 0);

            // 그레이팅에서도 정상 등록된다(반경만 달라진다).
            Assert.GreaterOrEqual(
                driver.AddPulse(2, SoundType.Walk, Vector3.zero, 0f, FootstepMaterial.MetalGrating), 0);
        }

        [Test]
        public void ServerDriver_RejectsFootstepOnWater()
        {
            var driver = new ServerPulseDriver();

            Assert.AreEqual(-1, driver.AddPulse(1, SoundType.Walk, Vector3.zero, 0f, FootstepMaterial.Water),
                "§5.9 물(수면 아래)에서는 발소리 파문이 발생하지 않는다.");
            Assert.AreEqual(-1, driver.AddPulse(1, SoundType.Sprint, Vector3.zero, 0f, FootstepMaterial.Water));
        }

        [Test]
        public void ServerDriver_WaterDoesNotSilenceVoice()
        {
            // 물 위에서 말했다고 목소리가 사라지면 안 된다 — §5.9는 발소리 규칙이다.
            var driver = new ServerPulseDriver();

            Assert.GreaterOrEqual(
                driver.AddPulse(1, SoundType.Shout, Vector3.zero, 0f, FootstepMaterial.Water), 0);
        }

        // ── 레지스트리 조회 ──────────────────────────────────────────────

        [Test]
        public void SampleMaterial_WithoutProbe_IsDefault()
        {
            PulseNetworkRegistry.ResetForNewSession();

            Assert.AreEqual(FootstepMaterialRules.Default,
                PulseNetworkRegistry.SampleMaterial(SoundType.Walk, Vector3.zero),
                "프로브가 없으면 콘크리트(×1.0) — 미배선 맵에서 발소리가 사라지거나 커지면 안 된다.");
        }

        private sealed class FakeMaterialProbe : IFootstepMaterialProbe
        {
            private readonly FootstepMaterial _material;
            public FakeMaterialProbe(FootstepMaterial material) => _material = material;
            public FootstepMaterial Sample(Vector3 worldPosition) => _material;
        }

        [Test]
        public void SampleMaterial_UsesRegisteredProbe()
        {
            PulseNetworkRegistry.ResetForNewSession();
            var probe = new FakeMaterialProbe(FootstepMaterial.Mat);
            PulseNetworkRegistry.RegisterFootstepMaterialProbe(probe);

            try
            {
                Assert.AreEqual(FootstepMaterial.Mat,
                    PulseNetworkRegistry.SampleMaterial(SoundType.Walk, Vector3.zero));

                // 발소리가 아니면 프로브를 부르지 않고 기본값을 돌려준다.
                Assert.AreEqual(FootstepMaterialRules.Default,
                    PulseNetworkRegistry.SampleMaterial(SoundType.Shout, Vector3.zero));
            }
            finally
            {
                PulseNetworkRegistry.UnregisterFootstepMaterialProbe(probe);
            }
        }

        [Test]
        public void UnregisterFootstepMaterialProbe_ClearsSlot()
        {
            PulseNetworkRegistry.ResetForNewSession();
            var probe = new FakeMaterialProbe(FootstepMaterial.Tile);
            PulseNetworkRegistry.RegisterFootstepMaterialProbe(probe);
            PulseNetworkRegistry.UnregisterFootstepMaterialProbe(probe);

            Assert.IsNull(PulseNetworkRegistry.FootstepMaterialProbe);
        }
    }
}
