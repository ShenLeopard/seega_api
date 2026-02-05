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
        public const int SIZE = 65536;
        public const int MASK = SIZE - 1;
        public const int LOCK_COUNT = 256;

        public TTEntry[] Entries = new TTEntry[SIZE];
        public readonly object[] Locks;

        public GameTTContext()
        {
            Locks = new object[LOCK_COUNT];
            for (int i = 0; i < LOCK_COUNT; i++)
                Locks[i] = new object();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetLock(int index) => Locks[index % LOCK_COUNT];
    }
}