#ifndef EFFECTS_H
#define EFFECTS_H

#include <windows.h>
#ifndef DIRECTINPUT_VERSION
#define DIRECTINPUT_VERSION 0x0800
#endif
#include <dinput.h>
#include <dinputd.h>

#define MAX_EFFECTS 16

typedef struct Effect {
    BOOL used, playing;
    DWORD typeId;
    DWORD duration, gain, startDelay, loops;
    LONG dirFactor; /* -10000..10000 */
    BOOL hasEnv;
    DIENVELOPE env;
    DICONSTANTFORCE cf;
    DIRAMPFORCE rf;
    DIPERIODIC pf;
    DICONDITION cond;
    LONGLONG startQpc, pausedAccum;
} Effect;

/* Full-scale constants for normalized velocity/acceleration. */
#define VEL_FULLSCALE 40000
#define ACC_FULLSCALE 400000

/* handle = index+1. If *handle==0 on entry, allocates a free slot and writes it to *handle. */
HRESULT effects_download(Effect effects[MAX_EFFECTS], DWORD typeId, DWORD *handle,
                          LPCDIEFFECT eff, DWORD flags);
HRESULT effects_start(Effect effects[MAX_EFFECTS], DWORD handle, DWORD mode, DWORD count);
HRESULT effects_stop(Effect effects[MAX_EFFECTS], DWORD handle);
HRESULT effects_destroy(Effect effects[MAX_EFFECTS], DWORD handle);
HRESULT effects_status(Effect effects[MAX_EFFECTS], DWORD handle, DWORD *status);

/* Sums the "force" contributions (constant/ramp/periodic) of all active effects, -10000..10000.
   Also updates 'playing' when an effect reaches the end of its duration. */
LONG effects_mix_force(Effect effects[MAX_EFFECTS], LONGLONG nowQpc, LONGLONG freq);

/* Sums the contributions of the active conditions (spring/damper/inertia/friction), -10000..10000.
   pos/velN/accN are the wheel's current normalized metrics (-10000..10000). */
LONG effects_mix_cond(Effect effects[MAX_EFFECTS], LONGLONG nowQpc, LONGLONG freq,
                       LONG pos, LONG velN, LONG accN);

#endif /* EFFECTS_H */
