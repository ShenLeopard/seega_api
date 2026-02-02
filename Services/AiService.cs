using SeegaGame.Models;

namespace SeegaGame.Services
{
    public class AiService : IAiService
    {
        private readonly IGameService _gameService;
        private readonly ILogger<AiService> _logger;

        private const double SCORE_WIN = 100000.0;
        private const double SCORE_MATERIAL = 1000.0;
        private const double SCORE_CENTER = 60.0;

        // 平衡調整：威脅扣分回調，避免 AI 為了進攻而無視被吃
        private const double SCORE_THREAT = -150.0;

        private const double SCORE_MOBILITY = 30.0;
        private const double SCORE_PRESSURE = 20.0;

        public AiService(IGameService gameService, ILogger<AiService> logger)
        {
            _gameService = gameService;
            _logger = logger;
        }

        public Move? GetBestMove(string?[][] board, string aiPlayer, GamePhase phase, int difficulty, Move? lastMoveX, Move? lastMoveO)
        {
            // 1. 受困模式：AI 計算拆哪一顆最有利
            if (phase == GamePhase.STUCK_REMOVAL)
            {
                var removeMoves = _gameService.GetValidMoves(board, aiPlayer, GamePhase.STUCK_REMOVAL, null, null);
                if (!removeMoves.Any()) return null;

                // 拆子策略：優先拆掉能讓我下一步吃子的棋子
                return removeMoves
                    .OrderByDescending(m => EvaluateRemoval(board, m, aiPlayer, lastMoveX, lastMoveO))
                    .First();
            }

            // 2. 正常模式
            var moves = _gameService.GetValidMoves(board, aiPlayer, phase, lastMoveX, lastMoveO);
            if (!moves.Any()) return null;

            int depth = (phase == GamePhase.PLACEMENT) ? 2 : difficulty;

            return ExecuteSearch(board, aiPlayer, phase, depth, moves, lastMoveX, lastMoveO);
        }

        private double EvaluateRemoval(string?[][] board, Move move, string aiPlayer, Move? lastX, Move? lastO)
        {
            var tempBoard = _gameService.CloneBoard(board);
            tempBoard[move.To.R][move.To.C] = null;

            // 移除後，我的機動性如何？
            var myMoves = _gameService.GetValidMoves(tempBoard, aiPlayer, GamePhase.MOVEMENT, lastX, lastO);
            double score = myMoves.Count * 50.0;

            // 移除後，我能否立刻吃子？(連動獎勵)
            foreach (var m in myMoves)
            {
                var simBoard = _gameService.CloneBoard(tempBoard);
                if (m.From != null) simBoard[m.From.R][m.From.C] = null;
                simBoard[m.To.R][m.To.C] = aiPlayer;
                var (_, captured) = _gameService.ProcessMoveEffect(simBoard, m.To, aiPlayer, GamePhase.MOVEMENT, m.From);
                if (captured.Count > 0) score += 3000.0; // 發現移除後的連殺機會
            }
            return score;
        }

        private Move? ExecuteSearch(string?[][] board, string aiPlayer, GamePhase phase, int depth, List<Move> moves, Move? lastMoveX, Move? lastMoveO)
        {
            Move? bestMove = null;
            double bestScore = double.NegativeInfinity;
            string opponent = _gameService.GetOpponent(aiPlayer);

            var orderedMoves = moves
                .Select(m => new { Move = m, Score = QuickEvaluate(board, m, aiPlayer, phase) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Move.To.R).ThenBy(x => x.Move.To.C)
                .ToList();

            foreach (var item in orderedMoves)
            {
                var nextBoard = SimulateMove(board, item.Move, aiPlayer, phase);
                Move? nextX = (aiPlayer == "X") ? item.Move : lastMoveX;
                Move? nextO = (aiPlayer == "O") ? item.Move : lastMoveO;

                double score = Minimax(nextBoard, depth - 1, double.NegativeInfinity, double.PositiveInfinity, false, aiPlayer, opponent, nextX, nextO, phase);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = item.Move;
                }
            }
            return bestMove;
        }

        private double Minimax(string?[][] board, int depth, double alpha, double beta, bool isMaximizing, string aiPlayer, string opponent, Move? lastMoveX, Move? lastMoveO, GamePhase phase)
        {
            string? winner = _gameService.CheckWinner(board, phase);
            if (winner == aiPlayer) return SCORE_WIN + depth * 1000;
            if (winner == opponent) return -SCORE_WIN - depth * 1000;

            // 葉節點評估
            if (depth == 0) return EvaluateBoard(board, aiPlayer, opponent, lastMoveX, lastMoveO);

            var currentPlayer = isMaximizing ? aiPlayer : opponent;
            var moves = _gameService.GetValidMoves(board, currentPlayer, GamePhase.MOVEMENT, lastMoveX, lastMoveO);

            // 受困價值反轉
            if (!moves.Any())
            {
                // 先計算當前盤面分數
                double currentScore = EvaluateBoard(board, aiPlayer, opponent, lastMoveX, lastMoveO);

                // 判斷誰受困？
                if (currentPlayer == aiPlayer)
                {
                    // AI (我) 受困 -> 我可以移除對手一顆子
                    // 效果等同於我吃了一顆子 (+1000)
                    // 所以這裡要回傳「比當前盤面更好」的分數
                    return currentScore + SCORE_MATERIAL;
                }
                else
                {
                    // 對手受困 -> 對手可以移除我一顆子
                    // 效果等同於我被吃了一顆子 (-1000)
                    // 所以這裡要回傳「比當前盤面更差」的分數
                    return currentScore - SCORE_MATERIAL;
                }
            }

            if (isMaximizing)
            {
                double maxEval = double.NegativeInfinity;
                foreach (var move in moves)
                {
                    var nextBoard = SimulateMove(board, move, aiPlayer, GamePhase.MOVEMENT);
                    Move? nextX = (aiPlayer == "X") ? move : lastMoveX;
                    Move? nextO = (aiPlayer == "O") ? move : lastMoveO;

                    double eval = Minimax(nextBoard, depth - 1, alpha, beta, false, aiPlayer, opponent, nextX, nextO, GamePhase.MOVEMENT);
                    maxEval = Math.Max(maxEval, eval);
                    alpha = Math.Max(alpha, eval);
                    if (beta <= alpha) break;
                }
                return maxEval;
            }
            else
            {
                double minEval = double.PositiveInfinity;
                foreach (var move in moves)
                {
                    var nextBoard = SimulateMove(board, move, opponent, GamePhase.MOVEMENT);
                    Move? nextX = (opponent == "X") ? move : lastMoveX;
                    Move? nextO = (opponent == "O") ? move : lastMoveO;

                    double eval = Minimax(nextBoard, depth - 1, alpha, beta, true, aiPlayer, opponent, nextX, nextO, GamePhase.MOVEMENT);
                    minEval = Math.Min(minEval, eval);
                    beta = Math.Min(beta, eval);
                    if (beta <= alpha) break;
                }
                return minEval;
            }
        }

        private double QuickEvaluate(string?[][] board, Move move, string player, GamePhase phase)
        {
            var nextBoard = SimulateMove(board, move, player, phase);
            string opponent = _gameService.GetOpponent(player);

            // 殺招
            if (_gameService.CountPlayerPieces(nextBoard, opponent) < 2) return 10000000.0;

            // 吃子
            int captured = _gameService.CountPlayerPieces(board, opponent) - _gameService.CountPlayerPieces(nextBoard, opponent);

            // 移動中心
            double centerBonus = 0;
            if (phase == GamePhase.MOVEMENT && move.To.R == 2 && move.To.C == 2) centerBonus = 200.0;

            return (captured * 1000.0) + centerBonus;
        }

        private double EvaluateBoard(string?[][] board, string aiPlayer, string opponent, Move? lastX, Move? lastO)
        {
            double score = 0;
            int aiCount = 0;
            int opCount = 0;

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    string? p = board[r][c];
                    if (p == null) continue;

                    bool isCenter = (r >= 1 && r <= 3 && c >= 1 && c <= 3);

                    if (p == aiPlayer)
                    {
                        aiCount++;
                        if (isCenter) score += SCORE_CENTER;
                        if (IsNextToEnemy(board, r, c, opponent)) score += SCORE_PRESSURE;

                        // 威脅扣分 (現在是 -150，平衡點)
                        if (opCount >= 2 && IsThreatened(board, r, c, aiPlayer, opponent))
                            score += SCORE_THREAT;
                    }
                    else if (p == opponent)
                    {
                        opCount++;
                        if (isCenter) score -= SCORE_CENTER;
                    }
                }
            }

            // 機動性
            var aiMoves = _gameService.GetValidMoves(board, aiPlayer, GamePhase.MOVEMENT, lastX, lastO);
            var opMoves = _gameService.GetValidMoves(board, opponent, GamePhase.MOVEMENT, lastX, lastO);
            score += (aiMoves.Count - opMoves.Count) * SCORE_MOBILITY;

            // 材力
            int total = aiCount + opCount;
            double phaseFactor = 1.0 + Math.Max(0, (24 - total) / 8.0);
            score += (aiCount - opCount) * SCORE_MATERIAL * phaseFactor;

            return score;
        }

        private string?[][] SimulateMove(string?[][] board, Move move, string player, GamePhase phase)
        {
            var temp = _gameService.CloneBoard(board);
            if (move.From != null) temp[move.From.R][move.From.C] = null;
            temp[move.To.R][move.To.C] = player;
            var (res, _) = _gameService.ProcessMoveEffect(temp, move.To, player, phase, move.From);
            return res;
        }

        private bool IsNextToEnemy(string?[][] board, int r, int c, string enemy)
        {
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i], nc = c + dc[i];
                if (nr >= 0 && nr < 5 && nc >= 0 && nc < 5 && board[nr][nc] == enemy) return true;
            }
            return false;
        }

        private bool IsThreatened(string?[][] board, int r, int c, string me, string enemy)
        {
            bool top = IsEnemy(board, r - 1, c, enemy);
            bool bot = IsEnemy(board, r + 1, c, enemy);
            if (top && bot) return true;
            bool left = IsEnemy(board, r, c - 1, enemy);
            bool right = IsEnemy(board, r, c + 1, enemy);
            if (left && right) return true;
            return false;
        }

        private bool IsEnemy(string?[][] board, int r, int c, string enemy)
        {
            if (r < 0 || r >= 5 || c < 0 || c >= 5) return false;
            return board[r][c] == enemy;
        }
    }
}