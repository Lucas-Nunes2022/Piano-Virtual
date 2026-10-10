# Changelog

Each version is described in English and then in Portuguese. The section of a version becomes
the text of its GitHub release.

## 1.6.1

### English

- **Arranger shortcuts under your hands.** While the arranger is playing, the three keys of the
  middle row that play no note control it, so a fill-in no longer takes a hand off the keys:
  F plays a fill-in, A switches between variations A and B (with a fill-in) and K finishes
  with the ending. They work with or without Shift, which may be down for a minor chord.
  F12, Shift+F12 and Shift+F11 still work.
- Asking for the ending twice no longer plays it twice in styles with two beats to the bar.

### Português

- **Atalhos do arranjador debaixo das mãos.** Com o arranjador tocando, as três teclas da
  fileira do meio que não tocam nota passam a comandá-lo, e a virada não tira mais a mão das
  teclas: F faz a virada, A troca entre as variações A e B (com virada) e K encerra com a
  finalização. Funcionam com ou sem Shift, que pode estar apertado por causa de um acorde
  menor. F12, Shift+F12 e Shift+F11 continuam funcionando.
- Pedir a finalização duas vezes não toca mais o final duas vezes em estilos de dois tempos
  por compasso.

## 1.6

### English

**New**

- **Ninth chords.** The arranger recognizes C9, Cm9, Cmaj9, Cadd9 and Cmadd9, in any key and
  inversion. In the ones with a seventh, the fifth may be left out.
- **Several instruments in the same part.** In the style recorder, every recording on bass,
  chords and pad now adds to the part instead of replacing it. Choose another instrument and
  record again: it joins the ones already there, so the chords can be an electric piano, a
  guitar and an organ. A style holds up to 6 instruments besides the drums (it was 3).
- **What you record is kept as you played it.** Bass, chords and pad keep their notes, octave,
  lengths and repeated hits; only the timing is adjusted. Ctrl+Left and Ctrl+Right set up to
  which chord each part goes up before it goes down instead.
- **Sevenths and ninths in recorded styles.** When you play a chord with a seventh or a ninth,
  chords and pad recorded without them get them from the arranger. Ctrl+N turns this off for
  a part.
- **Quantize in the style recorder.** Ctrl+Q chooses the figure the recordings are adjusted to,
  from quarter notes to thirty-second notes, with triplets. It can change between recordings:
  a straight kick under a triplet hi-hat. Thirty-second notes are also new in the song quantize.
- The Arrocha style is new.

**Improved**

- The fill-in is recorded as a single bar, and only the fill-in plays while you record it.
- The closed hi-hat now cuts the open one, and the same goes for the triangle, the whistle,
  the guiro and the cuica, even when the sound font does not define it.
- Reverb now reaches soft notes too. Before, notes played softly got no reverb at all.
- The sound is produced at the sample rate of the sound card, with no conversion by Windows,
  which should reduce crackling on some sound cards.
- In the help, arrow keys are written as words, which screen readers read properly.

**Fixed**

- The end of the reverb of a song could be heard at the start of the next playback.

### Português

**Novidades**

- **Acordes com nona.** O arranjador reconhece C9, Cm9, Cmaj9, Cadd9 e Cmadd9, em qualquer tom
  e inversão. Nos que têm sétima, a quinta pode ficar de fora.
- **Vários instrumentos na mesma parte.** No gravador de estilos, cada gravação no baixo, nos
  acordes e no pad agora soma em vez de substituir. Escolha outro instrumento e grave de novo:
  ele entra junto com os que já estão lá, então os acordes podem ser um piano elétrico, uma
  guitarra e um órgão. O estilo comporta até 6 instrumentos além da bateria (eram 3).
- **O que você grava fica como foi tocado.** Baixo, acordes e pad guardam as notas, a oitava,
  as durações e as repetições; só o tempo é ajustado. Ctrl+Seta Esquerda e Ctrl+Seta Direita
  definem até que acorde cada parte sobe antes de passar a descer.
- **Sétima e nona nos estilos gravados.** Quando você faz um acorde com sétima ou com nona, os
  acordes e o pad gravados sem elas ganham essas notas do arranjador. Ctrl+N desliga isso
  numa parte.
- **Quantização no gravador de estilos.** Ctrl+Q escolhe a figura para a qual as gravações são
  ajustadas, de semínimas a fusas, com tercinas. Dá para trocar entre uma gravação e outra:
  bumbo reto com chimbal em tercinas. As fusas também são novidade no quantizar da música.
- O estilo Arrocha é novo.

**Melhorias**

- A virada é gravada com um compasso só, e só ela toca enquanto você grava.
- O chimbal fechado agora corta o aberto, e o mesmo vale para o triângulo, o apito, o
  reco-reco e a cuíca, mesmo quando o arquivo de sons não define isso.
- O reverb agora alcança também as notas fracas. Antes, notas tocadas com pouca força ficavam
  sem reverb nenhum.
- O som é gerado na taxa da própria placa de som, sem conversão pelo Windows, o que deve
  reduzir os picotes em algumas placas.
- Na ajuda, as setas são escritas por extenso, e os leitores de tela leem direito.

**Correções**

- O fim do reverb de uma música podia ser ouvido no começo da reprodução seguinte.

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
