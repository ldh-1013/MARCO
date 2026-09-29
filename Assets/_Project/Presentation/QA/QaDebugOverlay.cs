#if MARCO_QA_BUILD
using System;
using System.Text;
using Marco.Core.Breath;
using Marco.Core.Locomotion;
using Marco.Core.Water;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Marco.Presentation.QA
{
    /// <summary>
    /// QA 전용 디버그 오버레이 — 원격 실기 검증용. §16.1 암전 때문에 원격 데스크톱 화면이 완전히 검어
    /// 좌표 · 지형 기준이 없고, §31-4(물 진입 · 투명벽 통과)처럼 눈으로 봐야 하는 항목을 확인할 수 없어서 만들었다.
    ///
    /// <para>
    /// <b>QA 빌드에만 존재한다.</b> 파일 전체가 <c>MARCO_QA_BUILD</c>로 감싸여 있고, 이 심볼은
    /// <c>MarcoBuildTool</c>(Tools/MARCO/Build Windows (3인 테스트))이 그 빌드에만 넘긴다
    /// (<c>BuildPlayerOptions.extraScriptingDefines</c> — PlayerSettings에 저장되지 않는다). 심볼을 넘기지 않는
    /// 빌드 경로(향후 릴리즈/스팀 빌드)와 에디터 Play에서는 컴파일 단계에서 통째로 빠진다.
    /// </para>
    ///
    /// <para>
    /// 씬에 놓지 않는다 — 첫 씬 로드 뒤 스스로 생겨 씬 전환에도 남는다(씬 YAML · 파이프라인 무변경).
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><b>F3</b> 텍스트 오버레이 — 로컬 플레이어 좌표 · 발밑 구역 · 숨 상태. 요청은 F1이었지만
    /// F1은 설정 창(<c>SettingsScreen</c>)이 이미 쓴다.</item>
    /// <item><b>F2</b> QA 라이트 — 이 클라이언트에만 방향광 2개를 켠다. 씬의 암전 값(RenderSettings ·
    /// 카메라)은 건드리지 않고 조명을 <b>더하기만</b> 하므로 끄면 그대로 원상태다. 서버 · 다른 플레이어 화면 ·
    /// 게임플레이 판정(숨 게이지 · 파문 · 차폐 레이)과 무관하다.</item>
    /// <item><b>F4</b> QA 순간이동(09-27 요청으로 추가) — 미리 정한 QA 지점을 순서대로 돈다(로컬 플레이어).
    /// 이동은 소유자 권한(Player <c>NetworkTransform</c> clientAuthoritative)이라 서버 RPC 없이 로컬 배치가 그대로
    /// 동기화된다 — <c>PawnPhaseTeleporter.PlaceExactly</c>와 같은 방식(CharacterController를 끄고 위치 대입).</item>
    /// </list>
    /// 미니맵 · 노클립은 넣지 않는다(검증 대상 동작을 바꾸지 않는 것이 이 도구의 조건).
    /// </summary>
    public sealed class QaDebugOverlay : MonoBehaviour
    {
        private const Key OverlayKey = Key.F3;
        private const Key LightKey = Key.F2;
        private const Key TeleportKey = Key.F4;

        private readonly struct QaPoint
        {
            public readonly string Name;
            public readonly Vector3 Feet;
            public readonly float Yaw;

            public QaPoint(string name, float x, float y, float z, float yaw)
            {
                Name = name;
                Feet = new Vector3(x, y, z);
                Yaw = yaw;
            }
        }

        /// <summary>
        /// 맵 v2(<c>MapV2Layout</c>) 좌표. 09-27 Game 씬에서 배치모드로 확인 — 발밑 바닥 콜라이더 · 캡슐(반지름 0.35)
        /// 겹침 없음 · 대화(9m) 벽 윤곽 차폐 결과. 발 높이는 바닥 윗면 + 0.05(스폰 앵커와 같은 여유). 맵이 바뀌면 다시 잡는다.
        /// </summary>
        private static readonly QaPoint[] QaPoints =
        {
            new QaPoint("① 기계실 중앙 — 네 벽이 반경 안", 45f, 0.05f, 8.5f, 0f),
            new QaPoint("② 기계실 문 바깥 — 문 너머 기계실 벽", 45f, 0.05f, 4.3f, 0f),
            new QaPoint("③ 기계실 서벽 바깥 — 벽 하나 너머 기계실", 39.5f, 0.05f, 8.5f, 90f),
            new QaPoint("④ 라커룸 1층 — 위층 물탱크실과 겹침", 7f, 0.05f, 26.5f, 0f),
            new QaPoint("⑤ 물탱크실 2층 — 아래층 라커룸과 겹침", 7f, 3.55f, 26.5f, 0f),
        };

        /// <summary>문자열 재조립 주기(초). 매 프레임 만들 필요가 없다.</summary>
        private const float RefreshInterval = 0.1f;

        // 맵 v2 생성기(MapV2GeneratorTool)의 계층 이름 — 구역 바닥은 Ground|Upper/Zone_<이름>/Floor*,
        // 풀 바닥은 Water/Water_<이름>/PoolBed*. 구역은 트리거가 아니라 좌표 상자라서, 발밑 바닥 콜라이더의
        // 조상 이름으로 구역을 읽는다(2층이 1층과 XZ가 겹쳐도 밟은 바닥이 층을 정한다).
        private const string ZonePrefix = "Zone_";
        private const string WaterPrefix = "Water_";
        private const string UpperGroupName = "Upper";

        private const float ProbeStartHeight = 0.5f;

        /// <summary>풀 바닥(최대 수심 3.5m)까지 닿는 길이.</summary>
        private const float ProbeLength = 8f;

        // QA 라이트 — 선형 색 공간에서 바닥 알베도(sRGB 0.1)가 흑과 구별되는 밝기. 너무 어둡거나 밝으면 이 값만 조정.
        private const float KeyIntensity = 8f;
        private const float FillIntensity = 3f;
        private static readonly Quaternion KeyRotation = Quaternion.Euler(60f, 35f, 0f);

        private static QaDebugOverlay _instance;

        private readonly StringBuilder _sb = new StringBuilder(512);
        private GameObject _overlayRoot;
        private Text _text;
        private GameObject _lightRig;
        private int _groundMask = ~0;
        private float _nextRefresh;
        private int _qaPointIndex = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            var go = new GameObject("[QA] Debug Overlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<QaDebugOverlay>();
            Debug.Log($"[QA] 디버그 오버레이 포함 빌드(MARCO_QA_BUILD) — {OverlayKey} 텍스트 오버레이 · {LightKey} QA 라이트(로컬 전용) · " +
                      $"{TeleportKey} QA 순간이동.");
        }

        private void Awake()
        {
            int soundBlocking = LayerMask.GetMask(PhysicsOcclusionProbe.SoundBlockingLayerName);
            _groundMask = soundBlocking == 0 ? ~0 : ~soundBlocking;

            BuildUi();
            BuildLightRig();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard[OverlayKey].wasPressedThisFrame)
                {
                    _overlayRoot.SetActive(!_overlayRoot.activeSelf);
                    _nextRefresh = 0f;
                }

                if (keyboard[LightKey].wasPressedThisFrame)
                {
                    _lightRig.SetActive(!_lightRig.activeSelf);
                    _nextRefresh = 0f;
                    Debug.Log($"[QA] QA 라이트 {(_lightRig.activeSelf ? "켜짐" : "꺼짐")}(로컬 전용).");
                }

                if (keyboard[TeleportKey].wasPressedThisFrame)
                {
                    TeleportToNextPoint();
                    _nextRefresh = 0f;
                }
            }

            if (!_overlayRoot.activeSelf || Time.unscaledTime < _nextRefresh)
                return;

            _nextRefresh = Time.unscaledTime + RefreshInterval;
            _text.text = Compose();
        }

        private string Compose()
        {
            _sb.Clear();
            _sb.Append("[QA] ").Append(OverlayKey).Append(" 오버레이 · ").Append(LightKey)
               .Append(" QA 라이트 ").Append(_lightRig.activeSelf ? "켜짐" : "꺼짐").Append('\n');
            _sb.Append("씬  ").Append(SceneManager.GetActiveScene().name).Append('\n');
            _sb.Append("QA 지점  ");
            if (_qaPointIndex < 0)
                _sb.Append("— (").Append(TeleportKey).Append("로 순간이동)");
            else
                _sb.Append(_qaPointIndex + 1).Append('/').Append(QaPoints.Length).Append(' ')
                   .Append(QaPoints[_qaPointIndex].Name).Append("  · ").Append(TeleportKey).Append(" 다음");
            _sb.Append('\n');

            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null)
            {
                _sb.Append("로컬 플레이어 없음(스폰 전)");
                return _sb.ToString();
            }

            Vector3 feet = player.transform.position;
            _sb.Append("위치  X ").Append(feet.x.ToString("0.00"))
               .Append("   Y ").Append(feet.y.ToString("0.00"))
               .Append("   Z ").Append(feet.z.ToString("0.00")).Append('\n');

            _sb.Append("구역  ");
            AppendGround(feet);
            _sb.Append('\n');

            // 서버(PulseNetworkSync.ZoneOf)와 같은 지오메트리 식 — 판정이 아니라 표시용 재계산이다.
            WaterSample water = WaterVolumeRegistry.Sample(feet);
            MovementState state = player.CurrentState;
            BreathZone zone = DiveRules.ZoneOf(water, feet.y, diving: state == MovementState.Diving);
            _sb.Append("BreathZone  ").Append(zone).Append("  (로컬 판정 · 이동 ").Append(state).Append(")\n");

            _sb.Append("물  ");
            if (water.BodyInWater)
            {
                _sb.Append("수면 Y ").Append(water.SurfaceY.ToString("0.00"))
                   .Append(" · 바닥 Y ").Append(water.BedY.ToString("0.00"))
                   .Append(" · 수심 ").Append(water.Depth.ToString("0.00"));
            }
            else
            {
                _sb.Append("물 밖");
            }

            _sb.Append('\n');

            _sb.Append("숨(서버 사본)  ").Append(BreathClientState.Remaining.ToString("0.0"))
               .Append(" / ").Append(BreathConfig.TotalSeconds.ToString("0.0"))
               .Append("초 (").Append((BreathClientState.Normalized * 100f).ToString("0")).Append("%)")
               .Append(" · 잠수 ").Append(BreathClientState.Submerged ? "예" : "아니오")
               .Append(" · 질식 감속 ").Append(BreathClientState.ChokePenaltyActive ? "예" : "아니오");

            return _sb.ToString();
        }

        /// <summary>
        /// 다음 QA 지점으로 로컬 플레이어를 옮긴다. <c>PawnPhaseTeleporter.PlaceExactly</c>와 같다 — CharacterController가
        /// 켜져 있으면 위치 대입이 무시될 수 있어 잠깐 끈다. 시선은 yaw만 맞춘다(pitch는 마우스 상태 그대로).
        /// </summary>
        private void TeleportToNextPoint()
        {
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null)
            {
                Debug.Log("[QA] 순간이동 — 로컬 플레이어가 아직 없습니다(스폰 전).");
                return;
            }

            _qaPointIndex = (_qaPointIndex + 1) % QaPoints.Length;
            QaPoint point = QaPoints[_qaPointIndex];

            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled)
                controller.enabled = false;

            player.transform.SetPositionAndRotation(point.Feet, Quaternion.Euler(0f, point.Yaw, 0f));

            if (wasEnabled)
                controller.enabled = true;

            Debug.Log($"[QA] 순간이동 {_qaPointIndex + 1}/{QaPoints.Length} {point.Name} → " +
                      $"({point.Feet.x:0.00}, {point.Feet.y:0.00}, {point.Feet.z:0.00}) yaw {point.Yaw:0}");
        }

        /// <summary>발밑 바닥 콜라이더 → 조상의 <c>Zone_</c> / <c>Water_</c> 이름. 없으면 콜라이더 이름만.</summary>
        private void AppendGround(Vector3 feet)
        {
            if (!Physics.Raycast(feet + Vector3.up * ProbeStartHeight, Vector3.down, out RaycastHit hit,
                    ProbeLength, _groundMask, QueryTriggerInteraction.Ignore))
            {
                _sb.Append("발밑 콜라이더 없음");
                return;
            }

            string label = "구역 밖";
            for (Transform t = hit.collider.transform; t != null; t = t.parent)
            {
                string name = t.name;
                if (name.StartsWith(ZonePrefix, StringComparison.Ordinal))
                {
                    bool upper = t.parent != null && t.parent.name == UpperGroupName;
                    label = name.Substring(ZonePrefix.Length) + (upper ? " (2층)" : "");
                    break;
                }

                if (name.StartsWith(WaterPrefix, StringComparison.Ordinal))
                {
                    label = name.Substring(WaterPrefix.Length) + " (풀 바닥)";
                    break;
                }
            }

            _sb.Append(label).Append("  ← ").Append(hit.collider.name)
               .Append(" (바닥 Y ").Append(hit.point.y.ToString("0.00")).Append(')');
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("QA Canvas");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000; // 어떤 화면(설정 400 · 메인 메뉴 300)보다 위 — 암전 · 전면 흑 Image와 무관하게 보인다

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _overlayRoot = new GameObject("QA Text");
            _overlayRoot.transform.SetParent(canvasGo.transform, worldPositionStays: false);

            var rect = _overlayRoot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);
            rect.sizeDelta = new Vector2(1100f, 400f);

            _text = _overlayRoot.AddComponent<Text>();
            _text.font = ResolveFont();
            _text.fontSize = 22;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.color = new Color(0.55f, 1f, 0.55f, 1f);
            _text.raycastTarget = false;

            // QA 라이트를 켜면 배경이 밝아진다 — 외곽선으로 어느 배경에서도 읽히게.
            var outline = _overlayRoot.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            _overlayRoot.SetActive(false);
        }

        /// <summary>
        /// 서로 반대 방향의 방향광 2개 — 축 정렬 면 6방향이 전부 한쪽 빛은 받는다. 그림자 없음.
        /// 씬 밖(DontDestroyOnLoad) 오브젝트라 씬 전환에도 켜진 상태가 유지되고, 씬 조명 값은 그대로다.
        /// </summary>
        private void BuildLightRig()
        {
            _lightRig = new GameObject("QA Light Rig");
            _lightRig.transform.SetParent(transform, worldPositionStays: false);

            AddDirectional("QA Key Light", KeyRotation, KeyIntensity);
            AddDirectional("QA Fill Light", Quaternion.LookRotation(KeyRotation * Vector3.back), FillIntensity);

            _lightRig.SetActive(false);
        }

        private void AddDirectional(string name, Quaternion rotation, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_lightRig.transform, worldPositionStays: false);
            go.transform.rotation = rotation;

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        private static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 22);

            if (font == null)
                Debug.LogWarning("[QA] 빌트인 폰트를 찾지 못했습니다 — 오버레이 텍스트가 보이지 않습니다.");

            return font;
        }
    }
}
#endif
