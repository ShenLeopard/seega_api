using System.Diagnostics;
using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        // Debug 計數器
        private long _debugNodes = 0;
        private Stopwatch _sw = new Stopwatch();

        private Move? RootSearch(GameTTContext ctx, AiMoveRequest req, long h, int d, List<Move> moves)
        {
            Move? bestM = null;
            int bestScore = -WIN * 2;
            int alpha = -WIN * 2;

            ProbeTT(ctx, h, 0, -2000000, 2000000, out _, out Move? ttMove);

            var ordered = moves.OrderByDescending(m =>
            {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;
                return GetHeavyMoveOrderingScore(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex, req.LastMoveX, req.LastMoveO);
            }).ToList();

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                var state = GetNextState(h, m, req.CurrentPlayer, req.Phase, req.MoveIndex, ud);
                Move? nX = (req.CurrentPlayer == "X") ? m : req.LastMoveX;
                Move? nO = (req.CurrentPlayer == "O") ? m : req.LastMoveO;

                int score;
                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, req.Board, state.nextHash, d - 1, alpha, WIN * 2, req.CurrentPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1);
                else
                    score = -AlphaBeta(ctx, req.Board, state.nextHash, d - 1, -WIN * 2, -alpha, state.nextPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1);

                _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);

                if (score > bestScore) { bestScore = score; bestM = m; }
                alpha = Math.Max(alpha, bestScore);

                // 絕殺截斷：既然能贏，就不再搜尋其他分支
                if (bestScore >= WIN) break;
            }

            if (bestM != null) StoreTT(ctx, h, d, bestScore, 0, bestM);
            return bestM;
        }
        private int SearchRemoval(GameTTContext ctx, string?[][] board, long h, int d, string curr, Move? lX, Move? lO, int idx)
        {
            // Debug: 檢查是否頻繁進入移除模式
            // Console.WriteLine($"[Removal] Triggered at depth {d}"); 

            var removalMoves = _gs.GetValidMoves(board, curr, GamePhase.STUCK_REMOVAL, lX, lO);

            if (removalMoves.Count == 0)
                return EvaluatePosition(board, curr, GamePhase.MOVEMENT, idx);

            int bestS = -2000000;

            foreach (var m in removalMoves)
            {
                var ud = _gs.MakeMove(board, m, curr, GamePhase.STUCK_REMOVAL, idx);
                long nh = UpdatePieceHash(h, m, curr, ud.Captured, null, GamePhase.STUCK_REMOVAL);

                // 移除後繼續搜 MOVEMENT
                int score = AlphaBeta(ctx, board, nh, d, -2000000, 2000000, curr, lX, lO, GamePhase.MOVEMENT, idx + 1);

                _gs.UnmakeMove(board, ud, curr);
                if (score > bestS) bestS = score;
            }
            return bestS;
        }

        private int AlphaBeta(GameTTContext ctx, string?[][] board, long h, int d,
             int alpha, int beta, string curr, Move? lX, Move? lO,
             GamePhase ph, int idx)
        {
            if (ProbeTT(ctx, h, d, alpha, beta, out int ttScore, out Move? ttMove)) return ttScore;

            string? winner = _gs.CheckWinner(board);
            if (winner != null) return (winner == curr) ? (WIN + d) : (-WIN - d);

            if (d <= 0) return Quiesce(ctx, board, h, alpha, beta, curr, lX, lO, ph, idx);

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            if (moves.Count == 0)
            {
                if (ph == GamePhase.MOVEMENT) return SearchRemoval(ctx, board, h, d, curr, lX, lO, idx);
                return EvaluatePosition(board, curr, ph, idx) + STUCK_ADVANTAGE;
            }

            // 每一層都進行排序，且現在包含自殺步預判
            var ordered = moves.OrderByDescending(m =>
            {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;
                return GetFastMoveOrderingScore(board, m, curr);
            });

            int bestS = -WIN * 2;
            Move? bestM = null;

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                int score;
                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, board, state.nextHash, d - 1, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1);
                else
                    score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                // 跳恰恰修正：非吃子步微量懲罰，鼓勵進攻
                if (ph == GamePhase.MOVEMENT && ud.Captured.Count == 0) score -= 10;

                _gs.UnmakeMove(board, ud, curr);

                if (score > bestS) { bestS = score; bestM = m; }
                alpha = Math.Max(alpha, score);
                if (alpha >= beta) break;
            }

            StoreTT(ctx, h, d, bestS, (bestS <= alpha ? 1 : (bestS >= beta ? 2 : 0)), bestM);
            return bestS;
        }

        private int Quiesce(GameTTContext ctx, string?[][] board, long h,
                   int alpha, int beta, string curr,
                   Move? lX, Move? lO, GamePhase ph, int idx)
        {
            int standPat = EvaluatePosition(board, curr, ph, idx);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            var captureMoves = moves.Where(m => IsCaptureMove(board, m, curr))
                                   .OrderByDescending(m => GetFastMoveOrderingScore(board, m, curr));

            foreach (var m in captureMoves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                int score;
                if (state.isSamePlayer)
                    score = Quiesce(ctx, board, state.nextHash, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1);
                else
                    score = -Quiesce(ctx, board, state.nextHash, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                _gs.UnmakeMove(board, ud, curr);
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        private bool IsCaptureMove(string?[][] board, Move m, string player)
        {
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            string op = player == "X" ? "O" : "X";
            for (int i = 0; i < 4; i++)
            {
                int r1 = m.To.R + dr[i], c1 = m.To.C + dc[i];
                int r2 = m.To.R + dr[i] * 2, c2 = m.To.C + dc[i] * 2;
                if (In(r2, c2) && board[r1][c1] == op && board[r2][c2] == player) return true;
            }
            return false;
        }
    }
}