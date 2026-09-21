using System;
using System.Collections.Generic;

namespace Marco.Core.Util
{
    /// <summary>
    /// 시드를 주입받는 결정론적 난수. <b>Core는 <c>UnityEngine.Random</c>을 쓸 수 없다</b>
    /// (§15.2 계층 경계 — 전역 상태이고 EditMode 테스트에서 고정할 수 없다).
    ///
    /// <para>
    /// 쓰이는 곳은 두 군데다 — §6.1-0 매 라운드 활성 밸브 선택, §6.5-2 활성 배수구 선택.
    /// 둘 다 <b>서버만</b> 굴리고, 결과를 네트워크로 알린다(클라이언트가 같은 시드로
    /// 다시 굴리게 만들지 않는다 — 그러면 서버 권위가 아니라 합의가 된다).
    /// </para>
    ///
    /// <para>
    /// xorshift32다. 암호학적 용도가 아니고, 5개 중 4개 고르기에 필요한 것은
    /// "시드가 같으면 같은 결과, 다르면 다른 결과"뿐이다.
    /// </para>
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            // 0은 xorshift의 고정점이라 영원히 0을 내놓는다. 0이 들어오면 옮긴다.
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        /// <summary>다음 난수(0 이상).</summary>
        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        /// <summary>[0, exclusiveMax) 정수. exclusiveMax가 0 이하면 0.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
                return 0;

            return (int)(NextUInt() % (uint)exclusiveMax);
        }

        /// <summary>
        /// 리스트에서 <paramref name="count"/>개를 <b>중복 없이</b> 고른다(부분 Fisher–Yates).
        /// 원본을 건드리지 않고 새 리스트를 돌려준다. count가 원본보다 크면 전부 돌려준다.
        /// </summary>
        public List<T> Choose<T>(IReadOnlyList<T> source, int count)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var pool = new List<T>(source);
            if (count >= pool.Count)
                return pool;

            for (int i = 0; i < count; i++)
            {
                int j = i + NextInt(pool.Count - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            pool.RemoveRange(count, pool.Count - count);
            return pool;
        }
    }
}
