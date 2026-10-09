# Changelog

Each version is described in English and then in Portuguese. The section of a version becomes
the text of its GitHub release.

## 1.5.1

### English

- The download is now a single `piano.exe` (plus the sound font and the help files) instead of
  hundreds of files, and it is smaller.

### Português

- O download agora é um único `piano.exe` (mais o arquivo de sons e os arquivos de ajuda), em
  vez de centenas de arquivos, e ficou menor.

## 1.5

### English

**New**

- **Arranger (automatic accompaniment).** With the arranger on, the keys below the split point
  choose the chord, and drums, bass and harmony play by themselves. F11 starts and stops it,
  Shift+F11 adds an intro or an ending, F12 plays a fill-in and Shift+F12 switches between
  variations A and B. Ctrl+arrows change the style and the tempo.
- **13 styles:** Pop, Rock, Ballad, Disco, Reggae, Country, Bossa Nova, Samba, Baião, Arrocha, Swing,
  Blues Shuffle and Waltz.
- **Create your own styles by playing.** The style recorder records drums, bass, chords and
  pad one part at a time, for variations A and B and for the fill-in. You play in C and the
  arranger follows your chords afterwards.
- Styles are plain text files in Documents\Piano Virtual\Styles, so they can also be edited
  in Notepad and shared.
- **Song (track recording).** Record up to 6 tracks, each with its own instrument, plus the
  accompaniment. Ctrl+R records, Ctrl+P plays, Ctrl+Q quantizes a track. Songs are saved and
  opened as MIDI files.
- **English and Portuguese.** The program follows the language of Windows; it can be changed
  in the settings.
- A MIDI keyboard now gets the layer, the pedal modes, the arranger and track recording.

**Improved**

- The metronome is now exact: it is timed by the audio itself instead of a system timer.
- Notes from the keyboard, the MIDI input and the metronome no longer reach the synthesizer
  while it is producing sound, which could cause clicks and stuck notes.
- Notes no longer get stuck when the window loses focus.
- Closing the program while recording audio no longer leaves a damaged WAV file.
- The update check runs in the background: it no longer delays the start or shows an error when offline.
- Settings are always saved next to the program, and the settings screen shows the MIDI
  device in use.
- The layer follows the reverb, chorus and modulation of the main instrument.
- Moved to .NET 10. The download now includes the runtime.

### Português

**Novidades**

- **Arranjador (acompanhamento automático).** Com o arranjador ligado, as teclas abaixo do
  ponto de divisão escolhem o acorde, e bateria, baixo e harmonia tocam sozinhos. F11 liga e
  desliga, Shift+F11 acrescenta introdução ou finalização, F12 faz a virada e Shift+F12 troca
  entre as variações A e B. Ctrl+setas trocam o estilo e o andamento.
- **13 estilos:** Pop, Rock, Balada, Disco, Reggae, Country, Bossa Nova, Samba, Baião, Arrocha, Suingue,
  Blues Shuffle e Valsa.
- **Crie seus próprios estilos tocando.** O gravador de estilos grava bateria, baixo, acordes e
  pad, uma parte de cada vez, para as variações A e B e para a virada. Você toca em Dó e depois
  o arranjador segue os seus acordes.
- Os estilos são arquivos de texto em Documentos\Piano Virtual\Styles, então também dá para
  editar no Bloco de Notas e compartilhar.
- **Música (gravação por pistas).** Grave até 6 pistas, cada uma com seu instrumento, mais o
  acompanhamento. Ctrl+R grava, Ctrl+P toca, Ctrl+Q quantiza uma pista. As músicas são salvas e
  abertas como arquivos MIDI.
- **Inglês e português.** O programa segue o idioma do Windows; dá para trocar nas configurações.
- O teclado MIDI agora usa a camada, os modos de pedal, o arranjador e a gravação de pistas.

**Melhorias**

- O metrônomo agora é exato: é contado pelo próprio áudio, e não por um temporizador do sistema.
- As notas do teclado, da entrada MIDI e do metrônomo não chegam mais ao sintetizador enquanto
  ele está gerando som, o que podia causar estalos e notas presas.
- As notas não ficam mais presas quando a janela perde o foco.
- Fechar o programa durante uma gravação de áudio não deixa mais um arquivo WAV danificado.
- A verificação de atualização roda em segundo plano: não atrasa mais a abertura nem mostra erro sem internet.
- As configurações são sempre salvas junto do programa, e a tela de configurações mostra o
  dispositivo MIDI em uso.
- A camada acompanha o reverb, o chorus e a modulação do instrumento principal.
- Migrado para .NET 10. O download agora inclui o runtime.
