using Microsoft.OpenApi;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);


// 註冊 Controller 並設定 JSON 選項
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
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
    });
});

builder.Services.AddMemoryCache();

// 設定 CORS (跨來源資源共用)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("https://seega.pages.dev")
               .WithOrigins("http://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});


var app = builder.Build();


app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Seega Game API v1");
    options.RoutePrefix = string.Empty;
});

// 啟用 CORS
app.UseCors("AllowFrontend");

app.UseAuthorization();

// 映射 Controller 路由
app.MapControllers();

// 啟動專案
app.Run();