# Teste da Fase 7: LEDs de RPM

Aba **LEDs** (ícone `•••••` na barra lateral do volante). Os LEDs funcionam com o app aberto ou na bandeja.

Já validado pelo Claude Code em 22/10/2026:
- 76 testes automáticos: os 3 modos, os limites, limitador de box, combustível, e a leitura dos pacotes de BeamNG, OutGauge, Forza e F1 (formatos 2022 e 2023+);
- com o app aberto e o volante ligado, uma acelerada falsa no formato do BeamNG (1.000 → 7.200 rpm) chegou ao app ("Recebendo telemetria: BeamNG.drive" no console) e parou sozinha 1 s depois do fim.

## 1. Teste sem jogo
- [ ] **TESTAR LEDS**: os LEDs do volante enchem de fora para o centro (verde → vermelho) e, no fim, piscam todos. O desenho na tela faz o mesmo.
- [ ] Troque o modo e teste de novo:
  - **PONTO DE TROCA**: apagados, só piscam todos no fim;
  - **LIMITADOR**: enchem e ficam acesos, piscam só no último instante (corte de giro).
- [ ] Mude o LED 5 para 99 e teste: a barra demora mais para piscar.
- [ ] **Ordem física**: o LED 1 (verde) é o par **de fora** e o LED 5 o par vermelho **do meio**? Se for ao contrário, me avise.

## 2. BeamNG.drive
- [ ] Na aba LEDs → **INSTALAR NO BEAMNG** (cria `mods\unpacked\g27hub` na pasta do BeamNG; o mesmo botão remove).
- [ ] Abra o BeamNG e entre num carro. No console (Ctrl+Shift+L) aparece `TELEMETRIA  Recebendo telemetria: BeamNG.drive`.
- [ ] Acelere em ponto morto e em 1ª: os LEDs acompanham o giro e piscam perto do corte. Na aba LEDs aparece "BeamNG.drive · 4.350 / 7.000 rpm (62%) · 1ª".
- [ ] Troque de carro (um carro mais "giro alto" e um caminhão): o ponto de troca muda sozinho, porque cada carro manda a sua RPM máxima.
- [ ] Com o carro em câmbio automático, a luz de troca do próprio jogo também faz piscar ("Permitir que os jogos controlem os LEDs"). Desmarque para usar só os limites.
- [ ] Minimize o app para a bandeja: os LEDs continuam funcionando.
- [ ] Feche o BeamNG: os LEDs apagam em 1 s.

## 3. Euro Truck Simulator 2 (plugin feito em 22/10)
Já instalei o plugin no seu ETS2 (`bin\win_x64\plugins\g27hub_telemetry.dll`). Na aba LEDs há o botão INSTALAR/REMOVER NO ETS2 / ATS.
Testado aqui com um "ETS2 falso" que carrega o plugin como o jogo faz: o app recebeu a telemetria.
- [ ] Abra o ETS2: ele avisa que achou plugins do SDK. Confirme (OK).
- [ ] No console do app: `Recebendo telemetria: ETS2 / ATS`.
- [ ] Acelere parado e rodando: os LEDs acompanham o giro do caminhão (o limite é a RPM máxima de cada caminhão, ~2.500).
- [ ] Pause o jogo (Esc): os LEDs apagam em 1 s; ao voltar, acendem de novo.

## 4. Outros jogos (se tiver)
- Assetto Corsa / ACC: automático, sem ajustar nada.
- F1 22–25: Configurações → Telemetria → UDP ligado, IP 127.0.0.1, porta 20777 (usa a barra de giro do próprio jogo).
- Forza: HUD e jogabilidade → Data Out ligado, IP 127.0.0.1, porta 5300.
- Live for Speed: OutGauge para 127.0.0.1, porta 4444 (a RPM máxima é aprendida no primeiro giro alto).

## Fora desta fase
- iRacing e rFactor 2: precisam de um plugin de telemetria de terceiros ou de memória compartilhada que eu não consigo testar aqui. Entram quando você jogar algum deles.
- Rotação automática por carro: o BeamNG e o AC já fazem a trava virtual pelo force feedback; mudar a rotação do volante no meio do jogo deixaria a direção fora do 1:1.
- Bandeiras: só AC/ACC/iRacing mandam, e nenhum está instalado.

## Aceite
No BeamNG, os LEDs acendem com o giro e piscam no ponto de troca de cada carro, com o app minimizado.
