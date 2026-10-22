# Teste da Fase 5b — Atribuições

Já validado pelo Claude Code: 38 testes automáticos do motor (tecla segurada, combinação na ordem certa, camada shift,
direcional, pedal com folga, câmbio sequencial ignorando ponto morto, macro, mídia, troca de perfil, atalho de rotação)
e o envio de tecla pelo Windows (aceito, com scan code — o jeito que os jogos leem).

**Importante:** com a janela do G27 Hub em foco, as atribuições ficam pausadas (para o volante não digitar dentro do app).
Teste com o **Bloco de Notas** na frente.

## 1. Tecla e combinação
- [ ] Aba Atribuições → aperte um botão do volante (ele fica selecionado) → TECLA OU COMBINAÇÃO → clique no campo e aperte `A`.
- [ ] Abra o Bloco de Notas, segure o botão: sai `aaaa…` enquanto segura (repetição normal do teclado).
- [ ] Troque para `Ctrl+Shift+...` ou outra combinação e confira.

## 2. Macro
- [ ] MACRO → GRAVAR → digite `ola` → PARAR (com o mouse). No Bloco de Notas, o botão digita `ola`.
- [ ] REPETE ENQUANTO SEGURA e LIGA/DESLIGA funcionam como o nome diz.

## 3. Mídia, programa, site, perfil
- [ ] MÍDIA → VOLUME+ / PLAY/PAUSA (com música tocando).
- [ ] ABRIR PROGRAMA → escolha a calculadora (`C:\Windows\System32\calc.exe`).
- [ ] ABRIR SITE → `youtube.com`.
- [ ] TROCAR PARA O PERFIL → outro perfil: o topo muda.

## 4. Camada shift
- [ ] Num botão (ex.: borboleta esquerda) marque **ESTE BOTÃO É O SHIFT**.
- [ ] CAMADA SHIFT → escolha outra ação para um botão. Segurando o shift, o botão faz a ação da camada 2; solto, a normal.

## 5. Desligar no jogo
- [ ] No perfil de um jogo, desmarque **ATRIBUIÇÕES LIGADAS NESTE PERFIL**: nenhum botão manda tecla com esse perfil ativo.

## 6. Pedal como tecla (aba Pedais)
- [ ] EMBREAGEM COMO TECLA, 60%, tecla `Espaço`: no Bloco de Notas, pisar passando de 60% digita espaço; soltar para.

## 7. Câmbio sequencial (aba Câmbio)
- [ ] LIGADO, teclas `E`/`Q`: no Bloco de Notas, 1ª → 3ª digita `EE`; 3ª → ré digita `QQQQ`; ponto morto não digita nada.

## 8. Atalho de rotação (aba Volante)
- [ ] Segure os dois vermelhos de **cima** do volante (7 e 8): a rotação passa 180 → 270 → 540 → 900, com aviso na tela e LEDs acendendo 1, 2, 3, 4. Funciona até com o G27 Hub em foco; uma troca por aperto (intervalo mínimo de 0,6 s).

## Aceite
Botões do volante e do câmbio, direcional, pedais e marchas mandando teclas/macros/ações por perfil, com camada shift e desligáveis por jogo.
