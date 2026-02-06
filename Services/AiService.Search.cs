using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private Move? RootSearch(GameTTContext ctx, AiMoveRequest req, long h, int d, List<Move> moves)
        {
            Move? bestM = null;
            int bestScore = -WIN * 2;
            int alpha = -WIN * 2;

            if (!moves.Any()) return null;

            ProbeTT(ctx, h, 0, -2000000, 2000000, out _, out Move? ttMove);  // 移除 req.Phase 參數

            var ordered = moves.OrderByDescending(m =>
            {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;

                if (d <= 2 && req.Phase == GamePhase.MOVEMENT)
                {
                    var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                    int captured = ud.Captured.Count;
                    _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);

                    if (captured > 0) return 1000000 + captured;
                }

                return GetMoveOrderingScore(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex, req.LastMoveX, req.LastMoveO);
            });

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                var state = GetNextState(h, m, req.CurrentPlayer, req.Phase, req.MoveIndex, ud);

                int score;
                Move? nX = (req.CurrentPlayer == "X") ? m : req.LastMoveX;
                Move? nO = (req.CurrentPlayer == "O") ? m : req.LastMoveO;

                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, req.Board, state.nextHash, d - 1, alpha, WIN * 2, req.CurrentPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1);
                else
                    score = -AlphaBeta(ctx, req.Board, state.nextHash, d - 1, -WIN * 2, -alpha, state.nextPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1);

                _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestM = m;
                }
                alpha = Math.Max(alpha, bestScore);
            }

            if (bestM != null) StoreTT(ctx, h, d, bestScore, 0, bestM);
            return bestM;
        }
        private int SearchRemoval(GameTTContext ctx, string?[][] board, long h, int d, string curr, Move? lX, Move? lO, int idx)
        {
            var removalMoves = _gs.GetValidMoves(board, curr, GamePhase.STUCK_REMOVAL, lX, lO);

            if (removalMoves.Count == 0)
                return EvaluatePosition(board, curr, GamePhase.MOVEMENT, idx);

            int bestS = -2000000;

            foreach (var m in removalMoves)
            {
                var ud = _gs.MakeMove(board, m, curr, GamePhase.STUCK_REMOVAL, idx);

                // 修正：明確傳入 STUCK_REMOVAL 階段，UpdatePieceHash 就不會在那格變出己方棋子
                long nh = UpdatePieceHash(h, m, curr, ud.Captured, null, GamePhase.STUCK_REMOVAL);

                // 移除後不 XOR SideHash，因為同一個人立刻進行 MOVEMENT
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
            int originalAlpha = alpha;

            if (ProbeTT(ctx, h, d, alpha, beta, out int ttScore, out Move? ttMove)) return ttScore;  // 移除 ph 參數

            string? winner = _gs.CheckWinner(board);
            if (winner != null) return (winner == curr) ? (WIN + d) : (-WIN - d);

            if (d <= 0) return Quiesce(ctx, board, h, alpha, beta, curr, lX, lO, ph, idx);

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            if (moves.Count == 0)
            {
                if (ph == GamePhase.MOVEMENT) return SearchRemoval(ctx, board, h, d, curr, lX, lO, idx);
                return EvaluatePosition(board, curr, ph, idx) + STUCK_ADVANTAGE;
            }

            var ordered = moves.OrderByDescending(m => (ttMove != null && IsSameMove(m, ttMove)) ? 1000000 : 0);

            int bestS = -WIN * 2;
            Move? bestM = null;
            int movesSearched = 0;

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);

                int score;
                Move? nX = (curr == "X") ? m : lX;
                Move? nO = (curr == "O") ? m : lO;

                if (movesSearched >= 4 && d >= 3 && !state.isSamePlayer && ud.Captured.Count == 0)
                {
                    score = -AlphaBeta(ctx, board, state.nextHash, d - 2, -alpha - 1, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                    if (score > alpha)
                        score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);
                }
                else
                {
                    if (state.isSamePlayer)
                        score = AlphaBeta(ctx, board, state.nextHash, d - 1, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1);
                    else
                        score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);
                }

                _gs.UnmakeMove(board, ud, curr);
                movesSearched++;

                if (score > bestS) { bestS = score; bestM = m; }
                alpha = Math.Max(alpha, score);
                if (alpha >= beta) break;
            }

            int flag = (bestS <= originalAlpha) ? 1 : (bestS >= beta ? 2 : 0);
            StoreTT(ctx, h, d, bestS, flag, bestM);
            return bestS;
        }

        private int Quiesce(GameTTContext ctx, string?[][] board, long h,
                   int alpha, int beta, string curr,
                   Move? lX, Move? lO, GamePhase ph, int idx)
        {
            if (ProbeTT(ctx, h, 0, alpha, beta, out int ttScore, out _)) return ttScore;  // 移除 ph 參數

            int standPat = EvaluatePosition(board, curr, ph, idx);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);

            foreach (var m in moves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                if (ud.Captured.Count == 0) { _gs.UnmakeMove(board, ud, curr); continue; }

                var state = GetNextState(h, m, curr, ph, idx, ud);
                Move? nX = (curr == "X") ? m : lX;
                Move? nO = (curr == "O") ? m : lO;

                int score;
                if (state.isSamePlayer)
                    score = Quiesce(ctx, board, state.nextHash, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1);
                else
                    score = -Quiesce(ctx, board, state.nextHash, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                _gs.UnmakeMove(board, ud, curr);

                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }

            StoreTT(ctx, h, 0, alpha, 0, null);
            return alpha;
        }
        private int CalculateSearchDepth(AiMoveRequest req)
        {
            if (req.Phase == GamePhase.PLACEMENT)
            {
                // 如果是第 24 手，這是佈陣轉交戰的生死關頭，必須深算
                if (req.MoveIndex == 24) return Math.Max(req.Difficulty, 6);

                // 其他佈陣階段：末期稍深，初期較淺
                return (req.MoveIndex >= 18) ? Math.Min(7, (24 - req.MoveIndex) + 2) : 3;
            }

            if (req.Phase == GamePhase.MOVEMENT)
            {
                int myCount = 0;
                int opCount = 0;
                foreach (var row in req.Board)
                    foreach (var cell in row)
                    {
                        if (cell == req.CurrentPlayer) myCount++;
                        else if (cell != null) opCount++;
                    }

                // 優勢收割模式：對手剩不到 4 顆且我方大贏 5 顆以上時，降深度以加快反應
                if ((myCount - opCount >= 5) && (opCount <= 4))
                {
                    return 2;
                }
            }

            return req.Difficulty;
        }
    }
}