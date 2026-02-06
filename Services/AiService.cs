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
            long h = InitialHash(req.Board, req.CurrentPlayer);  // 移除 req.Phase 參數

            // 1. 處理受困移除模式
            if (req.Phase == GamePhase.STUCK_REMOVAL)
            {
                return _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, null, null)
                    .OrderByDescending(m => EvaluateRemovalMove(req.Board, m, req.CurrentPlayer, req.LastMoveX, req.LastMoveO))
                    .FirstOrDefault();
            }

            // 2. 取得合法步
            var moves = _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, req.LastMoveX, req.LastMoveO);
            if (!moves.Any()) return null;

            // 3. 絕殺偵測：如果在移動階段能一步獲勝，直接回傳
            if (req.Phase == GamePhase.MOVEMENT)
            {
                foreach (var m in moves)
                {
                    var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                    if (_gs.CheckWinner(req.Board) == req.CurrentPlayer)
                    {
                        _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);
                        return m;
                    }
                    _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);
                }
            }

            // 4. 計算深度並開始搜尋 (包含第 24 手的深度優化)
            int d = CalculateSearchDepth(req);

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