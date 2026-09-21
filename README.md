<img src="icon.png" width="96" align="right" alt="icone do VoiceEn">

# voice-en

Fale em português, receba o texto em inglês — em qualquer programa do Windows.

Um atalho global grava o microfone, o [Whisper](https://github.com/SYSTRAN/faster-whisper) traduz
localmente (tarefa `translate`, PT → EN) e o texto é colado onde o cursor estiver: terminal, editor,
navegador, chat. No modo padrão nada de áudio sai da máquina e não há custo por uso.

## Como funciona

```
Ctrl+Alt+Espaço ──> VoiceEn.exe (bandeja do Windows)
                      │  grava o microfone (MCI, 16 kHz mono)
                      ▼
                    server.py (WSL2) ── Whisper "medium", modelo sempre em memória
                      │  texto em inglês
                      ▼
                    Ctrl+V na janela em foco
```

- `VoiceEn.cs` — aplicativo de bandeja: atalho global, gravação, aviso na tela, colagem. É compilado
  com o `csc.exe` que já vem no Windows, então não precisa instalar nada do lado do Windows.
- `server.py` / `translate.py` — servidor de tradução que roda no WSL e mantém o modelo carregado.
- `voice-en` + `rec.ps1` — modo alternativo só para terminal (veja abaixo).

## Requisitos

- Windows 10/11 com WSL2 (testado com Ubuntu 24.04)
- ~2 GB de disco para o modelo e ~2 GB de RAM enquanto o aplicativo está aberto
- Internet apenas na instalação (download do modelo)

## Instalação

Dentro do WSL:

```bash
git clone <url-deste-repo> ~/.local/share/voice-en
cd ~/.local/share/voice-en
./install.sh
```

O script cria o ambiente Python, baixa o modelo e compila o `VoiceEn.exe` em
`%LOCALAPPDATA%\VoiceEn\`. Depois é só abrir o `VoiceEn.exe`.

## Uso

1. Clique no campo onde quer o texto.
2. Aperte **Ctrl+Alt+Espaço** e fale em português.
3. Aperte o atalho de novo para terminar. Em alguns segundos o texto em inglês é colado.

O ícone na bandeja mostra o estado: verde (pronto), vermelho (gravando), amarelo (traduzindo).
A gravação para sozinha após 2 minutos.

### Trocar o atalho

Botão direito no ícone da bandeja → **Mudar atalho...** → aperte a combinação desejada → Enter.
Vale qualquer combinação com Ctrl ou Alt, ou uma tecla de função sozinha (F1–F24). A escolha fica
salva em `%LOCALAPPDATA%\VoiceEn\hotkey.txt`.

### Abrir junto com o Windows

Botão direito no ícone da bandeja → **Iniciar com o Windows**.

### Modo terminal (opcional)

`voice-en` sem argumentos grava, imprime a tradução e copia para a área de transferência.
Usado como `EDITOR` (`voice-en <arquivo>`), acrescenta a tradução ao arquivo — no Claude Code isso
transforma o Ctrl+G em "ditar para o prompt".

## Modo nuvem (opcional, bem mais rápido)

Por padrão tudo roda local: nenhum áudio sai da máquina, ao custo de ~8 s por frase na CPU e ~2 GB
de RAM. Com uma chave gratuita da [Groq](https://console.groq.com/keys) a tradução passa a ser feita
na nuvem pelo `whisper-large-v3` (modelo maior que o local), em cerca de 1 s, e o aplicativo deixa
de carregar o modelo na memória.

```bash
mkdir -p ~/.config/voice-en
nano ~/.config/voice-en/groq-key      # cole a chave e salve
chmod 600 ~/.config/voice-en/groq-key
```

Vale a partir da próxima gravação, sem reiniciar. Se a nuvem falhar (sem internet, limite
excedido), a tradução cai sozinha para o modelo local. Para voltar ao modo 100% local, apague o
arquivo. No modo nuvem o **áudio gravado é enviado à Groq**; o plano gratuito tem limite de
requisições por dia, folgado para ditado.

## Ajustes

Variáveis de ambiente lidas por `translate.py`:

| Variável | Padrão | Descrição |
|---|---|---|
| `VOICE_EN_MODEL` | `medium` | Modelo do Whisper. `small` é ~2x mais rápido e erra mais. |
| `VOICE_EN_LANG` | `pt` | Idioma falado. |
| `VOICE_EN_DEVICE` | `cpu` | `cuda` exige as bibliotecas cuBLAS/cuDNN e uma GPU livre. |

Os modelos `turbo` do Whisper não servem aqui: eles não foram treinados para traduzir.

## Solução de problemas

O log fica em `%LOCALAPPDATA%\VoiceEn\voice-en.log`.

- **"atalho já está em uso"** — outro programa registrou a combinação; troque pelo menu da bandeja.
- **Nada é colado** — botão direito no ícone → **Copiar última tradução** e cole com Ctrl+V.
  (Depois de colar, o aplicativo devolve à área de transferência o que você tinha copiado antes.)
- **Microfone errado** — a gravação usa o dispositivo de entrada padrão do Windows
  (Configurações → Sistema → Som → Entrada).
