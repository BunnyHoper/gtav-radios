# GTA V Radios (RadioOnline)

Radio por internet dentro de GTA V (modo historia, Legacy). Mientras vas en un vehículo suena una emisora online y la radio del juego se apaga.

Viene configurado con emisoras españolas: LOS40 Urban, LOS40 (Anda Ya), LOS40 Dance, Megastar FM, Hit FM, Kiss FM, Cadena 100, Cadena Dial, Chocolate FM y Loca FM Urban.

## Requisitos

- ScriptHookV
- ScriptHookVDotNet 3 (probado con la 3.7.0 nightly, juego build 3889)
- `NAudio.dll` 1.10 en la carpeta `scripts` (viene, por ejemplo, con ExtendedLSC)

## Instalación

Copia `scripts\RadioOnline.dll` y `scripts\RadioOnline.ini` a la carpeta `scripts` del juego.

## Controles (se cambian en `RadioOnline.ini`)

| Tecla | Acción |
|---|---|
| Inicio | Encender / apagar |
| RePág / AvPág | Emisora siguiente / anterior |
| + / − del teclado numérico | Volumen |

## Emisoras

En `RadioOnline.ini`, sección `[STATIONS]`, una por línea:

```
Station11=Nombre|https://url-del-stream.mp3
```

Solo streams MP3. Los errores de conexión se muestran en pantalla y se guardan en `%TEMP%\RadioOnline.log`.

## Compilar

```powershell
.\build.ps1 -Game "E:\Grand Theft Auto V Legacy"
```
