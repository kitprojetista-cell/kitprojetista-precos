"""
Avisos automáticos da Calculadora do Projetista (Firebase Cloud Messaging, por tópico).

  A) Vídeo novo no canal @KitProjetista com #KPC no título ou na descrição  -> tópico "novidades"
  B) Tabela de preços (precos.json) de um mês novo                          -> tópico "precos"

Regras:
  - No máximo 1 aviso automático a cada 7 dias, somando A e B (o app promete isso a quem liga os avisos).
    O que cair dentro da semana espera a próxima rodada livre.
  - Vídeo com mais de 14 dias que não foi avisado é deixado de lado (aviso velho não serve).
  - Primeira rodada: só marca os vídeos que já existem como vistos (não avisa o passado).
  - A chave da conta de serviço vem do segredo FIREBASE_CONTA_SERVICO (nunca fica no código).
  - --simular: mostra o que mandaria, sem enviar e sem mudar o estado.

O estado (vídeos vistos, último aviso, mês já avisado) fica em avisos/estado.json, gravado de volta pelo workflow.
"""
import argparse, datetime as dt, json, os, re, sys, urllib.request
import xml.etree.ElementTree as ET

AQUI = os.path.dirname(os.path.abspath(__file__))
ESTADO = os.path.join(AQUI, "estado.json")
PRECOS = os.path.join(AQUI, "..", "precos.json")
CANAL = "UCIMbCVULnEmC2F117Rp943Q"  # @KitProjetista
FEED = f"https://www.youtube.com/feeds/videos.xml?channel_id={CANAL}"
PROJETO = "kit-projetista"
CHAVE_VIDEO = "#kpc"
INTERVALO = dt.timedelta(days=7)
VIDEO_VELHO = dt.timedelta(days=14)
MESES = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"]
NS = {"a": "http://www.w3.org/2005/Atom", "yt": "http://www.youtube.com/xml/schemas/2015", "media": "http://search.yahoo.com/mrss/"}


def agora():
    return dt.datetime.now(dt.timezone.utc)


def ler_estado():
    try:
        with open(ESTADO, encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return {"iniciado": False, "videos_vistos": [], "ultimo_aviso": None, "mes_precos_avisado": None, "historico": []}


def gravar_estado(e):
    e["videos_vistos"] = e["videos_vistos"][-200:]
    e["historico"] = e["historico"][-50:]
    with open(ESTADO, "w", encoding="utf-8") as f:
        json.dump(e, f, ensure_ascii=False, indent=2)
        f.write("\n")


def videos():
    with urllib.request.urlopen(FEED, timeout=30) as r:
        raiz = ET.fromstring(r.read())
    lista = []
    for e in raiz.findall("a:entry", NS):
        vid = e.find("yt:videoId", NS).text
        titulo = e.find("a:title", NS).text or ""
        desc = e.find("media:group/media:description", NS)
        desc = (desc.text if desc is not None else "") or ""
        pub = dt.datetime.fromisoformat(e.find("a:published", NS).text)
        lista.append({"id": vid, "titulo": titulo, "descricao": desc, "publicado": pub})
    return sorted(lista, key=lambda v: v["publicado"])


def titulo_limpo(t):
    """Título do vídeo sem as hashtags (no aviso elas só atrapalham)."""
    t = re.sub(r"\s*#\S+", "", t).strip()
    return t[:110] if t else "Vídeo novo"


def mes_por_extenso(mes):  # "2026-10" -> "outubro de 2026"
    a, m = mes.split("-")
    return f"{MESES[int(m) - 1]} de {a}"


def token_acesso():
    from google.oauth2 import service_account
    from google.auth.transport.requests import Request
    bruto = os.environ.get("FIREBASE_CONTA_SERVICO", "").strip()
    if not bruto:
        raise RuntimeError("o segredo FIREBASE_CONTA_SERV não está configurado neste repositório (Settings › Secrets and variables › Actions)")
    try:
        info = json.loads(bruto)
    except json.JSONDecodeError:
        raise RuntimeError("o segredo FIREBASE_CONTA_SERV não é um JSON válido: cole o conteúdo inteiro do arquivo .json, do { ao }")
    cred = service_account.Credentials.from_service_account_info(info, scopes=["https://www.googleapis.com/auth/firebase.messaging"])
    cred.refresh(Request())
    return cred.token


def enviar(topico, titulo, texto, link=None, imagem=None):
    msg = {
        "topic": topico,
        "notification": {"title": titulo, "body": texto},
        "android": {"notification": {"channel_id": "avisos", "icon": "notificacao", "color": "#2B6C91"}},
        "data": {"titulo": titulo, "texto": texto},
    }
    if link:
        msg["data"]["link"] = link
    if imagem:
        msg["notification"]["image"] = imagem
    req = urllib.request.Request(
        f"https://fcm.googleapis.com/v1/projects/{PROJETO}/messages:send",
        data=json.dumps({"message": msg}).encode(),
        headers={"Authorization": "Bearer " + token_acesso(), "Content-Type": "application/json; charset=utf-8"},
    )
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read()).get("name", "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--simular", action="store_true", help="mostra o que mandaria, sem enviar")
    ap.add_argument("--testar-envio", action="store_true", help="manda um aviso de teste (não conta no limite semanal)")
    args = ap.parse_args()
    if args.testar_envio:
        print("Enviado:", enviar("novidades", "Teste da automação ✅", "Os avisos automáticos pelo GitHub estão funcionando."))
        return
    e = ler_estado()
    t = agora()

    # ── candidatos ──
    vids = videos()
    if not e["iniciado"]:
        e["videos_vistos"] = [v["id"] for v in vids]
        e["iniciado"] = True
        with open(PRECOS, encoding="utf-8") as f:
            e["mes_precos_avisado"] = json.load(f).get("mes")
        print(f"Primeira rodada: {len(vids)} vídeos e a tabela {e['mes_precos_avisado']} marcados como já vistos (nada é avisado).")
        if not args.simular:
            gravar_estado(e)
        return

    candidatos = []
    for v in vids:
        if v["id"] in e["videos_vistos"]:
            continue
        marcado = CHAVE_VIDEO in (v["titulo"] + " " + v["descricao"]).lower()
        if not marcado:
            e["videos_vistos"].append(v["id"])  # sem #KPC: nunca avisa
            print(f"Vídeo sem {CHAVE_VIDEO.upper()}, ignorado: {v['titulo']}")
            continue
        if t - v["publicado"] > VIDEO_VELHO:
            e["videos_vistos"].append(v["id"])
            print(f"Vídeo com mais de 14 dias, deixado de lado: {v['titulo']}")
            continue
        candidatos.append(("video", v))

    with open(PRECOS, encoding="utf-8") as f:
        mes = json.load(f).get("mes")
    if mes and mes != e.get("mes_precos_avisado"):
        candidatos.append(("precos", mes))

    if not candidatos:
        print("Nada novo para avisar.")
        if not args.simular:
            gravar_estado(e)
        return

    # ── limite de 1 por semana ──
    if e.get("ultimo_aviso"):
        ultimo = dt.datetime.fromisoformat(e["ultimo_aviso"])
        if t - ultimo < INTERVALO:
            livre = ultimo + INTERVALO
            print(f"{len(candidatos)} aviso(s) esperando: o último foi em {ultimo:%d/%m %H:%M} UTC; o próximo pode sair a partir de {livre:%d/%m %H:%M} UTC.")
            if not args.simular:
                gravar_estado(e)
            return

    # Tabela de preços tem prioridade (vale para todo mundo); depois o vídeo mais antigo da fila.
    candidatos.sort(key=lambda c: (c[0] != "precos", c[1]["publicado"] if c[0] == "video" else t))
    tipo, item = candidatos[0]
    if tipo == "precos":
        topico, titulo = "precos", "Tabela de preços atualizada 📊"
        texto = f"Os preços de referência (SINAPI) de {mes_por_extenso(item)} já estão no app. Abra para ver os valores novos."
        link = imagem = None
    else:
        topico, titulo = "novidades", "Vídeo novo do Kit Projetista 🎬"
        texto = titulo_limpo(item["titulo"])
        link = f"https://www.youtube.com/watch?v={item['id']}"
        imagem = f"https://i.ytimg.com/vi/{item['id']}/hqdefault.jpg"

    print(f"Aviso [{topico}]: {titulo} | {texto}" + (f" | {link}" if link else ""))
    if args.simular:
        print("(simulação: nada foi enviado)")
        return
    nome = enviar(topico, titulo, texto, link, imagem)
    print("Enviado:", nome)
    if tipo == "precos":
        e["mes_precos_avisado"] = item
    else:
        e["videos_vistos"].append(item["id"])
    e["ultimo_aviso"] = t.isoformat(timespec="seconds")
    e["historico"].append({"data": e["ultimo_aviso"], "topico": topico, "texto": texto, "link": link})
    gravar_estado(e)


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # falha clara no log do Actions, sem mostrar a chave
        print("Erro:", type(ex).__name__, str(ex)[:300], file=sys.stderr)
        sys.exit(1)
