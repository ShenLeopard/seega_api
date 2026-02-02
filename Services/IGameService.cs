using SeegaGame.Models;

namespace SeegaGame.Services
{
    public interface IGameService
    {
        /// <summary>
        /// 初始化 5x5 棋盤
        /// </summary>
        string?[][] InitBoard();

        /// <summary>
        /// 取得合法移動列表 (包含佈陣階段避開中心、移動階段的相鄰判斷)
        /// </summary>
        List<Move> GetValidMoves(string?[][] board, string player, GamePhase phase, Move? lastMoveX, Move? lastMoveO);


        /// <summary>
        /// 執行移動核心邏輯 (包含防呆驗證、吃子計算、勝負判斷)
        /// </summary>
        MoveResponse ExecuteMove(string?[][] board, string player, GamePhase phase, Move move, Move? lastMoveX, Move? lastMoveO, int moveIndex);



        /// <summary>
        /// 處理移動後的效應 (計算夾擊吃子)
        /// </summary>
        (string?[][] NewBoard, List<Position> Captured) ProcessMoveEffect(string?[][] board, Position to, string player, GamePhase phase, Position? from);

        /// <summary>
        /// 檢查是否有獲勝者 (棋子數量 < 2)
        /// </summary>
        string? CheckWinner(string?[][] board, GamePhase phase);

        /// <summary>
        /// 取得對手代號
        /// </summary>
        string GetOpponent(string player);

        /// <summary>
        /// 複製棋盤 (Deep Copy)
        /// </summary>
        string?[][] CloneBoard(string?[][] board);

        /// <summary>
        /// 計算某方棋子數量
        /// </summary>
        int CountPlayerPieces(string?[][] board, string player);
    }
}