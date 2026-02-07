using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private Move? RootSearch(GameTTContext ctx, AiMoveRequest req, long h, int d, List<Move> moves, string attackerName)
        {
            Move? bestM = null;
            int bestScore = -WIN * 2;
            int alpha = -WIN * 2;
            ProbeTT(ctx, h, 0, -2000000, 2000000, out _, out Move? ttMove);

            var ordered = moves.OrderByDescending(m => {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;
                return GetHeavyMoveOrderingScore(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex, req.LastMoveX, req.LastMoveO, attackerName);
            }).ToList();

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                var state = GetNextState(h, m, req.CurrentPlayer, req.Phase, req.MoveIndex, ud);
                Move? nX = (req.CurrentPlayer == "X") ? m : req.LastMoveX;
                Move? nO = (req.CurrentPlayer == "O") ? m : req.LastMoveO;

                int score;
                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, req.Board, state.nextHash, d, alpha, WIN * 2, req.CurrentPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1, attackerName);
                else
                    score = -AlphaBeta(ctx, req.Board, state.nextHash, d - 1, -WIN * 2, -alpha, state.nextPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1, attackerName);

                _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);
                if (score > bestScore) { bestScore = score; bestM = m; }
                alpha = Math.Max(alpha, bestScore);
                if (bestScore >= WIN) break;
            }
            if (bestM != null) StoreTT(ctx, h, d, bestScore, 0, bestM);
            return bestM;
        }

        private int AlphaBeta(GameTTContext ctx, string?[][] board, long h, int d, int alpha, int beta, string curr, Move? lX, Move? lO, GamePhase ph, int idx, string attackerName)
        {
            if (ProbeTT(ctx, h, d, alpha, beta, out int ttScore, out Move? ttMove)) return ttScore;

            string? winner = _gs.CheckWinner(board);
            if (winner != null) return (winner == curr) ? (WIN + d) : (-WIN - d);
            if (d <= 0) return Quiesce(ctx, board, h, alpha, beta, curr, lX, lO, ph, idx, attackerName);

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            if (moves.Count == 0)
            {
                if (ph == GamePhase.MOVEMENT) return SearchRemoval(ctx, board, h, d, curr, lX, lO, idx, attackerName);
                return EvaluatePosition(board, curr, ph, idx, attackerName) + STUCK_ADVANTAGE;
            }

            var ordered = moves.OrderByDescending(m => {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;
                return GetFastMoveOrderingScore(board, m, curr, attackerName);
            });

            int bestS = -WIN * 2;
            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                int score;
                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, board, state.nextHash, d, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1, attackerName);
                else
                    score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1, attackerName);

                _gs.UnmakeMove(board, ud, curr);
                if (score > bestS) bestS = score;
                alpha = Math.Max(alpha, score);
                if (alpha >= beta) break;
            }
            StoreTT(ctx, h, d, bestS, (bestS <= alpha ? 1 : (bestS >= beta ? 2 : 0)), null);
            return bestS;
        }

        private int Quiesce(GameTTContext ctx, string?[][] board, long h, int alpha, int beta, string curr, Move? lX, Move? lO, GamePhase ph, int idx, string attackerName)
        {
            int standPat = EvaluatePosition(board, curr, ph, idx, attackerName);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            var captureMoves = moves.Where(m => IsCaptureMove(board, m, curr));

            foreach (var m in captureMoves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);
                int score;
                if (state.isSamePlayer)
                    score = Quiesce(ctx, board, state.nextHash, alpha, beta, curr, (curr == "X" ? m : lX), (curr == "O" ? m : lO), state.nextPhase, idx + 1, attackerName);
                else
                    score = -Quiesce(ctx, board, state.nextHash, -beta, -alpha, state.nextPlayer, (curr == "X" ? m : lX), (curr == "O" ? m : lO), state.nextPhase, idx + 1, attackerName);
                _gs.UnmakeMove(board, ud, curr);
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        private int SearchRemoval(GameTTContext ctx, string?[][] board, long h, int d, string curr, Move? lX, Move? lO, int idx, string attackerName)
        {
            var moves = _gs.GetValidMoves(board, curr, GamePhase.STUCK_REMOVAL, lX, lO);
            if (moves.Count == 0) return EvaluatePosition(board, curr, GamePhase.MOVEMENT, idx, attackerName);
            int best = -2000000;
            foreach (var m in moves)
            {
                var ud = _gs.MakeMove(board, m, curr, GamePhase.STUCK_REMOVAL, idx);
                long nh = UpdatePieceHash(h, m, curr, ud.Captured, null, GamePhase.STUCK_REMOVAL);
                int s = AlphaBeta(ctx, board, nh, d - 1, -2000000, 2000000, curr, lX, lO, GamePhase.MOVEMENT, idx + 1, attackerName);
                _gs.UnmakeMove(board, ud, curr);
                if (s > best) best = s;
            }
            return best;
        }

        private int CalculateSearchDepth(AiMoveRequest req)
        {
            if (req.Phase == GamePhase.PLACEMENT)
            {
                if (req.MoveIndex == 24) return Math.Max(req.Difficulty, 6);
                return (req.MoveIndex >= 18) ? Math.Min(7, (24 - req.MoveIndex) + 2) : 3;
            }
            if (req.Phase == GamePhase.MOVEMENT)
            {
                int my = 0, op = 0;
                foreach (var r in req.Board) foreach (var c in r)
                    {
                        if (c == req.CurrentPlayer) my++; else if (c != null) op++;
                    }
                if (my - op >= 5 && op <= 4) return 4;
            }
            return req.Difficulty;
        }
    }
}