<img src="icon.png" width="96" align="right" alt="icone do VoiceEn">

# voice-en

Fale em português, receba o texto em inglês — em qualquer programa do Windows.

Um atalho global grava o microfone, o Whisper traduz a fala (PT → EN) e o texto é colado onde o
cursor estiver: terminal, editor, navegador, chat.

| Modo | Tempo por frase | RAM | Para onde vai o áudio | Custo |
|---|---|---|---|---|
| **Nuvem** (Groq, `whisper-large-v3`) | ~0,6 s | ~20 MB | servidores da Groq | plano gratuito |
| **Local** ([faster-whisper](https://github.com/SYSTRAN/faster-whisper) `medium`, CPU) | ~8–12 s | ~2 GB | não sai da máquina | nenhum |

Tempos medidos com uma frase de 18 segundos. O modo é escolhido pela presença de uma chave da Groq;
com a chave, o local vira plano B automático quando a nuvem falha.

## Guia rápido

### 1. Instalar

Requisitos: Windows 10/11 com WSL2 (testado com Ubuntu 24.04). Nada precisa ser instalado do lado
do Windows — o aplicativo é compilado com o `csc.exe` que já vem nele.

Dentro do WSL:

```bash
git clone git@github.com:CristianFreitas/voice-en.git ~/.local/share/voice-en
cd ~/.local/share/voice-en
./install.sh              # completo: baixa também o modelo local (~1,5 GB)
./install.sh --no-model   # só nuvem: instalação leve, sem baixar o modelo
```

O script cria o ambiente Python e compila o `VoiceEn.exe` em `%LOCALAPPDATA%\VoiceEn\`.

### 2. Ligar o modo nuvem (recomendado)

Crie uma chave gratuita em [console.groq.com/keys](https://console.groq.com/keys) e salve-a:

```bash
mkdir -p ~/.config/voice-en && chmod 700 ~/.config/voice-en
nano ~/.config/voice-en/groq-key      # cole a chave e salve
chmod 600 ~/.config/voice-en/groq-key
```

A chave é relida a cada gravação: trocar (rotacionar) ou apagar o arquivo vale na hora, sem
reiniciar o aplicativo. Ela fica fora do repositório; nunca a coloque em arquivos versionados.

### 3. Usar

1. Abra o `VoiceEn.exe`. Um círculo verde aparece na bandeja, perto do relógio.
2. Clique no campo onde quer o texto.
3. Aperte **Ctrl+Alt+Espaço** e fale em português.
4. Aperte o atalho de novo para terminar. O texto em inglês é colado no lugar do cursor.

O ícone mostra o estado: verde (pronto), vermelho (gravando), amarelo (traduzindo). A gravação para
sozinha após 2 minutos. Depois de colar, o aplicativo devolve à área de transferência o que você
tinha copiado antes.

Menu do ícone (botão direito):

| Opção | O que faz |
|---|---|
| **Idioma** | Português → Inglês (tradução, o padrão), Português → Português ou Inglês → Inglês (só transcrição). Fica salvo em `%LOCALAPPDATA%\VoiceEn\mode.txt`. |
| **Mudar atalho...** | Aperte a nova combinação e confirme com Enter. Vale qualquer combinação com Ctrl ou Alt, ou uma tecla de função sozinha (F1–F24). Fica salvo em `%LOCALAPPDATA%\VoiceEn\hotkey.txt`. |
| **Copiar última tradução** | Para quando a colagem não pegou na janela. |
| **Iniciar com o Windows** | Abre o aplicativo no login. |
| **Sair** | Fecha o aplicativo e o servidor de tradução. |

### 4. Conferir qual modo foi usado

O log em `%LOCALAPPDATA%\VoiceEn\voice-en.log` registra cada tradução e o caminho que ela tomou:

```
[servidor] groq pt-en 0.6s
traduzido em 0.7s: Refactor the authentication ...
```

`groq` é a nuvem; `local` é o modelo na máquina. Uma linha `nuvem falhou (...)` antes de um `local`
mostra o motivo do plano B (sem internet, chave inválida, limite do plano gratuito).

## Como funciona

```
atalho global ──> VoiceEn.exe (bandeja do Windows)
                    │  grava o microfone padrão (MCI, 16 kHz mono)
                    ▼
                  server.py (WSL2)
                    ├─ com chave: envia o áudio à Groq (audio/translations)
                    └─ sem chave ou em falha: Whisper local, modelo mantido em memória
                    │  texto em inglês
                    ▼
                  Ctrl+V na janela em foco
```

- `VoiceEn.cs` — aplicativo de bandeja: atalho global, gravação, aviso na tela, colagem.
- `server.py` / `translate.py` — servidor de tradução (nuvem e local) que roda no WSL.
- `voice-en` + `rec.ps1` — modo alternativo só para terminal (abaixo).
- `make_icon.py` — desenha o ícone (`uv run --with pillow python make_icon.py`).

### Privacidade

No modo nuvem, o **áudio de cada gravação é enviado à Groq** para ser traduzido. No modo local nada
sai da máquina. Para voltar ao local, apague `~/.config/voice-en/groq-key`.

### Modo terminal (opcional)

`voice-en` sem argumentos grava, imprime a tradução e copia para a área de transferência.
Usado como `EDITOR` (`voice-en <arquivo>`), acrescenta a tradução ao arquivo — no Claude Code isso
transforma o Ctrl+G em "ditar para o prompt".

## Ajustes do modo local

Variáveis de ambiente lidas por `translate.py`:

| Variável | Padrão | Descrição |
|---|---|---|
| `VOICE_EN_MODEL` | `medium` | Modelo do Whisper. `small` é ~2x mais rápido e erra mais. |
| `VOICE_EN_DEVICE` | `cpu` | `cuda` exige as bibliotecas cuBLAS/cuDNN e uma GPU livre. |
| `VOICE_EN_GROQ_KEY` | — | Alternativa ao arquivo `groq-key`. |

Os modelos `turbo` do Whisper não servem aqui, nem local nem na nuvem: eles não foram treinados
para traduzir.

## Solução de problemas

- **"atalho já está em uso"** — outro programa registrou a combinação; troque pelo menu da bandeja.
- **Nada é colado** — use **Copiar última tradução** no menu e cole com Ctrl+V.
- **Ficou lento de repente** — a nuvem falhou e o plano B local assumiu; o log diz o motivo.
- **Microfone errado** — a gravação usa o dispositivo de entrada padrão do Windows
  (Configurações → Sistema → Som → Entrada).
- **Mudou o código do servidor** — feche o aplicativo (**Sair**) e abra de novo; o `server.py` só é
  recarregado quando o aplicativo inicia.
