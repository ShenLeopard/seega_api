// Models/GameConstants.cs
namespace SeegaGame.Models
{
    public static class GameConstants
    {
        public const int BOARD_SIZE = 5;
        public const int CENTER_R = 2;
        public const int CENTER_C = 2;
        public const int TOTAL_PIECES_PER_PLAYER = 12;

        // 內部代號
        public const string PLAYER_X = "X";
        public const string PLAYER_O = "O";

        public const string PLAYER_X_NAME = "黑方";
        public const string PLAYER_O_NAME = "白方";

        public static class Scores
        {
            public const double WIN = 100000.0;
            public const double PIECE_VALUE = 100.0;
            public const double DANGER_PENALTY = 5000.0;
        }
    }
}