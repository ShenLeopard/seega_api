// Services/IAiService.cs
using SeegaGame.Models;

namespace SeegaGame.Services
{
    public interface IAiService
    {
        Move? GetBestMove(string?[][] board, string aiPlayer, GamePhase phase, int difficulty, Move? lastMoveX, Move? lastMoveO);

    }
}