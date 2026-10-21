#ifndef G27HID_H
#define G27HID_H

#include <windows.h>

typedef struct {
    WCHAR path[512];
    USHORT pid;
    USHORT version;
} G27Device;

/* Returns the number of G27s found (valid compat or native), filling out[0..n-1] (n <= max). */
int g27_find(G27Device *out, int max);

HANDLE g27_open(const WCHAR *path, BOOL overlapped);

BOOL g27_send(HANDLE h, const BYTE cmd7[7]);

BOOL g27_caps(HANDLE h, USHORT *inLen, USHORT *outLen);

/* Maps a 0..100 strength percentage to the wheel's two spring fields.
   Returns FALSE if pct==0, meaning "no spring" - coeff/clip are then untouched.
   This is the pure curve, with no I/O: see g27hid.c for its shape and rationale. */
BOOL g27_autocenter_fields(unsigned pct, BYTE *coeff, BYTE *clip);

/* Applies the hardware auto-center spring. Use these rather than sending the reports
   yourself: the wheel LATCHES the spring parameters while the spring is enabled, so a
   new parameter report is silently ignored unless the spring is disabled first. These
   always disable before setting, which is the only sequence that works. */
BOOL g27_autocenter_apply(HANDLE h, unsigned pct);

/* Same, with the two protocol fields given explicitly rather than derived from a
   percentage. Intended for diagnostics and for porting to other wheels, where the
   usable range of these fields has to be found by experiment. */
BOOL g27_autocenter_apply_raw(HANDLE h, BYTE coeff, BYTE clip);

#endif /* G27HID_H */
