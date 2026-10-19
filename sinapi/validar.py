"""
Confere o precos.json novo (gerado pelo robô a partir do SINAPI) antes de publicar.
Uso: validar.py <precos.json atual> <precos.json novo> <mês esperado AAAA-MM>
Sai com código 0 se pode publicar; senão mostra o motivo e sai com 1 (nada é publicado).
"""
import json, statistics, sys

atual, novo, esperado = json.load(open(sys.argv[1], encoding="utf-8")), json.load(open(sys.argv[2], encoding="utf-8")), sys.argv[3]
erros = []
if novo.get("mes") != esperado:
    erros.append(f"mês da planilha é {novo.get('mes')}, esperado {esperado}")
if set(novo.get("itens", {})) != set(atual.get("itens", {})):
    faltam = set(atual["itens"]) - set(novo.get("itens", {}))
    erros.append(f"lista de materiais mudou (faltando: {sorted(faltam)[:10]})")
ufs = novo.get("precos", {})
if len(ufs) != 27:
    erros.append(f"{len(ufs)} estados (esperado 27)")
razoes = []
for uf, tab in ufs.items():
    for k, v in tab.items():
        if not isinstance(v, (int, float)) or v <= 0:
            erros.append(f"{uf} {k}: preço inválido {v}")
            continue
        velho = atual.get("precos", {}).get(uf, {}).get(k)
        if velho:
            r = v / velho
            razoes.append(r)
            if r < 0.4 or r > 2.5:
                erros.append(f"{uf} {k}: {velho} -> {v} (mudança grande demais)")
    faltam = set(atual.get("precos", {}).get(uf, {})) - set(tab)
    if faltam:
        erros.append(f"{uf}: sumiram {sorted(faltam)[:5]}")
if razoes:
    med = statistics.median(razoes)
    print(f"Variação mediana dos preços: {(med - 1) * 100:+.1f}% ({len(razoes)} preços comparados)")
    if med < 0.8 or med > 1.25:
        erros.append(f"variação mediana fora do normal ({(med - 1) * 100:+.1f}%)")
if erros:
    print("NÃO PUBLICADO. Problemas encontrados:")
    for e in erros[:40]:
        print(" -", e)
    sys.exit(1)
print(f"OK: tabela {esperado} conferida.")
