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
        private static readonly long PhaseToggleHash; // 用於區分 PLACEMENT(0) 與 MOVEMENT(1)

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
            PhaseToggleHash = RL(rand); // 產生單一階段切換開關
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
            long h = InitialHash(req.Board, req.CurrentPlayer, req.Phase);

            if (req.Phase == GamePhase.STUCK_REMOVAL)
            {
                return _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, null, null)
                    .OrderByDescending(m => EvaluateRemovalMove(req.Board, m, req.CurrentPlayer, req.LastMoveX, req.LastMoveO))
                    .FirstOrDefault();
            }

            int d = (req.Phase == GamePhase.PLACEMENT && req.MoveIndex >= 18) 
                ? Math.Min(7, (24 - req.MoveIndex) + 2) 
                : req.Difficulty;

            return RootSearch(ctx, req, h, d);
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