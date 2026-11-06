FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/OEESentinel.WebUI/OEESentinel.WebUI.csproj", "src/OEESentinel.WebUI/"]
COPY ["src/OEESentinel.Application/OEESentinel.Application.csproj", "src/OEESentinel.Application/"]
COPY ["src/OEESentinel.Domain/OEESentinel.Domain.csproj", "src/OEESentinel.Domain/"]
COPY ["src/OEESentinel.Infrastructure/OEESentinel.Infrastructure.csproj", "src/OEESentinel.Infrastructure/"]
RUN dotnet restore "src/OEESentinel.WebUI/OEESentinel.WebUI.csproj"
COPY . .
WORKDIR "/src/src/OEESentinel.WebUI"
RUN dotnet publish "OEESentinel.WebUI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OEESentinel.WebUI.dll"]