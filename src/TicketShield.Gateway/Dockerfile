FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY ["src/TicketShield.Gateway/TicketShield.Gateway.csproj", "src/TicketShield.Gateway/"]
RUN dotnet restore "src/TicketShield.Gateway/TicketShield.Gateway.csproj"

COPY . .
WORKDIR "/app/src/TicketShield.Gateway"
RUN dotnet publish "TicketShield.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "TicketShield.Gateway.dll"]
