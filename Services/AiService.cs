using SeegaGame.Models;
using Microsoft.Extensions.Caching.Memory;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private readonly GameService _gs;
        private readonly IMemoryCache _cache;
        private static readonly long[,,] ZobristPiece;
        private static readonly long SideHash;

        protected const int WIN = 1000000;
        protected const int MAT = 2000;
        protected const int STUCK_ADVANTAGE = 2500;
        protected const int CEN = 60;
        protected const int CONTACT_BONUS = 30;
        protected const int PROXIMITY_WEIGHT = 15;

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
            byte[] b = new byte[8]; r.NextBytes(b);
            return BitConverter.ToInt64(b, 0);
        }

        public AiService(GameService gs, IMemoryCache cache)
        {
            _gs = gs; _cache = cache;
        }

        private GameTTContext GetContext(string uuid) =>
            _cache.GetOrCreate(uuid, entry => {
                entry.SlidingExpiration = TimeSpan.FromMinutes(30);
                return new GameTTContext();
            })!;

        public Move? GetBestMove(AiMoveRequest req)
        {
            req.LastMoveX = ValidateLastMove(req.Board, req.LastMoveX, "X");
            req.LastMoveO = ValidateLastMove(req.Board, req.LastMoveO, "O");

            var ctx = GetContext(req.GameUUId);
            long h = InitialHash(req.Board, req.CurrentPlayer);

            // 【鋼鐵修正】基於預測表判定誰是 P2 (第 25 手攻擊者)
            string attackerName;
            if (req.MoveIndex <= 2) attackerName = _gs.GetOpponent(req.CurrentPlayer); // P1在動, P2是攻
            else if (req.MoveIndex <= 4) attackerName = req.CurrentPlayer;             // P2在動, 自己是攻
            else attackerName = (req.MoveIndex % 2 == 0) ? req.CurrentPlayer : _gs.GetOpponent(req.CurrentPlayer);

            if (req.Phase == GamePhase.STUCK_REMOVAL)
            {
                var movesR = _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, null, null);
                return movesR.OrderByDescending(m => EvaluateRemovalMove(req.Board, m, req.CurrentPlayer, req.LastMoveX, req.LastMoveO, attackerName)).FirstOrDefault();
            }

            var moves = _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, req.LastMoveX, req.LastMoveO);
            if (!moves.Any()) return null;

            int d = CalculateSearchDepth(req);
            return RootSearch(ctx, req, h, d, moves, attackerName);
        }

        private int EvaluateRemovalMove(string?[][] board, Move m, string player, Move? lastX, Move? lastO, string attackerName)
        {
            var ud = _gs.MakeMove(board, m, player, GamePhase.STUCK_REMOVAL, 0);
            var myMoves = _gs.GetValidMoves(board, player, GamePhase.MOVEMENT, lastX, lastO);
            int score = myMoves.Count * 10;
            foreach (var nextMove in myMoves)
            {
                var ud2 = _gs.MakeMove(board, nextMove, player, GamePhase.MOVEMENT, 1);
                if (ud2.Captured.Count > 0) score += 5000;
                _gs.UnmakeMove(board, ud2, player);
            }
            _gs.UnmakeMove(board, ud, player);
            return score;
        }

        private bool In(int r, int c) => r >= 0 && r < 5 && c >= 0 && c < 5;

        private bool IsSameMove(Move? a, Move? b)
        {
            if (a == null || b == null) return false;
            if (a.From == null && b.From == null) return a.To.R == b.To.R && a.To.C == b.To.C;
            if (a.From != null && b.From != null)
                return a.From.R == b.From.R && a.From.C == b.From.C && a.To.R == b.To.R && a.To.C == b.To.C;
            return false;
        }
    }
}