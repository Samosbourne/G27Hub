# Prompt para o Gemini: repaginar a interface do G27 Hub

Como usar: abra o Gemini, anexe as 8 imagens da pasta `docs/design/telas-atuais/` e cole o texto abaixo (da linha
"Você é um designer" até o fim). Quando ele responder, me mande tudo: o texto, as imagens que ele gerar e o código HTML.

---

Você é um designer de interface de produtos para PC gamer. Vou te mandar 8 capturas de tela de um aplicativo para
Windows chamado **G27 Hub**. Ele configura o volante Logitech G27: força (force feedback), pedais, câmbio, luzes de
RPM, atribuições de botões e perfis por jogo. O visual atual imita o Logitech G HUB: fundo preto, painéis cinza,
destaque azul `#1A9BFF`, barra de ícones à esquerda, painel de ajustes no meio-esquerda e um desenho grande e animado
do volante à direita.

Quero uma **repaginada**, não uma interface nova. Mantenha a essência e a organização (que funcionam e o usuário já
conhece), mas deixe o app com **identidade própria**, mais moderno e mais caprichado.

## Regras
1. **Manter**: tema escuro; barra lateral de ícones; painel de ajustes à esquerda + visual animado grande à direita;
   cabeçalho com voltar, título, seletor de perfil, globo de idioma, conta e engrenagem; console de diagnóstico que
   sobe de baixo; notificações no canto inferior direito.
2. **Não copiar** a Logitech nem o G HUB: nada de logotipos, nomes ou a cor azul exata deles. Proponha uma cor de
   destaque própria (pode ser azul, mas diferente) e diga o hexadecimal.
3. **Implementável em WPF (.NET)**, sem bibliotecas extras: cores sólidas e gradientes simples, cantos arredondados,
   sombras leves, ícones Segoe MDL2/Fluent, fonte Segoe UI (ou diga uma alternativa gratuita). Nada que dependa de
   efeitos de navegador.
4. Tamanho de referência da janela: **1280 × 760**.
5. Textos em português (o app também tem inglês; prever espaço para textos ~20% maiores).

## O que eu quero receber
1. **Diagnóstico curto**: o que está bom e o que melhorar em cada tela (hierarquia, espaçamento, contraste, densidade,
   consistência dos botões, estados de hover/selecionado).
2. **Guia de estilo** em tabela: paleta (hex de fundo, painéis, bordas, texto principal/secundário/fraco, destaque,
   sucesso, aviso, erro), tamanhos de fonte por nível (título da seção, rótulo de ajuste, texto de dica, valores),
   espaçamentos (múltiplos de 4 ou 8), raios dos cantos, estilo dos botões (principal, secundário, perigo), sliders,
   checkboxes, rádios, abas laterais.
3. **Mockups** de 4 telas, como **imagens** e também como **um único arquivo HTML/CSS** autocontido (sem frameworks):
   - tela de dispositivos (card do volante conectado);
   - aba Volante (ajustes + desenho do volante com o arco de graus);
   - aba Força (ajustes + gráfico da força ao vivo);
   - aba LEDs (ajustes + barra de 5 LEDs + volante).
4. **Lista de mudanças por prioridade** (o que dá mais resultado com menos trabalho primeiro).

Seja específico nos números (px, hex, pesos de fonte): quem vai implementar vai copiar os valores.

---

## Versão em partes (se o chat do Gemini travar)
Mande o texto principal acima com a troca abaixo, e depois peça uma parte por mensagem.

Troque a seção "O que eu quero receber" por:
> Responda em partes. **Agora, só a parte 1**: diagnóstico curto de cada tela + guia de estilo em tabela
> (paleta em hex, fontes, espaçamentos, raios, botões, sliders, checkboxes, rádios, abas laterais). Sem código e sem
> imagens. Eu peço as outras partes depois.

Depois, uma mensagem por vez:
> Parte 2: só a imagem do mockup da aba Volante, seguindo o guia de estilo.

> Parte 3: só a imagem do mockup da aba Força.

> Parte 4: só a imagem do mockup da aba LEDs e da tela de dispositivos.

> Parte 5: a lista de mudanças por prioridade, em tópicos curtos.

Se quiser o HTML, peça uma tela por vez e use o **Canvas** do Gemini (o código abre ao lado, sem travar o chat;
dá para baixar o arquivo e me mandar).
