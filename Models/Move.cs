namespace SeegaGame.Models
{
    public class Move
    {
        public Position? From { get; set; }
        public Position To { get; set; } = null!;
    }
}