using Microsoft.AspNetCore.Mvc;
using SeegaGame.Models;
using SeegaGame.Services;

namespace SeegaGame.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly IGameService _gameService;
        private readonly IAiService _aiService;

        public GameController(IGameService gameService, IAiService aiService)
        {
            _gameService = gameService;
            _aiService = aiService;
        }

        [HttpPost("player-move")]
        public ActionResult<MoveResponse> PlayerMove([FromBody] PlayerMoveRequest request)
        {
            var resp = _gameService.ExecuteMove(request.Board, request.CurrentPlayer, request.Phase, request.Move, request.LastMoveX, request.LastMoveO, request.MoveIndex);
            return resp.Success ? Ok(resp) : BadRequest(resp);
        }

        [HttpPost("ai-move")]
        public ActionResult<MoveResponse> AiMove([FromBody] AiMoveRequest request)
        {
            var bestMove = _aiService.GetBestMove(request.Board, request.CurrentPlayer, request.Phase, request.Difficulty, request.LastMoveX, request.LastMoveO);
            if (bestMove == null) return BadRequest(new { Success = false, Message = "AI µLªk²¾°Ê" });
            var resp = _gameService.ExecuteMove(request.Board, request.CurrentPlayer, request.Phase, bestMove, request.LastMoveX, request.LastMoveO, request.MoveIndex);
            return Ok(resp);
        }
    }
}