using SeegaGame.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Runtime.CompilerServices;

namespace SeegaGame.Services
{

    // 每個遊戲局的獨立上下文 (約佔 1MB RAM)
    public class GameTTContext
    {
        // 2^16 = 65536 筆資料
        // Mask = 0xFFFF
        public const int SIZE = 65536;
        public const int MASK = SIZE - 1;

        public TTEntry[] Entries = new TTEntry[SIZE];
        public readonly object SyncRoot = new object(); // 用於該局的寫入鎖
    }

    public class AiService
    {
        private readonly GameService _gs;
        private readonly IMemoryCache _cache;

        // Zobrist 隨機表保持 static (唯讀，執行緒安全，節省記憶體)
        private static readonly long[,,] Zobrist;
        private static readonly long SideHash;

        // 評分常數
        private const double WIN = 1000000.0;
        private const double MAT = 2000.0;
        private const double STUCK_ADVANTAGE = 2500.0;
        private const double CEN = 60.0;
        private const double FIRST_MOVE_BONUS = 6000.0;
        private const double SUFFOCATE_BONUS = 3000.0;
        private const double MOBILITY_LIGHT = 8.0;

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

        // 取得該局遊戲專屬的 TT Context
        private GameTTContext GetContext(string uuid) =>
            _cache.GetOrCreate(uuid, entry =>
            {
                // 設定 30 分鐘滑動過期，自動回收記憶體
                entry.SlidingExpiration = TimeSpan.FromMinutes(30);
                return new GameTTContext();
            })!;

        public Move? GetBestMove(AiMoveRequest req)
        {
            // 數據清洗
            req.LastMoveX = ValidateLastMove(req.Board, req.LastMoveX, "X");
            req.LastMoveO = ValidateLastMove(req.Board, req.LastMoveO, "O");

            // 取得該局專屬的 TT
            var ctx = GetContext(req.GameUUId);
            long h = InitialHash(req.Board, req.CurrentPlayer);

            // STUCK_REMOVAL 模式：簡單貪婪
            if (req.Phase == GamePhase.STUCK_REMOVAL)
            {
                return _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, null, null)
                    .OrderByDescending(m => EvaluateRemovalMove(req.Board, m, req.CurrentPlayer, req.LastMoveX, req.LastMoveO))
                    .FirstOrDefault();
            }

            int d = CalculateSearchDepth(req.Phase, req.MoveIndex, req.Difficulty);
            return RootSearch(ctx, req, h, d);
        }

        // ===== TT 存取方法 (含鎖定) =====

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void StoreTT(GameTTContext ctx, long h, int d, double score, int flag, Move? m)
        {
            int index = (int)(h & GameTTContext.MASK);

            // 使用鎖防止 Struct Tearing
            lock (ctx.SyncRoot)
            {
                ref TTEntry entry = ref ctx.Entries[index];

                // 深度優先替換策略
                if (entry.Key == 0 || entry.Key == h || d >= entry.Depth)
                {
                    entry.Key = h;
                    entry.Score = (int)score;
                    entry.Depth = (byte)d;
                    entry.Flag = (byte)flag;
                    entry.BestMove = EncodeMove(m);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ProbeTT(GameTTContext ctx, long h, int d, double alpha, double beta, out double score, out Move? bestMove)
        {
            int index = (int)(h & GameTTContext.MASK);
            score = 0;
            bestMove = null;

            // 讀取也需要鎖，確保讀到完整的 struct
            lock (ctx.SyncRoot)
            {
                ref TTEntry entry = ref ctx.Entries[index];

                if (entry.Key == h)
                {
                    bestMove = DecodeMove(entry.BestMove);
                    if (entry.Depth >= d)
                    {
                        if (entry.Flag == 0) { score = entry.Score; return true; }
                        if (entry.Flag == 1 && entry.Score <= alpha) { score = alpha; return true; }
                        if (entry.Flag == 2 && entry.Score >= beta) { score = beta; return true; }
                    }
                }
            }
            return false;
        }

        // ===== 搜尋主邏輯 =====

        private Move? RootSearch(GameTTContext ctx, AiMoveRequest req, long h, int d)
        {
            Move? bestM = null;
            double bestScore = double.NegativeInfinity;
            double alpha = double.NegativeInfinity;

            var moves = _gs.GetValidMoves(req.Board, req.CurrentPlayer, req.Phase, req.LastMoveX, req.LastMoveO);
            if (!moves.Any()) return null;

            // 嘗試從 TT 獲取 BestMove 進行排序
            ProbeTT(ctx, h, 0, -double.MaxValue, double.MaxValue, out _, out Move? ttMove);

            var ordered = moves.OrderByDescending(m =>
            {
                if (ttMove != null && IsSameMove(m, ttMove)) return 1000000;
                if (m.To.R == 2 && m.To.C == 2) return 100;
                return GetMoveOrderingScore(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex, req.LastMoveX, req.LastMoveO);
            });

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(req.Board, m, req.CurrentPlayer, req.Phase, req.MoveIndex);
                var state = GetNextState(h, m, req.CurrentPlayer, req.Phase, req.MoveIndex, ud);

                double score;
                Move? nX = (req.CurrentPlayer == "X") ? m : req.LastMoveX;
                Move? nO = (req.CurrentPlayer == "O") ? m : req.LastMoveO;

                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, req.Board, state.nextHash, d - 1, alpha, double.PositiveInfinity, req.CurrentPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1);
                else
                    score = -AlphaBeta(ctx, req.Board, state.nextHash, d - 1, double.NegativeInfinity, -alpha, state.nextPlayer, nX, nO, state.nextPhase, req.MoveIndex + 1);

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

        private double AlphaBeta(GameTTContext ctx, string?[][] board, long h, int d,
                                double alpha, double beta, string curr, Move? lX, Move? lO,
                                GamePhase ph, int idx)
        {
            double originalAlpha = alpha;

            if (ProbeTT(ctx, h, d, alpha, beta, out double ttScore, out _)) return ttScore;

            string? winner = _gs.CheckWinner(board,ph);
            if (winner != null) return (winner == curr) ? (WIN + d) : (-WIN - d);

            if (d <= 0) return Quiesce(ctx, board, h, alpha, beta, curr, lX, lO, ph, idx);

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);
            if (moves.Count == 0) return EvaluatePosition(board, curr, ph, idx) + STUCK_ADVANTAGE;

            // 內部排序嘗試獲取 TT BestMove
            ProbeTT(ctx, h, 0, -double.MaxValue, double.MaxValue, out _, out Move? ttMove);
            var ordered = moves.OrderByDescending(m => (ttMove != null && IsSameMove(m, ttMove)) ? 1000000 : 0);

            double bestS = double.NegativeInfinity;
            Move? bestM = null;

            foreach (var m in ordered)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                var state = GetNextState(h, m, curr, ph, idx, ud);

                double score;
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                if (state.isSamePlayer)
                    score = AlphaBeta(ctx, board, state.nextHash, d - 1, alpha, beta, curr, nX, nO, state.nextPhase, idx + 1);
                else
                    score = -AlphaBeta(ctx, board, state.nextHash, d - 1, -beta, -alpha, state.nextPlayer, nX, nO, state.nextPhase, idx + 1);

                _gs.UnmakeMove(board, ud, curr);

                if (score > bestS) { bestS = score; bestM = m; }
                alpha = Math.Max(alpha, score);
                if (alpha >= beta) break;
            }

            int flag = (bestS <= originalAlpha) ? 1 : (bestS >= beta ? 2 : 0);
            StoreTT(ctx, h, d, bestS, flag, bestM);
            return bestS;
        }

        private double Quiesce(GameTTContext ctx, string?[][] board, long h,
                              double alpha, double beta, string curr,
                              Move? lX, Move? lO, GamePhase ph, int idx)
        {
            if (ProbeTT(ctx, h, 0, alpha, beta, out double ttScore, out _)) return ttScore;

            double standPat = EvaluatePosition(board, curr, ph, idx);
            if (standPat >= beta) return beta;
            if (standPat > alpha) alpha = standPat;

            var moves = _gs.GetValidMoves(board, curr, ph, lX, lO);

            foreach (var m in moves)
            {
                var ud = _gs.MakeMove(board, m, curr, ph, idx);
                if (ud.Captured.Count == 0) { _gs.UnmakeMove(board, ud, curr); continue; }

                var state = GetNextState(h, m, curr, ph, idx, ud);
                Move? nX = (curr == "X") ? m : lX; Move? nO = (curr == "O") ? m : lO;

                double score;
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

        // ===== 輔助邏輯 =====

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

        private double EvaluatePosition(string?[][] board, string currentPlayer, GamePhase phase, int moveIndex)
        {
            double score = 0;
            int myPieces = 0; int opPieces = 0;
            int myMobilityLight = 0; int opMobilityLight = 0;
            string opponent = _gs.GetOpponent(currentPlayer);
            bool iAmAttacker = IsAttacker(currentPlayer, currentPlayer, moveIndex);

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    string? piece = board[r][c];
                    if (piece == null) continue;
                    if (piece == currentPlayer)
                    {
                        myPieces++;
                        if (r == 2 && c == 2) score += CEN;
                        if (phase == GamePhase.PLACEMENT)
                            score -= iAmAttacker ? CalculateVulnerability(board, r, c, opponent) * 100 : CalculateVulnerability(board, r, c, opponent) * 700;
                        if (phase == GamePhase.MOVEMENT) myMobilityLight += CountAdjacentEmpty(board, r, c);
                    }
                    else
                    {
                        opPieces++;
                        if (phase == GamePhase.MOVEMENT) opMobilityLight += CountAdjacentEmpty(board, r, c);
                    }
                }
            }

            if (phase == GamePhase.PLACEMENT)
            {
                // 正確使用 GetNextPlayer
                string nextPlayer = GetNextPlayer(currentPlayer, moveIndex, phase);
                score += (nextPlayer == currentPlayer) ? FIRST_MOVE_BONUS : -FIRST_MOVE_BONUS;
            }

            if (phase == GamePhase.MOVEMENT) score += (myMobilityLight - opMobilityLight) * MOBILITY_LIGHT;
            score += (myPieces - opPieces) * MAT;
            return score;
        }

        // ===== 核心規則判斷 (已修正) =====
        public string GetNextPlayer(string currentPlayer, int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT) return _gs.GetOpponent(currentPlayer);
            // 2+2 開局：1,3 手連動(不換人)；2,4 手換人
            if (moveIndex == 1 || moveIndex == 3) return currentPlayer;
            if (moveIndex == 2 || moveIndex == 4) return _gs.GetOpponent(currentPlayer);
            // 第 24 手連動
            if (moveIndex == 24) return currentPlayer;
            // 正常交替
            return _gs.GetOpponent(currentPlayer);
        }

        private bool IsComboMove(int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT) return false;
            return (moveIndex == 1 || moveIndex == 3 || moveIndex == 24);
        }

        private string InferFirstPlayer(string currentPlayer, int moveIndex)
        {
            if (moveIndex <= 0) return "X";
            bool isFirstPlayerMove = (moveIndex <= 4) ? (moveIndex <= 2) : (moveIndex % 2 == 1);
            return isFirstPlayerMove ? currentPlayer : _gs.GetOpponent(currentPlayer);
        }

        private bool IsAttacker(string player, string currentPlayer, int moveIndex)
        {
            string firstPlayer = InferFirstPlayer(currentPlayer, moveIndex);
            return player == _gs.GetOpponent(firstPlayer); // 默認 B 是攻擊方
        }

        // ===== Move 編碼/解碼 =====
        private short EncodeMove(Move? m)
        {
            if (m == null) return 0;
            byte fromIdx = (m.From == null) ? (byte)255 : (byte)(m.From.R * 5 + m.From.C);
            byte toIdx = (byte)(m.To.R * 5 + m.To.C);
            return (short)((fromIdx << 8) | toIdx);
        }

        private Move? DecodeMove(short val)
        {
            if (val == 0) return null;
            byte fromIdx = (byte)((val >> 8) & 0xFF);
            byte toIdx = (byte)(val & 0xFF);
            return new Move
            {
                From = (fromIdx == 255) ? null : new Position { R = fromIdx / 5, C = fromIdx % 5 },
                To = new Position { R = toIdx / 5, C = toIdx % 5 }
            };
        }

        // ===== 其他輔助方法 =====
        private Move? ValidateLastMove(string?[][] board, Move? lastMove, string player)
        {
            if (lastMove?.To == null) return null;
            if (lastMove.To.R < 0 || lastMove.To.R >= 5 || lastMove.To.C < 0 || lastMove.To.C >= 5) return null;
            if (board[lastMove.To.R][lastMove.To.C] != player) return null;
            return lastMove;
        }

        private int CalculateSearchDepth(GamePhase phase, int moveIndex, int difficulty)
        {
            if (phase == GamePhase.PLACEMENT)
                return (moveIndex >= 18) ? Math.Min(7, (24 - moveIndex) + 2) : 3;
            return difficulty;
        }

        private int CountAdjacentEmpty(string?[][] board, int r, int c)
        {
            int count = 0;
            int[] dr = { -1, 1, 0, 0 }; int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++) { int nr = r + dr[i], nc = c + dc[i]; if (In(nr, nc) && board[nr][nc] == null) count++; }
            return count;
        }

        private int CalculateVulnerability(string?[][] board, int r, int c, string opponent)
        {
            int v = 0;
            int[] dr = { -1, 1, 0, 0 }; int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i], nc = c + dc[i], fr = r - dr[i], fc = c - dc[i];
                if (In(nr, nc) && In(fr, fc) && board[nr][nc] == opponent && board[fr][fc] == null) v++;
            }
            return v;
        }

        private double GetMoveOrderingScore(string?[][] board, Move m, string player, GamePhase phase, int moveIndex, Move? lastX, Move? lastO)
        {
            if (phase != GamePhase.MOVEMENT) return 0;
            var ud = _gs.MakeMove(board, m, player, phase, moveIndex);
            double score = ud.Captured.Count * 1000;
            string op = _gs.GetOpponent(player);
            if (_gs.GetValidMoves(board, op, GamePhase.MOVEMENT, (player == "X" ? m : lastX), (player == "O" ? m : lastO)).Count == 0) score += SUFFOCATE_BONUS;
            _gs.UnmakeMove(board, ud, player);
            return score;
        }

        private double EvaluateRemovalMove(string?[][] board, Move m, string player, Move? lastX, Move? lastO)
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

        private bool IsSameMove(Move? a, Move? b)
        {
            if (a == null || b == null) return false;
            if (a.From == null && b.From == null) return a.To.R == b.To.R && a.To.C == b.To.C;
            if (a.From != null && b.From != null) return a.From.R == b.From.R && a.From.C == b.From.C && a.To.R == b.To.R && a.To.C == b.To.C;
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