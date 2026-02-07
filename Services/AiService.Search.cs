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
            _sw.Restart();
            _debugNodes = 0;
            Move? bestM = null;
            int bestScore = -WIN * 2;
            int alpha = -WIN * 2;

            Console.WriteLine($"[AI Start] Index: {req.MoveIndex}, Depth: {d}, Moves: {moves.Count}");

            if (!moves.Any()) return null;

            ProbeTT(ctx, h, 0, -2000000, 2000000, out _, out Move? ttMove);

            var ordered = moves.OrderByDescending(m =>
            {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;
                return GetHeavyMoveOrderingScore(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex, req.LastMoveX, req.LastMoveO);
            }).ToList();

            int moveCount = 0;
            foreach (var m in ordered)
            {
                moveCount++;
                var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                var state = GetNextState(h, m, req.CurrentPlayer, req.Phase, req.MoveIndex, ud);

                // ★★★ 補上這兩行定義 nX 和 nO ★★★
                Move? nX = (req.CurrentPlayer == "X") ? m : req.LastMoveX;
                Move? nO = (req.CurrentPlayer == "O") ? m : req.LastMoveO;

                int score;
                if (state.isSamePlayer)
                    // 這裡原本報錯是因為 nX, nO 未定義，現在補上了
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

                // 必勝剪枝：如果分數已經大於 WIN (代表必勝)，直接跳出，不用再算剩下的爛棋
                if (bestScore >= WIN)
                {
                    Console.WriteLine($"[Fast Win] Found winning move {FormatMove(m)} with score {bestScore}. Stopping.");
                    break;
                }
            }

            if (bestM != null) StoreTT(ctx, h, d, bestScore, 0, bestM);

            _sw.Stop();
            Console.WriteLine($"[AI Done] Best: {FormatMove(bestM)}, Score: {bestScore}, Time: {_sw.ElapsedMilliseconds}ms");
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
            _debugNodes++;

            // [致命錯誤修正] 應該先檢查 TT，這沒錯
            if (ProbeTT(ctx, h, d, alpha, beta, out int ttScore, out Move? ttMove)) return ttScore;

            // 檢查勝負
            string? winner = _gs.CheckWinner(board);
            if (winner != null) return (winner == curr) ? (WIN + d) : (-WIN - d);

            // 進入靜態搜尋
            if (d <= 0) return Quiesce(ctx, board, h, alpha, beta, curr, lX, lO, ph, idx);

            // 檢查受困
            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            if (moves.Count == 0)
            {
                if (ph == GamePhase.MOVEMENT) return SearchRemoval(ctx, board, h, d, curr, lX, lO, idx);
                return EvaluatePosition(board, curr, ph, idx) + STUCK_ADVANTAGE;
            }

            // 排序 (使用輕量排序)
            var ordered = moves.OrderByDescending(m =>
            {
                if (ttMove != null && IsSameMove(m, ttMove)) return 2000000;
                return GetFastMoveOrderingScore(board, m, curr);
            });

            int originalAlpha = alpha;
            int bestS = -WIN * 2;
            Move? bestM = null;

            // [關鍵] 增加一個已搜尋節點數檢查，若第一個分支分數極高，後續可考慮 LMR (Late Move Reduction)
            // 但為求穩，我們先修復基礎剪枝

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                int score;
                if (state.isSamePlayer)
                    // 換手不換人，alpha/beta 保持不變，深度也不變 (或減1，視規則)
                    // Seega 連動攻擊通常算同一回合，深度不減，或僅微減
                    score = AlphaBeta(ctx, board, state.nextHash, d, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1);
                else
                    // 換人，視角切換，alpha/beta 翻轉
                    score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                _gs.UnmakeMove(board, ud, curr);

                if (score > bestS) { bestS = score; bestM = m; }

                // 更新 Alpha
                if (bestS > alpha) alpha = bestS;

                // [剪枝核心]
                if (alpha >= beta)
                {
                    break; // Beta Cut-off
                }
            }

            // 儲存 TT
            int flag = (bestS <= originalAlpha) ? 1 : (bestS >= beta ? 2 : 0);
            StoreTT(ctx, h, d, bestS, flag, bestM);

            return bestS;
        }

        private int Quiesce(GameTTContext ctx, string?[][] board, long h,
            int alpha, int beta, string curr,
            Move? lX, Move? lO, GamePhase ph, int idx)
        {
            _debugNodes++;

            // 1. 站立評估 (Stand-pat)
            int standPat = EvaluatePosition(board, curr, ph, idx);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            // 2. 取得所有步數 (這裡會回傳所有步，這是效能瓶頸之一)
            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);

            // ★★★ 關鍵修正：只篩選出「真的能吃子」的步數 ★★★
            // 使用 Where 過濾，確保不會進入無意義的靜態搜尋深淵
            var captureMoves = moves
                .Where(m => IsCaptureMove(board, m, curr))
                .OrderByDescending(m => GetFastMoveOrderingScore(board, m, curr));

            foreach (var m in captureMoves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                // 雙重檢查：如果 MakeMove 後發現沒吃到子 (極少見)，就還原並跳過
                if (ud.Captured.Count == 0)
                {
                    _gs.UnmakeMove(board, ud, curr);
                    continue;
                }

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
                int r2 = m.To.R + dr[i] * 2, c2 = m.To.C + dc[i] * 2;
                // 檢查是否形成夾擊
                if (In(r2, c2) && board[m.To.R + dr[i]][m.To.C + dc[i]] == op
                    && board[r2][c2] == player) return true;
            }
            return false;
        }

     

        private int CalculateSearchDepth(AiMoveRequest req)
        {
            // Debug: 顯示計數
            int myCount = 0;
            int opCount = 0;
            if (req.Phase == GamePhase.MOVEMENT)
            {
                foreach (var row in req.Board)
                    foreach (var cell in row)
                    {
                        if (cell == req.CurrentPlayer) myCount++;
                        else if (cell != null) opCount++;
                    }
                // Console.WriteLine($"[Counts] Me: {myCount}, Op: {opCount}");
            }

            if (req.Phase == GamePhase.PLACEMENT)
            {
                if (req.MoveIndex == 24) return Math.Max(req.Difficulty, 6);
                return (req.MoveIndex >= 18) ? Math.Min(7, (24 - req.MoveIndex) + 2) : 3;
            }

            if (req.Phase == GamePhase.MOVEMENT)
            {
                // ★ 修正：這裡一定要是 4，否則看不到誘敵
                if ((myCount - opCount >= 5) && (opCount <= 4))
                {
                    Console.WriteLine("[Strategy] Advantage mode: Depth set to 4");
                    return 4;
                }
            }

            return req.Difficulty;
        }

        // 輔助顯示
        private string FormatMove(Move? m)
        {
            if (m == null) return "null";
            string from = m.From == null ? "" : $"{_gs.FormatPos(m.From)}->";
            return $"{from}{_gs.FormatPos(m.To)}";
        }
    }
}