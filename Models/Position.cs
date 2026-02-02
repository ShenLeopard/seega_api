namespace SeegaGame.Models
{
    public class Position
    {
        public int R { get; set; }
        public int C { get; set; }
        public Position() { }
        public Position(int r, int c) { R = r; C = c; }
        public override string ToString() => $"({R},{C})";
    }
}