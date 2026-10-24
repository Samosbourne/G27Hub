#ifndef G27LOG_H
#define G27LOG_H

#include <windows.h>

/* Nivel 1 = eventos (INFO), 2 = trace detalhado (DEBUG). Filtrado pelo valor "Log"
   de HKCU\Software\G27Hub\FFB (padrao 1). Nunca bloqueia: vai para uma fila. */
void g27_log(int level, const char *fmt, ...);

/* Linha com nivel explicito do console do G27 Hub: "OK", "AVISO", "ERRO"... Sempre registrada. */
void hub_log(const char *nivel, const char *fmt, ...);

/* Publica a forca atual (-10000..10000, antes do corte) na memoria compartilhada
   Local\G27Hub_Shared, para o app mostrar o grafico e o clipping. */
void hub_publish(LONG force, BOOL clipped);

/* Vibracao extra pedida pelo app (motor, pista, trancos), -10000..10000; 0 sem app ou sem telemetria.
   Chamada a cada tick do mixer (dt em segundos). */
LONG hub_vibration(double dt);

#endif /* G27LOG_H */
