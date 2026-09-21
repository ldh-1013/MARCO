using System.Collections.Generic;
using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §10.5 출구(정문·배수로) 위치를 Net 레이어가 어셈블리 경계를 넘어 읽게 해주는
    /// 지연 바인딩 지점. <c>EscapeGateRegistry</c>·<c>WaterVolumeRegistry</c>와 같은 패턴이다.
    ///
    /// <para>
    /// <b>왜 필요한가</b>: §6.2-1 종반 압박이 *"잔여 30초에 양쪽 출구에서 고함급 파문"* 을
    /// 요구하는데, 그 발행 주체는 라운드 상태기계(<c>RoundNetworkSync</c>, Net)이고
    /// 출구 오브젝트는 Presentation(<c>EscapePointTrigger</c>)에 있다. §15.2상 Net은
    /// Presentation의 구체 타입을 찾을 수 없다.
    /// </para>
    ///
    /// <para>
    /// <b>좌표를 코드에 박지 않는 이유</b>: §10.5 출구 좌표(7,40)·(46,2)를 Net에 적으면
    /// §10.1 맵 데이터와 갈라진다. 씬에 실제로 놓인 출구를 읽는 쪽이 항상 맞다.
    /// </para>
    /// </summary>
    public static class EscapePointRegistry
    {
        private static readonly List<Transform> Points = new List<Transform>();
        private static readonly List<Vector3> PositionBuffer = new List<Vector3>();

        /// <summary>등록된 출구 수. 진단용.</summary>
        public static int Count => Points.Count;

        /// <summary>
        /// 등록된 출구들의 현재 위치. <b>호출마다 같은 버퍼를 다시 채워 돌려준다</b> —
        /// 종반 30초 구간에서 주기적으로 도는 경로라 매번 리스트를 할당하지 않는다.
        /// 파괴된 오브젝트는 건너뛴다.
        /// </summary>
        public static List<Vector3> Positions
        {
            get
            {
                PositionBuffer.Clear();
                for (int i = 0; i < Points.Count; i++)
                {
                    if (Points[i] != null)
                        PositionBuffer.Add(Points[i].position);
                }

                return PositionBuffer;
            }
        }

        public static void Register(Transform point)
        {
            if (point != null && !Points.Contains(point))
                Points.Add(point);
        }

        public static void Unregister(Transform point)
        {
            Points.Remove(point);
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 파괴된 이전 판의 출구가 남는다.
        /// 진입 시 비운다(다른 레지스트리들과 같은 안전장치).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            Points.Clear();
            PositionBuffer.Clear();
        }
    }
}
