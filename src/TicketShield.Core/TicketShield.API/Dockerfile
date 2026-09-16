FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ["src/TicketShield.Contracts/TicketShield.Contracts.csproj", "src/TicketShield.Contracts/"]
COPY ["src/TicketShield.Core/TicketShield.Domain/TicketShield.Domain.csproj", "src/TicketShield.Core/TicketShield.Domain/"]
COPY ["src/TicketShield.Core/TicketShield.Application/TicketShield.Application.csproj", "src/TicketShield.Core/TicketShield.Application/"]
COPY ["src/TicketShield.Core/TicketShield.Infrastructure/TicketShield.Infrastructure.csproj", "src/TicketShield.Core/TicketShield.Infrastructure/"]
COPY ["src/TicketShield.Core/TicketShield.API/TicketShield.API.csproj", "src/TicketShield.Core/TicketShield.API/"]

RUN dotnet restore "src/TicketShield.Core/TicketShield.API/TicketShield.API.csproj"

COPY . .
WORKDIR "/app/src/TicketShield.Core/TicketShield.API"
RUN dotnet publish "TicketShield.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5003
EXPOSE 5003
ENTRYPOINT ["dotnet", "TicketShield.API.dll"]
