#include "effects.h"
#include "g27log.h"

#include <math.h>
#include <string.h>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

static int find_free_slot(Effect effects[MAX_EFFECTS]) {
    for (int i = 0; i < MAX_EFFECTS; i++) {
        if (!effects[i].used) {
            return i;
        }
    }
    return -1;
}

static LONG compute_direction(LPCDIEFFECT eff) {
    if (eff->cAxes == 0) {
        return 10000;
    }
    if (eff->dwFlags & DIEFF_CARTESIAN) {
        if (eff->cAxes == 1) {
            return eff->rglDirection[0] < 0 ? -10000 : 10000;
        }
        double x = (double)eff->rglDirection[0];
        double y = (double)eff->rglDirection[1];
        double len = sqrt(x * x + y * y);
        return len > 0.0 ? (LONG)(x * 10000.0 / len) : 10000;
    }
    if (eff->dwFlags & DIEFF_POLAR) {
        double angle = (double)eff->rglDirection[0]; /* 1/100 degree, 0=north, 9000=east */
        return (LONG)(sin(angle * M_PI / 18000.0) * 10000.0);
    }
    if (eff->dwFlags & DIEFF_SPHERICAL) {
        double angle = (double)eff->rglDirection[0];
        return (LONG)(cos(angle * M_PI / 18000.0) * 10000.0);
    }
    return 10000;
}

HRESULT effects_download(Effect effects[MAX_EFFECTS], DWORD typeId, DWORD *handle,
                          LPCDIEFFECT eff, DWORD flags) {
    if (!handle || !eff) {
        return DIERR_INVALIDPARAM;
    }
    if (eff->dwSize != sizeof(DIEFFECT) && eff->dwSize != sizeof(DIEFFECT_DX5)) {
        return DIERR_INVALIDPARAM;
    }
    if (eff->cAxes > 8) {
        return DIERR_INVALIDPARAM;
    }
    if (eff->cAxes > 0 && (!eff->rgdwAxes || !eff->rglDirection)) {
        return DIERR_INVALIDPARAM;
    }

    int idx;
    BOOL isNewEffect;
    if (*handle == 0) {
        idx = find_free_slot(effects);
        if (idx < 0) {
            return DIERR_DEVICEFULL;
        }
        isNewEffect = TRUE;
    } else {
        if (*handle < 1 || *handle > MAX_EFFECTS) {
            return DIERR_INVALIDPARAM;
        }
        idx = (int)(*handle - 1);
        isNewEffect = !effects[idx].used;
    }

    Effect *e = &effects[idx];
    if (isNewEffect) {
        /* Only for a brand-new effect do we clear all state (including playing/startQpc).
           A parameter update (SetParameters on an already-running effect, typically
           a high-frequency call without DIEP_START) must NOT stop the effect. */
        memset(e, 0, sizeof(*e));
        e->used = TRUE;
        e->loops = 1;
    }
    e->typeId = typeId;
    e->duration = eff->dwDuration;
    e->gain = (eff->dwGain > 10000) ? 10000 : eff->dwGain;
    e->startDelay = (eff->dwSize == sizeof(DIEFFECT)) ? eff->dwStartDelay : 0;

    e->hasEnv = FALSE;
    if (eff->lpEnvelope && eff->lpEnvelope->dwSize >= sizeof(DIENVELOPE)) {
        e->hasEnv = TRUE;
        e->env = *eff->lpEnvelope;
    }

    e->dirFactor = compute_direction(eff);

    LONG mag = 0;
    switch (typeId) {
        case 0: /* Constant */
            if (eff->cbTypeSpecificParams < sizeof(DICONSTANTFORCE)) {
                e->used = FALSE;
                return DIERR_INVALIDPARAM;
            }
            e->cf = *(const DICONSTANTFORCE *)eff->lpvTypeSpecificParams;
            mag = e->cf.lMagnitude;
            break;
        case 1: /* Ramp */
            if (eff->cbTypeSpecificParams < sizeof(DIRAMPFORCE)) {
                e->used = FALSE;
                return DIERR_INVALIDPARAM;
            }
            e->rf = *(const DIRAMPFORCE *)eff->lpvTypeSpecificParams;
            mag = e->rf.lEnd;
            break;
        case 2: case 3: case 4: case 5: case 6: /* Periodic */
            if (eff->cbTypeSpecificParams < sizeof(DIPERIODIC)) {
                e->used = FALSE;
                return DIERR_INVALIDPARAM;
            }
            e->pf = *(const DIPERIODIC *)eff->lpvTypeSpecificParams;
            mag = (LONG)e->pf.dwMagnitude;
            break;
        case 7: case 8: case 9: case 10: /* Conditions */
            if (eff->cbTypeSpecificParams < sizeof(DICONDITION)) {
                e->used = FALSE;
                return DIERR_INVALIDPARAM;
            }
            e->cond = *(const DICONDITION *)eff->lpvTypeSpecificParams;
            g27_log(isNewEffect ? 1 : 2, "Cond h=%u type=%u off=%ld pos=%ld neg=%ld satP=%u satN=%u dead=%ld",
                    (unsigned)(idx + 1), (unsigned)typeId, (long)e->cond.lOffset,
                    (long)e->cond.lPositiveCoefficient, (long)e->cond.lNegativeCoefficient,
                    (unsigned)e->cond.dwPositiveSaturation, (unsigned)e->cond.dwNegativeSaturation,
                    (long)e->cond.lDeadBand);
            break;
        default:
            /* Unknown type (e.g. CustomForce 0x100): accepted, contributes 0. */
            break;
    }

    *handle = (DWORD)(idx + 1);

    /* G27 Hub: jogos atualizam efeitos centenas de vezes por segundo; so o efeito novo e nivel 1 */
    g27_log(isNewEffect ? 1 : 2, "Download h=%u type=%u mag=%ld dur=%u gain=%u dir=%ld flags=%08X",
            (unsigned)*handle, (unsigned)typeId, (long)mag, (unsigned)e->duration,
            (unsigned)e->gain, (long)e->dirFactor, (unsigned)flags);

    if (flags & DIEP_START) {
        effects_start(effects, *handle, 0, 1);
    }

    return DI_OK;
}

HRESULT effects_start(Effect effects[MAX_EFFECTS], DWORD handle, DWORD mode, DWORD count) {
    if (handle < 1 || handle > MAX_EFFECTS) {
        return DIERR_INVALIDPARAM;
    }
    int idx = (int)(handle - 1);
    if (!effects[idx].used) {
        return DIERR_INVALIDPARAM;
    }

    if (mode & DIES_SOLO) {
        for (int i = 0; i < MAX_EFFECTS; i++) {
            if (i != idx) {
                effects[i].playing = FALSE;
            }
        }
    }

    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);

    effects[idx].playing = TRUE;
    effects[idx].loops = count ? count : 1;
    effects[idx].startQpc = now.QuadPart;
    effects[idx].pausedAccum = 0;

    g27_log(1, "Start h=%u mode=%u count=%u", (unsigned)handle, (unsigned)mode, (unsigned)count);

    return DI_OK;
}

HRESULT effects_stop(Effect effects[MAX_EFFECTS], DWORD handle) {
    if (handle < 1 || handle > MAX_EFFECTS) {
        return DIERR_INVALIDPARAM;
    }
    int idx = (int)(handle - 1);
    if (!effects[idx].used) {
        return DIERR_INVALIDPARAM;
    }
    effects[idx].playing = FALSE;
    return DI_OK;
}

HRESULT effects_destroy(Effect effects[MAX_EFFECTS], DWORD handle) {
    if (handle < 1 || handle > MAX_EFFECTS) {
        return DIERR_INVALIDPARAM;
    }
    int idx = (int)(handle - 1);
    if (!effects[idx].used) {
        return DIERR_INVALIDPARAM;
    }
    memset(&effects[idx], 0, sizeof(Effect));
    return DI_OK;
}

/* Returns FALSE (and sets playing=FALSE) once the effect has exhausted duration*loops. */
static BOOL effect_elapsed(Effect *e, LONGLONG nowQpc, LONGLONG freq, LONGLONG *tlOut) {
    LONGLONG elQpc = nowQpc - e->startQpc - e->pausedAccum;
    LONGLONG el_us = (freq > 0) ? (elQpc * 1000000LL) / freq : 0;
    LONGLONG t = el_us - (LONGLONG)e->startDelay;
    if (t < 0) {
        t = 0;
    }

    if (e->duration != INFINITE) {
        if (e->loops != INFINITE) {
            LONGLONG total = (LONGLONG)e->duration * (LONGLONG)e->loops;
            if (t >= total) {
                e->playing = FALSE;
                return FALSE;
            }
        }
        *tlOut = (e->duration > 0) ? (t % (LONGLONG)e->duration) : 0;
    } else {
        *tlOut = t;
    }
    return TRUE;
}

/* scale = mag ? lvl/mag : 0. With no envelope there is no attenuation (scale=1). */
static double envelope_scale(Effect *e, LONGLONG tl, double mag) {
    if (!e->hasEnv) {
        return 1.0;
    }
    if (mag == 0.0) {
        return 0.0;
    }
    double lvl = mag;
    if (tl < (LONGLONG)e->env.dwAttackTime) {
        lvl = (double)e->env.dwAttackLevel +
              (mag - (double)e->env.dwAttackLevel) * (double)tl / (double)e->env.dwAttackTime;
    } else if (e->duration != INFINITE &&
               tl > (LONGLONG)e->duration - (LONGLONG)e->env.dwFadeTime) {
        LONGLONG fadeStart = (LONGLONG)e->duration - (LONGLONG)e->env.dwFadeTime;
        lvl = mag + ((double)e->env.dwFadeLevel - mag) * (double)(tl - fadeStart) / (double)e->env.dwFadeTime;
    }
    return lvl / mag;
}

/* Raw effect value (before gain/dirFactor), from its type and local time tl. */
static LONG effect_raw_value(Effect *e, LONGLONG tl) {
    switch (e->typeId) {
        case 0: { /* Constant */
            double mag = fabs((double)e->cf.lMagnitude);
            double scale = envelope_scale(e, tl, mag);
            return (LONG)((double)e->cf.lMagnitude * scale);
        }
        case 1: { /* Ramp */
            double v;
            if (e->duration == INFINITE) {
                v = (double)e->rf.lStart;
            } else {
                v = (double)e->rf.lStart +
                    ((double)e->rf.lEnd - (double)e->rf.lStart) * (double)tl / (double)e->duration;
            }
            double magStart = fabs((double)e->rf.lStart);
            double magEnd = fabs((double)e->rf.lEnd);
            double mag = (magStart > magEnd) ? magStart : magEnd;
            double scale = envelope_scale(e, tl, mag);
            return (LONG)(v * scale);
        }
        case 2: case 3: case 4: case 5: case 6: { /* Periodic: Square/Sine/Triangle/SawUp/SawDown */
            DWORD period = e->pf.dwPeriod ? e->pf.dwPeriod : 1000;
            double phase = (double)e->pf.dwPhase / 36000.0 + (double)tl / (double)period;
            double p = phase - floor(phase);
            double w;
            switch (e->typeId) {
                case 2: w = (p < 0.5) ? 1.0 : -1.0; break;
                case 3: w = sin(2.0 * M_PI * p); break;
                case 4:
                    if (p < 0.25) w = 4.0 * p;
                    else if (p < 0.75) w = 2.0 - 4.0 * p;
                    else w = 4.0 * p - 4.0;
                    break;
                case 5: w = 2.0 * p - 1.0; break;
                case 6: w = 1.0 - 2.0 * p; break;
                default: w = 0.0; break;
            }
            double mag = (double)e->pf.dwMagnitude;
            double scale = envelope_scale(e, tl, mag);
            double magEnv = mag * scale;
            double v = (double)e->pf.lOffset + magEnv * w;
            return (LONG)v;
        }
        default:
            return 0;
    }
}

HRESULT effects_status(Effect effects[MAX_EFFECTS], DWORD handle, DWORD *status) {
    if (handle < 1 || handle > MAX_EFFECTS || !status) {
        return DIERR_INVALIDPARAM;
    }
    int idx = (int)(handle - 1);
    if (!effects[idx].used) {
        return DIERR_INVALIDPARAM;
    }

    if (effects[idx].playing) {
        LARGE_INTEGER now, freq;
        QueryPerformanceCounter(&now);
        QueryPerformanceFrequency(&freq);
        LONGLONG tl;
        effect_elapsed(&effects[idx], now.QuadPart, freq.QuadPart, &tl);
    }

    *status = effects[idx].playing ? DIEGES_PLAYING : 0;
    return DI_OK;
}

LONG effects_mix_force(Effect effects[MAX_EFFECTS], LONGLONG nowQpc, LONGLONG freq) {
    LONGLONG total = 0;

    for (int i = 0; i < MAX_EFFECTS; i++) {
        Effect *e = &effects[i];
        if (!e->used || !e->playing) {
            continue;
        }

        LONGLONG tl;
        if (!effect_elapsed(e, nowQpc, freq, &tl)) {
            continue;
        }

        LONG v = effect_raw_value(e, tl);

        LONGLONG contrib = (LONGLONG)v * (LONGLONG)e->gain / 10000;
        contrib = contrib * (LONGLONG)e->dirFactor / 10000;
        total += contrib;
    }

    if (total > 10000) total = 10000;
    if (total < -10000) total = -10000;
    return (LONG)total;
}

static LONGLONG sat0(DWORD s) {
    return s ? (LONGLONG)s : 10000;
}

LONG effects_mix_cond(Effect effects[MAX_EFFECTS], LONGLONG nowQpc, LONGLONG freq,
                       LONG pos, LONG velN, LONG accN, const LONG condGain[4]) {
    LONGLONG total = 0;

    for (int i = 0; i < MAX_EFFECTS; i++) {
        Effect *e = &effects[i];
        if (!e->used || !e->playing) {
            continue;
        }
        if (e->typeId < 7 || e->typeId > 10) {
            continue;
        }

        LONGLONG tl;
        if (!effect_elapsed(e, nowQpc, freq, &tl)) {
            continue;
        }

        LONGLONG f = 0;

        if (e->typeId == 10) {
            /* Friction: based on velocity, not on the generic condition formula. */
            if (velN > 50 || velN < -50) {
                LONG coeff = (velN > 0) ? e->cond.lPositiveCoefficient : e->cond.lNegativeCoefficient;
                DWORD sat = (velN > 0) ? e->cond.dwPositiveSaturation : e->cond.dwNegativeSaturation;
                f = -(LONGLONG)coeff * (velN > 0 ? 1 : -1);
                LONGLONG satv = sat0(sat);
                if (f > satv) f = satv;
                if (f < -satv) f = -satv;
            }
        } else {
            LONG metric;
            switch (e->typeId) {
                case 7: metric = pos; break;  /* Spring */
                case 8: metric = velN; break; /* Damper */
                default: metric = accN; break; /* 9: Inertia */
            }

            LONG x = metric - e->cond.lOffset;
            if (x > e->cond.lDeadBand) {
                f = -(LONGLONG)e->cond.lPositiveCoefficient * (LONGLONG)(x - e->cond.lDeadBand) / 10000;
                LONGLONG satv = sat0(e->cond.dwPositiveSaturation);
                if (f > satv) f = satv;
                if (f < -satv) f = -satv;
            } else if (x < -e->cond.lDeadBand) {
                f = -(LONGLONG)e->cond.lNegativeCoefficient * (LONGLONG)(x + e->cond.lDeadBand) / 10000;
                LONGLONG satv = sat0(e->cond.dwNegativeSaturation);
                if (f > satv) f = satv;
                if (f < -satv) f = -satv;
            } else {
                f = 0;
            }
        }

        LONGLONG contrib = f * (LONGLONG)e->gain / 10000;
        contrib = contrib * condGain[e->typeId - 7] / 100; /* G27 Hub: intensidade por tipo (aba Forca) */
        total += contrib;
    }

    if (total > 10000) total = 10000;
    if (total < -10000) total = -10000;
    return (LONG)total;
}
