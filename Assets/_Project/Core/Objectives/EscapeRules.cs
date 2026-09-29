using System.Collections.Generic;
using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// 출구 판정 규칙(09-30) — 반경 상수 <b>하나</b>를 출구 트리거(<c>EscapePointTrigger</c>, 클라이언트)와 서버(태그 대신 탈출 판정)가
    /// 같이 쓴다. 전에는 반경이 트리거의 인스펙터 값(2m, GAP-12 잠정)뿐이라 서버가 "출구에 닿았는가"를 셀 수 없었다.
    /// </summary>
    public static class EscapeRules
    {
        /// <summary>출구 판정 반경(m) — 발(transform.position)과 출구 중심의 3D 거리. GAP-12 잠정값(기획서에 수치 없음).</summary>
        public const float ExitRadiusMeters = 2f;

        /// <summary>이 발 위치가 출구 판정 반경 안인가(경계 포함).</summary>
        public static bool IsWithinExit(Vector3 feet, Vector3 exit) => Vector3.Distance(feet, exit) <= ExitRadiusMeters;

        /// <summary>어느 출구든 판정 반경 안인가.</summary>
        public static bool IsWithinAnyExit(Vector3 feet, IReadOnlyList<Vector3> exits)
        {
            if (exits == null)
                return false;

            for (int i = 0; i < exits.Count; i++)
            {
                if (IsWithinExit(feet, exits[i]))
                    return true;
            }

            return false;
        }
    }
}
