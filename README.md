# broker-ledger

Dom maklerski przed giełdą [Parkiet](https://github.com/Sitkowski01/parkiet-exchange): rachunki
klientów, księga podwójnego zapisu, blokada środków pod zlecenia i rozliczanie transakcji,
które giełda publikuje w Kafce.

```
klient ──► broker-ledger ──zlecenie──► Parkiet (giełda) ──transakcje──► Kafka
               ▲                                                         │
               └──────────────── rozliczenie ◄───────────────────────────┘
```

C# / .NET 10, ASP.NET Core, xUnit, Stryker.NET.

## Zakres

| Etap | Co | Stan |
|---|---|---|
| 1 | Szkielet: domena oddzielona od API, testy architektury, mutation testing w CI, typ `Money` | ✅ |
| 2 | Księga podwójnego zapisu: dokument = zapisy sumujące się do zera, saldo liczone z zapisów | |
| 3 | PostgreSQL (EF Core), księga append-only, idempotencja, Testcontainers | |
| 4 | API: rejestracja i JWT, wpłaty, wypłaty, saldo, historia; rate limiting | |
| 5 | Zlecenia: blokada środków → giełda → zwolnienie przy odrzuceniu (saga) | |
| 6 | Rozliczenie transakcji z Kafki, odporne na powtórki | |
| 7 | Portfel: pozycje, średnia cena, zysk/strata, kursy NBP | |

## Pieniądze

`Money` to kwota w `decimal` z walutą. Nigdy `double`: 0,1 + 0,2 w `double` to nie 0,3,
a w księdze każdy grosz musi się zgadzać.

- **Ułamek grosza to błąd**, nie coś do cichego zaokrąglenia: `Money.Pln(250.405m)` rzuca wyjątek.
- **Nie da się dodać złotówek do euro** — operacje na różnych walutach rzucają wyjątek;
  przeliczenie będzie jawne, po kursie.
- **Klasa, nie struct** — `default` dla structa dałby kwotę bez waluty.

## Uruchomienie

```bash
dotnet run --project src/BrokerLedger.Api     # http://localhost:8091
curl localhost:8091/health                    # Healthy
```

## Testy

```bash
dotnet test                   # jednostkowe, architektura, API
dotnet tool restore
dotnet stryker                # testy mutacyjne domeny, próg 80%
```

| Rodzaj | Co sprawdza |
|---|---|
| Jednostkowe | `Money` i `Currency`: grosze, waluty, porównania, `null` |
| Architektura | domena nie zależy od ASP.NET, EF Core, HTTP ani Kafki (NetArchTest) |
| API | aplikacja wstaje w pamięci (`WebApplicationFactory`), `/health` odpowiada |
| Mutacyjne | Stryker psuje kod domeny i sprawdza, czy testy to wykryją — wynik 100% |

Na Windowsie z zainstalowanym Visual Studio 2022 Stryker bierze jego starszy MSBuild, który nie zna
.NET 10 — wtedy `dotnet stryker --msbuild-path "C:/Program Files/dotnet/sdk/<wersja>/MSBuild.dll"`.
