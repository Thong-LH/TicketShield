FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ["src/TicketShield.Contracts/TicketShield.Contracts.csproj", "src/TicketShield.Contracts/"]
COPY ["src/TicketShield.Identity/TicketShield.Identity.Domain/TicketShield.Identity.Domain.csproj", "src/TicketShield.Identity/TicketShield.Identity.Domain/"]
COPY ["src/TicketShield.Identity/TicketShield.Identity.Application/TicketShield.Identity.Application.csproj", "src/TicketShield.Identity/TicketShield.Identity.Application/"]
COPY ["src/TicketShield.Identity/TicketShield.Identity.Infrastructure/TicketShield.Identity.Infrastructure.csproj", "src/TicketShield.Identity/TicketShield.Identity.Infrastructure/"]
COPY ["src/TicketShield.Identity/TicketShield.Identity.API/TicketShield.Identity.API.csproj", "src/TicketShield.Identity/TicketShield.Identity.API/"]

RUN dotnet restore "src/TicketShield.Identity/TicketShield.Identity.API/TicketShield.Identity.API.csproj"

COPY . .
WORKDIR "/app/src/TicketShield.Identity/TicketShield.Identity.API"
RUN dotnet publish "TicketShield.Identity.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5002
EXPOSE 5002
ENTRYPOINT ["dotnet", "TicketShield.Identity.API.dll"]
