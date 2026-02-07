using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private Move? ValidateLastMove(string?[][] board, Move? lastMove, string player)
        {
            if (lastMove?.To == null) return null;
            if (!In(lastMove.To.R, lastMove.To.C)) return null;
            if (board[lastMove.To.R][lastMove.To.C] != player) return null;
            return lastMove;
        }

        // 核心修正：移除容易混淆的 InferFirstPlayer，邏輯已移至 GetBestMove 並固定。
        // 其餘輔助規則保持原樣。
        private int CalculateVulnerability(string?[][] board, int r, int c, string opponent)
        {
            int v = 0; int[] dr = { 1, 0 }, dc = { 0, 1 };
            for (int i = 0; i < 2; i++)
            {
                int r1 = r + dr[i], c1 = c + dc[i], r2 = r - dr[i], c2 = c - dc[i];
                if (In(r1, c1) && In(r2, c2))
                {
                    if ((board[r1][c1] == opponent && (board[r2][c2] == null || (r2 == 2 && c2 == 2))) ||
                        (board[r2][c2] == opponent && (board[r1][c1] == null || (r1 == 2 && c1 == 2)))) v++;
                }
            }
            return v;
        }
    }
}