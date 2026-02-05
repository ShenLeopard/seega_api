using SeegaGame.Models;

namespace SeegaGame.Services
{
    public class GameService
    {

        public string GetOpponent(string p) => p == "O" ? "X" : "O";
        private bool In(int r, int c) => r >= 0 && r < 5 && c >= 0 && c < 5;
        // === 核心邏輯：執行動作並回傳 Undo 資料 (給 AI 遞迴用) ===
        public UndoData MakeMove(string?[][] board, Move move, string player, GamePhase phase, int moveIndex)
        {
            var undo = new UndoData { Move = move, PrevPhase = phase };

            if (phase == GamePhase.STUCK_REMOVAL)
            {
                // 移除模式：目標位置是敵方棋子
                // 記錄被移除的棋子以便悔棋
                undo.Captured.Add((move.To, GetOpponent(player)));
                board[move.To.R][move.To.C] = null;
            }
            else
            {
                // 移動/佈陣模式
                if (move.From != null) board[move.From.R][move.From.C] = null;
                board[move.To.R][move.To.C] = player;

                // 特殊規則：第 24 手佈陣結束，強制清空中心
                if (phase == GamePhase.PLACEMENT && moveIndex == 24)
                {
                    undo.ClearedCenterPiece = board[2][2];
                    board[2][2] = null;
                }
                else if (phase == GamePhase.MOVEMENT)
                {
                    // 吃子判定
                    int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };
                    string op = GetOpponent(player);
                    for (int i = 0; i < 4; i++)
                    {
                        int r1 = move.To.R + dr[i], c1 = move.To.C + dc[i];
                        int r2 = move.To.R + dr[i] * 2, c2 = move.To.C + dc[i] * 2;

                        if (In(r2, c2) && board[r1][c1] == op && board[r2][c2] == player)
                        {
                            undo.Captured.Add((new Position { R = r1, C = c1 }, op));
                            board[r1][c1] = null;
                        }
                    }
                }
            }
            return undo;
        }

        // === 核心邏輯：撤銷動作 (AI 遞迴用) ===
        public void UnmakeMove(string?[][] board, UndoData undo, string player)
        {
            if (undo.PrevPhase == GamePhase.STUCK_REMOVAL)
            {
                // 還原被移除的敵方棋子
                var cap = undo.Captured[0];
                board[cap.Pos.R][cap.Pos.C] = cap.Player;
            }
            else
            {
                // 還原移動
                board[undo.Move.To.R][undo.Move.To.C] = null;
                if (undo.Move.From != null) board[undo.Move.From.R][undo.Move.From.C] = player;

                // 還原被吃的子
                foreach (var cap in undo.Captured)
                    board[cap.Pos.R][cap.Pos.C] = cap.Player;

                // 還原中心點 (如果是第 24 手)
                if (undo.ClearedCenterPiece != null)
                    board[2][2] = undo.ClearedCenterPiece;
            }
        }

        // === 核心邏輯：取得合法步 (包含禁止回頭路) ===
        public List<Move> GetValidMoves(string?[][] board, string player, GamePhase phase, Move? lastX, Move? lastO)
        {
            var moves = new List<Move>();

            if (phase == GamePhase.STUCK_REMOVAL)
            {
                string op = GetOpponent(player);
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 5; c++)
                        if (board[r][c] == op) moves.Add(new Move { To = new Position { R = r, C = c } });
                return moves;
            }

            if (phase == GamePhase.PLACEMENT)
            {
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 5; c++)
                        if ((r != 2 || c != 2) && board[r][c] == null)
                            moves.Add(new Move { To = new Position { R = r, C = c } });
            }
            else
            {
                // MOVEMENT
                Move? myL = (player == "X") ? lastX : lastO;
                int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };

                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 5; c++)
                        if (board[r][c] == player)
                            for (int i = 0; i < 4; i++)
                            {
                                int nr = r + dr[i], nc = c + dc[i];
                                if (In(nr, nc) && board[nr][nc] == null)
                                {
                                    // 禁止回頭路
                                    if (myL != null && myL.From != null &&
                                        r == myL.To.R && c == myL.To.C &&
                                        nr == myL.From.R && nc == myL.From.C)
                                        continue;

                                    moves.Add(new Move { From = new Position { R = r, C = c }, To = new Position { R = nr, C = nc } });
                                }
                            }
            }
            return moves;
        }

        public string? CheckWinner(string?[][] board)
        {
            int x = 0, o = 0;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    if (board[r][c] == "X") x++;
                    else if (board[r][c] == "O") o++;
                }
            if (x < 2) return "O";
            if (o < 2) return "X";
            return null;
        }
        // 將座標 (r:1, c:2) 轉換為人類可讀的 B3 格式
        public string FormatPos(Position p)
        {
            if (p == null) return "??";

            // R: 0->A, 1->B, 2->C, 3->D, 4->E
            char rowChar = (char)('A' + p.R);

            // C: 0->1, 1->2, 2->3, 3->4, 4->5
            int colNum = p.C + 1;

            return $"{rowChar}{colNum}";
        }
        // === 核心邏輯：執行請求並回傳結果 (Controller 用) ===
        public MoveResponse ExecuteMove(string?[][] board, string player, GamePhase phase, Move move, Move? lastMoveX, Move? lastMoveO, int moveIndex)
        {
            // 1. 物理防呆
            if (phase != GamePhase.STUCK_REMOVAL && board[move.To.R][move.To.C] != null)
            {
                return new MoveResponse { Success = false, Error = "該位置已有棋子" };
            }

            // 2. 執行物理動作 (產生新盤面)
            string?[][] newBoard = new string?[5][];
            for (int r = 0; r < 5; r++) newBoard[r] = (string?[])board[r].Clone();

            var ud = MakeMove(newBoard, move, player, phase, moveIndex);

            // 3. 準備基本訊息 (移動位置與吃子數)
            string toStr = FormatPos(move.To);
            string actionDesc = (phase == GamePhase.PLACEMENT) ? $"在 {toStr} 佈陣" :
                                (phase == GamePhase.STUCK_REMOVAL ? $"移除 {toStr} 敵子" :
                                $"從 {FormatPos(move.From!)} 移動到 {toStr}");

            string baseMsg = $"玩家 {player} {actionDesc}";
            if (ud.Captured.Count > 0) baseMsg += $"，吃掉 {ud.Captured.Count} 子";

            // ============================================================
            // ★ 核心修正點 1：勝負判定擁有「絕對優先權」
            // ============================================================
            string? winner = CheckWinner(newBoard);
            if (winner != null)
            {
                // 只要有人贏了，立刻回傳，後面的受困邏輯「絕對」不會跑
                return new MoveResponse
                {
                    Success = true,
                    NewBoard = newBoard,
                    NextPlayer = string.Empty,
                    NextPhase = GamePhase.GAME_OVER, // 進入結束階段
                    Move = move,
                    MoveIndex = moveIndex + 1, // 步數正確遞增
                    CapturedPieces = ud.Captured.Select(c => c.Pos).ToList(),
                    CapturedCount = ud.Captured.Count,
                    Winner = winner,
                    IsGameOver = true,
                    Message = baseMsg + $"。🎉 遊戲結束！獲勝者：{winner}"
                };
            }

            // ============================================================
            // ★ 核心修正點 2：只有遊戲「未結束」時，才執行狀態轉換與受困檢查
            // ============================================================
            string nextPlayer = GetOpponent(player);
            GamePhase nextPhase = phase;

            if (phase == GamePhase.PLACEMENT)
            {
                if (moveIndex == 24) { nextPhase = GamePhase.MOVEMENT; nextPlayer = player; baseMsg += " (連動攻擊開始)"; }
                else if (moveIndex == 1 || moveIndex == 3) { nextPlayer = player; }
            }
            else if (phase == GamePhase.STUCK_REMOVAL)
            {
                nextPhase = GamePhase.MOVEMENT;
                nextPlayer = player;
            }

            // 只有在進入移動階段時，才預判受困
            if (nextPhase == GamePhase.MOVEMENT)
            {
                Move? nX = (nextPlayer == "X" && player == "X") ? move : lastMoveX;
                Move? nO = (nextPlayer == "O" && player == "O") ? move : lastMoveO;

                // 這裡檢查下一位玩家是否有合法步數
                if (GetValidMoves(newBoard, nextPlayer, GamePhase.MOVEMENT, nX, nO).Count == 0)
                {
                    nextPhase = GamePhase.STUCK_REMOVAL;
                    baseMsg += $"。⚠️ {nextPlayer} 無路可走，進入移除模式";
                }
            }

            return new MoveResponse
            {
                Success = true,
                NewBoard = newBoard,
                NextPlayer = nextPlayer,
                NextPhase = nextPhase,
                Move = move,
                MoveIndex = moveIndex + 1,
                CapturedPieces = ud.Captured.Select(c => c.Pos).ToList(),
                CapturedCount = ud.Captured.Count,
                Winner = null,
                IsGameOver = false,
                Message = baseMsg
            };
        }
    }
}