# Teste da Fase 5a — Perfis

Já validado pelo Claude Code no volante (21/10/2026):
- o config antigo virou o perfil "Desktop: Padrão" sem perder nada (900°, mola 21%);
- um perfil de teste ligado ao Bloco de Notas trocou sozinho ao abrir (540° no volante) e voltou ao Desktop ao fechar (900°);
- com o cadeado travado, abrir/fechar o "jogo" não trocou o perfil.

Abra com `g27hub.cmd`.

## 1. Criar perfil de jogo
- [ ] Clique em **DESKTOP: Padrão** (topo) → **+ ADICIONAR JOGO (.EXE)** → escolha o `.exe` de um jogo (ex.: Forza, AC). O perfil aparece com o ícone do jogo e o nome do `.exe` embaixo.
- [ ] Abra o jogo: em até ~2 s aparece a notificação "Perfil: <jogo>" e o topo mostra o nome do jogo. Feche o jogo: volta para "DESKTOP: Padrão".

## 2. Configurações especiais por jogo
- [ ] Com o perfil do jogo ativo (clique nele na lista), vá na aba **Volante**: aparece **USAR CONFIGURAÇÕES ESPECIAIS DO VOLANTE NESTE PERFIL**.
- [ ] Desmarcada: o aviso diz que mexer ali muda o Desktop.
- [ ] Marque, mude a rotação (ex.: 540) e a força (aba Força). Volte para o Desktop: tudo volta ao que era. Volte para o jogo: 540 de novo.

## 3. Cadeado (travar perfil)
- [ ] Clique no cadeado de qualquer aba (ou "TRAVAR NESTE PERFIL" na lista, ou em Configurações do app). O topo mostra **PERFIL TRAVADO** e o cadeado fica azul.
- [ ] Abra o jogo: o perfil **não** troca. Destrave: troca na hora.

## 4. Lista de perfis (botão ⋯ de cada um)
- [ ] **Duplicar** cria "(cópia)" sem `.exe` (não troca sozinho).
- [ ] **Renomear…** abre a caixinha e muda o nome.
- [ ] **Exportar…** salva um `.g27profile`; **IMPORTAR PERFIL** na lista traz de volta como perfil novo.
- [ ] **Excluir** pede confirmação. O Desktop não pode ser renomeado nem excluído.

## 5. Menu ☰ e engrenagens
- [ ] Na tela da lista de dispositivos, ☰ → Jogos e apps / Configurações / Console / Sobre.
- [ ] Engrenagem do card do G27 (e a da barra lateral): mostra nome, ID, versão, modo, e os botões Testar LEDs e Reaplicar.
- [ ] Engrenagem do topo: **NOTIFICAÇÕES** desligada → só erros aparecem.

## Aceite
Perfis por jogo criados pelo `.exe`, trocando sozinhos ao abrir/fechar o jogo, com ajustes próprios quando marcado, cadeado funcionando e exportar/importar ok.
