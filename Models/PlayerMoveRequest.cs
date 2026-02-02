namespace SeegaGame.Models
{
    public class PlayerMoveRequest
    {
        public string?[][] Board { get; set; } = null!;
        public string CurrentPlayer { get; set; } = ""; // "X" 或 "O"
        public GamePhase Phase { get; set; }
        public Move Move { get; set; } = null!;
        public Move? LastMoveX { get; set; }
        public Move? LastMoveO { get; set; }
        public int MoveIndex { get; set; }
    }
}