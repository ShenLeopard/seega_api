using SeegaGame.Models;

namespace SeegaGame.Services
{
    public class GameService : IGameService
    {
        public string?[][] InitBoard()
        {
            var board = new string?[5][];
            for (int i = 0; i < 5; i++)
            {
                board[i] = new string?[5];
            }
            return board;
        }

        public string GetOpponent(string player)
        {
            return player == "O" ? "X" : "O";
        }

        public string?[][] CloneBoard(string?[][] board)
        {
            var newBoard = new string?[5][];
            for (int i = 0; i < 5; i++)
            {
                newBoard[i] = (string?[])board[i].Clone();
            }
            return newBoard;
        }

        public int CountPlayerPieces(string?[][] board, string player)
        {
            int count = 0;
            foreach (var row in board)
            {
                foreach (var cell in row)
                {
                    if (cell == player) count++;
                }
            }
            return count;
        }

        // 取得合法移動 (區分 X 與 O 的回頭路)
        public List<Move> GetValidMoves(string?[][] board, string player, GamePhase phase, Move? lastMoveX, Move? lastMoveO)
        {
            var moves = new List<Move>();

            // 1. 受困移除模式：回傳所有敵方棋子
            if (phase == GamePhase.STUCK_REMOVAL)
            {
                string opponent = GetOpponent(player);
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        if (board[r][c] == opponent)
                        {
                            moves.Add(new Move { To = new Position { R = r, C = c } });
                        }
                    }
                }
                return moves;
            }

            // 2. 佈陣模式：除了中心 (2,2) 的所有空格
            if (phase == GamePhase.PLACEMENT)
            {
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        if (r == 2 && c == 2) continue; // 中心禁放
                        if (board[r][c] == null)
                        {
                            moves.Add(new Move { To = new Position { R = r, C = c } });
                        }
                    }
                }
            }
            // 3. 移動模式：檢查相鄰空格 + 禁止回頭路
            else
            {
                // 根據當前是誰，決定要檢查哪一個 LastMove
                Move? myLastMove = (player == "X") ? lastMoveX : lastMoveO;

                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        // 找到己方棋子
                        if (board[r][c] == player)
                        {
                            int[] dr = { -1, 1, 0, 0 };
                            int[] dc = { 0, 0, -1, 1 };

                            for (int i = 0; i < 4; i++)
                            {
                                int nr = r + dr[i];
                                int nc = c + dc[i];

                                // 邊界與空格檢查
                                if (IsValidPos(nr, nc) && board[nr][nc] == null)
                                {
                                    // 禁止回頭路規則
                                    if (myLastMove != null && myLastMove.From != null)
                                    {
                                        // 如果我想去的 (nr,nc) 是我上一步離開的 (From)
                                        // 且我現在的位置 (r,c) 是我上一步到達的 (To)
                                        if (r == myLastMove.To.R && c == myLastMove.To.C &&
                                            nr == myLastMove.From.R && nc == myLastMove.From.C)
                                        {
                                            continue; // 跳過此步
                                        }
                                    }

                                    moves.Add(new Move
                                    {
                                        From = new Position { R = r, C = c },
                                        To = new Position { R = nr, C = nc }
                                    });
                                }
                            }
                        }
                    }
                }
            }
            return moves;
        }

        // 執行移動 (包含轉場、2+2、受困預判)
        public MoveResponse ExecuteMove(string?[][] board, string player, GamePhase phase, Move move, Move? lastMoveX, Move? lastMoveO, int moveIndex)
        {
            var tempBoard = CloneBoard(board);
            string message = "";
            GamePhase nextPhase = phase;
            List<Position> captured = new List<Position>();

            // --- 1. 執行物理動作 ---
            if (phase == GamePhase.STUCK_REMOVAL)
            {
                // 移除敵方棋子
                tempBoard[move.To.R][move.To.C] = null;
                message = $"玩家 {player} 移除敵方棋子解圍。";
                nextPhase = GamePhase.MOVEMENT; // 移除完必定回到移動模式
            }
            else
            {
                // 防呆：目標格已有棋子
                if (board[move.To.R][move.To.C] != null)
                {
                    return new MoveResponse { Success = false, Message = "該位置已有棋子，請選擇空格！" };
                }
                // 防呆：佈陣中心
                if (phase == GamePhase.PLACEMENT && move.To.R == 2 && move.To.C == 2)
                {
                    return new MoveResponse { Success = false, Message = "佈陣階段不可佔領中心點！" };
                }

                // 移動處理
                if (move.From != null) tempBoard[move.From.R][move.From.C] = null;
                tempBoard[move.To.R][move.To.C] = player;

                // 吃子處理
                var result = ProcessMoveEffect(tempBoard, move.To, player, phase, move.From);
                tempBoard = result.NewBoard;
                captured = result.Captured;
                message = captured.Count > 0 ? $"玩家 {player} 吃掉 {captured.Count} 子。" : $"玩家 {player} 移動完成。";
            }

            // --- 2. 決定下一位玩家 ---
            string nextPlayer = GetOpponent(player); // 預設換人

            if (phase == GamePhase.PLACEMENT)
            {
                // 規則：第 24 手結束，轉場並連動
                if (moveIndex == 24)
                {
                    nextPhase = GamePhase.MOVEMENT;
                    nextPlayer = player; // 不換人
                    tempBoard[2][2] = null; // 強制清空中心 (確保移動有路)
                    message += " (佈陣結束，由您發動首波攻擊)";
                }
                else
                {
                    // 規則：2+2 開局
                    if (moveIndex == 1) nextPlayer = "X";
                    else if (moveIndex == 2) nextPlayer = "O";
                    else if (moveIndex == 3) nextPlayer = "O";
                    else if (moveIndex == 4) nextPlayer = "X";
                }
            }
            else if (phase == GamePhase.STUCK_REMOVAL)
            {
                // 規則：解圍後獲得一次移動機會
                nextPlayer = player;
            }
            // 規則：移動階段吃子後，強制換人 (不允許連續行動) -> 使用預設 nextPlayer

            // --- 3. 預判下一位玩家是否受困 ---
            if (nextPhase == GamePhase.MOVEMENT)
            {
                // 這裡要傳入「下一位玩家」對應的 LastMove
                // 如果 nextPlayer 是 "X"，傳入 lastMoveX；如果是 "O"，傳入 lastMoveO
                // 注意：如果 nextPlayer == player (連動中)，則他的 LastMove 應該更新為「剛剛走的這一步(move)」

                Move? checkX = lastMoveX;
                Move? checkO = lastMoveO;

                if (nextPlayer == "X" && player == "X") checkX = move;
                if (nextPlayer == "O" && player == "O") checkO = move;

                var nextValidMoves = GetValidMoves(tempBoard, nextPlayer, GamePhase.MOVEMENT, checkX, checkO);

                if (nextValidMoves.Count == 0)
                {
                    nextPhase = GamePhase.STUCK_REMOVAL;
                    message += $" ⚠️ 輪到 {nextPlayer} 但無路可走，進入移除模式。";
                }
            }

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
                Message = message,
                Move = move
            };
        }

        public (string?[][] NewBoard, List<Position> Captured) ProcessMoveEffect(string?[][] board, Position to, string player, GamePhase phase, Position? from)
        {
            if (phase != GamePhase.MOVEMENT) return (board, new List<Position>());

            var captured = new List<Position>();
            string opponent = GetOpponent(player);
            int[] dr = { -1, 1, 0, 0 };
            int[] dc = { 0, 0, -1, 1 };

            for (int i = 0; i < 4; i++)
            {
                int r1 = to.R + dr[i];
                int c1 = to.C + dc[i];
                int r2 = to.R + (dr[i] * 2);
                int c2 = to.C + (dc[i] * 2);

                if (IsValidPos(r2, c2))
                {
                    // 夾擊判斷：我 - 敵 - 我
                    if (board[r1][c1] == opponent && board[r2][c2] == player)
                    {
                        captured.Add(new Position { R = r1, C = c1 });
                        board[r1][c1] = null; // 移除被吃掉的棋子
                    }
                }
            }
            return (board, captured);
        }

        public string? CheckWinner(string?[][] board, GamePhase phase)
        {
            if (phase != GamePhase.MOVEMENT && phase != GamePhase.STUCK_REMOVAL) return null;

            int oCount = CountPlayerPieces(board, "O");
            int xCount = CountPlayerPieces(board, "X");

            if (oCount < 2) return "X";
            if (xCount < 2) return "O";

            return null;
        }

        private bool IsValidPos(int r, int c)
        {
            return r >= 0 && r < 5 && c >= 0 && c < 5;
        }
    }
}