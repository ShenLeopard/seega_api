using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {// 用於搜尋前的快速著法排序 (Heuristic Move Ordering)
        private int GetMoveOrderingScore(string?[][] board, Move m, string player,
                                        GamePhase phase, int moveIndex, Move? lastX, Move? lastO)
        {
            // 如果是佈陣階段，優先佔領靠近中心的格子
            if (phase == GamePhase.PLACEMENT)
            {
                int rDist = Math.Abs(m.To.R - 2);
                int cDist = Math.Abs(m.To.C - 2);
                return 100 - (rDist + cDist) * 10;
            }

            // 如果是移動階段，看吃子與窒息潛力
            if (phase == GamePhase.MOVEMENT)
            {
                var ud = _gs.MakeMove(board, m, player, phase, moveIndex);
                int score = 0;

                // 立即吃子權重最高
                score += ud.Captured.Count * 2000;

                // 預判是否將對手鎖死 (Stuck)
                string op = _gs.GetOpponent(player);
                Move? nX = (player == "X") ? m : lastX;
                Move? nO = (player == "O") ? m : lastO;

                var opMoves = _gs.GetValidMoves(board, op, GamePhase.MOVEMENT, nX, nO);
                if (opMoves.Count == 0) score += 3000; // SUFFOCATE_BONUS

                _gs.UnmakeMove(board, ud, player);
                return score;
            }

            return 0;
        }
        private int EvaluatePosition(string?[][] board, string currentPlayer, GamePhase phase, int moveIndex)
        {
            int score = 0;
            int myPieces = 0, opPieces = 0;
            int myMobility = 0, opMobility = 0;
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
                        if (phase == GamePhase.MOVEMENT) myMobility += CountAdjacentEmpty(board, r, c);
                    }
                    else
                    {
                        opPieces++;
                        if (phase == GamePhase.MOVEMENT) opMobility += CountAdjacentEmpty(board, r, c);
                    }
                }
            }

            if (phase == GamePhase.PLACEMENT)
            {
                // --- 核心優化：結構化威脅掃描 (防止 O-X-O 陷阱) ---
                int maxThreatToMe = GetMaxCaptureThreat(board, currentPlayer, opponent);
                if (maxThreatToMe > 0)
                {
                    // 防守方(後手)懲罰極高 (-4000)，攻擊方(先手)懲罰中等 (-1500)
                    int penaltyUnit = iAmAttacker ? 1500 : 4000;
                    score -= (maxThreatToMe * penaltyUnit);
                }

                string nextPlayer = GetNextPlayer(currentPlayer, moveIndex, phase);
                score += (nextPlayer == currentPlayer) ? FIRST_MOVE_BONUS : -FIRST_MOVE_BONUS;
            }

            if (phase == GamePhase.MOVEMENT)
                score += (myMobility - opMobility) * MOBILITY_LIGHT;

            score += (myPieces - opPieces) * MAT;
            return score;
        }

        // 偵測盤面上威脅最大的「空格」
        private int GetMaxCaptureThreat(string?[][] board, string myColor, string opColor)
        {
            int maxThreat = 0;
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    // 只掃描空格 (佈陣時中心點 C3 視為潛在空格)
                    if (board[r][c] != null && !(r == 2 && c == 2)) continue;

                    int threatInThisSpot = 0;
                    // 檢查水平: Op - [Spot] - My 或 My - [Spot] - Op (這會導致 My 被吃)
                    if (IsThreatSpot(board, r, c, 0, -1, 0, 1, myColor, opColor)) threatInThisSpot++;
                    // 檢查垂直
                    if (IsThreatSpot(board, r, c, -1, 0, 1, 0, myColor, opColor)) threatInThisSpot++;

                    if (threatInThisSpot > maxThreat) maxThreat = threatInThisSpot;
                }
            }
            return maxThreat;
        }

        private bool IsThreatSpot(string?[][] b, int r, int c, int dr1, int dc1, int dr2, int dc2, string my, string op)
        {
            int r1 = r + dr1, c1 = c + dc1, r2 = r + dr2, c2 = c + dc2;
            if (In(r1, c1) && In(r2, c2))
            {
                // 如果一邊是敵人，另一邊是我方，則這個空格是我的「死亡點」
                return (b[r1][c1] == my && b[r2][c2] == op) || (b[r1][c1] == op && b[r2][c2] == my);
            }
            return false;
        }

        private int CountAdjacentEmpty(string?[][] board, int r, int c)
        {
            int count = 0;
            int[] dr = { -1, 1, 0, 0 }; int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i], nc = c + dc[i];
                if (In(nr, nc) && board[nr][nc] == null) count++;
            }
            return count;
        }

        private int EvaluateRemovalMove(string?[][] board, Move m, string player, Move? lastX, Move? lastO)
        {
            // 1. 模擬移除
            var ud = _gs.MakeMove(board, m, player, GamePhase.STUCK_REMOVAL, 0);

            // 2. 評估移除後，我的行動力 (能走幾步)
            var myMoves = _gs.GetValidMoves(board, player, GamePhase.MOVEMENT, lastX, lastO);
            int score = myMoves.Count * 10;

            // 3. 檢查移除後，我是否能立刻發動吃子 (Combo)
            foreach (var nextMove in myMoves)
            {
                var ud2 = _gs.MakeMove(board, nextMove, player, GamePhase.MOVEMENT, 1);
                if (ud2.Captured.Count > 0) score += 5000; // 發現連殺機會，權重極高
                _gs.UnmakeMove(board, ud2, player);
            }

            // 4. 復原
            _gs.UnmakeMove(board, ud, player);
            return score;
        }
    }
}