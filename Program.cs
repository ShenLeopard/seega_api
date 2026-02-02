using Microsoft.OpenApi;
using SeegaGame.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. 註冊服務 (必須在 builder.Build() 之前)
// ==========================================

// 註冊 Controller 並設定 JSON 選項
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // ✅ 讓 Enum (GamePhase) 在 JSON 中顯示為 "MOVEMENT" 而非數字 1
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 註冊 Swagger 生成器
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Seega Game API",
        Version = "v1",
        Description = "古埃及 Seega 棋類遊戲後端 API",
        Contact = new OpenApiContact
        {
            Name = "Seega 開發團隊",
            Email = "support@seegagame.com"
        }
    });
});

// ✅ 註冊依賴注入 (DI)
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IAiService, AiService>();

// ✅ 設定 CORS (跨來源資源共用)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173", // Vue 3 Vite 預設
            "http://localhost:3000", // React 預設
            "http://localhost:8080"  // 舊版 Vue CLI
        )
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
});

// ==========================================
// 2. 建立應用程式 (Build)
// ==========================================
var app = builder.Build();


app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Seega Game API v1");
    options.RoutePrefix = string.Empty; // 設定根路徑即顯示 Swagger UI
});

// 啟用 CORS
app.UseCors("AllowFrontend");

app.UseAuthorization();

// 映射 Controller 路由
app.MapControllers();

// 啟動專案
app.Run();