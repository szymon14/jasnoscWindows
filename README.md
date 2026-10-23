# Jasność

Lekka aplikacja dla Windows, która siedzi w zasobniku systemowym (obok zegara) i pozwala regulować jasność **wszystkich monitorów** — zarówno wbudowanego ekranu laptopa, jak i monitorów zewnętrznych — z jednego małego okienka.

## Funkcje

- **Ekran laptopa** — sterowanie jasnością przez WMI (`WmiMonitorBrightnessMethods`).
- **Monitory zewnętrzne** — sterowanie przez DDC/CI (`dxva2.dll`), bez instalowania sterowników producenta.
- **Osobny suwak dla każdego monitora** w okienku otwieranym kliknięciem ikony słońca w zasobniku.
- **Płynna praca** — cała komunikacja z monitorami odbywa się w osobnym wątku, więc UI się nie zacina, a przy przeciąganiu suwaka wysyłana jest tylko najnowsza wartość.
- **Automatyczne odświeżanie** po podłączeniu/odłączeniu monitora, zmianie ustawień ekranu i wybudzeniu komputera.
- **Jasny/ciemny motyw** zgodny z ustawieniami Windows (ikona i okienko dostosowują się automatycznie).
- **Autostart** — opcja „Uruchamiaj razem z Windows” w menu kontekstowym ikony.
- Podpowiedź na ikonie pokazuje aktualną jasność każdego monitora.
- Uruchamia się tylko jedna instancja aplikacji.

## Wymagania

- Windows 10 lub 11
- .NET Framework 4.x (wbudowany w system)
- Monitory zewnętrzne muszą obsługiwać **DDC/CI** (zwykle trzeba to włączyć w menu OSD monitora). Monitory bez DDC/CI są oznaczane jako „brak DDC/CI”.

## Użycie

1. Zbuduj program (patrz [Budowanie](#budowanie)) i uruchom `Jasnosc.exe`.
2. Kliknij **lewym** przyciskiem ikonę słońca w zasobniku, aby otworzyć suwaki jasności.
3. Kliknij **prawym** przyciskiem, aby:
   - odświeżyć listę monitorów,
   - włączyć/wyłączyć uruchamianie razem z Windows,
   - zamknąć aplikację.

## Budowanie

Nie trzeba instalować Visual Studio ani .NET SDK — wystarczy kompilator `csc.exe` wbudowany w Windows:

```cmd
build.cmd
```

Skrypt tworzy `Jasnosc.exe` w katalogu głównym repozytorium i czeka na naciśnięcie klawisza, żeby można było przeczytać wynik. Plik `.exe` nie jest przechowywany w repozytorium — każdy buduje go sam.

## Struktura projektu

| Plik | Opis |
|------|------|
| `src/TrayApp.cs` | Punkt wejścia, ikona w zasobniku, menu, motyw, autostart, reakcja na zdarzenia systemowe |
| `src/PopupForm.cs` | Okienko z suwakami jasności |
| `src/BrightnessService.cs` | Wątek w tle obsługujący odczyt i zapis jasności |
| `src/Displays.cs` | Wykrywanie monitorów i obsługa WMI oraz DDC/CI |
| `src/Native.cs` | Deklaracje funkcji WinAPI (P/Invoke) |
| `app.manifest` | Manifest aplikacji (DPI awareness, uprawnienia zwykłego użytkownika) |
| `build.cmd` | Skrypt kompilacji |
