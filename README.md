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
| 2 | Księga podwójnego zapisu: dokumenty bilansujące się w każdym aktywie, saldo z zapisów, blokady | ✅ |
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

## Księga podwójnego zapisu

Każda operacja to **dokument księgowy**: zapisy po stronie Wn (debet) i Ma (kredyt), które
w każdym aktywie sumują się do tej samej kwoty. Pieniądz nie powstaje ani nie znika — tylko
przechodzi z konta na konto. Saldo nie jest kolumną do nadpisania: liczy się je z zapisów,
więc zawsze da się pokazać, skąd się wzięło.

| Konto | Czyje | Rośnie po | Co znaczy |
|---|---|---|---|
| `ClientAvailable` | klienta | Ma | wolne środki / akcje — do wypłaty albo pod zlecenie |
| `ClientBlocked` | klienta | Ma | zablokowane pod zlecenie czekające na giełdzie |
| `Bank` | brokera | Wn | gotówka brokera w banku |
| `Depository` | brokera | Wn | akcje przechowywane za klientów |
| `FeeIncome` | brokera | Ma | przychód z prowizji |

```
wpłata 1000 zł        Wn Bank:PLN              1000  │  Ma ClientAvailable:PLN   1000
blokada pod zlecenie  Wn ClientAvailable:PLN   250   │  Ma ClientBlocked:PLN      250
wypłata 100 zł        Wn ClientAvailable:PLN   100   │  Ma Bank:PLN               100
```

- **Aktywo to waluta albo akcje spółki.** Dokument bilansuje się osobno w każdym aktywie —
  10 akcji CDR nie zrównoważy 10 zł. Gotówka liczy się do grosza, akcje w sztukach.
- **Żadne konto nie zejdzie poniżej zera** — klient nie wyda więcej, niż ma, a broker nie odda
  gotówki ani akcji, których nie trzyma. Dokument, który by to zrobił, jest odrzucany w całości.
- **Saldo to suma zapisów.** Księga trzyma je bieżąco (żeby nie przechodzić całej historii),
  ale zmienia je wyłącznie dokument — testy sprawdzają, że zgadza się z sumą zapisów.
- **Księga tylko się dopisuje** — błędu nie poprawia się edycją, tylko kolejnym dokumentem.

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
| Jednostkowe | `Money`, konta, zapisy, dokumenty, księga: bilansowanie, brak środków, atomowość dokumentu |
| Własności (FsCheck) | losowe ciągi wpłat, wypłat, blokad i zwolnień kontra prosty model; po każdym kroku bilans próbny i brak ujemnych sald klienta |
| Architektura | domena nie zależy od ASP.NET, EF Core, HTTP ani Kafki (NetArchTest) |
| API | aplikacja wstaje w pamięci (`WebApplicationFactory`), `/health` odpowiada |
| Mutacyjne | Stryker psuje kod domeny i sprawdza, czy testy to wykryją — wynik 100% |

Na Windowsie z zainstalowanym Visual Studio 2022 Stryker bierze jego starszy MSBuild, który nie zna
.NET 10 — wtedy `dotnet stryker --msbuild-path "C:/Program Files/dotnet/sdk/<wersja>/MSBuild.dll"`.
