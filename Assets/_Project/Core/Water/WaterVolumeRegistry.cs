using System.Collections.Generic;
using UnityEngine;

namespace Marco.Core.Water
{
    /// <summary>
    /// §10.1 수면(물) 영역 하나. 레벨 지오메트리의 사실만 답한다 — 규칙은 여기 없다.
    ///
    /// <para>
    /// <b>수심을 지점별로 묻는 이유</b>: §6.5-2가 유아풀 배수구 지점만 국소 침강부(sump)
    /// 3.5m로 파도록 정했다. 수면 하나에 수심이 하나라고 가정하면 그 침강부를 표현할 수 없다.
    /// </para>
    /// </summary>
    public interface IWaterVolume
    {
        /// <summary>이 지점이 수면 영역의 <b>수평 범위</b> 안인가(높이는 보지 않는다).</summary>
        bool ContainsHorizontally(Vector3 point);

        /// <summary>수면 높이(월드 Y).</summary>
        float SurfaceY { get; }

        /// <summary>이 지점의 <b>바닥</b> 높이(월드 Y). 국소 침강부에서는 더 낮다.</summary>
        float BedYAt(Vector3 point);
    }

    /// <summary>§5.9-1 숨 상태를 판정하는 데 필요한 지오메트리 관측 결과.</summary>
    public readonly struct WaterSample
    {
        /// <summary>몸이 물 안인가 — §5.9-1 "물 밖: 콜라이더가 물 볼륨과 완전 분리"의 반대.</summary>
        public readonly bool BodyInWater;

        /// <summary>수면 높이. <see cref="BodyInWater"/>가 false면 의미 없다.</summary>
        public readonly float SurfaceY;

        /// <summary>이 지점 바닥 높이. <see cref="BodyInWater"/>가 false면 의미 없다.</summary>
        public readonly float BedY;

        public WaterSample(bool bodyInWater, float surfaceY, float bedY)
        {
            BodyInWater = bodyInWater;
            SurfaceY = surfaceY;
            BedY = bedY;
        }

        /// <summary>이 지점의 수심(m). 물 밖이면 0.</summary>
        public float Depth => BodyInWater ? SurfaceY - BedY : 0f;

        public static readonly WaterSample OutOfWater = new WaterSample(false, 0f, 0f);
    }

    /// <summary>
    /// 씬의 수면 영역을 Net·Core가 어셈블리 경계를 넘어 조회하게 해주는 지연 바인딩 지점.
    /// <c>EscapeGateRegistry</c>·<c>TagTargetRegistry</c>·<c>LocalPlayerRegistry</c>와 같은 패턴이다.
    ///
    /// <para>
    /// <b>왜 레지스트리인가</b>: 서버(<c>PulseNetworkSync.TickBreath</c>, Net)가 각 플레이어의
    /// §5.9-1 숨 상태를 직접 재계산해야 하는데, §15.2상 Net은 Presentation의 구체 타입을
    /// <c>FindObjectsByType&lt;T&gt;()</c>로 찾을 수 없다. 수면 영역이 스스로 등록하면
    /// 서버는 Core 인터페이스만 본다.
    /// </para>
    ///
    /// <para>
    /// <b>서버 권위(GAP-24)</b>: 여기 담긴 것은 전부 <b>레벨 지오메트리</b>다 — 클라이언트가
    /// 주장한 값이 하나도 없다. 서버는 동기화된 위치를 이 지오메트리에 대고 재계산하므로,
    /// "나는 물속이다"는 클라이언트 주장을 믿을 일이 없다.
    /// </para>
    ///
    /// <para>
    /// 수면은 여러 개다(§10.1 메인 풀 + 유아풀)므로 <c>Current</c> 단일 슬롯이 아니라 목록이다.
    /// </para>
    /// </summary>
    public static class WaterVolumeRegistry
    {
        private static readonly List<IWaterVolume> Volumes = new List<IWaterVolume>();

        /// <summary>등록된 수면 개수. 진단용.</summary>
        public static int Count => Volumes.Count;

        public static void Register(IWaterVolume volume)
        {
            if (volume != null && !Volumes.Contains(volume))
                Volumes.Add(volume);
        }

        public static void Unregister(IWaterVolume volume)
        {
            Volumes.Remove(volume);
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 static 상태가 남는다.
        /// 파괴된 이전 판의 볼륨을 물지 않도록 진입 시 비운다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            Volumes.Clear();
        }

        /// <summary>
        /// 발 위치로 수면을 조회한다. 수평 범위 안이고 <b>발이 수면보다 낮으면</b> 물 안이다.
        ///
        /// <para>
        /// 겹친 수면이 있으면 <b>수면이 더 높은 쪽</b>을 택한다 — 국소 침강부를 별도 볼륨으로
        /// 깔았을 때 둘 다 수평으로 포함될 수 있고, 그때 얕은 쪽을 골라 잠수가 막히면
        /// §6.5-2가 조용히 무너진다. 같은 수면 높이면 <b>더 깊은 바닥</b>을 택한다.
        /// </para>
        /// </summary>
        public static WaterSample Sample(Vector3 feetPosition)
        {
            var best = WaterSample.OutOfWater;

            for (int i = 0; i < Volumes.Count; i++)
            {
                IWaterVolume v = Volumes[i];
                if (v == null || !v.ContainsHorizontally(feetPosition))
                    continue;

                float surfaceY = v.SurfaceY;
                if (feetPosition.y >= surfaceY)
                    continue; // 발이 수면 위 — 덱에 서 있다

                float bedY = v.BedYAt(feetPosition);
                if (!best.BodyInWater || surfaceY > best.SurfaceY ||
                    (Mathf.Approximately(surfaceY, best.SurfaceY) && bedY < best.BedY))
                {
                    best = new WaterSample(true, surfaceY, bedY);
                }
            }

            return best;
        }
    }
}
