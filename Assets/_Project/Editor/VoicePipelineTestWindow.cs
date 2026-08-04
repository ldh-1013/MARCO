using Marco.Core.Voice;
using UnityEditor;
using UnityEngine;

namespace Marco.EditorTools
{
    /// <summary>
    /// §5.2 음성 분류를 **Play 모드 없이** 확인하는 에디터 창(스프린트 26a).
    ///
    /// **왜 필요한가**: 음성 확인에 exe 빌드나 두 번째 클라이언트가 필요 없다 —
    /// 분류는 네트워크와 무관한 순수 계산이고(<see cref="VoiceClassifier"/>), 캡처도
    /// Unity <see cref="Microphone"/>이라 에디터에서 그대로 돈다. 임계값을 만지며
    /// 반복 확인할 때 Play 진입·종료를 오가는 것이 가장 큰 낭비라 그 고리를 끊는다.
    ///
    /// <see cref="EditorApplication.update"/>로 폴링하며, 창을 닫으면 캡처를 정지한다.
    /// **Play 중에는 시작하지 않는다** — 같은 마이크를 <c>LocalVoicePipeline</c>과
    /// 동시에 잡으면 둘 다 불안정해진다.
    /// </summary>
    public sealed class VoicePipelineTestWindow : EditorWindow
    {
        private const int HistorySize = 60;

        private AudioClip _clip;
        private string _device;
        private float[] _buffer;
        private bool _capturing;
        private string _status = "정지됨";

        private float _dbfs = VoiceConfig.MinDbfs;
        private VoiceGrade _grade = VoiceGrade.Silence;
        private float _gainDb = VoiceConfig.DefaultInputGainDb;
        private bool _logToConsole = true;
        private VoiceGrade _lastLoggedGrade = VoiceGrade.Silence;

        private readonly float[] _history = new float[HistorySize];
        private int _historyHead;

        [MenuItem("Tools/MARCO/Voice Pipeline Test", priority = 208)]
        public static void Open()
        {
            var window = GetWindow<VoicePipelineTestWindow>("Voice Test");
            window.minSize = new Vector2(420f, 320f);
            window.Show();
        }

        private void OnEnable() => EditorApplication.update += Poll;

        private void OnDisable()
        {
            EditorApplication.update -= Poll;
            StopCapture();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("§5.2 음성 파이프라인 테스트", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Play 없이 마이크 → RMS → dBFS → §5.1 3등급 분류를 확인합니다.\n" +
                "1단계 범위라 캘리브레이션·대역 필터·연속성·디바운스는 아직 없습니다(GAP-51) — 오탐이 나오는 것이 정상입니다.",
                MessageType.Info);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                StopCapture();
                EditorGUILayout.HelpBox(
                    "Play 중에는 이 창이 마이크를 잡지 않습니다 — 씬의 LocalVoicePipeline과 충돌하기 때문입니다.\n" +
                    "Play 중에는 화면 좌하단 HUD의 🎤 표시나 [Voice] 로그로 확인하세요.",
                    MessageType.Warning);
                return;
            }

            DrawDeviceSection();
            EditorGUILayout.Space();
            DrawLevelSection();
            EditorGUILayout.Space();
            DrawThresholdReference();

            Repaint();
        }

        private void DrawDeviceSection()
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                EditorGUILayout.HelpBox("마이크 장치를 찾지 못했습니다. 장치를 연결한 뒤 창을 다시 여세요.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("장치", devices[0] + (devices.Length > 1 ? $" 외 {devices.Length - 1}개" : ""));

            _gainDb = EditorGUILayout.Slider(
                new GUIContent("입력 게인 (dB)", "§12.6 오디오 탭 −20~+20dB. 이 창에서만 쓰는 값이며 설정에 저장되지 않습니다."),
                _gainDb, VoiceConfig.MinInputGainDb, VoiceConfig.MaxInputGainDb);

            _logToConsole = EditorGUILayout.Toggle(
                new GUIContent("등급 변화를 Console에 기록", "등급이 바뀔 때만 남깁니다."), _logToConsole);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!_capturing && GUILayout.Button("캡처 시작", GUILayout.Height(28f)))
                    StartCapture();

                if (_capturing && GUILayout.Button("정지", GUILayout.Height(28f)))
                    StopCapture();
            }

            EditorGUILayout.LabelField("상태", _status);
        }

        private void DrawLevelSection()
        {
            EditorGUILayout.LabelField("판정", EditorStyles.boldLabel);

            // dBFS를 0~1로 눌러 진행 막대로 보여준다(−60dBFS를 바닥으로 잡는다).
            float normalized = Mathf.InverseLerp(-60f, 0f, _dbfs);
            Rect bar = EditorGUILayout.GetControlRect(false, 22f);
            EditorGUI.ProgressBar(bar, normalized, $"{_dbfs:0.0} dBFS");

            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            EditorGUILayout.LabelField(_grade.ToString(), style);
        }

        private void DrawThresholdReference()
        {
            EditorGUILayout.LabelField("§5.1 기준", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"  속삭임  {VoiceConfig.WhisperFloorDbfs:0} ~ {VoiceConfig.TalkFloorDbfs:0} dBFS  (반경 4m)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  대화    {VoiceConfig.TalkFloorDbfs:0} ~ {VoiceConfig.ShoutFloorDbfs:0} dBFS  (반경 9m)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  고함    {VoiceConfig.ShoutFloorDbfs:0} dBFS 이상          (반경 22m)", EditorStyles.miniLabel);
        }

        private void StartCapture()
        {
            if (_capturing || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                _status = "마이크 장치 없음";
                return;
            }

            _device = null; // 시스템 기본 장치
            _clip = Microphone.Start(_device, loop: true, lengthSec: 1, frequency: VoiceConfig.SampleRateHz);
            if (_clip == null)
            {
                _status = "Microphone.Start 실패(권한 거부 또는 장치 점유)";
                return;
            }

            _buffer = new float[VoiceConfig.FrameSamples];
            _capturing = true;
            _status = $"캡처 중 — {_clip.frequency}Hz";
        }

        private void StopCapture()
        {
            if (!_capturing)
                return;

            Microphone.End(_device);
            _capturing = false;
            _clip = null;
            _status = "정지됨";
            _dbfs = VoiceConfig.MinDbfs;
            _grade = VoiceGrade.Silence;
        }

        private void Poll()
        {
            if (!_capturing || _clip == null)
                return;

            int position = Microphone.GetPosition(_device);
            int start = position - VoiceConfig.FrameSamples;
            if (start < 0)
                return; // 아직 한 프레임이 안 쌓였다.

            if (!_clip.GetData(_buffer, start))
                return;

            _grade = VoiceClassifier.ClassifySamples(_buffer, _gainDb, out _dbfs);

            _history[_historyHead] = _dbfs;
            _historyHead = (_historyHead + 1) % HistorySize;

            if (_logToConsole && _grade != _lastLoggedGrade)
            {
                _lastLoggedGrade = _grade;
                Debug.Log($"[VoiceTest] {_grade} ({_dbfs:0.0} dBFS, 게인 {_gainDb:+0.#;-0.#;0} dB)");
            }
        }
    }
}
