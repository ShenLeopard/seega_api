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
            if (phase == GamePhase.PLACEMENT)
            {
                int rDist = Math.Abs(m.To.R - 2);
                int cDist = Math.Abs(m.To.C - 2);
                return 100 - (rDist + cDist) * 10;
            }

            if (phase == GamePhase.MOVEMENT)
            {
                // --- 修正：禁止在排序中呼叫 GetValidMoves ---
                int score = 0;
                int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
                string op = player == "X" ? "O" : "X";

                // 1. 立即吃子判定 (預判，不需 MakeMove)
                for (int i = 0; i < 4; i++)
                {
                    int r1 = m.To.R + dr[i], c1 = m.To.C + dc[i];
                    int r2 = m.To.R + dr[i] * 2, c2 = m.To.C + dc[i] * 2;
                    if (In(r2, c2) && board[r1][c1] == op && board[r2][c2] == player)
                        score += 10000;
                }

                // 2. 禁止回頭路判定 (如果上一手就是從 To 走過來的，直接排到最後)
                Move? myL = (player == "X") ? lastX : lastO;
                if (myL != null && myL.From != null &&
                    m.To.R == myL.From.R && m.To.C == myL.From.C) score -= 5000;

                return score;
            }
            return 0;
        }
        // 【新增】輕量級排序：專供 AlphaBeta 遞迴內部使用
        // 絕對禁止在此呼叫 MakeMove 或 GetValidMoves，只做 O(1) 的座標運算
        private int GetFastMoveOrderingScore(string?[][] board, Move m, string player)
        {
            int score = 0;
            string op = player == "X" ? "O" : "X";
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };

            // 1. 快速預判吃子 (Capture Heuristic)
            for (int i = 0; i < 4; i++)
            {
                int r1 = m.To.R + dr[i], c1 = m.To.C + dc[i];
                int r2 = m.To.R + dr[i] * 2, c2 = m.To.C + dc[i] * 2;

                // 只要能吃子，分數加爆，確保先搜這步
                if (In(r2, c2) && board[r1][c1] == op && board[r2][c2] == player)
                    score += 10000;
            }

            // 2. 距離引導 (可選)：在沒吃子的情況下，稍微傾向靠近中心或敵人，避免在外圍發呆
            // 這裡只給很小的權重，以免干擾吃子判斷
            score += (10 - (Math.Abs(m.To.R - 2) + Math.Abs(m.To.C - 2)));

            return score;
        }

        // 【重新命名】重型排序：只在 RootSearch (第一層) 使用
        // 這裡保留你原本的邏輯：包含吃子模擬 + 檢查對手是否窒息 (Stuck/Suffocate)
        private int GetHeavyMoveOrderingScore(string?[][] board, Move m, string player,
                        GamePhase phase, int moveIndex, Move? lastX, Move? lastO)
        {
            if (phase == GamePhase.PLACEMENT)
            {
                // 1. 基本中心引力 (但避開正中心 2,2)
                if (m.To.R == 2 && m.To.C == 2) return -1000;

                int rDist = Math.Abs(m.To.R - 2);
                int cDist = Math.Abs(m.To.C - 2);
                int score = 100 - (rDist + cDist) * 10;

                // 2. 致命傷偵測：絕對不下會被開局殺的位置
                if (IsVulnerableToOpeningKill(board, m.To.R, m.To.C, player))
                    return -50000;

                return score;
            }

            if (phase == GamePhase.MOVEMENT)
            {
                // 移動階段邏輯 (已在之前修復，保持不變)
                var ud = _gs.MakeMove(board, m, player, phase, moveIndex);
                int score = ud.Captured.Count * 2000;

                string op = _gs.GetOpponent(player);
                Move? nX = (player == "X") ? m : lastX;
                Move? nO = (player == "O") ? m : lastO;

                var opMoves = _gs.GetValidMoves(board, op, GamePhase.MOVEMENT, nX, nO);
                if (opMoves.Count == 0)
                {
                    int opCount = 0;
                    foreach (var row in board) foreach (var cell in row) if (cell == op) opCount++;
                    score += (opCount <= 4) ? -5000 : 3000;
                }
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

                        // 只有在移動階段中心點才有分數
                        if (phase == GamePhase.MOVEMENT && r == 2 && c == 2) score += CEN;

                        if (phase == GamePhase.PLACEMENT)
                        {
                            // 佈陣期：強化相鄰加分，形成防禦鏈
                            int neighbors = CountAdjacentFriendly(board, r, c, currentPlayer);
                            score += Math.Min(neighbors, 2) * 60;

                            // 脆弱性檢查
                            int v = CalculateVulnerability(board, r, c, opponent);
                            score -= iAmAttacker ? v * 100 : v * 700;
                        }

                        if (phase == GamePhase.MOVEMENT)
                        {
                            myMobility += CountAdjacentEmpty(board, r, c);
                            if (IsNextToEnemy(board, r, c, opponent)) score += CONTACT_BONUS;
                        }
                    }
                    else
                    {
                        opPieces++;
                        opPos.Add(new Position { R = r, C = c });
                        if (phase == GamePhase.MOVEMENT) opMobility += CountAdjacentEmpty(board, r, c);
                    }
                }
            }

            // 2. 佈陣階段特殊邏輯：開局殺預判
            if (phase == GamePhase.PLACEMENT)
            {
                // [關鍵] 判斷誰會拿到第 25 手的先攻權
                // 在 Seega 規則下，放下第 24 子的人擁有第 25 手移動權
                bool iMoveFirst = (moveIndex % 4 == 0 || moveIndex % 4 == 3); // 簡易判斷，依據 2+2 規則

                score += GetOpeningKillScore(board, currentPlayer, opponent, iMoveFirst);

                // 基礎佈陣加分 (原有邏輯)
                score += GetPotentialCaptureScore(board, currentPlayer, opponent, phase, moveIndex) * 2000;
            }

            // 3. 移動階段評估
            if (phase == GamePhase.MOVEMENT && myPos.Count > 0 && opPos.Count > 0)
            {
                score += CalculateProximityScore(myPos, opPos);
                score += (myMobility - opMobility) * 5;

                // 殘局威脅感知 (封鎖線偵測)
                if (opPieces <= 4)
                {
                    foreach (var opP in opPos)
                    {
                        int[] dr = { -1, 1, 0, 0 }; int[] dc = { 0, 0, -1, 1 };
                        for (int i = 0; i < 4; i++)
                        {
                            int r1 = opP.R + dr[i], c1 = opP.C + dc[i];
                            int r2 = opP.R - dr[i], c2 = opP.C - dc[i];
                            if (In(r1, c1) && In(r2, c2))
                            {
                                if ((board[r1][c1] == currentPlayer && board[r2][c2] == null) ||
                                    (board[r1][c1] == null && board[r2][c2] == currentPlayer))
                                    score += 1500;
                            }
                        }
                    }
                }
            }

            score += (myPieces - opPieces) * MAT;
            return score;
        }

        private int GetOpeningKillScore(string?[][] b, string me, string op, bool iMoveFirst)
        {
            int bonus = 0;
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int adjR = 2 + dr[i], adjC = 2 + dc[i];         // 中心點周圍 (C2, C4, B3, D3)
                int farR = 2 + dr[i] * 2, farC = 2 + dc[i] * 2; // 對面位置 (C1, C5, A3, E3)

                if (In(adjR, adjC) && In(farR, farC))
                {
                    // A. 我方發動開局殺的條件：
                    // 中心對面是我，中心旁邊是敵，且我擁有第 25 手移動權
                    if (b[farR][farC] == me && b[adjR][adjC] == op)
                    {
                        if (iMoveFirst && HasNearbyPiece(b, 2, 2, me)) bonus += 5000; // 高額獎勵進攻
                        else bonus += 500; // 即使沒先手，這也是好的防禦佈陣
                    }

                    // B. 我方會被開局殺的條件 (重罰)：
                    // 中心旁邊是我，中心對面是敵，且敵方擁有第 25 手移動權
                    if (b[farR][farC] == op && b[adjR][adjC] == me)
                    {
                        if (!iMoveFirst) bonus -= 8000; // 極重罰，這是必死位
                        else bonus -= 1000; // 即使我有先手，這位置也太危險
                    }
                }
            }
            return bonus;
        }
        private bool IsVulnerableToOpeningKill(string?[][] b, int r, int c, string me)
        {
            // 只檢查中心周圍的四個關鍵位置
            if (Math.Abs(r - 2) + Math.Abs(c - 2) != 1) return false;

            string op = _gs.GetOpponent(me);
            // 找出中心對稱點 (如果我在 C2，對稱點就是 C4)
            int oppR = 2 + (2 - r);
            int oppC = 2 + (2 - c);

            // 如果對稱點已經有敵方棋子，那這一步就是送死
            if (In(oppR, oppC) && b[oppR][oppC] == op) return true;

            return false;
        }

        // 檢查周圍是否有棋子可以跳入指定位置
        private bool HasNearbyPiece(string?[][] b, int r, int c, string me)
        {
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = r + dr[i], nc = c + dc[i];
                if (In(nr, nc) && b[nr][nc] == me) return true;
            }
            return false;
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