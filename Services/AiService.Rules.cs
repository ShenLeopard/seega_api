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
            if (moveIndex <= 0) return "X";
            bool isFirstPlayerMove = (moveIndex <= 4) ? (moveIndex <= 2) : (moveIndex % 2 == 1);
            return isFirstPlayerMove ? currentPlayer : _gs.GetOpponent(currentPlayer);
        }

        private bool IsAttacker(string player, string currentPlayer, int moveIndex)
        {
            string firstPlayer = InferFirstPlayer(currentPlayer, moveIndex);
            return player == _gs.GetOpponent(firstPlayer);
        }

        private bool IsComboMove(int moveIndex, GamePhase phase)
        {
            if (phase != GamePhase.PLACEMENT) return false;
            return (moveIndex == 1 || moveIndex == 3 || moveIndex == 24);
        }
    }
}