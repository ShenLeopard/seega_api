using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private int EvaluatePosition(string?[][] board, string currentPlayer, GamePhase phase, int moveIndex, string attackerName)
        {
            int score = 0, myPieces = 0, opPieces = 0, dangerScore = 0;
            int[] myQuadrants = new int[4];
            List<Position> myPos = new(), opPos = new();
            string opponent = _gs.GetOpponent(currentPlayer);
            bool iAmAttacker = (currentPlayer == attackerName);

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    string? piece = board[r][c];
                    if (piece == null) continue;
                    if (piece == currentPlayer)
                    {
                        myPieces++; myPos.Add(new Position { R = r, C = c });
                        int q = (r <= 2 ? 0 : 2) + (c <= 2 ? 0 : 1); myQuadrants[q]++;
                        if (phase == GamePhase.PLACEMENT)
                        {
                            int v = CalculateVulnerability(board, r, c, opponent);
                            score -= iAmAttacker ? v * 150 : v * 900;
                        }
                        else
                        {
                            if (IsPieceAtRisk(board, r, c, currentPlayer, opponent)) dangerScore -= 1600;
                        }
                    }
                    else
                    {
                        opPieces++; opPos.Add(new Position { R = r, C = c });
                        if (phase == GamePhase.MOVEMENT && IsPieceAtRisk(board, r, c, opponent, currentPlayer)) dangerScore += 1200;
                    }
                }
            }

            if (phase == GamePhase.MOVEMENT)
            {
                int occupied = 0; foreach (int count in myQuadrants) if (count > 0) occupied++;
                score += occupied * 400;
                foreach (int count in myQuadrants) if (count > 5) score -= (count - 5) * 500;
                score += CalculateProximityScore(myPos, opPos, opPieces);
            }
            else if (phase == GamePhase.PLACEMENT)
            {
                score += GetOpeningKillScore(board, currentPlayer, opponent, iAmAttacker);
            }
            return score + dangerScore + (myPieces - opPieces) * MAT;
        }

        private int CalculateProximityScore(List<Position> myPos, List<Position> opPos, int opPieces)
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
            return opPieces <= 3 ? totalProximity * 3 : totalProximity;
        }

        private int GetOpeningKillScore(string?[][] b, string me, string op, bool iAmAttacker)
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
                        bonus += iAmAttacker ? 5000 : 500;
                    if (b[farR][farC] == op && b[adjR][adjC] == me)
                        bonus -= iAmAttacker ? 1000 : 8000;
                }
            }
            return bonus;
        }

        private int GetHeavyMoveOrderingScore(string?[][] board, Move m, string player, GamePhase phase, int moveIndex, Move? lastX, Move? lastO, string attackerName)
        {
            if (phase == GamePhase.PLACEMENT)
            {
                if (m.To.R == 2 && m.To.C == 2) return -1000;
                int score = 100 - (Math.Abs(m.To.R - 2) + Math.Abs(m.To.C - 2)) * 10;
                if (IsVulnerableToOpeningKill(board, m.To.R, m.To.C, player)) return -50000;
                return score;
            }
            var ud = _gs.MakeMove(board, m, player, phase, moveIndex);
            int s = ud.Captured.Count * 2000;
            _gs.UnmakeMove(board, ud, player);
            return s;
        }

        private int GetFastMoveOrderingScore(string?[][] board, Move m, string player, string attackerName)
        {
            int score = 0; string op = _gs.GetOpponent(player);
            if (IsCaptureMove(board, m, player)) score += 15000;
            if (m.From != null && IsSuicideMove(board, m, player, op)) score -= 25000;
            score += (10 - (Math.Abs(m.To.R - 2) + Math.Abs(m.To.C - 2)));
            return score;
        }

        private bool IsSuicideMove(string?[][] board, Move m, string player, string op)
        {
            if (m.From == null) return false;
            string? originFrom = board[m.From.R][m.From.C];
            string? originTo = board[m.To.R][m.To.C];
            board[m.From.R][m.From.C] = null; board[m.To.R][m.To.C] = player;
            bool risk = IsPieceAtRisk(board, m.To.R, m.To.C, player, op);
            board[m.From.R][m.From.C] = originFrom; board[m.To.R][m.To.C] = originTo;
            return risk;
        }

        private bool IsPieceAtRisk(string?[][] b, int r, int c, string me, string op)
        {
            int[] dr = { 1, 0 }, dc = { 0, 1 };
            for (int i = 0; i < 2; i++)
            {
                int r1 = r + dr[i], c1 = c + dc[i], r2 = r - dr[i], c2 = c - dc[i];
                if (In(r1, c1) && In(r2, c2))
                {
                    if (b[r1][c1] == op && b[r2][c2] == null && CanPlayerReach(b, op, r2, c2)) return true;
                    if (b[r1][c1] == null && b[r2][c2] == op && CanPlayerReach(b, op, r1, c1)) return true;
                }
            }
            return false;
        }

        private bool CanPlayerReach(string?[][] b, string player, int tr, int tc)
        {
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            for (int i = 0; i < 4; i++)
            {
                int nr = tr + dr[i], nc = tc + dc[i];
                if (In(nr, nc) && b[nr][nc] == player) return true;
            }
            return false;
        }

        private bool IsCaptureMove(string?[][] board, Move m, string player)
        {
            int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
            string op = _gs.GetOpponent(player);
            for (int i = 0; i < 4; i++)
            {
                int r1 = m.To.R + dr[i], c1 = m.To.C + dc[i], r2 = m.To.R + dr[i] * 2, c2 = m.To.C + dc[i] * 2;
                if (In(r2, c2) && board[r1][c1] == op && board[r2][c2] == player) return true;
            }
            return false;
        }

        private bool IsVulnerableToOpeningKill(string?[][] b, int r, int c, string me)
        {
            if (Math.Abs(r - 2) + Math.Abs(c - 2) != 1) return false;
            string op = _gs.GetOpponent(me);
            int oppR = 2 + (2 - r), oppC = 2 + (2 - c);
            if (In(oppR, oppC) && b[oppR][oppC] == op) return true;
            return false;
        }
    }
}