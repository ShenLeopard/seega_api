namespace SeegaGame.Models
{
    public class MoveResponse
    {
        public bool Success { get; set; }
        public Move? Move { get; set; }
        public string?[][] NewBoard { get; set; } = null!;
        public List<Position> CapturedPieces { get; set; } = new();
        public int CapturedCount { get; set; }
        public string NextPlayer { get; set; } = "";
        public GamePhase NextPhase { get; set; }
        public string? Winner { get; set; }
        public bool IsGameOver { get; set; }
        public string Message { get; set; } = "";
        public string? Error { get; set; }
    }
}