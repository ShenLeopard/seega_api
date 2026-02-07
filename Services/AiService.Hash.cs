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
                    if (board[r][c] != null) h ^= ZobristPiece[r, c, board[r][c] == "X" ? 0 : 1];
            if (currentPlayer == "O") h ^= SideHash;
            return h;
        }

        private long UpdatePieceHash(long h, Move m, string player, List<(Position Pos, string Player)> captures, string? clearedCenter, GamePhase phase)
        {
            int pi = player == "X" ? 0 : 1;
            if (phase != GamePhase.STUCK_REMOVAL)
            {
                if (m.From != null) h ^= ZobristPiece[m.From.R, m.From.C, pi];
                h ^= ZobristPiece[m.To.R, m.To.C, pi];
            }
            foreach (var cap in captures) h ^= ZobristPiece[cap.Pos.R, cap.Pos.C, cap.Player == "X" ? 0 : 1];
            if (clearedCenter != null) h ^= ZobristPiece[2, 2, clearedCenter == "X" ? 0 : 1];
            return h;
        }

        private (long nextHash, string nextPlayer, GamePhase nextPhase, bool isSamePlayer) GetNextState(long h, Move m, string curr, GamePhase ph, int idx, UndoData ud)
        {
            long nh = UpdatePieceHash(h, m, curr, ud.Captured, ud.ClearedCenterPiece, ph);
            bool isCombo = (ph == GamePhase.PLACEMENT && (idx == 1 || idx == 3 || idx == 24));
            if (isCombo)
            {
                GamePhase nextPh = (ph == GamePhase.PLACEMENT && idx == 24) ? GamePhase.MOVEMENT : ph;
                return (nh, curr, nextPh, true);
            }
            return (nh ^ SideHash, _gs.GetOpponent(curr), ph, false);
        }
    }
}