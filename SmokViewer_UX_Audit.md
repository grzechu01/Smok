# SmokViewer — audyt UX/UI (2026-11-01)

Źródła: aplikacja na żywo (https://smok-viewer.azurewebsites.net/, desktop 800 px i telefon 375 px), kod `Viewer/`, wcześniejsze dokumenty `SmokViewer_Analysis.md` i `SmokViewer_UI_Design_Brief.md`.

## P0 — błędy i wydajność (psują dane albo bazę)

1. **Każde wejście = pełny `SELECT *` z 7 dni (~1000 wierszy), nawet gdy oglądasz 24 h.** Filtrowanie i uśrednianie dzieje się w C#. Na Azure SQL Basic (5 DTU) jedno takie zapytanie podbija DTU do ~91%.
2. **Zapytanie wykonuje się dwa razy na wizytę.** `_Host.cshtml` używa `render-mode="ServerPrerendered"`, a dane są ładowane w `OnInitializedAsync` — raz przy prerenderze, drugi raz po podłączeniu obwodu SignalR.
3. **Strona czeka na bazę, zanim cokolwiek pokaże.** Prerender blokuje się na zapytaniu; nie ma szkieletu ani stanu „ładowanie”, a przy zimnym starcie (Free plan) to kilkadziesiąt sekund pustego ekranu.
4. **Zakres „24 h” obejmuje w praktyce 22–23 h.** `LineChart.UpdateChart` porównuje czas już przeliczony na polski z `DateTime.UtcNow`, więc ucina 1–2 h (zależnie od DST).
5. **Brak dłuższych zakresów.** Są tylko 24 h / 3 dni / 7 dni, a w bazie jest ~148 tys. wierszy (ok. 2,8 roku).
6. **Wszystko jest uśredniane do pełnych godzin, także widok 24 h.** Gubi to rozdzielczość 10-minutową; dziury w danych (np. 969 zamiast ~1008 wierszy w 7 dniach) są cicho łączone linią.

## P1 — czytelność

7. **Brak odpowiedzi na podstawowe pytanie „jak jest teraz?”.** Nie ma aktualnego odczytu, czasu ostatniego pomiaru, informacji, czy czujnik żyje, ani oceny jakości powietrza (np. wg indeksu GIOŚ).
8. **Wilgotność i ciśnienie są w bazie i w tabeli, ale nie ma ich na wykresach.** Pierwszy wykres pokazuje tylko temperaturę, choć `SmokViewer_Analysis.md` twierdzi inaczej.
9. **Wykresy nie mają tytułów, jednostek (°C, µg/m³) ani punktów odniesienia.** Bez norm (WHO / GIOŚ) nie wiadomo, czy 35 µg/m³ to dużo.
10. **Mieszany język i formaty.** Etykiety są po angielsku („24 hours”, „Show details”), godziny w formacie 12 h (11PM), daty w tabeli w formacie USA (10/25/2026), a `lang="en"`.
11. **Tabela „Show details” renderuje naraz ~1000 wierszy surowych danych** (strona robi się na ~41 000 px). Kolumny nie mają jednostek, nic nie da się sortować ani pobrać.

## P1 — responsywność (telefon 375 px)

12. **Wykresy na telefonie są niskie i ściśnięte.** Etykiety osi są obrócone o 45°, legenda zajmuje dużo miejsca, a punkt na każdej godzinie zlewa się w kreskę.
13. **Przyciski zakresu (radio 22 px) zawijają się**: „7 / days” łamie się na dwie linie. Własna klasa `.container` koliduje z klasą `.container` z Bootstrapa.
14. **Na dole zostaje biała pustka** (brak układu treści), a przycisk „Show details” ma domyślny styl przeglądarki.

## P2 — wygląd i kod

15. **Wygląd jak szablon.** Brak hierarchii, kart, motywu ciemnego i spójnej palety; kolory pochodzą z przykładowych danych Chart.js (`SampleUtils`).
16. **Ładowane jest sporo zbędnych zasobów.** Pełny Bootstrap 5 i open-iconic są ładowane, ale prawie nieużywane; `NavMenu.razor` to martwy kod z szablonu (Counter/Fetch data). Chart.js 2.9 i moment.js (2020) też są przestarzałe.
17. **Ryzyko na Linuksie przy strefie czasowej.** `TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time")` to identyfikator Windows; działa dzięki ICU, ale `Europe/Warsaw` jest pewniejsze.

## Plan przeprojektowania

- **Dane:**
  - agregacja w SQL, a nie w C#: ≤ 2 dni → punkty 10-min (surowe), dłuższe zakresy → `GROUP BY` godzina (avg/min/max/count);
  - grupowanie w większe kubełki w czasie polskim (24 h: 10 min, 7 dni: 1 h, 30 dni: 6 h, rok/wszystko: 1 dzień, zakres własny dobierany automatycznie, maks. ~400 punktów);
  - aktualny odczyt pobierany przez `TOP 1 … ORDER BY Id DESC`;
  - cache wyników na kilka minut (dane przychodzą co 10 min);
  - przerwy w danych widoczne jako przerwy w linii.
- **Ładowanie:**
  - prerender pokazuje od razu szkielet strony, a dane są pobierane dopiero po podłączeniu (`OnAfterRenderAsync`), więc jedno zapytanie zamiast dwóch;
  - osobne stany: ładowanie, błąd z przyciskiem „Spróbuj ponownie” i brak danych;
  - zmiana zakresu anuluje poprzednie zapytanie.
- **UI:**
  - nagłówek ze statusem czujnika („ostatni pomiar 4 min temu”);
  - karty bieżących wartości z oceną PM2.5/PM10 wg indeksu GIOŚ;
  - przełącznik zakresu jako segmenty (24 h / 7 dni / 30 dni / rok / własny) zapisywany w URL;
  - wykres pyłów z liniami norm WHO;
  - temperatura, wilgotność i ciśnienie z pasmem min–max;
  - tabela danych zagregowanych (zwijana, z pobieraniem CSV);
  - całość po polsku, z formatem 24 h i lokalizacją `pl-PL`;
  - jasny i ciemny motyw.
- **Technicznie:**
  - Chart.js 4 przez małą warstwę JS interop (zamiast ChartJs.Blazor / Chart.js 2.9 / moment);
  - własny lekki CSS zamiast Bootstrapa.

## Do decyzji (nie robię tego sam)

- **Indeks na `Timestamp`:** po pierwszym podłączeniu do bazy sprawdzę, czy jest potrzebny. Jeśli jest tylko klucz klastrowy na `Id`, każde zapytanie po zakresie czasu skanuje całą tabelę. Rekomendacja i polecenie są w raporcie końcowym. Użytkownik `smok_app` nie może tworzyć indeksów.
- **.NET 10:** .NET 8 traci wsparcie 2026-11-10. Przejście Viewer na `net10.0` pasuje do tej pracy (zmiana w csproj i test lokalny), ale wymaga też zmiany stosu na App Service przy wdrożeniu.
- **Firewall Azure SQL:** obecnie dopuszcza tylko usługi Azure, więc lokalny test na prawdziwej bazie wymaga reguły dla Twojego IP.

- **Założenie o kolejności wierszy (ważne przy zmianach w Function):**
  - Viewer zakłada, że `Id` rośnie razem z `Timestamp`. Dziś to prawda, bo Function zapisuje `Timestamp = DateTime.UtcNow` w chwili wstawienia.
  - Jeśli Function zacznie zapisywać czas z urządzenia albo ktoś doimportuje stare dane, zakresy w Viewerze będą po cichu gubić pomiary.
  - Przy takiej zmianie trzeba założyć indeks na `Timestamp` (konto admina) i usunąć ograniczenie po `Id` w `MeasurementsService`.

## Wynik (wdrożone lokalnie, niewdrożone na Azure)

- **Koszt zapytań:** seek po `Id` zamiast skanu (Id i Timestamp rosną razem — sprawdzone na całej tabeli: 0 wyjątków).
  - 24 h: ok. 33 odczyty stron;
  - rok: ok. 1050 odczytów stron (zapytanie z dziennymi średnimi);
  - dla porównania stary `SELECT *` z 7 dni robił pełny skan: 1535 odczytów stron.
  - Wyniki są cache'owane 2–15 min (zakresy z przeszłości: 6 h).
- **Indeks na `Timestamp` nie jest potrzebny** przy obecnym wzorcu zapytań. Pomógłby dopiero przy częstych zakresach własnych z odległej przeszłości (wtedy `CREATE NONCLUSTERED INDEX IX_Measurements_Timestamp ON dbo.Measurements([Timestamp]) INCLUDE (Temperature, Humidity, Pressure, PM1, PM25, PM10)`, z konta admina).
- **Jakość danych:** czujnik temperatury, wilgotności i ciśnienia regularnie zwraca błędne wartości (zera, ciśnienie 0 / 718 / 2080 hPa).
  - Skala w 2026 r. (42 tys. odczytów): ok. 2,6 tys. odczytów z samymi zerami (6%) i ok. 2,8 tys. z ciśnieniem poza zakresem (7%); łącznie ok. 13%.
  - Viewer pomija je przy liczeniu temperatury, wilgotności i ciśnienia (warunek: 950–1060 hPa, 0–100 %, −35…50 °C) i pokazuje liczbę pominiętych odczytów pod tabelą.
  - Odczyty pyłu (PM) nie są filtrowane.
- **Do obejrzenia:** latem dobowe średnie temperatury sięgają ok. 40 °C, a maksima 46 °C. Wygląda to na czujnik nagrzewany przez słońce lub obudowę, a nie na błąd odczytu, więc te wartości zostały w danych.
- **Uwaga do starszych dokumentów:** `SmokViewer_Analysis.md` jest częściowo nieaktualny. Twierdzi, że wykres pokazuje temperaturę, wilgotność i ciśnienie (pokazywał tylko temperaturę) oraz że zapytania są „efektywne”.
