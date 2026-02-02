namespace SeegaGame.Models
{
    public class AiMoveRequest
    {
        public string?[][] Board { get; set; } = null!;
        public string CurrentPlayer { get; set; } = "";
        public GamePhase Phase { get; set; }
        public int Difficulty { get; set; }
        public Move? LastMoveX { get; set; }
        public Move? LastMoveO { get; set; }
        public int MoveIndex { get; set; }
    }
}