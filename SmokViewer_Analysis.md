# Smok — opis systemu (stan na 2026-11-01)

Domowa stacja pomiaru powietrza. Czujnik co kilka minut wysyła pomiar do Azure Function, która zapisuje go w Azure SQL. Viewer (Blazor Server) pokazuje bieżące warunki i historię.

```
czujnik (ESP8266: PMS7003 + BME280)
   │  HTTPS POST JSON, nagłówek x-functions-key
   ▼
Function  smok.azurewebsites.net/api/store      (Flex Consumption, .NET 10 isolated)
   │  INSERT
   ▼
Azure SQL  smok-database-server / smok-database (Basic, 5 DTU)
   ▲  SELECT (zagregowane w SQL)
   │
Viewer    smok-viewer.azurewebsites.net         (App Service F1, .NET 10, Blazor Server)
```

Wszystkie zasoby są w grupie `smok-rg` (West Europe).

## Urządzenie

- Płytka NodeMCU v3 (ESP8266) z czujnikami PMS7003 (PM1, PM2.5, PM10) i BME280 (temperatura, wilgotność, ciśnienie). Firmware: repozytorium `SmogomierzSlim`.
- Od 2026-11-02 płytka działa na ESPHome (`esphome/smogomierz.yaml` w `SmogomierzSlim`). Wysyła pomiar co 3 minuty na `https://smok.azurewebsites.net/api/store` z kluczem funkcji `smogomierz` w nagłówku `x-functions-key`; adres i klucz są w `secrets.yaml`.
- Stary firmware Arduino (HTTP, port 80, bez klucza) został nadpisany i po tej zmianie już by nie działał.
- Treść żądania: `{"PM1":…, "PM25":…, "PM10":…, "Temperature":…, "Humidity":…, "Pressure":…}`.

## Function (`Function/`)

- Funkcja `store` (GET/POST, wymaga klucza funkcji) zapisuje pomiar z `Timestamp = DateTime.UtcNow`, czyli z czasem serwera, a nie urządzenia.
- JSON jest czytany pobłażliwie, tak jak w dawnej wersji in-process (Newtonsoft):
  - wielkość liter w nazwach pól nie ma znaczenia;
  - liczby mogą przyjść jako tekst;
  - brakujące pola dają 0.
- Błędny lub pusty JSON dostaje odpowiedź 400.
- Ustawienia aplikacji: `SmokSql` (connection string), `AzureWebJobsStorage`, `DEPLOYMENT_STORAGE_CONNECTION_STRING` (konto `smokstorage`, StorageV2, TLS 1.2) i `APPLICATIONINSIGHTS_CONNECTION_STRING` (`SmokInsights`).
- Plan Flex Consumption: 512 MB pamięci na instancję, najwyżej 2 instancje. Ruch mieści się w darmowym limicie.
- Aplikacja wymaga HTTPS (`httpsOnly`): żądania po HTTP dostają przekierowanie 301. Bez ważnego klucza odpowiedź to 401.
- Klucze funkcji: `default`, `storeMeasurement` i `smogomierz` (używany przez płytkę).

## Baza (`dbo.Measurements`)

| Kolumna | Typ |
|---|---|
| `Id` | int identity, klastrowy klucz główny |
| `Timestamp` | datetime2, UTC |
| `Temperature`, `Humidity`, `Pressure`, `PM1`, `PM25`, `PM10` | float |

- Dane od 2021-12-05, ok. 148 tys. wierszy, ok. 41 MB z 2 GB.
- Użytkownik `smok_app` ma tylko uprawnienia SELECT i INSERT na tej tabeli.
- **`Id` rośnie razem z `Timestamp`** (sprawdzone na całej tabeli). Viewer na tym polega: zapytania o zakres czasu szukają po kluczu `Id` zamiast skanować tabelę.
  - Jeśli Function zacznie zapisywać czas z urządzenia albo ktoś doimportuje stare dane, trzeba założyć indeks na `Timestamp` (konto administratora) i usunąć ograniczenie po `Id` w `Viewer/Data/MeasurementsService.cs`.
- Czujnik BME280 zwraca czasem błędne wartości: zera albo ciśnienie 0, 718 czy 2080 hPa. W 2026 r. to ok. 13% wierszy. Zostają w bazie, ale Viewer ich nie liczy.
- Wiersze 148388 i 148389 to testy migracji Function, kopie prawdziwych odczytów z 2026-11-01.

## Viewer (`Viewer/`)

- Blazor Server na .NET 10. Strona `/` renderuje się od razu jako szkielet z placeholderami, a dane pobiera dopiero po nawiązaniu połączenia. Ma stany ładowania, błędu i braku danych.
- **Teraz:**
  - ocena jakości powietrza wg indeksu GIOŚ, liczona ze średnich 1-godzinnych PM2.5 i PM10;
  - temperatura, wilgotność i ciśnienie ze zmianą w ciągu ostatniej godziny;
  - status czujnika; po ponad godzinie bez danych karty pokazują „Brak aktualnych danych”.
- **Historia:** zakresy 24 h, 7 dni, 30 dni, rok i własny (`?range=24h|7d|30d|1y` albo `?from=RRRR-MM-DD&to=RRRR-MM-DD`). Wykresy Chart.js 4: pył z liniami wytycznych WHO 2021 oraz temperatura, wilgotność i ciśnienie z pasmem min–maks. Pod wykresami tabela z eksportem CSV.
- **Agregacja w SQL:**
  - zakres do 2 dni: surowe pomiary;
  - dłuższe zakresy: średnia, minimum i maksimum w kubełkach 1 h, 3 h, 6 h, 12 h, 1 doby lub 1 tygodnia (najwyżej ok. 400 punktów);
  - kubełki są cięte w czasie Europe/Warsaw, z uwzględnieniem zmiany czasu;
  - pierwszy niepełny kubełek jest pomijany.
- **Obciążenie bazy:**
  - 24 h to ok. 33 odczyty stron, rok ok. 1050;
  - wyniki są trzymane w pamięci 2–15 min, a zakresy z przeszłości 6 h;
  - cache ma limit rozmiaru;
  - do bazy idą najwyżej 2 zapytania o zakresy naraz.
- **Biblioteki z CDN** (jsdelivr, z sumami SRI): Chart.js 4.4.1, Luxon 3.4.4 i chartjs-adapter-luxon 1.3.1. Gdy się nie załadują, wykresy pokazują komunikat, a reszta strony działa dalej.
- **Hosting:** App Service F1, Linux, `DOTNETCORE|10.0`, WebSockety włączone. Na planie F1 nie ma Always On, więc pierwsze wejście po przerwie startuje aplikację od zera.

## Praca lokalna

- **Viewer:** `dotnet run` w `Viewer/` (środowisko Development) bierze `ConnectionStrings:SmokSql` z `dotnet user-secrets`.
- **Function:** `func start` w `Function/` (Azure Functions Core Tools 4) bierze `SmokSql` z `local.settings.json`, który nie jest w repozytorium.
- **Dostęp do bazy:** firewall Azure SQL wpuszcza tylko usługi Azure i adresy dodane regułą. Dla lokalnego IP potrzebna jest reguła, obecnie `dev-grzechu`.

## Wdrożenie

```bash
# Viewer
dotnet publish Viewer/SmokViewer.csproj -c Release -o out/viewer
(cd out/viewer && zip -qr ../viewer.zip .)
az webapp deploy -g smok-rg -n smok-viewer --src-path out/viewer.zip --type zip

# Function
dotnet publish Function/Smok.csproj -c Release -o out/function
(cd out/function && zip -qr ../function.zip .)
az functionapp deployment source config-zip -g smok-rg -n smok --src out/function.zip
```

## Koszty

Ok. 4,3 € miesięcznie, prawie w całości za Azure SQL Basic. Viewer (F1), Function (Flex), storage i Application Insights mieszczą się w darmowych limitach.

Basic to najtańszy plan Azure SQL. Darmowa oferta Azure SQL nie pasuje, bo zapis co kilka minut nie pozwala bazie się uśpić. Koszt da się zbić prawie do zera tylko zmianą przechowywania danych:
- Azure Table Storage na istniejącym koncie (z podsumowaniami godzinowymi i dobowymi liczonymi przy zapisie);
- darmowy poziom Cosmos DB;
- przeniesienie całości do Home Assistanta, do którego ESPHome trafia natywnie.

## Znane ograniczenia

- Klucz funkcji jest zapisany w firmware płytki. Jeśli wycieknie (np. z `secrets.yaml`), trzeba go zmienić w Azure i wgrać firmware ponownie.
- Błędne odczyty BME280 są tylko pomijane w Viewerze, a nie usuwane z bazy.
- Audyt UX i decyzje projektowe Viewera: `SmokViewer_UX_Audit.md`.
