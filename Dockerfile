# 1. 使用 .NET 10.0 SDK 進行編譯
FROM ://mcr.microsoft.com AS build-env
WORKDIR /app

# 2. 複製 csproj 並還原依賴
COPY *.csproj ./
RUN dotnet restore

# 3. 複製所有原始碼並發佈 Release 版本
COPY . ./
RUN dotnet publish -c Release -o out

# 4. 使用 .NET 10.0 執行環境
FROM ://mcr.microsoft.com
WORKDIR /app
COPY --from=build-env /app/out .

# 5. 設定 Render 要求的 Port
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# 6. 啟動程式
ENTRYPOINT ["dotnet", "seega.dll"]
