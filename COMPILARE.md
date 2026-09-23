# Come compilare MhxrPatcher

Da fare **sul PC personale**, non su quello di lavoro: l'antivirus aziendale
mette in quarantena l'eseguibile appena creato, e le esclusioni le decide
l'amministratore.

## Una volta sola

1. Installa il **.NET SDK 9 o superiore** da https://dotnet.microsoft.com/download
   (serve solo a te per compilare, non a chi userà il programma).
2. Copia questa cartella intera sul PC personale.
3. Se l'antivirus personale si lamenta, aggiungi un'esclusione su questa
   cartella. È un falso positivo: un eseguibile appena compilato, non firmato,
   che apre e riscrive file APK è esattamente il profilo che le euristiche
   colpiscono.

## Compilare

Dalla cartella del progetto:

```
dotnet publish -c Release
```

Le impostazioni stanno già nel `.csproj`, quindi non serve aggiungere niente
alla riga di comando. Il risultato è **un solo file**:

```
bin\Release\net9.0-windows\win-x64\publish\MhxrPatcher.exe
```

Pesa sui 150 MB perché contiene anche il .NET: è il prezzo per non far
installare niente a chi lo scarica.

## Provarlo senza interfaccia

L'eseguibile accetta anche argomenti, utile per rifare la patch in fretta:

```
MhxrPatcher.exe --apk=gioco.apk --out=modificato.apk --url=http://tuoserver/ --inglese
```

Attenzione: essendo un programma con finestre non ha una console, quindi
lanciato così non stampa nulla a schermo. Serve per automatizzare, non per
vedere cosa succede.

## I file e cosa fanno

| File | Ruolo |
|---|---|
| `Patcher.cs` | apre l'APK, cerca e sostituisce l'indirizzo, cambia i testi, ricostruisce il pacchetto |
| `Firma.cs` | crea la chiave e firma l'APK usando gli strumenti del JDK |
| `Form1.cs` | la finestra con i quattro passaggi |
| `Program.cs` | avvio: senza argomenti apre la finestra, con argomenti lavora in silenzio |
| `GUI_msg_en.arc` | i testi tradotti, incorporati nell'eseguibile |

## Aggiornare la traduzione

Quando la traduzione cresce, si rigenera l'archivio con gli strumenti del
server (`tools-js`), si sostituisce `GUI_msg_en.arc` qui dentro e si ricompila.
Non serve toccare il codice.

## Se un domani Java desse fastidio agli utenti

La firma vive tutta dentro `Firma.cs`, separata dal resto. Si può sostituire
con una firma scritta in C# — così l'utente non installerebbe più nulla —
senza toccare né l'interfaccia né la logica di modifica dell'APK.
