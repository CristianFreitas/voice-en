# voice-en

Fale em português, receba o texto em inglês — em qualquer programa do Windows.

Um atalho global grava o microfone, o [Whisper](https://github.com/SYSTRAN/faster-whisper) traduz
localmente (tarefa `translate`, PT → EN) e o texto é colado onde o cursor estiver: terminal, editor,
navegador, chat. Nada de áudio sai da máquina e não há custo por uso.

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

### Modo terminal (opcional)

`voice-en` sem argumentos grava, imprime a tradução e copia para a área de transferência.
Usado como `EDITOR` (`voice-en <arquivo>`), acrescenta a tradução ao arquivo — no Claude Code isso
transforma o Ctrl+G em "ditar para o prompt".

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
- **Nada é colado** — o texto traduzido continua na área de transferência; cole com Ctrl+V.
- **Microfone errado** — a gravação usa o dispositivo de entrada padrão do Windows
  (Configurações → Sistema → Som → Entrada).
