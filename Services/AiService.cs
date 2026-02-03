using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SeegaGame.Models;
using SeegaGame.Services;

namespace SeegaGame.Services
{
    public class AiService
    {
        private readonly GameService _gs;
        private readonly IMemoryCache _cache;
        private static readonly long[,,] Zobrist;
        private static readonly long SideHash;

        // 評分常數
        private const double WIN = 1000000.0;
        private const double MAT = 2000.0;
        private const double STUCK_ADVANTAGE = 2500.0;
        private const double CEN = 60.0;
        private const double FIRST_MOVE_BONUS = 6000.0;
        private const double SUFFOCATE_BONUS = 3000.0;
        private const double MOBILITY_LIGHT = 8.0;  // 輕量化機動性權重

        static AiService()
        {
            var rand = new Random(1688);
            Zobrist = new long[5, 5, 2];
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    Zobrist[r, c, 0] = RL(rand);
                    Zobrist[r, c, 1] = RL(rand);
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

        private Dictionary<long, TTEntry> GetTT(string uuid) =>
            _cache.GetOrCreate(uuid, e =>
            {
                e.SlidingExpiration = TimeSpan.FromMinutes(30);
                return new Dictionary<long, TTEntry>();
            })!;

        public Move? GetBestMove(AiMoveRequest req)
        {
            req.LastMoveX = ValidateLastMove(req.Board, req.LastMoveX, "X");
            req.LastMoveO = ValidateLastMove(req.Board, req.LastMoveO, "O");

            var tt = GetTT(req.GameUUId);
            long h = InitialHash(req.Board, req.CurrentPlayer);

            if (req.Phase == GamePhase.STUCK_REMOVAL)
            {
                return _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, null, null)
                    .OrderByDescending(m => EvaluateRemovalMove(req.Board, m, req.CurrentPlayer, req.LastMoveX, req.LastMoveO))
                    .FirstOrDefault();
            }

            int d = CalculateSearchDepth(req.Phase, req.MoveIndex, req.Difficulty);
            return RootSearch(tt, req, h, d);
        }

        private Move? ValidateLastMove(string?[][] board, Move? lastMove, string player)
        {
            if (lastMove?.To == null) return null;
            if (lastMove.To.R < 0 || lastMove.To.R >= 5 || lastMove.To.C < 0 || lastMove.To.C >= 5)
                return null;
            if (board[lastMove.To.R][lastMove.To.C] != player)
                return null;
            if (lastMove.From != null)
            {
                if (lastMove.From.R < 0 || lastMove.From.R >= 5 ||
                    lastMove.From.C < 0 || lastMove.From.C >= 5)
                    return null;
            }
            return lastMove;
        }

        private int CalculateSearchDepth(GamePhase phase, int moveIndex, int difficulty)
        {
            if (phase == GamePhase.PLACEMENT)
            {
                return (moveIndex >= 18) ? Math.Min(7, (24 - moveIndex) + 2) : 3;
            }
            return difficulty;
        }

        private string InferFirstPlayer(string currentPlayer, int moveIndex)
        {
            if (moveIndex <= 0) return "X";
            bool isFirstPlayerMove = (moveIndex <= 4) ? (moveIndex <= 2) : (moveIndex % 2 == 1);
            return isFirstPlayerMove ? currentPlayer : _gs.GetOpponent(currentPlayer);
        }

        private string GetNextPlayer(string currentPlayer, int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT)
                return _gs.GetOpponent(currentPlayer);

            if (moveIndex == 1 || moveIndex == 3)
                return currentPlayer;
            else if (moveIndex == 2 || moveIndex == 4)
                return _gs.GetOpponent(currentPlayer);

            return _gs.GetOpponent(currentPlayer);
        }

        private bool IsAttacker(string player, string currentPlayer, int moveIndex)
        {
            string firstPlayer = InferFirstPlayer(currentPlayer, moveIndex);
            string secondPlayer = _gs.GetOpponent(firstPlayer);
            return player == secondPlayer;
        }

        private bool IsComboMove(int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT) return false;
            return (moveIndex == 1 || moveIndex == 3 || moveIndex == 24);
        }

        // ===== 修正 1: 統一狀態轉換邏輯 =====
        private (long nextHash, string nextPlayer, GamePhase nextPhase, bool isSamePlayer)
            GetNextState(long currentHash, Move move, string curr, GamePhase ph, int idx, UndoData ud)
        {
            long nh = UpdatePieceHash(currentHash, move, curr, ud.Captured, ud.ClearedCenterPiece);
            bool isCombo = IsComboMove(idx, ph);

            if (isCombo)
            {
                GamePhase nextPhase = (idx == 24) ? GamePhase.MOVEMENT : ph;
                return (nh, curr, nextPhase, true);
            }
            else
            {
                return (nh ^ SideHash, _gs.GetOpponent(curr), ph, false);
            }
        }

        private Move? RootSearch(Dictionary<long, TTEntry> tt, AiMoveRequest req, long h, int d)
        {
            Move? bestM = null;
            double bestScore = double.NegativeInfinity;
            double alpha = double.NegativeInfinity;

            var moves = _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, req.LastMoveX, req.LastMoveO);
            if (!moves.Any()) return null;

            tt.TryGetValue(h, out var entry);

            // ===== 修正 2: 增強 Move Ordering =====
            var ordered = moves.OrderByDescending(m =>
            {
                // TT 最佳著法優先
                if (entry?.BestMove != null && IsSameMove(m, entry.BestMove))
                    return 1000000;

                // 中心點優先
                if (m.To.R == 2 && m.To.C == 2)
                    return 100;

                // 其他啟發式評估
                return GetMoveOrderingScore(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex,
                                           req.LastMoveX, req.LastMoveO);
            });

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);

                var state = GetNextState(h, m, req.CurrentPlayer, req.Phase, req.MoveIndex, ud);

                double score;
                Move? nextX = (req.CurrentPlayer == "X") ? m : req.LastMoveX;
                Move? nextO = (req.CurrentPlayer == "O") ? m : req.LastMoveO;

                if (state.isSamePlayer)
                {
                    score = AlphaBeta(tt, req.Board, state.nextHash, d - 1, alpha, double.PositiveInfinity,
                                     req.CurrentPlayer, nextX, nextO, state.nextPhase, req.MoveIndex + 1);
                }
                else
                {
                    score = -AlphaBeta(tt, req.Board, state.nextHash, d - 1, double.NegativeInfinity, -alpha,
                                      state.nextPlayer, nextX, nextO, state.nextPhase, req.MoveIndex + 1);
                }

                _gs.UnmakeMove(req.Board, ud, req.CurrentPlayer);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestM = m;
                }
                alpha = Math.Max(alpha, bestScore);
            }

            if (bestM != null && tt.Count < 500000)
            {
                tt[h] = new TTEntry
                {
                    Depth = d,
                    Score = bestScore,
                    Flag = 0,
                    BestMove = bestM
                };
            }

            return bestM;
        }

        private double AlphaBeta(Dictionary<long, TTEntry> tt, string?[][] board, long h, int d,
                                double alpha, double beta, string curr, Move? lX, Move? lO,
                                GamePhase ph, int idx)
        {
            double originalAlpha = alpha;

            if (tt.TryGetValue(h, out var entry) && entry.Depth >= d)
            {
                if (entry.Flag == 0) return entry.Score;
                if (entry.Flag == 1 && entry.Score <= alpha) return alpha;
                if (entry.Flag == 2 && entry.Score >= beta) return beta;
            }

            string? winner = _gs.CheckWinner(board, ph);
            if (winner != null)
            {
                return (winner == curr) ? (WIN + d) : (-WIN - d);
            }

            if (d <= 0)
            {
                return Quiesce(tt, board, h, alpha, beta, curr, lX, lO, ph, idx);
            }

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);

            if (moves.Count == 0)
            {
                double baseScore = EvaluatePosition(board, curr, ph, idx);
                return baseScore + STUCK_ADVANTAGE;
            }

            double bestScore = double.NegativeInfinity;
            Move? bestMove = null;

            foreach (var m in moves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);

                var state = GetNextState(h, m, curr, ph, idx, ud);

                double score;
                Move? nextX = (curr == "X") ? m : lX;
                Move? nextO = (curr == "O") ? m : lO;

                if (state.isSamePlayer)
                {
                    score = AlphaBeta(tt, board, state.nextHash, d - 1, alpha, beta, curr,
                                     nextX, nextO, state.nextPhase, idx + 1);
                }
                else
                {
                    score = -AlphaBeta(tt, board, state.nextHash, d - 1, -beta, -alpha,
                                      state.nextPlayer, nextX, nextO, state.nextPhase, idx + 1);
                }

                _gs.UnmakeMove(board, ud, curr);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = m;
                }

                alpha = Math.Max(alpha, score);
                if (alpha >= beta) break;
            }

            int flag = (bestScore <= originalAlpha) ? 1 : (bestScore >= beta) ? 2 : 0;
            if (tt.Count < 500000)
            {
                tt[h] = new TTEntry
                {
                    Depth = d,
                    Score = bestScore,
                    Flag = flag,
                    BestMove = bestMove
                };
            }

            return bestScore;
        }

        // ===== 修正 3: Quiesce 使用統一狀態轉換 + 存表 =====
        private double Quiesce(Dictionary<long, TTEntry> tt, string?[][] board, long h,
                              double alpha, double beta, string curr,
                              Move? lX, Move? lO, GamePhase ph, int idx)
        {
            // 查表
            if (tt.TryGetValue(h, out var entry) && entry.Depth >= 0)
            {
                if (entry.Flag == 0) return entry.Score;
            }

            double standPat = EvaluatePosition(board, curr, ph, idx);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);

            foreach (var m in moves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);

                if (ud.Captured.Count == 0)
                {
                    _gs.UnmakeMove(board, ud, curr);
                    continue;
                }

                // 使用統一的狀態轉換（解決雜湊一致性問題）
                var state = GetNextState(h, m, curr, ph, idx, ud);

                Move? nextX = (curr == "X") ? m : lX;
                Move? nextO = (curr == "O") ? m : lO;

                double score;
                if (state.isSamePlayer)
                {
                    score = Quiesce(tt, board, state.nextHash, alpha, beta, curr,
                                   nextX, nextO, state.nextPhase, idx + 1);
                }
                else
                {
                    score = -Quiesce(tt, board, state.nextHash, -beta, -alpha, state.nextPlayer,
                                    nextX, nextO, state.nextPhase, idx + 1);
                }

                _gs.UnmakeMove(board, ud, curr);

                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }

            // 靜態搜尋也存入 TT
            if (tt.Count < 500000)
            {
                tt[h] = new TTEntry { Depth = 0, Score = alpha, Flag = 0 };
            }

            return alpha;
        }

        // ===== 修正 4: 輕量化評估函數（移除 GetValidMoves） =====
        private double EvaluatePosition(string?[][] board, string currentPlayer, GamePhase phase, int moveIndex)
        {
            double score = 0;
            int myPieces = 0;
            int opPieces = 0;
            int myMobilityLight = 0;  // 輕量化機動性
            int opMobilityLight = 0;
            string opponent = _gs.GetOpponent(currentPlayer);

            bool iAmAttacker = IsAttacker(currentPlayer, currentPlayer, moveIndex);

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    string? piece = board[r][c];

                    if (piece == currentPlayer)
                    {
                        myPieces++;
                        if (r == 2 && c == 2) score += CEN;

                        if (phase == GamePhase.PLACEMENT)
                        {
                            int vulnerability = CalculateVulnerability(board, r, c, opponent);
                            score -= iAmAttacker ? vulnerability * 100 : vulnerability * 700;
                        }

                        // 輕量化機動性：計算周圍空格數
                        if (phase == GamePhase.MOVEMENT)
                        {
                            myMobilityLight += CountAdjacentEmpty(board, r, c);
                        }
                    }
                    else if (piece == opponent)
                    {
                        opPieces++;

                        if (phase == GamePhase.MOVEMENT)
                        {
                            opMobilityLight += CountAdjacentEmpty(board, r, c);
                        }
                    }
                }
            }

            // 下一手權評估
            if (phase == GamePhase.PLACEMENT)
            {
                string nextPlayer = GetNextPlayer(currentPlayer, moveIndex, phase);
                score += (nextPlayer == currentPlayer) ? FIRST_MOVE_BONUS : -FIRST_MOVE_BONUS;
            }

            // 輕量化機動性評估（不呼叫 GetValidMoves）
            if (phase == GamePhase.MOVEMENT)
            {
                score += (myMobilityLight - opMobilityLight) * MOBILITY_LIGHT;
            }

            score += (myPieces - opPieces) * MAT;

            return score;
        }

        // ===== 輕量化機動性計算 =====
        private int CountAdjacentEmpty(string?[][] board, int r, int c)
        {
            int count = 0;
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i];
                int nc = c + dc[i];
                if (In(nr, nc) && board[nr][nc] == null)
                    count++;
            }

            return count;
        }

        private int CalculateVulnerability(string?[][] board, int r, int c, string opponent)
        {
            int vulnerability = 0;
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int nearR = r + dr[i];
                int nearC = c + dc[i];
                int farR = r - dr[i];
                int farC = c - dc[i];

                if (!In(nearR, nearC) || !In(farR, farC)) continue;

                if (board[nearR][nearC] == opponent && board[farR][farC] == null)
                {
                    vulnerability++;
                }
            }

            return vulnerability;
        }

        private double GetMoveOrderingScore(string?[][] board, Move m, string player,
                                           GamePhase phase, int moveIndex,
                                           Move? lastX, Move? lastO)
        {
            if (phase != GamePhase.MOVEMENT) return 0;

            var ud = _gs.MakeMove(board, m, player, phase, moveIndex);
            double score = ud.Captured.Count * 1000;

            Move? nextX = (player == "X") ? m : lastX;
            Move? nextO = (player == "O") ? m : lastO;
            var opMoves = _gs.GetValidMoves(board, _gs.GetOpponent(player),
                                           GamePhase.MOVEMENT, nextX, nextO);

            if (opMoves.Count == 0) score += SUFFOCATE_BONUS;

            _gs.UnmakeMove(board, ud, player);
            return score;
        }

        private double EvaluateRemovalMove(string?[][] board, Move m, string player,
                                          Move? lastX, Move? lastO)
        {
            var ud = _gs.MakeMove(board, m, player, GamePhase.STUCK_REMOVAL, 0);

            var myMoves = _gs.GetValidMoves(board, player, GamePhase.MOVEMENT, lastX, lastO);
            double score = myMoves.Count * 10.0;

            foreach (var nextMove in myMoves)
            {
                var ud2 = _gs.MakeMove(board, nextMove, player, GamePhase.MOVEMENT, 1);
                if (ud2.Captured.Count > 0) score += 5000.0;
                _gs.UnmakeMove(board, ud2, player);
            }

            _gs.UnmakeMove(board, ud, player);
            return score;
        }

        private bool IsSameMove(Move a, Move b)
        {
            if (a.From == null && b.From == null)
                return a.To.R == b.To.R && a.To.C == b.To.C;

            if (a.From != null && b.From != null)
                return a.From.R == b.From.R && a.From.C == b.From.C &&
                       a.To.R == b.To.R && a.To.C == b.To.C;

            return false;
        }

        private bool In(int r, int c) => r >= 0 && r < 5 && c >= 0 && c < 5;

        private long UpdatePieceHash(long h, Move m, string p, List<(Position Pos, string Player)> caps, string? cen)
        {
            int pi = p == "X" ? 0 : 1;
            if (m.From != null) h ^= Zobrist[m.From.R, m.From.C, pi];
            h ^= Zobrist[m.To.R, m.To.C, pi];
            foreach (var cap in caps) h ^= Zobrist[cap.Pos.R, cap.Pos.C, 1 - pi];
            if (cen != null) h ^= Zobrist[2, 2, cen == "X" ? 0 : 1];
            return h;
        }

        private long InitialHash(string?[][] b, string curr)
        {
            long h = (curr == "X" ? 0 : SideHash);
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    if (b[r][c] != null) h ^= Zobrist[r, c, b[r][c] == "X" ? 0 : 1];
            return h;
        }
    }
}