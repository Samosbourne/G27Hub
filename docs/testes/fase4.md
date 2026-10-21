# Teste da Fase 4 — Interface ligada ao G27 + console

Abra com dois cliques em **`g27hub.cmd`**. Volante ligado na tomada e no USB.
Feche o `g27cli monitor` se estiver aberto (não precisa mais dele).

## 1. Conexão
- [ ] Com o volante ligado: em 1–2 s a tela vai de "Conecte seu volante G27" para o card do G27, e aparece a notificação "G27 conectado — Modo nativo, 900°".
- [ ] O card mostra "Modo nativo · 900° · Force feedback ativo · Modo Padrão".
- [ ] Tire o USB: volta para "Conecte seu volante" + aviso "G27 desconectado". Recoloque: volta sozinho.

## 2. Volante (clique no card)
- [ ] Girar o volante gira o desenho e o número no topo, e o risco branco anda no arco.
- [ ] Batente esquerdo ≈ −450°, direito ≈ +450°.
- [ ] LIMITES OPERACIONAIS em 120 → o volante físico trava em ~60° para cada lado; o arco azul encolhe. Volte para 900.
- [ ] CENTRALIZAÇÃO DA MOLA: 0, 50, 100 → a força de volta ao centro muda. Desmarcar a caixinha → volante solto.
- [ ] Fechar e abrir o app: os valores continuam os mesmos.

## 3. Pedais — **anote se está trocado**
- [ ] Pise só na **embreagem**: enche a barra EMBREAGEM? ____
- [ ] Pise só no **freio**: enche a barra FREIO? ____
- [ ] Pise só no **acelerador**: enche a barra ACELERADOR? ____

## 4. Câmbio — **corrigido em 21/10, confira de novo**
- [ ] Engate 1ª a 6ª e ré: o número grande e o círculo azul acompanham? Se não, vá na aba **Teste** e anote qual botão (1–23) acende em cada marcha: 1ª __ 2ª __ 3ª __ 4ª __ 5ª __ 6ª __ R __
- [ ] Os 4 vermelhos, as setinhas e os 4 pretos da base acendem (borda azul) no desenho, sem mexer na marcha. A base agora segue a foto do G27 (losango preto em cima, direcional no meio, vermelhos embaixo): confira se cada preto acende no lugar certo do losango.

## 5. Teste e calibração
- [ ] Os quatro quadros mexem ao vivo (volante, pedais, câmbio, botões 1–23 e direcional).
- [ ] PULSO DE FORÇA DE TESTE → o volante dá dois toquinhos.
- [ ] Anote o número que acende para: borboleta esquerda __, borboleta direita __, botões vermelhos do volante __ __ __ __ __ __, botões da base do câmbio __ __ __ __ __ __ __ __.
- [ ] Algum número fica aceso com tudo solto? (no Fase 1 apareceu o 16) ____

## 6. LEDs
- [ ] TESTAR LEDS → acendem um por um no volante e na tela, depois todos, depois apagam.

## 2a. Botões do volante (desenho novo, igual à foto)
- [ ] Aba Volante: aperte cada vermelho das laterais e cada borboleta; o mesmo botão acende (borda azul) no desenho?
- [ ] Se acender o errado: na aba Atribuições cada ponto mostra o número; anote qual número acende para esquerda cima/meio/baixo __ __ __, direita cima/meio/baixo __ __ __, borboleta esq. __ dir. __

## 7b. Direção livre
- [ ] Aba Força → DIREÇÃO LIVRE. Acelerador e freio nos pedais, volante vira o carro. Pista infinita com lombadas, buracos (puxam o volante para o lado), cones em zigue-zague, terra e zebras. Sair muito da pista = "muro" e volta. ESC sai.

## 7a. Teste de force feedback (minijogo, sem jogo aberto)
- [ ] Aba Força → INICIAR TESTE DE FORCE FEEDBACK (pista em 3D). Segure o volante: em ~27 s ele vira sozinho nas curvas (~90°), trepida forte na zebra, dá dois trancos nas lombadas, ondula e bate na grama, fica áspero na terra e dá uma pancada de 100% na batida.
- [ ] ESC ou PARAR interrompe na hora e a mola volta.
- [ ] Se a força parecer forte demais: INTENSIDADE GLOBAL mais baixa também vale para o teste.

## 7. Força (com um jogo aberto, ex.: AC/ACC)
- [ ] A aba Força mostra o gráfico mexendo, "FORÇA ATUAL" e o nome do jogo.
- [ ] Mexer em INTENSIDADE GLOBAL / MOLA / AMORTECIMENTO / INVERTER **com o jogo aberto** muda a força em ~1 s.
- [ ] FORÇA CENTRAL ligada → mola extra puxando para o centro durante o jogo.
- [ ] FORÇA MÍNIMA em 5–10% → detalhes pequenos perto do centro aparecem (sem o volante tremer parado).
- [ ] PERMITIR QUE O JOGO AJUSTE desligado → a intensidade do FFB nas opções do jogo deixa de fazer efeito.
- [ ] Ao fechar o jogo, a mola do volante volta ao valor da aba Volante (antes ficava em 40%).

## 8. Console (ícone de terminal ou Ctrl+Shift+L)
- [ ] Abre deslizando de baixo; mostra as linhas de conexão, modo nativo e rotação.
- [ ] Na primeira abertura roda os **testes rápidos** sozinho (o volante dá dois toquinhos e os LEDs piscam). Todos ✓?
- [ ] Filtros INFO/OK/AVISO/ERRO, origem (clique para trocar), busca, pausar, limpar, copiar, exportar .txt, DETALHADO.
- [ ] Com jogo aberto, aparecem linhas `FFB` ("g27ffb.dll carregada no ...").

## 9. Bandeja e Windows
- [ ] O X esconde na bandeja (perto do relógio); duplo clique no ícone volta. Menu: Abrir / Reaplicar / Sair.
- [ ] Engrenagem do topo → "Iniciar com o Windows". Reinicie o PC: o G27 fica em modo nativo sem abrir janela.

## Aceite
Girar o volante gira a imagem com o ângulo certo; pedais enchem as barras; o console mostra conexão, modo nativo e erros.
