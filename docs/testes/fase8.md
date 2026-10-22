# Teste da Fase 8: Modo Compatibilidade Total (opcional)

A Fase 8 vem em 3 partes, cada uma com teste:
- **8a (esta):** ativar = instalar os drivers (vJoy + HidHide) só quando você pede; desativar = remover.
- **8b:** ligar o modo: G27 escondido dos jogos, volante virtual com sensibilidade, zona morta e pedais combinados;
  jogos com perfil em Modo Padrão continuam vendo o G27.
- **8c:** force feedback do jogo → vJoy → app → G27.

Instalar os drivers **não muda nada nos jogos**: o app continua no Modo Padrão até a 8b.

Já validado pelo Claude Code (22/10/2026):
- os instaladores oficiais são baixados do GitHub e conferidos pelo SHA-256 (vJoy 2.2.2.0 da Brunner Elektronik AG,
  HidHide 1.5.230 da Nefarius) e pela assinatura digital antes de rodar;
- os scripts de instalar e desinstalar são PowerShell válido; o ponto de restauração vem antes de qualquer instalação
  (sem ele, nada é instalado);
- 79 testes automáticos.
- Não foi testado aqui: a instalação de verdade (precisa da sua permissão de administrador e de reiniciar).

## 1. Ativar
- [ ] Engrenagem do topo → **MODO COMPATIBILIDADE TOTAL** → **ATIVAR (INSTALA OS DRIVERS)**.
- [ ] Aparece o aviso explicando tudo. Clique **Sim**.
- [x] O Windows pede permissão de administrador: **Sim**. Em ~1 minuto aparece "Drivers instalados... Reiniciar agora?".
      (John, 22/10: instalou; log com ponto de restauração + vJoy + HidHide OK)
- [ ] Barra de progresso (pedido do John): ao instalar/desinstalar, uma janela mostra cada passo (baixando, ponto de
      restauração, instalando cada driver) e a porcentagem.
- [ ] Depois de reiniciar: a engrenagem mostra "Drivers instalados (vJoy e HidHide)". O G27 continua funcionando
      normalmente no app e no BeamNG/ETS2 (nada muda até a 8b).
- [ ] Ponto de restauração: Iniciar → "Criar um ponto de restauração" → Restauração do Sistema: aparece
      "G27 Hub: antes do vJoy e do HidHide".

## 2. Desativar
- [ ] Mesma tela → **DESINSTALAR OS DRIVERS** → Sim → permissão de administrador → reiniciar.
- [ ] Depois: a tela volta a mostrar "Desligado". "vJoy" e "HidHide" somem de Configurações → Aplicativos.

## 3. Ligar o modo (8b)
Já validado pelo Claude Code (22/10/2026), com os drivers que você instalou:
- HidHide: com o modo ligado, um programa de fora deixou de ver o G27 (só via o vJoy); ao desligar, o G27 voltou;
- vJoy: os eixos e botões mandados pelo app chegam no dispositivo virtual;
- 87 testes automáticos (curvas de sensibilidade, achar o G27 na lista do HidHide, configuração do vJoy após instalar).

- [ ] Card do G27 → engrenagem → **MODO** → **COMPATIBILIDADE TOTAL**.
- [ ] Na primeira vez: aviso "Falta configurar o volante virtual" → Sim → permissão de administrador (barra de progresso).
- [ ] O app ganha a **borda vermelha** e o selo **● COMPATIBILIDADE TOTAL** no topo; aparece o aviso "Compatibilidade Total ligada".
- [ ] `joy.cpl` (Win+R): aparece só o **vJoy Device**, não o G27. Propriedades do vJoy: girar o volante move o X;
      acelerador = Y, freio = Z, embreagem = Rx (eixo Ry = acelerador − freio); botões e direcional acendem.
- [ ] Abas Volante e Pedais: as **sensibilidades** ficam liberadas. Sensibilidade do volante em 20 → o vJoy anda mais devagar
      no começo do giro; em 80 → mais rápido.
- [ ] Feche o app (Sair na bandeja): o G27 volta a aparecer no joy.cpl. Abra de novo: o modo religa sozinho.
- [ ] Volte para **PADRÃO**: borda e selo somem; o joy.cpl mostra o G27 de novo.

## 4. Force feedback pelo vJoy (8c)
O jogo manda o FFB para o vJoy; o app recebe e passa para o mesmo motor da DLL do Modo Padrão (os ajustes da aba Força valem).
Já validado pelo Claude Code (22/10/2026) com o `g27fftest ... vjoy` (um "jogo" DirectInput no vJoy):
força constante para um lado e para o outro, senoidal no período certo (200 ms) e mola, todos chegando ao G27.
- [ ] Com o modo ligado, abra o jogo (BeamNG/ETS2 configurado no vJoy) e ative o force feedback no jogo: o volante
      reage às curvas, batidas e piso como no Modo Padrão.
- [ ] Se a força vier ao contrário, marque INVERTER FORÇA na aba Força.
- [ ] Console (Ctrl+Shift+L): com o jogo, `O jogo começou a mandar força pelo vJoy`. 15 s depois de fechar o jogo:
      `Sem força do jogo há 15 s: motor parado, mola do volante de volta` (sem jogo o volante não fica solto).
- Medido em 22/10: pelo vJoy a força chega tão bem quanto direto (268–356 mudanças/s × 276 direto, vibração inteira).
  Se no BeamNG o piso parecer mais fraco pelo vJoy, é o jeito do jogo tratar o vJoy (joystick) × o G27 (volante):
  para BeamNG e ETS2 o Modo Padrão continua sendo o melhor.

## 5. Detector (com ele ligado)
- [ ] Com a Compatibilidade Total ligada, abrir o BeamNG/ETS2 mostra "funciona melhor no Modo Padrão" com o botão
      **VOLTAR AO MODO PADRÃO** (só recomenda; trocar é com o seu clique).

## Fora desta fase
- Modo por perfil (cada jogo com o seu modo): o jogo vê os dispositivos na hora em que abre, antes do app perceber
  que ele abriu; trocar o modo automaticamente chegaria tarde. Fica a troca manual (e o botão do detector).
- Zona morta e inverter por eixo: a sensibilidade está feita; o resto os jogos já têm nas opções de controle.

## Se algo der errado
O console (Ctrl+Shift+L, origem MODO) mostra cada passo. Me mande as linhas. O ponto de restauração desfaz tudo.

## Aceite (fase inteira)
Num jogo de teste funciona com sensibilidade e FFB; desativar devolve tudo ao normal após reiniciar; o detector
recomenda desligar ao abrir um jogo `nativo`.
