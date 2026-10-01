using System.Collections.Generic;
using Marco.Core.Objectives;
using Marco.Presentation.Objectives;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [10-02] 출구가 <b>눈에 보이는가</b> — 활성 출구(<see cref="EscapePointTrigger"/>)마다 그 출구에 딸린 문 렌더러가 판정 반경 안에 있어야 한다.
    /// 벽 렌더러는 세지 않는다(출구 주변에 벽이 있어도 출구 자체는 빈 마커였다 — "아무 MeshRenderer나 반경 안"이면 수정 전에도 통과한다).
    /// 문은 벽 안에 묻히지 않고(벽 앞면 쪽으로 나와 있고), 이동을 막는 고체 콜라이더를 더하지 않는다.
    /// </summary>
    public class ExitDoorSceneTests
    {
        private const string GameScenePath = "Assets/Scenes/Game.unity";

        private static List<MeshRenderer> DoorRenderers(EscapePointTrigger exit)
        {
            var result = new List<MeshRenderer>();
            foreach (MeshRenderer r in exit.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                if (r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterial != null)
                    result.Add(r);
            }

            return result;
        }

        private static bool Inside(in SceneGeometry.Aabb box, Vector3 p, float margin) =>
            p.x > box.Min.x + margin && p.x < box.Max.x - margin &&
            p.y > box.Min.y + margin && p.y < box.Max.y - margin &&
            p.z > box.Min.z + margin && p.z < box.Max.z - margin;

        [Test]
        public void EveryActiveExit_HasVisibleDoorRendererWithinExitRadius()
        {
            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                List<EscapePointTrigger> exits = SceneGeometry.FindAllActive<EscapePointTrigger>(game);
                Assert.AreEqual(2, exits.Count, "활성 출구 2개");

                List<SceneGeometry.Aabb> solids = SceneGeometry.CollectSolidBoxes(game);
                var failures = new List<string>();
                foreach (EscapePointTrigger exit in exits)
                {
                    Vector3 at = exit.transform.position;
                    List<MeshRenderer> doors = DoorRenderers(exit);
                    if (doors.Count == 0)
                    {
                        failures.Add($"출구 {exit.name} {at}: 출구에 딸린 MeshRenderer가 없다 — 어둠 속에서 출구를 볼 수단이 없다");
                        continue;
                    }

                    int near = 0;
                    foreach (MeshRenderer r in doors)
                    {
                        Bounds b = r.bounds;
                        float d = Vector3.Distance(b.ClosestPoint(at), at);
                        if (d <= exit.EscapeRadius)
                            near++;

                        // 벽 속에 통째로 묻힌 문은 빛이 닿지 않는다 — 중심이 어떤 고체 상자 안에도 있지 않아야 한다.
                        foreach (SceneGeometry.Aabb s in solids)
                        {
                            if (Inside(s, b.center, 0.001f))
                                failures.Add($"출구 {exit.name}: {SceneGeometry.PathOf(r.transform)} 중심 {b.center}이 고체 {s.Path} 안에 묻혔다");
                        }
                    }

                    if (near == 0)
                        failures.Add($"출구 {exit.name} {at}: 문 렌더러 {doors.Count}개가 전부 판정 반경 {exit.EscapeRadius}m 밖이다");

                    // 문은 보이기만 한다 — 이동 · 소리 차폐를 바꾸지 않게 고체 콜라이더를 두지 않는다.
                    foreach (Collider c in exit.GetComponentsInChildren<Collider>(includeInactive: true))
                    {
                        if (c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)
                            failures.Add($"출구 {exit.name}: 문에 고체 콜라이더 {SceneGeometry.PathOf(c.transform)}가 있다");
                    }
                }

                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }

        /// <summary>
        /// 파문 윤곽 상자가 문 전체(문짝 + 문틀)를 감싸는가. 10-02 실기: 문짝 상자로 그리면 테두리 선이 앞으로 더 나온 문틀 뒤에
        /// 가려져, 벽 윤곽은 보이는데 문 윤곽만 보이지 않았다.
        /// </summary>
        [Test]
        public void EveryDoorOutlineBox_EnclosesPanelAndFrame()
        {
            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                var failures = new List<string>();
                foreach (EscapePointTrigger exit in SceneGeometry.FindAllActive<EscapePointTrigger>(game))
                {
                    EscapeDoorVisual door = exit.GetComponentInChildren<EscapeDoorVisual>();
                    if (door == null)
                    {
                        failures.Add($"출구 {exit.name}: EscapeDoorVisual 없음");
                        continue;
                    }

                    var outline = (Transform)new SerializedObject(door).FindProperty("_outline").objectReferenceValue;
                    if (outline == null)
                    {
                        failures.Add($"출구 {exit.name}: 윤곽 상자(_outline)가 비었다 — 문짝 상자로 그려 문틀에 가려진다");
                        continue;
                    }

                    Matrix4x4 m = outline.localToWorldMatrix;
                    var box = new Bounds(m.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        box.Encapsulate(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f)));
                    box.Expand(0.002f);

                    foreach (MeshRenderer r in DoorRenderers(exit))
                    {
                        if (!box.Contains(r.bounds.min) || !box.Contains(r.bounds.max))
                            failures.Add($"출구 {exit.name}: {SceneGeometry.PathOf(r.transform)} {r.bounds.min}~{r.bounds.max}가 윤곽 상자 {box.min}~{box.max} 밖으로 나온다");
                    }
                }

                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }

        /// <summary>
        /// 출구마다 문짝 표시(<see cref="EscapeDoorVisual"/>) 하나 — 판정 반경 안, URP Lit(목소리 빛에 비친다). 닫힘 재질(렌더러에 붙은 것)은
        /// 발광 검정, 열림 재질은 <c>_EMISSION</c> 키워드가 켜져 있고 발광색 = <see cref="EscapeDoorLook.PanelEmission"/>(true).
        /// 키워드가 재질 에셋에 없으면 빌드에서 발광 변형이 빠져 게이트가 열려도 초록이 안 켜진다.
        /// </summary>
        [Test]
        public void EveryActiveExit_HasOneDoorVisual_ClosedDark_OpenGlowsGreen()
        {
            Scene game = EditorSceneManager.OpenPreviewScene(GameScenePath);
            try
            {
                var failures = new List<string>();
                foreach (EscapePointTrigger exit in SceneGeometry.FindAllActive<EscapePointTrigger>(game))
                {
                    EscapeDoorVisual[] doors = exit.GetComponentsInChildren<EscapeDoorVisual>(includeInactive: false);
                    if (doors.Length != 1)
                    {
                        failures.Add($"출구 {exit.name}: EscapeDoorVisual {doors.Length}개 (1개여야 한다)");
                        continue;
                    }

                    var r = doors[0].GetComponent<MeshRenderer>();
                    float d = Vector3.Distance(r.bounds.ClosestPoint(exit.transform.position), exit.transform.position);
                    if (d > exit.EscapeRadius)
                        failures.Add($"출구 {exit.name}: 문짝이 판정 반경 밖({d:0.00}m)");

                    Material closed = r.sharedMaterial;
                    var open = (Material)new SerializedObject(doors[0]).FindProperty("_openMaterial").objectReferenceValue;
                    foreach ((string label, Material mat) in new[] { ("닫힘", closed), ("열림", open) })
                    {
                        if (mat == null || mat.shader == null || mat.shader.name != "Universal Render Pipeline/Lit")
                            failures.Add($"출구 {exit.name}: {label} 재질 {(mat == null ? "없음" : mat.shader.name)} — URP Lit이어야 목소리 빛에 비친다");
                    }

                    if (closed != null && (closed.IsKeywordEnabled("_EMISSION") && closed.GetColor("_EmissionColor").maxColorComponent > 0f))
                        failures.Add($"출구 {exit.name}: 닫힘 재질 {closed.name}이 빛난다");
                    if (open != null)
                    {
                        if (!open.IsKeywordEnabled("_EMISSION"))
                            failures.Add($"출구 {exit.name}: 열림 재질 {open.name}에 _EMISSION 키워드가 없다");
                        if (open.GetColor("_EmissionColor") != EscapeDoorLook.PanelEmission(true))
                            failures.Add($"출구 {exit.name}: 열림 발광 {open.GetColor("_EmissionColor")} ≠ {EscapeDoorLook.PanelEmission(true)}");
                    }
                }

                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(game);
            }
        }
    }
}
