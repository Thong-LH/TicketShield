FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY ["src/TicketShield.Settlement.API/TicketShield.Settlement.API.csproj", "src/TicketShield.Settlement.API/"]
RUN dotnet restore "src/TicketShield.Settlement.API/TicketShield.Settlement.API.csproj"
COPY . .
WORKDIR "/app/src/TicketShield.Settlement.API"
RUN dotnet publish "TicketShield.Settlement.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5005
EXPOSE 5005
ENTRYPOINT ["dotnet", "TicketShield.Settlement.API.dll"]
