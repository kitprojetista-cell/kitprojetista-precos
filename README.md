# Preços de referência · Calculadora do Projetista

Este repositório guarda só o arquivo `precos.json`, que o app **Calculadora do Projetista** baixa para mostrar
o preço de referência dos materiais por estado.

- **Fonte:** SINAPI (Caixa Econômica Federal), preços de insumos do mês.
- **Atualização:** mensal. O arquivo é gerado a partir da planilha oficial do SINAPI.
- O app só lê este arquivo. Nenhum dado de quem usa o app é enviado.

Os preços são uma referência média do estado para obras e podem ser diferentes do preço do depósito.

## Avisos automáticos (notificações do app)

O workflow `Avisos do app` (`.github/workflows/avisos.yml`) roda a cada 2 horas e quando o `precos.json` muda:

- **Vídeo novo** no canal @KitProjetista com `#KPC` no título ou na descrição → aviso no tópico `novidades`, com o link do vídeo.
- **Tabela de um mês novo** no `precos.json` → aviso no tópico `precos`.
- **No máximo 1 aviso automático por semana** (somando os dois). O que cair na mesma semana espera.

A chave da conta de serviço do Firebase fica no segredo `FIREBASE_CONTA_SERV` (nunca no código).
Para testar sem enviar: Actions › Avisos do app › Run workflow (com "Só simular" marcado).
O estado (vídeos já vistos, último aviso) fica em `avisos/estado.json`.

O robô usa o gerador em `sinapi/GeradorPrecos` (cópia do `tools/GeradorPrecos` do Kit). Mudou um código SINAPI? Atualize os dois.
