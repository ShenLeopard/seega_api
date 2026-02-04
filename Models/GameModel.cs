namespace SeegaGame.Models
{
    public enum GamePhase { PLACEMENT, MOVEMENT, STUCK_REMOVAL, GAME_OVER }

    public class Position { public int R { get; set; } public int C { get; set; } }

    public class Move
    {
        public Position? From { get; set; }
        public Position To { get; set; } = null!;
    }
    public class PlayerMoveRequest
    {
        public string?[][] Board { get; set; } = null!;
        public string GameUUId { get; set; } = "";
        public string CurrentPlayer { get; set; } = ""; // "X" 或 "O"
        public GamePhase Phase { get; set; }
        public Move Move { get; set; } = null!;
        public Move? LastMoveX { get; set; }
        public Move? LastMoveO { get; set; }
        public int MoveIndex { get; set; }
    }

    public class AiMoveRequest
    {
        public string?[][] Board { get; set; } = null!;
        public string GameUUId { get; set; } = "";
        public string CurrentPlayer { get; set; } = "";
        public GamePhase Phase { get; set; }
        public int Difficulty { get; set; }
        public Move? LastMoveX { get; set; }
        public Move? LastMoveO { get; set; }
        public int MoveIndex { get; set; }
    }

    public class MoveResponse
    {
        // 基本狀態
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }

        // 棋盤與玩家狀態
        public string?[][] NewBoard { get; set; } = null!;
        public string NextPlayer { get; set; } = string.Empty; // "X" 或 "O"
        public GamePhase NextPhase { get; set; }
        public int MoveIndex { get; set; } // 回傳當前步數，確保前後端同步

        // 移動細節
        public Move? Move { get; set; }
        public List<Position> CapturedPieces { get; set; } = new();
        public int CapturedCount { get; set; }

        // 勝負資訊
        public string? Winner { get; set; } // "X" 或 "O" 或 null
        public bool IsGameOver { get; set; }
    }
    public struct TTEntry
    {
        public long Key;        // 8 bytes
        public int Score;       // 4 bytes
        public short BestMove;  // 2 bytes
        public byte Depth;      // 1 byte
        public byte Flag;       // 1 byte
    }

    // 用於 Undo Move 的紀錄物件
    public class UndoData
    {
        public Move Move { get; set; } = null!;
        public List<(Position Pos, string Player)> Captured { get; set; } = new();
        public string? ClearedCenterPiece { get; set; }
        public GamePhase PrevPhase { get; set; }
    }
}