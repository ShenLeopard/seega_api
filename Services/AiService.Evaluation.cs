using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {// 用於搜尋前的快速著法排序 (Heuristic Move Ordering)

        // 權重微調：讓機動性權重降低，避免 AI 為了刷步數而在後場亂跑
        // PROXIMITY_WEIGHT: 距離每近一格加幾分
        private const int PROXIMITY_WEIGHT = 15;
        // CONTACT_BONUS: 貼著敵人加幾分
        private const int CONTACT_BONUS = 30;
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

            // 用於計算引力 (Proximity)
            List<Position> myPos = new();
            List<Position> opPos = new();

            string opponent = _gs.GetOpponent(currentPlayer);
            bool iAmAttacker = IsAttacker(currentPlayer, currentPlayer, moveIndex);

            // 1. 盤面掃描
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    string? piece = board[r][c];
                    if (piece == null) continue;

                    if (piece == currentPlayer)
                    {
                        myPieces++;
                        myPos.Add(new Position { R = r, C = c });

                        if (r == 2 && c == 2) score += CEN;

                        // --- 修正 A: 只有佈陣階段才重罰脆弱性 ---
                        // 移動階段交給 AlphaBeta 搜尋去判斷死活，靜態評估不要過度恐嚇
                        if (phase == GamePhase.PLACEMENT)
                        {
                            int neighbors = CountAdjacentFriendly(board, r, c, currentPlayer);
                            score += Math.Min(neighbors, 2) * 60; // 限制相連加分

                            int v = CalculateVulnerability(board, r, c, opponent);
                            score -= iAmAttacker ? v * 100 : v * 700; // 佈陣時依然要小心
                        }

                        // --- 修正 B: 移動階段獎勵「貼身肉搏」 ---
                        if (phase == GamePhase.MOVEMENT)
                        {
                            myMobility += CountAdjacentEmpty(board, r, c);
                            // 如果貼著敵人，給予獎勵 (施壓)
                            if (IsNextToEnemy(board, r, c, opponent)) score += CONTACT_BONUS;
                        }
                    }
                    else // 對手
                    {
                        opPieces++;
                        opPos.Add(new Position { R = r, C = c });
                        if (phase == GamePhase.MOVEMENT) opMobility += CountAdjacentEmpty(board, r, c);
                    }
                }
            }

            // 2. 特殊階段加分
            if (phase == GamePhase.PLACEMENT)
            {
                score += GetPotentialCaptureScore(board, currentPlayer, opponent, phase, moveIndex) * 2000;
                string nextPlayer = GetNextPlayer(currentPlayer, moveIndex, phase);
                score += (nextPlayer == currentPlayer) ? FIRST_MOVE_BONUS : -FIRST_MOVE_BONUS;
            }

            // --- 修正 C: 加入「敵我距離引力」 (Proximity Gravity) ---
            if (phase == GamePhase.MOVEMENT && myPos.Count > 0 && opPos.Count > 0)
            {
                score += CalculateProximityScore(myPos, opPos);

                // 機動性權重降低 (從 8 降到 5)，避免為了步數而不敢進攻
                score += (myMobility - opMobility) * 5;
            }

            score += (myPieces - opPieces) * MAT;
            return score;
        }

        private bool IsNextToEnemy(string?[][] board, int r, int c, string opponent)
        {
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i], nc = c + dc[i];
                if (In(nr, nc) && board[nr][nc] == opponent) return true;
            }
            return false;
        }
        // 計算所有己方棋子與「最近敵軍」的距離總和
        // 距離越小，分數越高 -> 驅使 AI 往敵群移動
        private int CalculateProximityScore(List<Position> myPos, List<Position> opPos)
        {
            int totalProximity = 0;

            foreach (var my in myPos)
            {
                int minDist = 100;
                foreach (var op in opPos)
                {
                    // 曼哈頓距離
                    int dist = Math.Abs(my.R - op.R) + Math.Abs(my.C - op.C);
                    if (dist < minDist) minDist = dist;
                }

                // 距離越近分越高 (5x5 最大距離是 8)
                // 每個棋子最高貢獻 PROXIMITY_WEIGHT * 8
                totalProximity += (10 - minDist) * PROXIMITY_WEIGHT;
            }
            return totalProximity;
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