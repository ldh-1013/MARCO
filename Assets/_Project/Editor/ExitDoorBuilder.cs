using System.Text;
using Marco.Core.Objectives;
using Marco.Presentation.Objectives;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.EditorTools
{
    /// <summary>
    /// [10-02] 출구 문 생성 — 출구 마커(<see cref="EscapePointTrigger"/>, 빈 오브젝트)에 <b>보이는 문</b>을 자식으로 붙인다.
    /// 출구 Transform(판정 위치)은 옮기지 않는다. 좌표는 <see cref="MapV2Layout.ExitDoors"/>, 크기는 <see cref="EscapeDoorLook"/>.
    ///
    /// <para>
    /// 구성: <c>Door</c>(벽 안쪽 면 · 바닥, +z = 방 안쪽) 아래 문짝 <c>Panel</c>(<see cref="EscapeDoorVisual"/> — 열림 재질 교체 · 윤곽 등록), 문틀 3개, 윤곽 상자 <c>Outline</c>(빈 Transform). 모두 <b>콜라이더 없음</b> — 이동 · 소리 차폐 · 배치 검증을 바꾸지 않는다. 재질은 URP Lit(벽과 같은 셰이더)이라
    /// 목소리 빛이 비출 때만 보인다(닫힌 문은 발광 0, 열리면 초록 발광 재질로 바뀐다). 문짝은 어둡게, 문틀은 밝게 — 빛을 받으면 윤곽이 읽힌다.
    /// </para>
    ///
    /// <para><b>멱등</b>: 기존 <c>Door</c> 자식을 지우고 다시 만든다. 맵 v2 생성기(<c>BuildMarkers</c>)도 이것을 부른다.</para>
    /// </summary>
    public static class ExitDoorBuilder
    {
        public const string DoorName = "Door";

        private const string MaterialFolder = "Assets/_Project/Maps/ExitDoor";
        private const string PanelMaterialPath = MaterialFolder + "/ExitDoorPanel.mat";
        private const string PanelOpenMaterialPath = MaterialFolder + "/ExitDoorPanelOpen.mat";
        private const string FrameMaterialPath = MaterialFolder + "/ExitDoorFrame.mat";
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        /// <summary>문짝 두께 · 벽 면에서 띄우는 간격(z-파이팅 방지).</summary>
        private const float PanelDepth = 0.04f;
        private const float WallGap = 0.005f;

        /// <summary>문틀 폭 · 깊이 — 문짝보다 앞으로 더 나와 측면에서도 윤곽이 보인다. 배수로 통로(안쪽 2.3m)에 들어가는 폭.</summary>
        private const float FrameWidth = 0.1f;
        private const float FrameDepth = 0.08f;

        private static readonly Color PanelColor = new Color(0.30f, 0.24f, 0.18f, 1f);
        private static readonly Color FrameColor = new Color(0.80f, 0.78f, 0.72f, 1f);

        /// <summary>이 출구 이름의 문 배치가 있으면 문을 (다시) 만든다. 없으면 false.</summary>
        public static bool Build(Transform exit)
        {
            for (int i = 0; i < MapV2Layout.ExitDoors.Length; i++)
            {
                (string name, Vector2 face, Vector2 inward) = MapV2Layout.ExitDoors[i];
                if (name != exit.name)
                    continue;

                Transform old = exit.Find(DoorName);
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);

                Material panelMat = EnsureMaterial(PanelMaterialPath, PanelColor, EscapeDoorLook.PanelEmission(false));
                Material panelOpenMat = EnsureMaterial(PanelOpenMaterialPath, PanelColor, EscapeDoorLook.PanelEmission(true));
                Material frameMat = EnsureMaterial(FrameMaterialPath, FrameColor, Color.black);

                var door = new GameObject(DoorName);
                door.transform.SetParent(exit, worldPositionStays: false);
                door.transform.SetPositionAndRotation(new Vector3(face.x, 0f, face.y),
                    Quaternion.LookRotation(new Vector3(inward.x, 0f, inward.y), Vector3.up));

                const float w = EscapeDoorLook.WidthMeters, h = EscapeDoorLook.HeightMeters;
                GameObject panel = Part(door, "Panel", new Vector3(0f, h * 0.5f, WallGap + PanelDepth * 0.5f),
                    new Vector3(w, h, PanelDepth), panelMat);
                float frameZ = WallGap + FrameDepth * 0.5f;

                // 파문 윤곽 상자 — 문틀까지 감싼다(렌더러 · 콜라이더 없음). 문짝 상자로 그리면 테두리 선이 문틀 뒤에 가려진다.
                var outline = new GameObject("Outline").transform;
                outline.SetParent(door.transform, worldPositionStays: false);
                outline.localPosition = new Vector3(0f, (h + FrameWidth) * 0.5f, frameZ);
                outline.localRotation = Quaternion.identity;
                outline.localScale = new Vector3(w + FrameWidth * 2f, h + FrameWidth, FrameDepth);

                var visual = new SerializedObject(panel.AddComponent<EscapeDoorVisual>());
                visual.FindProperty("_openMaterial").objectReferenceValue = panelOpenMat;
                visual.FindProperty("_outline").objectReferenceValue = outline;
                visual.ApplyModifiedPropertiesWithoutUndo();

                Part(door, "Frame_L", new Vector3(-(w + FrameWidth) * 0.5f, (h + FrameWidth) * 0.5f, frameZ),
                    new Vector3(FrameWidth, h + FrameWidth, FrameDepth), frameMat);
                Part(door, "Frame_R", new Vector3((w + FrameWidth) * 0.5f, (h + FrameWidth) * 0.5f, frameZ),
                    new Vector3(FrameWidth, h + FrameWidth, FrameDepth), frameMat);
                Part(door, "Frame_Top", new Vector3(0f, h + FrameWidth * 0.5f, frameZ),
                    new Vector3(w + FrameWidth * 2f, FrameWidth, FrameDepth), frameMat);
                return true;
            }

            return false;
        }

        private static GameObject Part(GameObject door, string name, Vector3 localCenter, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // 보이기만 한다
            go.transform.SetParent(door.transform, worldPositionStays: false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// URP Lit 재질(없으면 만든다). 발광색이 검정이 아니면 <c>_EMISSION</c>을 켠다 — URP는 임포트 때 발광색으로 키워드를 다시 정하므로
        /// 검정 발광 재질에는 키워드가 남지 않는다(그래서 열림 재질을 따로 둔다 — <see cref="EscapeDoorVisual"/>).
        /// </summary>
        private static Material EnsureMaterial(string path, Color color, Color emission)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Maps", "ExitDoor");

            mat = new Material(Shader.Find(UrpLitShader)) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.2f);
            mat.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>
        /// 배치모드 진입점 — <c>-executeMethod Marco.EditorTools.ExitDoorBuilder.BuildInGameSceneBatch</c>. Game 씬의 활성 출구에 문을 붙이고,
        /// 구 맵의 잔재 <c>EscapeGate</c>(비활성 <c>Graybox</c> 자식) · <c>EscapePoint</c>(비활성 루트)를 지운 뒤 저장한다(성공 0 / 실패 1).
        /// </summary>
        public static void BuildInGameSceneBatch()
        {
            var log = new StringBuilder();
            log.AppendLine("=== 출구 문 생성(10-02) ===");

            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            int built = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (EscapePointTrigger exit in root.GetComponentsInChildren<EscapePointTrigger>(includeInactive: true))
                {
                    if (!exit.gameObject.activeInHierarchy)
                        continue;

                    bool ok = Build(exit.transform);
                    log.AppendLine($"  {exit.name}: {(ok ? "문 생성" : "배치 없음 — 건너뜀")}");
                    if (ok)
                        built++;
                }
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (t == null || t.gameObject.activeInHierarchy)
                        continue;
                    if ((t.name == "EscapeGate" && t.parent != null && t.parent.name == "Graybox") ||
                        (t.name == "EscapePoint" && t.parent == null))
                    {
                        log.AppendLine($"  구 잔재 삭제: {t.name}");
                        Object.DestroyImmediate(t.gameObject);
                    }
                }
            }

            bool saved = built == MapV2Layout.ExitDoors.Length && EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            log.AppendLine(saved ? $"완료 — 문 {built}개 · Game.unity 저장" : $"실패 — 문 {built}개, 저장하지 않음");
            Debug.Log(log.ToString());
            EditorApplication.Exit(saved ? 0 : 1);
        }
    }
}
