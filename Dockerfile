# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /app

COPY *.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app

COPY --from=build /app/out ./

# Render expects app to listen on $PORT
ENV ASPNETCORE_URLS=http://+:$PORT

ENTRYPOINT ["dotnet", "seega.dll"]
