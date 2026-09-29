using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 씬 검증 테스트 공용 기하 — 프리뷰로 연 씬의 BoxCollider(Game 씬 콜라이더는 전부 BoxCollider)를 크기 · 트랜스폼으로
    /// 월드 AABB로 바꿔 물리 시뮬레이션 없이 판정한다. <c>SpawnPointSceneTests</c> · <c>ExitValveSceneTests</c>가 쓴다.
    /// </summary>
    internal static class SceneGeometry
    {
        /// <summary>발밑 바닥 윗면과 발 높이의 허용 차.</summary>
        internal const float MaxFloorGap = 0.5f;

        /// <summary>바닥에 발이 닿는 자리(윗면)는 벽 겹침 판정에서 뺀다 — 이 높이 아래의 콜라이더는 바닥이다.</summary>
        internal const float FloorClearance = 0.02f;

        internal readonly struct Aabb
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

        internal static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T c in root.GetComponentsInChildren<T>(includeInactive: true))
                {
                    if (c.gameObject.activeInHierarchy)
                        return c;
                }
            }

            return null;
        }

        /// <summary>
        /// 켜져 있는(계층상 활성 · 컴포넌트 활성) 모든 컴포넌트. <c>includeInactive: false</c>만으로는 부족하다 — <b>비활성 루트</b>에서
        /// 부르면 그 아래가 그대로 나온다(구 맵 <c>Graybox</c> · <c>EscapePoint</c>가 비활성 루트다). 그래서 계층 활성을 직접 본다.
        /// </summary>
        internal static List<T> FindAllActive<T>(Scene scene) where T : Behaviour
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T c in root.GetComponentsInChildren<T>(includeInactive: true))
                {
                    if (c.enabled && c.gameObject.activeInHierarchy)
                        result.Add(c);
                }
            }

            return result;
        }

        /// <summary>활성 · 고체(트리거 아님) BoxCollider의 월드 AABB. 캐릭터 컨트롤러는 트리거와 부딪히지 않는다.</summary>
        internal static List<Aabb> CollectSolidBoxes(Scene scene)
        {
            var result = new List<Aabb>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(includeInactive: true))
                {
                    if (!box.enabled || box.isTrigger || !box.gameObject.activeInHierarchy)
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

        /// <summary>발밑 바닥 — 캡슐 반지름(<paramref name="inset"/>)만큼 안쪽까지 XZ가 덮이고 윗면이 발에서 <see cref="MaxFloorGap"/> 이내.</summary>
        internal static bool HasFloorUnder(List<Aabb> solids, Vector3 feet, float inset, out string note)
        {
            string nearest = "바닥 후보 없음";
            foreach (Aabb box in solids)
            {
                float gap = feet.y - box.Max.y;
                if (gap < -FloorClearance || gap > MaxFloorGap)
                    continue; // 발보다 위(벽 등)이거나 너무 아래

                if (ContainsXZ(box, feet, inset))
                {
                    note = null;
                    return true;
                }

                if (ContainsXZ(box, feet, 0f))
                    nearest = $"'{box.Path}'(x {box.Min.x:0.##}~{box.Max.x:0.##}, z {box.Min.z:0.##}~{box.Max.z:0.##}) 가장자리에 걸림";
            }

            note = nearest;
            return false;
        }

        /// <summary>
        /// 이 XZ에서 설 수 있는 가장 높은 바닥 윗면(≤ <paramref name="maxTop"/>) — 캡슐 반지름만큼 안쪽까지 덮는 고체.
        /// 2층 · 수중처럼 높이가 다른 층에서 "그 대상 가까이의 바닥"을 고를 때 쓴다.
        /// </summary>
        internal static bool TryStandTop(List<Aabb> solids, float x, float z, float inset, float maxTop, out float top)
        {
            top = float.NegativeInfinity;
            var p = new Vector3(x, 0f, z);
            foreach (Aabb box in solids)
            {
                if (box.Max.y > maxTop || !ContainsXZ(box, p, inset))
                    continue;
                if (box.Max.y > top)
                    top = box.Max.y;
            }

            return !float.IsNegativeInfinity(top);
        }

        /// <summary>캡슐(발 위 바닥 윗면부터 머리까지)과 겹치는 고체들의 설명. 비면 겹침 없음.</summary>
        internal static List<string> CapsuleOverlaps(List<Aabb> solids, Vector3 feet, float radius, float bottom, float top)
        {
            var hits = new List<string>();
            foreach (Aabb box in solids)
            {
                if (box.Max.y <= feet.y + FloorClearance)
                    continue; // 발밑 바닥
                if (box.Min.y >= feet.y + top || box.Max.y <= feet.y + bottom)
                    continue; // 캡슐 높이 밖

                float d = DistanceXZ(box, feet);
                if (d < radius)
                    hits.Add($"'{box.Path}'(수평 {d:0.00}m < {radius}m, x {box.Min.x:0.##}~{box.Max.x:0.##}, z {box.Min.z:0.##}~{box.Max.z:0.##}, y {box.Min.y:0.##}~{box.Max.y:0.##})");
            }

            return hits;
        }

        /// <summary>
        /// 이 자리 위 <paramref name="clearance"/> 안에 다른 고체가 덮여 있는가 — 다른 바닥 아래 갇힌 틈(예: 수영 바닥 아래 풀 바닥)은
        /// 걸어서 닿을 수 없는 자리다. 맵의 벽 높이가 3.5m라 정상 천장(2층 바닥 아래면 3.3m)은 걸리지 않는다.
        /// </summary>
        internal static bool HasLowCeiling(List<Aabb> solids, Vector3 feet, float clearance)
        {
            foreach (Aabb b in solids)
            {
                if (feet.x < b.Min.x || feet.x > b.Max.x || feet.z < b.Min.z || feet.z > b.Max.z)
                    continue;
                if (b.Min.y > feet.y + FloorClearance && b.Min.y < feet.y + clearance)
                    return true;
            }

            return false;
        }

        internal static float DistanceXZ(in Aabb box, Vector3 p)
        {
            float dx = Mathf.Max(box.Min.x - p.x, 0f, p.x - box.Max.x);
            float dz = Mathf.Max(box.Min.z - p.z, 0f, p.z - box.Max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static bool ContainsXZ(in Aabb box, Vector3 p, float inset) =>
            p.x >= box.Min.x + inset && p.x <= box.Max.x - inset && p.z >= box.Min.z + inset && p.z <= box.Max.z - inset;

        internal static string PathOf(Transform t)
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
