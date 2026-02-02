using System.Text.Json.Serialization;

namespace SeegaGame.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GamePhase
    {
        PLACEMENT,      // 佈局
        MOVEMENT,       // 移動
        STUCK_REMOVAL,  // 受困移除模式
        GAME_OVER       // 結束
    }
}