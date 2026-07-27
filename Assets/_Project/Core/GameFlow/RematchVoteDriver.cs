using System.Collections.Generic;

namespace Marco.Core.GameFlow
{
    /// <summary><see cref="RematchVoteDriver"/>의 판정 상태.</summary>
    public enum RematchVoteResult
    {
        /// <summary>투표 진행 중.</summary>
        Pending,

        /// <summary>과반 찬성 — 즉시 재시작(§12.5, §15.4 RoundEnd → RoleAssign).</summary>
        Passed,

        /// <summary>15초 만료까지 과반 미달 — 로비로(§15.4 RoundEnd → Lobby).</summary>
        Failed
    }

    /// <summary>
    /// §12.5 리매치 투표의 순수 구현(스프린트 18 — GAP-27 해소). 기획서 사양 그대로:
    /// "리매치 투표: **15초 카운트다운, 과반 찬성 시 즉시 재시작**"(§12.5 표 · §6.4 인근 동일 문구).
    /// 결과 분기도 §15.4 표 그대로다: 가결 → RoleAssign(즉시 재시작), 부결 → Lobby.
    ///
    /// **서버 전용**: 투표 집계·판정은 서버만 수행한다(스프린트 10~14 권위 모델 유지).
    /// FishNet도 UnityEngine도 모른다 — EditMode 테스트 가능.
    ///
    /// **과반**: 현재 접속 인원의 절반 초과(<see cref="RequiredVotes"/> = n/2 + 1).
    /// 2인 → 2표, 3인 → 2표, 4인 → 3표. 인원은 판정 시점의 실측 목록으로 계산해,
    /// 투표 중 이탈한 플레이어는 분모에서도 분자에서도 자동으로 빠진다.
    /// </summary>
    public sealed class RematchVoteDriver
    {
        /// <summary>§12.5 "15초 카운트다운".</summary>
        public const float VoteWindowSeconds = 15f;

        private readonly HashSet<ulong> _votes = new HashSet<ulong>();

        public float SecondsRemaining { get; private set; } = VoteWindowSeconds;
        public RematchVoteResult Result { get; private set; } = RematchVoteResult.Pending;

        /// <summary>과반 기준 표 수. 인원 0이면 0(판정 불가 — <see cref="Tick"/>이 통과시키지 않는다).</summary>
        public static int RequiredVotes(int playerCount) => playerCount <= 0 ? 0 : playerCount / 2 + 1;

        /// <summary>찬성 투표. 새로 집계됐을 때만 true(중복 투표 멱등, 종료 후 무시).</summary>
        public bool TryVote(ulong playerId)
        {
            if (Result != RematchVoteResult.Pending)
                return false;

            return _votes.Add(playerId);
        }

        /// <summary>현재 접속자 기준 유효 찬성 수(이탈자의 표는 세지 않는다).</summary>
        public int CountVotes(IReadOnlyList<ulong> livePlayers)
        {
            int count = 0;
            for (int i = 0; i < livePlayers.Count; i++)
            {
                if (_votes.Contains(livePlayers[i]))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 매 서버 틱 호출. 과반 도달 즉시 <see cref="RematchVoteResult.Passed"/>(§12.5 "즉시 재시작"),
        /// 15초 만료 시 <see cref="RematchVoteResult.Failed"/>. 한 번 결정되면 고정(latch)된다.
        /// </summary>
        public RematchVoteResult Tick(IReadOnlyList<ulong> livePlayers, float deltaSeconds)
        {
            if (Result != RematchVoteResult.Pending)
                return Result;

            int live = livePlayers.Count;
            if (live > 0 && CountVotes(livePlayers) >= RequiredVotes(live))
            {
                Result = RematchVoteResult.Passed;
                return Result;
            }

            SecondsRemaining -= deltaSeconds;
            if (SecondsRemaining <= 0f)
            {
                SecondsRemaining = 0f;
                Result = RematchVoteResult.Failed;
            }

            return Result;
        }
    }
}
