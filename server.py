"""Servidor de traducao para o VoiceEn.exe: mantem o modelo em memoria.

Protocolo (linhas em stdin/stdout): imprime READY quando o modelo carregou; para cada
caminho de wav recebido responde "OK\t<texto>" ou "ERR\t<mensagem>". Sai no EOF do stdin.
"""
import sys

from translate import load_model, translate


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    model = load_model()
    print("READY", flush=True)
    for line in sys.stdin:
        wav = line.strip()
        if not wav:
            continue
        try:
            text = translate(model, wav)
            print("OK\t" + text.replace("\n", " "), flush=True)
        except Exception as err:
            print("ERR\t" + str(err).replace("\n", " "), flush=True)


if __name__ == "__main__":
    main()
