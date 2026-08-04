using Marco.Core.Voice;
using UnityEngine;

namespace Marco.Presentation.Voice
{
    /// <summary>
    /// §5.2-1 "캡처" 단계의 Unity <see cref="Microphone"/> 래퍼(스프린트 26a).
    ///
    /// **Steamworks Voice API가 아니라 Unity Microphone을 쓰는 이유**: §5.2 원문은 Steamworks를
    /// 명시하지만 Steam 통합은 §13이고 Phase 4 후반(스프린트 31) 작업이다. 그때까지 음성 루프
    /// 전체를 막아 두는 것보다, 분류·판정을 먼저 세우고 캡처 계층만 나중에 교체하는 편이 낫다.
    /// 이 클래스가 **교체 지점**이며 상위(<see cref="LocalVoicePipeline"/>)는 캡처 구현을 모른다(GAP-49).
    ///
    /// **마이크가 없거나 권한이 거부돼도 게임은 그대로 성립해야 한다** — 밸브·태그·라운드는
    /// §5.2와 무관하다. 실패는 조용히 비활성으로 처리하고 로그 한 줄만 남긴다.
    ///
    /// Unity <see cref="Microphone"/>은 <see cref="AudioClip"/>을 **링 버퍼**로 계속 덮어쓴다.
    /// 그래서 "마지막으로 읽은 위치"를 들고 다니며 새로 들어온 구간만 꺼내야 한다.
    /// </summary>
    public sealed class MicrophoneCapture
    {
        private const int ClipLengthSeconds = 1; // 링 버퍼 길이. 20ms 프레임을 읽기에 충분하다.

        private string _device;
        private AudioClip _clip;
        private int _lastSamplePosition;
        private float[] _readBuffer;

        /// <summary>캡처가 살아 있는가. 마이크 없음·권한 거부면 false로 남는다.</summary>
        public bool IsCapturing { get; private set; }

        /// <summary>실패 사유(진단용). 성공했으면 null.</summary>
        public string FailureReason { get; private set; }

        /// <summary>
        /// 캡처를 시작한다. 이미 시작했으면 아무것도 하지 않는다.
        /// </summary>
        /// <param name="deviceName">null이면 시스템 기본 장치(§12.6 "시스템 기본 장치").</param>
        public bool Start(string deviceName = null)
        {
            if (IsCapturing)
                return true;

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Fail("마이크 장치가 없습니다");
                return false;
            }

            _device = string.IsNullOrEmpty(deviceName) ? null : deviceName;

            // §5.2-1 "16kHz PCM". 장치가 그 주파수를 지원하지 않으면 Unity가 가까운 값으로 맞춘다.
            _clip = Microphone.Start(_device, loop: true, lengthSec: ClipLengthSeconds, frequency: VoiceConfig.SampleRateHz);
            if (_clip == null)
            {
                Fail("Microphone.Start가 실패했습니다(권한 거부 또는 장치 점유)");
                return false;
            }

            _readBuffer = new float[VoiceConfig.FrameSamples];
            _lastSamplePosition = 0;
            IsCapturing = true;
            FailureReason = null;

            Debug.Log($"[Voice] 마이크 캡처 시작 — 장치='{(_device ?? Microphone.devices[0])}', " +
                      $"{_clip.frequency}Hz, 채널 {_clip.channels} (§5.2-1 목표 {VoiceConfig.SampleRateHz}Hz)");
            return true;
        }

        public void Stop()
        {
            if (!IsCapturing)
                return;

            Microphone.End(_device);
            IsCapturing = false;
            _clip = null;
            Debug.Log("[Voice] 마이크 캡처 정지");
        }

        /// <summary>
        /// 링 버퍼에서 **아직 읽지 않은 최신 구간**을 꺼낸다. 한 프레임(§5.2-1 20ms) 분량이
        /// 쌓이지 않았으면 false를 돌려주고 아무것도 소비하지 않는다.
        /// </summary>
        public bool TryReadFrame(out float[] samples, out int sampleCount)
        {
            samples = _readBuffer;
            sampleCount = 0;

            if (!IsCapturing || _clip == null)
                return false;

            int position = Microphone.GetPosition(_device);
            if (position < 0)
                return false;

            int available = position - _lastSamplePosition;
            if (available < 0)
                available += _clip.samples; // 링 버퍼가 한 바퀴 돌았다.

            if (available < VoiceConfig.FrameSamples)
                return false;

            // 뒤처졌으면 최신 한 프레임만 보고 나머지는 버린다 — 지연된 음성을 뒤늦게
            // 파문으로 만들면 §2.1의 "0~80ms 즉각 피드백" 원칙이 깨진다.
            int readStart = position - VoiceConfig.FrameSamples;
            if (readStart < 0)
                readStart += _clip.samples;

            if (!_clip.GetData(_readBuffer, readStart))
                return false;

            _lastSamplePosition = position;
            sampleCount = VoiceConfig.FrameSamples;
            return true;
        }

        private void Fail(string reason)
        {
            IsCapturing = false;
            FailureReason = reason;
            Debug.LogWarning($"[Voice] 마이크 캡처를 시작하지 못했습니다 — {reason}. " +
                             "음성만 비활성화되며 밸브·태그·라운드는 그대로 동작합니다(§5.2).");
        }
    }
}
