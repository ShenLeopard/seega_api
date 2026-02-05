using SeegaGame.Models;

namespace SeegaGame.Services
{
    public partial class AiService
    {
        private long InitialHash(string?[][] board, string currentPlayer, GamePhase phase)
        {
            long h = 0;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                    if (board[r][c] != null)
                        h ^= ZobristPiece[r, c, board[r][c] == "X" ? 0 : 1];

            if (currentPlayer == "O") h ^= SideHash;

            // 如果不是佈陣階段，就翻轉為移動階段雜湊
            if (phase != GamePhase.PLACEMENT) h ^= PhaseToggleHash;

            return h;
        }

        private long UpdatePieceHash(long h, Move m, string player,
                                     List<(Position Pos, string Player)> captures,
                                     string? clearedCenter)
        {
            int pi = player == "X" ? 0 : 1;

            if (m.From != null)
                h ^= ZobristPiece[m.From.R, m.From.C, pi];

            h ^= ZobristPiece[m.To.R, m.To.C, pi];

            foreach (var cap in captures)
                h ^= ZobristPiece[cap.Pos.R, cap.Pos.C, 1 - pi];

            if (clearedCenter != null)
                h ^= ZobristPiece[2, 2, clearedCenter == "X" ? 0 : 1];

            return h;
        }

        private (long nextHash, string nextPlayer, GamePhase nextPhase, bool isSamePlayer)
    GetNextState(long currentHash, Move move, string curr, GamePhase ph, int idx, UndoData ud)
        {
            // 1. 基礎棋子雜湊更新 (由 UpdatePieceHash 處理 move 與 captures)
            long nh = UpdatePieceHash(currentHash, move, curr, ud.Captured, ud.ClearedCenterPiece);

            // 2. 處理階段轉場 (第 24 手佈陣結束)
            bool phaseFlip = (ph == GamePhase.PLACEMENT && idx == 24);
            if (phaseFlip)
            {
                nh ^= PhaseToggleHash; // 翻轉雜湊狀態至 MOVEMENT
            }

            // 3. 處理連動與玩家切換
            bool isCombo = IsComboMove(idx, ph);
            if (isCombo)
            {
                // 連動：不換人，不 XOR SideHash
                GamePhase nextPhase = phaseFlip ? GamePhase.MOVEMENT : ph;
                return (nh, curr, nextPhase, true);
            }
            else
            {
                // 正常：換人，XOR SideHash
                // 注意：STUCK_REMOVAL 的移除動作會在這裡自然完成 (因為移除也算一次行動)
                return (nh ^ SideHash, _gs.GetOpponent(curr), ph, false);
            }
        }
    }
}