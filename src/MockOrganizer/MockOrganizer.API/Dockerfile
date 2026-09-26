FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ["src/TicketShield.Contracts/TicketShield.Contracts.csproj", "src/TicketShield.Contracts/"]
COPY ["src/MockOrganizer/MockOrganizer.API/MockOrganizer.API.csproj", "src/MockOrganizer/MockOrganizer.API/"]

RUN dotnet restore "src/MockOrganizer/MockOrganizer.API/MockOrganizer.API.csproj"

COPY . .
WORKDIR "/app/src/MockOrganizer/MockOrganizer.API"
RUN dotnet publish "MockOrganizer.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5001
EXPOSE 5004
ENTRYPOINT ["dotnet", "MockOrganizer.API.dll"]
