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

                        // --- 修正 1：限制相連加分 (上限 2 個鄰居) ---
                        // 讓棋子形成「鏈」或「排」，而非「實心方塊」，釋放棋盤空間
                        int neighbors = CountAdjacentFriendly(board, r, c, currentPlayer);
                        score += Math.Min(neighbors, 2) * 60;

                        if (phase == GamePhase.PLACEMENT)
                        {
                            // --- 修正 2：攻擊方與防守方的差異評估 ---
                            int v = CalculateVulnerability(board, r, c, opponent);
                            if (iAmAttacker)
                            {
                                // 我是獵人：不害怕空隙，甚至獎勵「瞄準」敵人的空隙 (+800)
                                score += v * 800;
                            }
                            else
                            {
                                // 我是獵物：極度害怕被夾擊 (-4000)
                                score -= v * 4000;
                            }
                        }

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
                // --- 修正 3：加入「開局殺」預判 ---
                // 如果我是第 24 手持有者，特別檢查第 25 手跳入中心或其他空格的吃子可能
                score += GetPotentialCaptureScore(board, currentPlayer, opponent, phase, moveIndex) * 2000;

                string nextPlayer = GetNextPlayer(currentPlayer, moveIndex, phase);
                score += (nextPlayer == currentPlayer) ? FIRST_MOVE_BONUS : -FIRST_MOVE_BONUS;
            }

            if (phase == GamePhase.MOVEMENT) score += (myMobility - opMobility) * MOBILITY_LIGHT;

            score += (myPieces - opPieces) * MAT;
            return score;
        }
        //計算單顆棋子的脆弱性 (是否容易被夾擊)
        private int CalculateVulnerability(string?[][] board, int r, int c, string opponent)
        {
            int v = 0;
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i]; // 鄰居座標
                int nc = c + dc[i];
                int fr = r - dr[i]; // 對向座標
                int fc = c - dc[i];

                if (In(nr, nc) && In(fr, fc))
                {
                    // 佈陣期：如果一邊是敵軍，另一邊是空格（或中心點 C3）
                    // 這代表移動階段一開始，對手只要跳進去，這顆子就必死無疑
                    if (board[nr][nc] == opponent && (board[fr][fc] == null || (fr == 2 && fc == 2)))
                    {
                        v++;
                    }
                }
            }
            return v;
        }
        // 新增：偵測潛在的開局殺機會
        private int GetPotentialCaptureScore(string?[][] b, string me, string op, GamePhase ph, int idx)
        {
            if (ph != GamePhase.PLACEMENT) return 0;
            bool iGet24 = ((24 - idx) % 2 == 0);
            if (!iGet24) return 0;

            int potential = 0;
            // 模擬第 25 手可能的跳入點 (包含中心點)
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    if (b[r][c] != null && !(r == 2 && c == 2)) continue;

                    // 如果這個空格周圍能形成對敵人的夾擊
                    if (IsThreatSpot(b, r, c, -1, 0, 1, 0, me, op)) potential++;
                    if (IsThreatSpot(b, r, c, 0, -1, 0, 1, me, op)) potential++;
                }
            }
            return potential;
        }
        private int CountAdjacentFriendly(string?[][] board, int r, int c, string me)
        {
            int count = 0;
            int[] dr = { -1, 1, 0, 0 }; int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i], nc = c + dc[i];
                if (In(nr, nc) && board[nr][nc] == me) count++;
            }
            return count;
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