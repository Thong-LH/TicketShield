FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ["src/TicketShield.AntiBot.Verification/TicketShield.AntiBot.Verification.csproj", "src/TicketShield.AntiBot.Verification/"]
COPY ["src/TicketShield.AntiBot.Gateway/TicketShield.AntiBot.Gateway.csproj", "src/TicketShield.AntiBot.Gateway/"]
RUN dotnet restore "src/TicketShield.AntiBot.Gateway/TicketShield.AntiBot.Gateway.csproj"

COPY . .
WORKDIR "/app/src/TicketShield.AntiBot.Gateway"
RUN dotnet publish "TicketShield.AntiBot.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false
RUN rm -f /app/publish/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5006
EXPOSE 5006
ENTRYPOINT ["dotnet", "TicketShield.AntiBot.Gateway.dll"]

