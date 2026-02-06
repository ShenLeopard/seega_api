using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private long InitialHash(string?[][] board, string currentPlayer)
        {
            long h = 0;

            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    if (board[r][c] != null)
                        h ^= ZobristPiece[r, c, board[r][c] == "X" ? 0 : 1];

            if (currentPlayer == "O") h ^= SideHash;

            return h;
        }

        private long UpdatePieceHash(long h, Move m, string player,
                             List<(Position Pos, string Player)> captures,
                             string? clearedCenter, GamePhase phase)  // 新增 phase 參數
        {
            int pi = player == "X" ? 0 : 1;

            if (phase == GamePhase.MOVEMENT)
            {
                // 移動模式：起點 XOR 掉，終點 XOR 進來
                if (m.From != null) h ^= ZobristPiece[m.From.R, m.From.C, pi];
                h ^= ZobristPiece[m.To.R, m.To.C, pi];
            }
            else if (phase == GamePhase.PLACEMENT)
            {
                // 佈陣模式：只有終點 XOR 進來 (m.From 為空)
                h ^= ZobristPiece[m.To.R, m.To.C, pi];
            }
            // 注意：如果是 STUCK_REMOVAL，我們不對 m.To 做己方棋子的 XOR，
            // 因為該位置是敵人的，會由下方的 captures 迴圈統一處理移除。

            // 處理所有被吃掉或被移除的棋子
            foreach (var cap in captures)
            {
                int victimPi = cap.Player == "X" ? 0 : 1;
                h ^= ZobristPiece[cap.Pos.R, cap.Pos.C, victimPi];
            }

            // 處理第 24 手自動清空的中心點
            if (clearedCenter != null)
            {
                int centerPi = clearedCenter == "X" ? 0 : 1;
                h ^= ZobristPiece[2, 2, centerPi];
            }

            return h;
        }

        private (long nextHash, string nextPlayer, GamePhase nextPhase, bool isSamePlayer)
    GetNextState(long currentHash, Move move, string curr, GamePhase ph, int idx, UndoData ud)
        {
            // 傳入當前 phase 以正確計算雜湊
            long nh = UpdatePieceHash(currentHash, move, curr, ud.Captured, ud.ClearedCenterPiece, ph);

            bool isCombo = IsComboMove(idx, ph);
            if (isCombo)
            {
                // 如果是第 24 手佈陣完畢，下一階段變為 MOVEMENT，但不換人
                GamePhase nextPhase = (ph == GamePhase.PLACEMENT && idx == 24) ? GamePhase.MOVEMENT : ph;
                return (nh, curr, nextPhase, true); // 不 XOR SideHash，同一人續動
            }
            else
            {
                return (nh ^ SideHash, _gs.GetOpponent(curr), ph, false);
            }
        }

    }
}