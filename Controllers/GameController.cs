using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SeegaGame.Models;
using SeegaGame.Services;

namespace SeegaGame.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly GameService _gameService;
        private readonly AiService _aiService;

        public GameController(IMemoryCache cache)
        {
            _gameService = new GameService();
            // 傳入需要的依賴
            _aiService = new AiService(_gameService, cache);
        }

        [HttpPost("player-move")]
        public ActionResult<MoveResponse> PlayerMove([FromBody] PlayerMoveRequest request)
        {
            // 直接從 request 物件提取參數傳給 ExecuteMove
            var resp = _gameService.ExecuteMove(
                request.Board,
                request.CurrentPlayer,
                request.Phase,
                request.Move,
                request.LastMoveX,
                request.LastMoveO,
                request.MoveIndex
            );

            return resp.Success ? Ok(resp) : BadRequest(resp);
        }

        [HttpPost("ai-move")]
        public ActionResult<MoveResponse> AiMove([FromBody] AiMoveRequest request)
        {
            // 修正點：直接將整顆 request 丟入 GetBestMove，不重複 new 物件
            var bestMove = _aiService.GetBestMove(request);

            if (bestMove == null)
                return BadRequest(new { Success = false, Message = "AI 無法移動或無合法路徑" });

            // 使用 AI 算出的最佳步數進行物理移動
            var resp = _gameService.ExecuteMove(
                request.Board,
                request.CurrentPlayer,
                request.Phase,
                bestMove,
                request.LastMoveX,
                request.LastMoveO,
                request.MoveIndex
            );

            return Ok(resp);
        }
    }
}