using System.Runtime.CompilerServices;
using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreTT(GameTTContext ctx, long h, int d, int score, int flag, Move? m)
        {
            int index = (int)(h & GameTTContext.MASK);
            lock (ctx.GetLock(index))
            {
                ref TTEntry entry = ref ctx.Entries[index];
                // 深度優先替換
                if (entry.Key == 0 || entry.Key == h || d >= entry.Depth)
                {
                    entry.Key = h;
                    entry.Score = score;
                    entry.Depth = (byte)d;
                    entry.Flag = (byte)flag;
                    entry.BestMove = EncodeMove(m);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ProbeTT(GameTTContext ctx, long h, int d, int alpha, int beta, out int score, out Move? bestMove)
        {
            int index = (int)(h & GameTTContext.MASK);
            score = 0;
            bestMove = null;

            // 無鎖讀取（性能優化）
            ref TTEntry entry = ref ctx.Entries[index];

            if (entry.Key == h)
            {
                bestMove = DecodeMove(entry.BestMove);

                if (entry.Depth >= d)
                {
                    if (entry.Flag == 0) { score = entry.Score; return true; }
                    if (entry.Flag == 1 && entry.Score <= alpha) { score = alpha; return true; }
                    if (entry.Flag == 2 && entry.Score >= beta) { score = beta; return true; }
                }
            }

            return false;
        }

        private short EncodeMove(Move? m)
        {
            if (m == null) return 0;
            byte f = (m.From == null) ? (byte)255 : (byte)(m.From.R * 5 + m.From.C);
            byte t = (byte)(m.To.R * 5 + m.To.C);
            return (short)((f << 8) | t);
        }

        private Move? DecodeMove(short v)
        {
            if (v == 0) return null;
            byte f = (byte)((v >> 8) & 0xFF);
            byte t = (byte)(v & 0xFF);
            return new Move
            {
                From = (f == 255 ? null : new Position { R = f / 5, C = f % 5 }),
                To = new Position { R = t / 5, C = t % 5 }
            };
        }
    }
}