using System.Runtime.CompilerServices;

namespace SeegaGame.Services
{
    public struct TTEntry
    {
        public long Key;
        public int Score;
        public short BestMove;
        public byte Depth;
        public byte Flag;
    }

    public class GameTTContext
    {
        // ★ 修正：加大到 2^20 (約 100 萬個項目)，佔用記憶體約 24MB
        // 這對於 Move 75 這種空曠盤面至關重要，能有效減少重複搜尋
        public const int SIZE = 1048576;
        public const int MASK = SIZE - 1;
        public const int LOCK_COUNT = 1024; // 稍微增加鎖數量

        public TTEntry[] Entries = new TTEntry[SIZE];
        public readonly object[] Locks;

        public GameTTContext()
        {
            Locks = new object[LOCK_COUNT];
            for (int i = 0; i < LOCK_COUNT; i++)
                Locks[i] = new object();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetLock(int index) => Locks[index & (LOCK_COUNT - 1)];
    }
}