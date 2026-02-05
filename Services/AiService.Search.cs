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

            ProbeTT(ctx, h, 0, -2000000, 2000000, req.Phase, out _, out Move? ttMove);

            var ordered = moves.OrderByDescending(m =>
            {
                // 1. TT 永遠最優先 (如果有的話)
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;

                // 2. 如果是優勢收割期，直接看這步「MakeMove」能吃多少
                if (d <= 2 && req.Phase == GamePhase.MOVEMENT)
                {
                    var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                    int captured = ud.Captured.Count;
                    _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);

                    if (captured > 0) return 1000000 + captured; // 有吃子就排最前面
                }

                // 3. 正常情況下的排序
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
        // 處理受困時的移除搜尋：此時 curr 移除一顆敵子後，繼續進行 MOVEMENT 搜尋
        private int SearchRemoval(GameTTContext ctx, string?[][] board, long h, int d, string curr, Move? lX, Move? lO, int idx)
        {
            // 取得所有可移除的敵方棋子 (STUCK_REMOVAL 階段目標)
            var removalMoves = _gs.GetValidMoves(board, curr, GamePhase.STUCK_REMOVAL, lX, lO);

            if (removalMoves.Count == 0)
                return EvaluatePosition(board, curr, GamePhase.MOVEMENT, idx);

            int bestS = -2000000;

            foreach (var m in removalMoves)
            {
                // 1. 執行移除物理動作
                var ud = _gs.MakeMove(board, m, curr, GamePhase.STUCK_REMOVAL, idx);

                // 2. 更新雜湊：僅移除棋子，不翻轉 SideHash (因為同一個人繼續動)
                long nh = UpdatePieceHash(h, m, curr, ud.Captured, null);

                // 3. 移除後，同一玩家 (curr) 立即進行 MOVEMENT 搜尋
                // 這裡 d 不減 1 或僅減 1 (視難度調整)，確保能算出解圍後的下一步
                int score = AlphaBeta(ctx, board, nh, d, -2000000, 2000000, curr, lX, lO, GamePhase.MOVEMENT, idx + 1);

                // 4. 復原
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

            if (ProbeTT(ctx, h, d, alpha, beta, ph, out int ttScore, out Move? ttMove)) return ttScore;

            string? winner = _gs.CheckWinner(board);
            if (winner != null) return (winner == curr) ? (WIN + d) : (-WIN - d);

            if (d <= 0) return Quiesce(ctx, board, h, alpha, beta, curr, lX, lO, ph, idx);

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            if (moves.Count == 0)
            {
                if (ph == GamePhase.MOVEMENT) return SearchRemoval(ctx, board, h, d, curr, lX, lO, idx);
                return EvaluatePosition(board, curr, ph, idx) + STUCK_ADVANTAGE;
            }

            // Move Ordering
            var ordered = moves.OrderByDescending(m => (ttMove != null && IsSameMove(m, ttMove)) ? 1000000 : 0);

            int bestS = -WIN * 2;
            Move? bestM = null;
            int movesSearched = 0;

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);

                int score;
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                // --- 修正：LMR (Late Move Reduction) ---
                // 如果已經搜尋了 4 個以上的著法且沒有發現殺招，對於剩下的著法減少搜尋深度
                if (movesSearched >= 4 && d >= 3 && !state.isSamePlayer && ud.Captured.Count == 0)
                {
                    // 先用較淺的深度 (d-2) 試探
                    score = -AlphaBeta(ctx, board, state.nextHash, d - 2, -alpha - 1, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                    // 如果淺層搜尋發現這步棋似乎還有點潛力，才補做完整搜尋
                    if (score > alpha)
                        score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);
                }
                else
                {
                    // 正常搜尋
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
            if (ProbeTT(ctx, h, 0, alpha, beta, ph, out int ttScore, out _)) return ttScore;

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
                return (req.MoveIndex >= 18) ? Math.Min(7, (24 - req.MoveIndex) + 2) : 3;

            // --- 新增：收割模式判斷 (優勢降級) ---
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

                // 差距 5 顆以上，且對手剩 4 顆以下
                if ((myCount - opCount >= 5) && (opCount <= 4))
                {
                    // 將搜尋深度降到 2。配合 Quiesce，這足以執行簡單吃子，反應時間會縮短 99%
                    return 2;
                }
            }

            return req.Difficulty;
        }
    }
}