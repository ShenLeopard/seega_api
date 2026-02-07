using SeegaGame.Models;
using Microsoft.Extensions.Caching.Memory;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private readonly GameService _gs;
        private readonly IMemoryCache _cache;

        // Zobrist 雜湊表
        private static readonly long[,,] ZobristPiece;
        private static readonly long SideHash;

        // 評分常數
        protected const int WIN = 1000000;
        protected const int MAT = 2000;
        protected const int STUCK_ADVANTAGE = 2500;
        protected const int CEN = 60;
        protected const int FIRST_MOVE_BONUS = 6000;
        protected const int SUFFOCATE_BONUS = 3000;
        protected const int MOBILITY_LIGHT = 8;

        static AiService()
        {
            var rand = new Random(1688);
            ZobristPiece = new long[5, 5, 2];

            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    ZobristPiece[r, c, 0] = RL(rand);
                    ZobristPiece[r, c, 1] = RL(rand);
                }

            SideHash = RL(rand);
        }

        private static long RL(Random r)
        {
            byte[] b = new byte[8];
            r.NextBytes(b);
            return BitConverter.ToInt64(b, 0);
        }

        public AiService(GameService gs, IMemoryCache cache)
        {
            _gs = gs;
            _cache = cache;
        }

        private GameTTContext GetContext(string uuid) =>
            _cache.GetOrCreate(uuid, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromMinutes(30);
                return new GameTTContext();
            })!;

        // ===== 主入口 =====
        public Move? GetBestMove(AiMoveRequest req)
        {
            req.LastMoveX = ValidateLastMove(req.Board, req.LastMoveX, "X");
            req.LastMoveO = ValidateLastMove(req.Board, req.LastMoveO, "O");

            var ctx = GetContext(req.GameUUId);
            long h = InitialHash(req.Board, req.CurrentPlayer);

            if (req.Phase == GamePhase.STUCK_REMOVAL)
            {
                return _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, null, null)
                    .OrderByDescending(m => EvaluateRemovalMove(req.Board, m, req.CurrentPlayer, req.LastMoveX, req.LastMoveO))
                    .FirstOrDefault();
            }

            var moves = _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, req.LastMoveX, req.LastMoveO);
            if (!moves.Any()) return null;

            // 計算子力
            int myCount = 0, opCount = 0;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    if (req.Board[r][c] == req.CurrentPlayer) myCount++;
                    else if (req.Board[r][c] != null) opCount++;
                }

            // 計算深度
            int d = req.Difficulty;

            if (req.Phase == GamePhase.PLACEMENT)
            {
                if (req.MoveIndex == 24)
                    d = Math.Max(req.Difficulty, 6);
                else
                    d = (req.MoveIndex >= 18) ? Math.Min(7, (24 - req.MoveIndex) + 2) : 3;
            }
            else if (req.Phase == GamePhase.MOVEMENT)
            {
                int diff = myCount - opCount;

                // ===== 修正：統一使用深度 2（快速且有效） =====

                // 對手 2-6 顆：深度 2（配合 Quiesce 和 Move Ordering 足夠找到好棋）
                if (opCount >= 2 && opCount <= 6)
                {
                    d = 4; // 4 層才能完整看清「誘敵 -> 對手動 -> 我吃子」的過程
                }
                // 對手 7-8 顆且領先 4 顆以上
                else if (opCount <= 8 && diff >= 4)
                {
                    d = 3;
                }
                // 對手只剩 1 顆或領先 10 顆以上
                else if (opCount == 1 || diff >= 10)
                {
                    d = 1;
                }
                // 均勢或劣勢
                else
                {
                    d = req.Difficulty;
                }
            }

            return RootSearch(ctx, req, h, d, moves);
        }
        // ===== 輔助方法 =====
        protected bool In(int r, int c) => r >= 0 && r < 5 && c >= 0 && c < 5;

        private bool IsSameMove(Move? a, Move? b)
        {
            if (a == null || b == null) return false;
            if (a.From == null && b.From == null) return a.To.R == b.To.R && a.To.C == b.To.C;
            if (a.From != null && b.From != null) 
                return a.From.R == b.From.R && a.From.C == b.From.C && 
                       a.To.R == b.To.R && a.To.C == b.To.C;
            return false;
        }
    }
}