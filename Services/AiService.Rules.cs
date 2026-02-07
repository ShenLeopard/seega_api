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
            if (lastMove.From != null && !In(lastMove.From.R, lastMove.From.C)) return null;
            return lastMove;
        }

        public string GetNextPlayer(string currentPlayer, int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT) return _gs.GetOpponent(currentPlayer);
            if (moveIndex == 1 || moveIndex == 3) return currentPlayer;
            if (moveIndex == 2 || moveIndex == 4) return _gs.GetOpponent(currentPlayer);
            return _gs.GetOpponent(currentPlayer);
        }

        private string InferFirstPlayer(string currentPlayer, int moveIndex)
        {
            if (moveIndex <= 0) return "X"; // 預設
            // 2+2 週期是 4。 (moveIndex-1) % 4 等於 0 或 1 是先手，2 或 3 是後手
            int offset = (moveIndex - 1) % 4;
            bool currentIsFirst = (offset == 0 || offset == 1);
            return currentIsFirst ? currentPlayer : _gs.GetOpponent(currentPlayer);
        }

        private bool IsAttacker(string player, string currentPlayer, int moveIndex)
        {
            string firstPlayer = InferFirstPlayer(currentPlayer, moveIndex);
            string secondPlayer = _gs.GetOpponent(firstPlayer);
            return player == secondPlayer;
        }

        private bool IsComboMove(int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT) return false;
            return (moveIndex == 1 || moveIndex == 3 || moveIndex == 24);
        }
    }
}