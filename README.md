# OLA Game Studio

Polski edytor prostych gier platformowych, odtwarzacz projektow `.ola` oraz biblioteka gier.

## Pobieranie

Pobierz `Setup_OlaGameStudio.exe` z zakladki **Releases**. Instalator pobiera najnowszy `OlaGameStudio.exe` z publicznego GitHub Release, a ponowne uruchomienie aktualizatora podmienia program. Konta, projekty i biblioteka sa przechowywane w profilu Windows i pozostaja po aktualizacji.

## Funkcje wersji PC

- Tworzenie, otwieranie i zapisywanie poziomow `.ola`.
- Ruch gracza strzalkami lub A/D; skok W, strzalka w gore albo spacja.
- Konta lokalne oraz opcjonalne logowanie Google.
- Osobna biblioteka i katalog projektow dla kazdego konta.
- Uruchamianie plikow gier i linkow online z biblioteki.

## Budowanie na Windows

Wymagany jest kompilator C# dostarczany z .NET Framework 4.x. Przed uruchomieniem `build.ps1` ustaw zmienna srodowiskowa `GOOGLE_OAUTH_CLIENT_SECRET`; skrypt wstawia ja tylko do tymczasowego pliku kompilacji i usuwa go po zbudowaniu. Pliki `OlaGameStudio.exe` i `Setup_OlaGameStudio.exe` powstana w folderze `release`.

## Dane lokalne

Konta i sesja: `%LOCALAPPDATA%\OlaGameStudio`. Projekty i biblioteka sa rozdzielone wedlug uzytkownika aplikacji w podfolderze `Users`.
