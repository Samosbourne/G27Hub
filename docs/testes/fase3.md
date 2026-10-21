# Fase 3 — Protótipo da interface (aprovação do John)

O protótipo é **só visual**: as telas não leem o volante ainda (isso é a Fase 4). Os números (−35°, pedais em 0/12/80%, marcha 3…) são exemplos.

## Como abrir
Dê dois cliques em **`g27hub.cmd`** na pasta do projeto (ou rode `.\g27hub` no PowerShell). A primeira vez demora alguns segundos para compilar.

## Como navegar no protótipo
| Ação | O que acontece |
|---|---|
| Clicar no meio da tela "Conecte seu volante G27" | simula o G27 sendo conectado → lista de dispositivos |
| Clicar no card do G27 | abre a página do volante + mostra uma notificação |
| Ícones da barra lateral | Atribuições, Volante, Pedais, Força, Câmbio, LEDs, Teste |
| Mexer no slider "LIMITES OPERACIONAIS" | o arco azul encolhe/cresce |
| Clicar nos números sobre o volante (Atribuições) | escolhe o botão |
| Ícone de terminal (rodapé da barra lateral) ou **Ctrl+Shift+L** | abre/fecha o console |
| **F2** | mostra as notificações de exemplo, uma de cada vez |
| "DESKTOP: Padrão" no topo | lista de perfis |
| ← no topo | volta para a lista de dispositivos |

## O que avaliar
Compare com os prints do G HUB (os seus em `docs\referencia-ui\` e o que você lembra do G29). Para cada tela, anote **ok** ou o que mudar (cor, tamanho, posição, texto, falta algo):

1. [ ] Tela inicial sem volante
2. [ ] Lista de dispositivos (card do G27)
3. [ ] Volante: arco de riscos, ângulo no topo, desenho do G27
4. [ ] Pedais: medidores e desenho dos pedais
5. [ ] Força: opções do LGS + gráfico
6. [ ] Câmbio: grade H e botões da base
7. [ ] LEDs: barra de LEDs e limites
8. [ ] Atribuições: pontos clicáveis e lista
9. [ ] Teste e calibração: quatro quadros ao vivo
10. [ ] Console de diagnóstico
11. [ ] Notificações (F2)
12. [ ] Geral: cor azul, fonte, tamanho dos textos, janela sem borda

Perguntas que ajudam:
- O azul está parecido com o do G HUB? Mais claro/escuro?
- O desenho do G27 está bom ou prefere mais realista/mais simples?
- Algum texto ficou estranho ou longo demais?

## Aceite
O John aprova o visual (com ou sem uma lista de ajustes). Os ajustes entram antes ou durante a Fase 4.
