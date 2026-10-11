# broker-ledger

Dom maklerski przed giełdą [Parkiet](https://github.com/Sitkowski01/parkiet-exchange): rachunki
klientów, księga podwójnego zapisu, blokada środków pod zlecenia i rozliczanie transakcji,
które giełda publikuje w Kafce.

```
klient ──► broker-ledger ──zlecenie──► Parkiet (giełda) ──transakcje──► Kafka
               ▲                                                         │
               └──────────────── rozliczenie ◄───────────────────────────┘
```

C# / .NET 10, ASP.NET Core (minimal API, JWT, rate limiting), EF Core + PostgreSQL, xUnit, FsCheck, Testcontainers, Stryker.NET.

## Zakres

| Etap | Co | Stan |
|---|---|---|
| 1 | Szkielet: domena oddzielona od API, testy architektury, mutation testing w CI, typ `Money` | ✅ |
| 2 | Księga podwójnego zapisu: dokumenty bilansujące się w każdym aktywie, saldo z zapisów, blokady | ✅ |
| 3 | PostgreSQL (EF Core): historia nie do przepisania, idempotencja, saldo bez wyścigów i zakleszczeń, Testcontainers | ✅ |
| 4 | API: rejestracja i JWT, wpłaty i wypłaty z kluczem idempotencji, salda, historia, rate limiting, testy e2e | ✅ |
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
- **Nowe konta w tej samej transakcji** (w stałej kolejności) — odrzucony dokument nie zostawia pustych kont.
- **Ta sama reguła co w pamięci.** Sprawdzenie robi domena (`EnsureNoAccountGoesNegative`), więc obie
  księgi zgłaszają to samo konto: najpierw klienta, potem brokera.

## API

| Metoda | Ścieżka | Co robi |
|---|---|---|
| `POST` | `/api/auth/register` | `{email, password}` → 201 `{clientId}`; zajęty e-mail → 409 |
| `POST` | `/api/auth/login` | → 200 `{accessToken, expiresAt}` (JWT, `sub` = id klienta); złe dane → 401 |
| `GET` | `/api/me/balances` | salda klienta: na każde aktywo `available` i `blocked` |
| `POST` | `/api/me/deposits` | wpłata `{amount: "250.40", currency: "PLN"}` (symulowany przelew przychodzący) |
| `POST` | `/api/me/withdrawals` | wypłata; za mało środków → 422 z kwotą dostępną |
| `GET` | `/api/me/history?limit=20&before=…` | dokumenty klienta od najnowszych, tylko jego zapisy; kursor `nextBefore` |

- **`Idempotency-Key` jest obowiązkowy** przy wpłacie i wypłacie. Pierwsze wykonanie → 201, powtórka
  z tym samym kluczem i treścią → 200 `alreadyPosted` (nic się nie zmienia), ten sam klucz z inną treścią → 409.
  Klucz jest zapisywany z prefiksem klienta, więc dwóch klientów może użyć tego samego.
- **Kwoty jako tekst** (`"250.40"`) — klient w JS nie zgubi groszy w `double`. Akceptowane są tylko cyfry
  i kropka: `1e3`, `-5`, `1,000.00` i ułamek grosza to 400. Waluta tylko `PLN` (Parkiet handluje
  w złotówkach) — dowolny kod z trzech liter zakładałby nowe konto banku.
- **Historia w kolejności zaksięgowania** — po numerze kolejnym (`sequence`), nie po czasie ani UUID:
  dwa dokumenty w tej samej milisekundzie mają ten sam czas, a UUID v7 w obrębie milisekundy jest losowy.
- **Błędy w formacie RFC 9457** (`application/problem+json`): 400 z polem, którego dotyczą, 401, 409, 422, 429.
  Brak środków na koncie *brokera* to błąd w kodzie, więc zostaje 500 — nie udaje „złego żądania”.
- **Hasła**: PBKDF2 (`PasswordHasher` z ASP.NET Identity). Zły e-mail i złe hasło dają tę samą odpowiedź
  w tym samym czasie (dla nieistniejącego konta też liczony jest hash), żeby nie dało się sprawdzić, kto ma konto.
- **Rate limiting**: logowanie i rejestracja — 10/min na adres IP (zgadywanie haseł), operacje na koncie —
  120/min na klienta (jeden klient nie zatka API innym). Przekroczenie → 429 z `Retry-After`.
  Za reverse proxy adres klienta bierze się z `X-Forwarded-For`, ale tylko od proxy wpisanych
  w `ReverseProxy:KnownProxies` — inaczej każdy mógłby podać dowolny adres i ominąć limit.
- **Zła konfiguracja wywala start**, a nie pierwsze żądanie: brak klucza JWT (min. 32 bajty), wystawcy
  albo dodatnich limitów → `OptionsValidationException` przy uruchomieniu — **zanim** migracje dotkną bazy.
- `/health` sprawdza połączenie z bazą — bez niej aplikacja nic nie zaksięguje.

## Uruchomienie

```bash
docker compose up -d                          # PostgreSQL na porcie 5434
dotnet run --project src/BrokerLedger.Api     # http://localhost:8091, migracje przy starcie (Development)

curl -X POST localhost:8091/api/auth/register -H 'Content-Type: application/json' \
     -d '{"email":"jan@example.com","password":"correct horse battery"}'
TOKEN=$(curl -s -X POST localhost:8091/api/auth/login -H 'Content-Type: application/json' \
     -d '{"email":"jan@example.com","password":"correct horse battery"}' | jq -r .accessToken)

curl -X POST localhost:8091/api/me/deposits -H "Authorization: Bearer $TOKEN" \
     -H 'Idempotency-Key: przelew-1' -H 'Content-Type: application/json' -d '{"amount":"1000.00","currency":"PLN"}'
curl localhost:8091/api/me/balances -H "Authorization: Bearer $TOKEN"
# [{"asset":"PLN","kind":"Cash","available":"1000.00","blocked":"0.00"}]
```

Klucz JWT w `appsettings.Development.json` służy tylko do lokalnego uruchomienia; poza nim
ustawia się go zmienną `Jwt__SigningKey`.

## Testy

```bash
dotnet test                   # wszystko; integracyjne potrzebuja Dockera (Testcontainers)
dotnet tool restore
dotnet stryker                # testy mutacyjne domeny, próg 80%
dotnet stryker -f stryker-infrastructure.json   # testy mutacyjne warstwy bazy (kilka minut)
dotnet stryker -f stryker-api.json              # testy mutacyjne API na testach e2e
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
| E2E (API) | cała aplikacja (`WebApplicationFactory`) na prawdziwym PostgreSQL: rejestracja i logowanie, sfałszowane i wygasłe tokeny, wpłata → wypłata → salda → historia ze stronicowaniem, powtórki i konflikty kluczy, walidacja, 10 równoległych wypłat po HTTP, limity 429, start z błędną konfiguracją, health bez bazy |
| Architektura (API) | warstwa web nie zależy od EF Core ani Npgsql — rozmawia z księgą tylko przez porty domeny |
| Mutacyjne | Stryker psuje kod i sprawdza, czy testy to wykryją — osobno domena, warstwa bazy i API |

Na Windowsie z zainstalowanym Visual Studio 2022 Stryker bierze jego starszy MSBuild, który nie zna
.NET 10 — wtedy `dotnet stryker --msbuild-path "C:/Program Files/dotnet/sdk/<wersja>/MSBuild.dll"`.
