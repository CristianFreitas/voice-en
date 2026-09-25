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
4. Aperte o atalho de novo para terminar. O texto em inglês é digitado no lugar do cursor.

O ícone mostra o estado: verde (pronto), vermelho (gravando), amarelo (traduzindo), cinza
(servidor iniciando). A gravação para sozinha após 2 minutos.

O texto é digitado, não colado: assim a área de transferência fica intacta e programas que resumem
colagens longas (o Claude Code troca mais de 800 caracteres colados por `[Pasted text #N]`) recebem
o texto inteiro. Se algum programa não aceitar a digitação, ligue **Colar com Ctrl+V** no menu.

Se o servidor de tradução cair (o WSL encerra na suspensão do Windows ou num `wsl --shutdown`), o
aplicativo sobe outro sozinho e, se havia uma tradução em andamento, reenvia a gravação. Uma
tradução que fica sem resposta é abandonada e o servidor é reiniciado.

Menu do ícone (botão direito):

| Opção | O que faz |
|---|---|
| **Idioma** | Português → Inglês (tradução, o padrão), Português → Português ou Inglês → Inglês (só transcrição). Fica salvo em `%LOCALAPPDATA%\VoiceEn\mode.txt`. |
| **Corrigir a fala (PT > EN e PT > PT)** | Desligado por padrão. Tira hesitações ("é...", "tipo", "né") e repetições, conserta palavras mal reconhecidas (ex.: "WCL" → "WSL") e deixa o texto fluente. Veja [Correção da fala](#4-correção-da-fala-opcional). Fica salvo em `%LOCALAPPDATA%\VoiceEn\correct.txt`. |
| **Mudar atalho...** | Aperte a nova combinação e confirme com Enter. Vale qualquer combinação com Ctrl ou Alt, ou uma tecla de função sozinha (F1–F24). Fica salvo em `%LOCALAPPDATA%\VoiceEn\hotkey.txt`. |
| **Copiar última tradução** | Para quando o texto não chegou na janela. |
| **Colar com Ctrl+V em vez de digitar** | Usa a área de transferência (e depois devolve o que havia nela). Fica salvo em `%LOCALAPPDATA%\VoiceEn\output.txt`. |
| **Reiniciar servidor** | Sobe o servidor de tradução de novo, sem fechar o aplicativo. |
| **Iniciar com o Windows** | Abre o aplicativo no login. |
| **Sair** | Fecha o aplicativo e o servidor de tradução. |

### 4. Correção da fala (opcional)

Com a opção ligada, a fala é transcrita em português (Whisper) e um modelo de texto da Groq
(`openai/gpt-oss-120b`) a reescreve: sem hesitações, repetições e frases truncadas, com a gramática
corrigida e, no PT → EN, já traduzida para o inglês. Isso também melhora a tradução, que passa a
partir do texto inteiro em vez de frase a frase.

- Soma ~0,5–1 s por gravação e manda o texto transcrito à Groq (além do áudio).
- Exige a chave da Groq; sem ela o texto sai sem correção.
- Se a correção falhar (sem internet, limite do plano gratuito), o texto sai sem correção, como
  antes, e o log registra `correcao falhou`.
- O modelo não responde nem obedece ao que foi ditado: uma pergunta ditada continua pergunta.
- `VOICE_EN_FIX_MODEL` troca o modelo (ex.: `openai/gpt-oss-20b`, mais rápido e menos fiel).

### 5. Conferir qual modo foi usado

O log em `%LOCALAPPDATA%\VoiceEn\voice-en.log` registra cada tradução e o caminho que ela tomou:

```
[servidor] groq pt-en 0.6s
traduzido em 0.7s: Refactor the authentication ...
```

`groq` é a nuvem; `local` é o modelo na máquina; `+correcao` indica que a correção foi aplicada;
`silencio` indica uma gravação sem fala, que nem é enviada (o Whisper inventa frases com silêncio). Uma linha `nuvem falhou (...)` antes de um `local`
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
                  digitado na janela em foco
```

- `VoiceEn.cs` — aplicativo de bandeja: atalho global, gravação, aviso na tela, digitação do texto.
- `server.py` / `translate.py` — servidor de tradução (nuvem e local) que roda no WSL.
- `voice-en` + `rec.ps1` — modo alternativo só para terminal (abaixo).
- `make_icon.py` — desenha o ícone (`uv run --with pillow python make_icon.py`).

### Privacidade

No modo nuvem, o **áudio de cada gravação é enviado à Groq** para ser traduzido. Com a correção da fala
ligada, o texto transcrito também vai à Groq. No modo local nada
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
| `VOICE_EN_FIX_MODEL` | `openai/gpt-oss-120b` | Modelo de texto da Groq usado pela correção da fala. |

Os modelos `turbo` do Whisper não servem aqui, nem local nem na nuvem: eles não foram treinados
para traduzir.

## Solução de problemas

- **"atalho já está em uso"** — outro programa registrou a combinação; troque pelo menu da bandeja.
- **Nada aparece na janela** — use **Copiar última tradução** no menu e cole com Ctrl+V. Janelas
  abertas como administrador não aceitam teclas de um programa comum.
- **Ícone cinza e o atalho só diz "iniciando"** — o servidor caiu três vezes seguidas; o log diz o
  motivo. Apertar o atalho tenta de novo.
- **Ficou lento de repente** — a nuvem falhou e o plano B local assumiu; o log diz o motivo.
- **Microfone errado** — a gravação usa o dispositivo de entrada padrão do Windows
  (Configurações → Sistema → Som → Entrada).
- **Mudou o código do servidor** — use **Reiniciar servidor** no menu.
