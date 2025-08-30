# 使用官方 .NET 7 runtime 作為 base image
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app

# 使用 .NET 7 SDK build image
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# 複製專案檔與程式碼
COPY . .

# restore 與 publish
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# 建立最終 runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# 設定 entrypoint
ENTRYPOINT ["dotnet", "WebexGeminiBot.dll"]
