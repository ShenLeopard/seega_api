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

            if (IsCaptureMove(board, m, player)) score += 15000; // 提高吃子權重

            if (m.From != null) // MOVEMENT 階段
            {
                // 自殺步檢查：扣分必須大於吃子得分，防止無腦換子
                if (IsSuicideMove(board, m, player, op)) score -= 20000;
            }

            score += (10 - (Math.Abs(m.To.R - 2) + Math.Abs(m.To.C - 2)));
            return score;
        }
        private bool IsSuicideMove(string?[][] board, Move m, string player, string op)
        {
            if (m.From == null) return false;
            string? originFrom = board[m.From.R][m.From.C];
            string? originTo = board[m.To.R][m.To.C];
            board[m.From.R][m.From.C] = null;
            board[m.To.R][m.To.C] = player;
            bool risk = IsPieceAtRisk(board, m.To.R, m.To.C, player, op);
            board[m.From.R][m.From.C] = originFrom;
            board[m.To.R][m.To.C] = originTo;
            return risk;
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
        // 【修正版】評估函式：加入對等危機意識
        private int EvaluatePosition(string?[][] board, string currentPlayer, GamePhase phase, int moveIndex)
        {
            int score = 0;
            int myPieces = 0, opPieces = 0;
            int myMobility = 0, opMobility = 0;
            int dangerScore = 0;

            int[] myQuadrants = new int[4];
            List<Position> myPos = new();
            List<Position> opPos = new();

            string opponent = _gs.GetOpponent(currentPlayer);
            bool currentIsAttacker = IsAttacker(currentPlayer, currentPlayer, moveIndex);

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
                        int q = (r <= 2 ? 0 : 2) + (c <= 2 ? 0 : 1);
                        myQuadrants[q]++;

                        if (phase == GamePhase.MOVEMENT)
                        {
                            myMobility += CountAdjacentEmpty(board, r, c);
                            if (IsNextToEnemy(board, r, c, opponent)) score += CONTACT_BONUS;
                            // 我方危機偵測 (防自殺)
                            if (IsPieceAtRisk(board, r, c, currentPlayer, opponent))
                                dangerScore -= (MAT * 4 / 5);
                        }
                        else if (phase == GamePhase.PLACEMENT)
                        {
                            int v = CalculateVulnerability(board, r, c, opponent);
                            // 防守方(先手)要極度小心佈陣，進攻方(後手)則可以稍激進
                            score -= currentIsAttacker ? v * 150 : v * 800;
                        }
                    }
                    else // 敵方
                    {
                        opPieces++;
                        opPos.Add(new Position { R = r, C = c });
                        if (phase == GamePhase.MOVEMENT)
                        {
                            opMobility += CountAdjacentEmpty(board, r, c);
                            // 敵方危機偵測 (增加攻擊直覺)
                            if (IsPieceAtRisk(board, r, c, opponent, currentPlayer))
                                dangerScore += (MAT * 3 / 4);
                        }
                    }
                }
            }

            if (phase == GamePhase.MOVEMENT)
            {
                // 空間平衡：鼓勵分散，懲罰擁擠
                int occupied = 0;
                foreach (int count in myQuadrants) if (count > 0) occupied++;
                score += occupied * 400;
                foreach (int count in myQuadrants) if (count > 5) score -= (count - 5) * 600;

                // 殘局收割邏輯
                if (opPieces <= 3)
                {
                    score += CalculateProximityScore(myPos, opPos) * 3;
                    score += (myMobility - opMobility) * 2;
                }
                else
                {
                    score += CalculateProximityScore(myPos, opPos);
                    score += (myMobility - opMobility) * 5;
                }
            }
            else if (phase == GamePhase.PLACEMENT)
            {
                // 誰是後手誰就擁有第 25 手的 Opening Kill 機會
                score += GetOpeningKillScore(board, currentPlayer, opponent, currentIsAttacker);
            }

            score += dangerScore;
            score += (myPieces - opPieces) * MAT;
            return score;
        }
        private bool IsPieceAtRisk(string?[][] b, int r, int c, string me, string op)
        {
            int[] dr = { 1, 0 }; int[] dc = { 0, 1 };
            for (int i = 0; i < 2; i++)
            {
                int r1 = r + dr[i], c1 = c + dc[i];
                int r2 = r - dr[i], c2 = c - dc[i];
                if (In(r1, c1) && In(r2, c2))
                {
                    if (b[r1][c1] == op && b[r2][c2] == null && CanPlayerReach(b, op, r2, c2)) return true;
                    if (b[r1][c1] == null && b[r2][c2] == op && CanPlayerReach(b, op, r1, c1)) return true;
                }
            }
            return false;
        }
        // 【新增】快速判定對手是否能移動到某個空格
        private bool CanPlayerReach(string?[][] b, string player, int tr, int tc)
        {
            int[] dr = { -1, 1, 0, 0 }; int[] dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = tr + dr[i], nc = tc + dc[i];
                if (In(nr, nc) && b[nr][nc] == player) return true;
            }
            return false;
        }
        private int GetOpeningKillScore(string?[][] b, string me, string op, bool iMoveFirst)
        {
            int bonus = 0;
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int adjR = 2 + dr[i], adjC = 2 + dc[i];
                int farR = 2 + dr[i] * 2, farC = 2 + dc[i] * 2;
                if (In(adjR, adjC) && In(farR, farC))
                {
                    if (b[farR][farC] == me && b[adjR][adjC] == op)
                        bonus += iMoveFirst ? 5000 : 500;
                    if (b[farR][farC] == op && b[adjR][adjC] == me)
                        bonus -= iMoveFirst ? 1000 : 8000;
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
                    int dist = Math.Abs(my.R - op.R) + Math.Abs(my.C - op.C);
                    if (dist < minDist) minDist = dist;
                }
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