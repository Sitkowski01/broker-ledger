# Obraz API: kompilacja w SDK, uruchomienie w samym runtime ASP.NET, bez roota.
# Najpierw same pliki projektow i restore — ta warstwa przebudowuje sie tylko przy zmianie pakietow.
#   docker compose --profile app up --build      (calosc: broker, front, gielda Parkiet, Kafka, bazy)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/BrokerLedger.Domain/BrokerLedger.Domain.csproj src/BrokerLedger.Domain/
COPY src/BrokerLedger.Infrastructure/BrokerLedger.Infrastructure.csproj src/BrokerLedger.Infrastructure/
COPY src/BrokerLedger.Api/BrokerLedger.Api.csproj src/BrokerLedger.Api/
RUN dotnet restore src/BrokerLedger.Api/BrokerLedger.Api.csproj
COPY src src
RUN dotnet publish src/BrokerLedger.Api/BrokerLedger.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
# Uzytkownik "app" jest w obrazach .NET od wersji 8 — proces nie ma praw roota w kontenerze.
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BrokerLedger.Api.dll"]
