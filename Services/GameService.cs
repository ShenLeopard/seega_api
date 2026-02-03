using SeegaGame.Models;

namespace SeegaGame.Services
{
    public class GameService
    {

        public string GetOpponent(string p) => p == "O" ? "X" : "O";

        public string?[][] CloneBoard(string?[][] board)
        {
            var newBoard = new string?[5][];
            for (int i = 0; i < 5; i++) newBoard[i] = (string?[])board[i].Clone();
            return newBoard;
        }
        private bool In(int r, int c) => r >= 0 && r < 5 && c >= 0 && c < 5;
        // 處理移動後的效應（主要是計算夾擊吃子）
        public (string?[][] NewBoard, List<Position> Captured) ProcessMoveEffect(string?[][] board, Position to, string player, GamePhase phase, Position? from)
        {
            // 只有「移動階段」才會有吃子發效
            if (phase != GamePhase.MOVEMENT)
            {
                return (board, new List<Position>());
            }

            var captured = new List<Position>();
            string opponent = GetOpponent(player);

            // 定義上下左右四個方向
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                // r1, c1: 緊鄰的格子 (可能是敵方棋子)
                int r1 = to.R + dr[i];
                int c1 = to.C + dc[i];

                // r2, c2: 再往外一格 (必須是我方棋子，形成夾擊)
                int r2 = to.R + (dr[i] * 2);
                int c2 = to.C + (dc[i] * 2);

                // 檢查邊界
                if (IsValidPos(r2, c2))
                {
                    // 夾擊判斷公式： [我方(to)] - [敵方(r1,c1)] - [我方(r2,c2)]
                    if (board[r1][c1] == opponent && board[r2][c2] == player)
                    {
                        // 記錄被吃掉的位置
                        captured.Add(new Position { R = r1, C = c1 });

                        // 從棋盤上移除該棋子
                        board[r1][c1] = null;
                    }
                }
            }

            return (board, captured);
        }

        // 輔助：檢查座標是否在棋盤內 (0~4)
        private bool IsValidPos(int r, int c)
        {
            return r >= 0 && r < 5 && c >= 0 && c < 5;
        }
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

        public string? CheckWinner(string?[][] board, GamePhase phase)
        {
            // 只有移動階段或移除階段才判贏
            if (phase == GamePhase.PLACEMENT) return null;

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

        // === 核心邏輯：執行請求並回傳結果 (Controller 用) ===
        public MoveResponse ExecuteMove(string?[][] board, string player, GamePhase phase, Move move, Move? lastMoveX, Move? lastMoveO, int moveIndex)
        {
            // 1. 物理防呆檢查
            if (phase != GamePhase.STUCK_REMOVAL && board[move.To.R][move.To.C] != null)
            {
                return new MoveResponse { Success = false, Message = "該位置已有棋子！" };
            }
            if (phase == GamePhase.PLACEMENT && move.To.R == 2 && move.To.C == 2)
            {
                return new MoveResponse { Success = false, Message = "佈陣階段不可佔領中心點！" };
            }

            // 2. 執行物理動作
            var tempBoard = CloneBoard(board);
            string actionDesc = "";
            List<Position> captured = new List<Position>();
            GamePhase nextPhase = phase;

            if (phase == GamePhase.STUCK_REMOVAL)
            {
                tempBoard[move.To.R][move.To.C] = null;
                actionDesc = $"移除 ({move.To.R},{move.To.C})";
                nextPhase = GamePhase.MOVEMENT;
            }
            else
            {
                if (move.From != null) tempBoard[move.From.R][move.From.C] = null;
                tempBoard[move.To.R][move.To.C] = player;

                var effect = ProcessMoveEffect(tempBoard, move.To, player, phase, move.From);
                tempBoard = effect.NewBoard;
                captured = effect.Captured;

                if (move.From == null) actionDesc = $"佈陣於 ({move.To.R},{move.To.C})";
                else actionDesc = $"移動 ({move.From.R},{move.From.C}) → ({move.To.R},{move.To.C})";
            }

            // 3. 決定下一位玩家 (核心修正)
            // 預設行為：換對手
            string nextPlayer = GetOpponent(player);

            if (phase == GamePhase.PLACEMENT)
            {
                // 規則：第 24 手結束，轉場並由最後下子者連動
                if (moveIndex == 24)
                {
                    nextPhase = GamePhase.MOVEMENT;
                    nextPlayer = player;
                    tempBoard[2][2] = null; // 強制清空中心
                    actionDesc += " (佈陣結束，連動開始)";
                }
                else
                {
                    // 修正 2+2 邏輯：相對判斷
                    // 第 1 手下完 -> 還是自己 (準備下第 2 手)
                    // 第 3 手下完 -> 還是自己 (準備下第 4 手)
                    // 第 2, 4 手下完 -> 預設換人 (GetOpponent)
                    if (moveIndex == 1 || moveIndex == 3)
                    {
                        nextPlayer = player;
                    }
                }
            }
            else if (phase == GamePhase.STUCK_REMOVAL)
            {
                nextPlayer = player; // 解圍後連動
            }
            // MOVEMENT 吃子不連動，維持預設換人

            // 4. 受困預判
            if (nextPhase == GamePhase.MOVEMENT)
            {
                Move? checkX = (nextPlayer == "X" && player == "X") ? move : lastMoveX;
                Move? checkO = (nextPlayer == "O" && player == "O") ? move : lastMoveO;

                var nextValidMoves = GetValidMoves(tempBoard, nextPlayer, GamePhase.MOVEMENT, checkX, checkO);
                if (nextValidMoves.Count == 0)
                {
                    nextPhase = GamePhase.STUCK_REMOVAL;
                    actionDesc += $"。⚠️ {nextPlayer} 受困，進入移除模式";
                }
            }

            string finalMessage = $"玩家 {player} {actionDesc}";
            if (captured.Count > 0) finalMessage += $"，吃掉 {captured.Count} 子";

            string? winner = CheckWinner(tempBoard, nextPhase);

            return new MoveResponse
            {
                Success = true,
                NewBoard = tempBoard,
                CapturedCount = captured.Count,
                CapturedPieces = captured,
                NextPlayer = nextPlayer,
                NextPhase = nextPhase,
                Winner = winner,
                IsGameOver = winner != null,
                Message = finalMessage,
                Move = move,
                MoveIndex = moveIndex // 回傳當前步數確認
            };
        }
    }
}