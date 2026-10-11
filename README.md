# broker-ledger

Dom maklerski przed giełdą [Parkiet](https://github.com/Sitkowski01/parkiet-exchange): rachunki
klientów, księga podwójnego zapisu, blokada środków pod zlecenia i rozliczanie transakcji,
które giełda publikuje w Kafce.

```
klient ──► broker-ledger ──zlecenie──► Parkiet (giełda) ──transakcje──► Kafka
               ▲                                                         │
               └──────────────── rozliczenie ◄───────────────────────────┘
```

C# / .NET 10, ASP.NET Core, EF Core + PostgreSQL, xUnit, FsCheck, Testcontainers, Stryker.NET.

## Zakres

| Etap | Co | Stan |
|---|---|---|
| 1 | Szkielet: domena oddzielona od API, testy architektury, mutation testing w CI, typ `Money` | ✅ |
| 2 | Księga podwójnego zapisu: dokumenty bilansujące się w każdym aktywie, saldo z zapisów, blokady | ✅ |
| 3 | PostgreSQL (EF Core): historia nie do przepisania, idempotencja, saldo bez wyścigów i zakleszczeń, Testcontainers | ✅ |
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

## Księga w PostgreSQL

```
 accounts                         journal_entries                  postings
 ─────────────────────────────    ──────────────────────────────   ─────────────────────────
 id                               id (uuid v7)                     entry_id   → journal_entries
 type, asset_kind, asset_code,    idempotency_key  UNIQUE          account_id → accounts
 client_id (NULL = broker)        description, created_at          side (Debit / Credit)
 normal_side                                                       amount  CHECK > 0
 balance  CHECK >= 0  ← projekcja
```

- **Źródłem prawdy są zapisy** (`postings`). `accounts.balance` to projekcja trzymana dla szybkości —
  zmienia się w tej samej transakcji co zapisy, a `LedgerAudit` sprawdza w czystym SQL, czy saldo
  równa się sumie zapisów i czy bilans próbny się zgadza.
- **Historii nie da się przepisać.** Trigger odrzuca `UPDATE`, `DELETE` i `TRUNCATE` na `journal_entries`
  i `postings` — także z ręcznego SQL-a. Błąd poprawia się kolejnym dokumentem.
- **Idempotencja.** Każdy dokument ma klucz od wywołującego (id przelewu, zdarzenia z giełdy).
  Ten sam klucz z tym samym dokumentem → „już zaksięgowane”, nic się nie zmienia; z innym → konflikt.
  Dokument jest wstawiany jako pierwszy, więc równoległa powtórka czeka na `UNIQUE` i nie dotyka sald.
- **Saldo bez wyścigu.** Konta dokumentu są blokowane (`SELECT balance ... FOR NO KEY UPDATE`),
  dopiero potem sprawdzane i zmieniane — do commita nikt inny ich nie ruszy. Z 20 równoległych
  wypłat po 10 zł ze 100 zł przejdzie dokładnie 10. `CHECK (balance >= 0)` to ostatnia linia obrony.
  `NO KEY UPDATE`, a nie `UPDATE`: wstawione wcześniej zapisy trzymają przez klucz obcy blokadę
  `KEY SHARE` na kontach, a `FOR UPDATE` by z nią kolidował i transakcje czekałyby na siebie nawzajem.
- **Bez zakleszczeń.** Wiersze kont są blokowane zawsze w kolejności `id`. Bez tego równoległe
  blokady i zwolnienia na tych samych dwóch kontach kończą się `40P01 deadlock detected` — test to sprawdza.
- **Ta sama reguła co w pamięci.** Sprawdzenie robi domena (`EnsureNoAccountGoesNegative`), więc obie
  księgi zgłaszają to samo konto: najpierw klienta, potem brokera.

## Uruchomienie

```bash
dotnet run --project src/BrokerLedger.Api     # http://localhost:8091
curl localhost:8091/health                    # Healthy
```

## Testy

```bash
dotnet test                   # wszystko; integracyjne potrzebuja Dockera (Testcontainers)
dotnet tool restore
dotnet stryker                # testy mutacyjne domeny, próg 80%
dotnet stryker -f stryker-infrastructure.json   # testy mutacyjne warstwy bazy (kilka minut)
```

| Rodzaj | Co sprawdza |
|---|---|
| Jednostkowe | `Money`, konta, zapisy, dokumenty, księga: bilansowanie, brak środków, atomowość dokumentu |
| Własności (FsCheck) | losowe ciągi wpłat, wypłat, blokad i zwolnień kontra prosty model; po każdym kroku bilans próbny i brak ujemnych sald |
| Architektura | domena nie zależy od ASP.NET, EF Core, HTTP ani Kafki (NetArchTest) |
| Architektura (baza) | warstwa bazy nie zna warstwy web i nie wystawia swoich typów — reszta widzi tylko `ILedgerStore` |
| Integracyjne | prawdziwy PostgreSQL w Dockerze, osobna baza na test (klon szablonu po migracjach): idempotencja, wycofanie odrzuconego dokumentu, trigger i `CHECK`-i, audyt wykrywa ręczną zmianę salda |
| Współbieżność | 20 równoległych wypłat, 20 powtórek jednego klucza, przeciwne ruchy na tych samych kontach (bez zakleszczeń) |
| Różnicowe | ten sam losowy ciąg operacji na księdze w pamięci i w PostgreSQL — te same decyzje i salda |
| API | aplikacja wstaje w pamięci (`WebApplicationFactory`), `/health` odpowiada |
| Mutacyjne | Stryker psuje kod i sprawdza, czy testy to wykryją: domena 100%, warstwa bazy osobno (z Testcontainers) |

Na Windowsie z zainstalowanym Visual Studio 2022 Stryker bierze jego starszy MSBuild, który nie zna
.NET 10 — wtedy `dotnet stryker --msbuild-path "C:/Program Files/dotnet/sdk/<wersja>/MSBuild.dll"`.
