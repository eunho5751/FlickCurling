# -*- coding: utf-8 -*-
"""
FlickCurling's background music, composed and synthesized here (plain Python, no packages), written as seamless loops:

    python Tools/Music/compose.py            -> Assets/Resources/Music/menu.wav, game.wav

menu: F major, 104 bpm, 32 bars - a marimba tune over a bouncing bass, offbeat marimba chords, a soft pad, a woodblock and
      a shaker (the wooden, cosy feel of the menus).
game: C major, 84 bpm, 24 bars - calmer, for aiming: marimba arpeggios, a pad, a slow bass, a faint shaker and a few bell
      notes; no tune to follow.

Every note that rings past the loop's end is wrapped round to its start, and the reverb runs round the loop twice, so the
end flows into the beginning without a seam. Mono, 44.1 kHz, 16 bit (Unity compresses it on import).
"""
import math, os, random, struct, sys, wave

RATE = 44100
TAU = 2 * math.pi


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12.0)


class Track:
    def __init__(self, bpm, bars):
        self.beat = 60.0 / bpm
        self.bar = 4 * self.beat
        self.n = int(round(bars * self.bar * RATE))
        self.buf = [0.0] * self.n

    def at(self, bar, beat):
        return (bar * 4 + beat) * self.beat

    # adds a voice (a function of the time since its start) from t0 for dur seconds, wrapping round the loop
    def add(self, t0, dur, f):
        s0 = int(t0 * RATE)
        buf, n = self.buf, self.n
        for i in range(int(dur * RATE)):
            buf[(s0 + i) % n] += f(i / RATE)


# ---------------- instruments ----------------

def marimba(tr, t0, note, vel=1.0):
    f = midi(note)
    tau = 0.55 * (440 / f) ** 0.35          # low notes ring longer
    p2, tau2 = f * 3.93, 0.06               # the bar's bright overtone, gone quickly
    rnd = random.Random(note * 7 + int(t0 * 1000))
    def v(t):
        a = min(1.0, t / 0.002)
        x = math.sin(TAU * f * t) * math.exp(-t / tau)
        x += 0.22 * math.sin(TAU * p2 * t) * math.exp(-t / tau2)
        if t < 0.006:                       # the mallet's knock
            x += 0.25 * (rnd.random() * 2 - 1) * (1 - t / 0.006)
        return vel * a * x
    tr.add(t0, min(3.0, tau * 6), v)


def glock(tr, t0, note, vel=1.0):
    f = midi(note)
    def v(t):
        a = min(1.0, t / 0.001)
        x = math.sin(TAU * f * t) * math.exp(-t / 1.1)
        x += 0.3 * math.sin(TAU * f * 2.76 * t) * math.exp(-t / 0.25)
        x += 0.1 * math.sin(TAU * f * 5.4 * t) * math.exp(-t / 0.08)
        return vel * a * x
    tr.add(t0, 4.0, v)


def bass(tr, t0, note, dur, vel=1.0):
    f = midi(note)
    rel = 0.08
    def v(t):
        a = min(1.0, t / 0.01)
        if t > dur:
            a *= max(0.0, 1 - (t - dur) / rel)
        x = math.sin(TAU * f * t) + 0.3 * math.sin(TAU * 2 * f * t) + 0.08 * math.sin(TAU * 3 * f * t)
        return vel * a * math.exp(-t / 0.9) * x
    tr.add(t0, dur + rel, v)


def pad(tr, t0, notes, dur, vel=1.0):
    att, rel = 0.5, 0.9
    fs = [midi(n) for n in notes]
    def v(t):
        a = min(1.0, t / att)
        if t > dur:
            a *= max(0.0, 1 - (t - dur) / rel)
        x = 0.0
        for f in fs:
            x += math.sin(TAU * f * t) + math.sin(TAU * f * 1.004 * t) + 0.18 * math.sin(TAU * 2 * f * t)
        return vel * a * x / len(fs)
    tr.add(t0, dur + rel, v)


def kick(tr, t0, vel=1.0):
    def v(t):
        f_ph = 45 * t + (110 - 45) * 0.03 * (1 - math.exp(-t / 0.03))   # 110 Hz falling to 45 Hz
        return vel * math.sin(TAU * f_ph) * math.exp(-t / 0.16) * min(1.0, t / 0.002)
    tr.add(t0, 0.6, v)


def woodblock(tr, t0, vel=1.0):
    def v(t):
        x = math.sin(TAU * 880 * t) + 0.5 * math.sin(TAU * 1390 * t)
        return vel * x * math.exp(-t / 0.028) * min(1.0, t / 0.0008)
    tr.add(t0, 0.2, v)


def shaker(tr, t0, vel=1.0, seed=0):
    rnd = random.Random(seed)
    state = [0.0, 0.0]
    def v(t):
        w = rnd.random() * 2 - 1
        state[0] += 0.5 * (w - state[0])          # (low-passed, then the low end taken off: a soft hiss)
        hp = state[0] - state[1]; state[1] += 0.05 * (state[0] - state[1])
        env = min(1.0, t / 0.012) * math.exp(-max(0.0, t - 0.012) / 0.035)
        return vel * hp * env
    tr.add(t0, 0.18, v)


# ---------------- mixing ----------------

def reverb_loop(buf, wet=0.2):
    """Schroeder reverb, run round the loop twice so its tail flows over the seam."""
    n = len(buf)
    combs = [(1557, 0.78), (1617, 0.77), (1491, 0.79), (1422, 0.76)]
    lines = [[0.0] * d for d, _ in combs]
    idx = [0] * len(combs)
    aps = [(225, 0.5), (556, 0.5)]
    ap_lines = [[0.0] * d for d, _ in aps]
    ap_idx = [0] * len(aps)
    out = [0.0] * n
    for pass_ in range(2):
        for i in range(n):
            x = buf[i]
            y = 0.0
            for k, (d, g) in enumerate(combs):
                line = lines[k]; j = idx[k]
                o = line[j]; y += o
                line[j] = x + o * g
                idx[k] = (j + 1) % d
            y *= 0.25
            for k, (d, g) in enumerate(aps):
                line = ap_lines[k]; j = ap_idx[k]
                o = line[j]
                v = y + o * g
                line[j] = v
                y = o - g * v
                ap_idx[k] = (j + 1) % d
            if pass_ == 1:
                out[i] = x + wet * y
    return out


def master(buf, peak=0.8):
    # a gentle low-pass (about 7 kHz), round the loop twice like the reverb, then to the peak, softly limited
    a = 1 - math.exp(-TAU * 7000 / RATE)
    y = 0.0
    out = [0.0] * len(buf)
    for pass_ in range(2):
        for i, x in enumerate(buf):
            y += a * (x - y)
            if pass_:
                out[i] = y
    m = max(abs(x) for x in out) or 1.0
    g = 1.15 * peak / m
    return [math.tanh(x * g) * peak / math.tanh(1.15) for x in out]


def write(path, buf):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b''.join(struct.pack('<h', int(max(-1.0, min(1.0, x)) * 32767)) for x in buf))


# ---------------- the menu tune ----------------

# chords: (root for the bass, pad voicing, tones for the offbeat marimba)
F, Dm, Bb, C, Gm, Am = 'F', 'Dm', 'Bb', 'C', 'Gm', 'Am'
CH = {
    F:  (41, [60, 65, 69], [60, 65, 69]),
    Dm: (38, [62, 65, 69], [62, 65, 69]),
    Bb: (46, [62, 65, 70], [62, 65, 70]),
    C:  (36, [60, 64, 67], [60, 64, 67]),
    Gm: (43, [62, 67, 70], [62, 67, 70]),
    Am: (45, [60, 64, 69], [60, 64, 69]),
}

MENU_CHORDS = [F, Dm, Bb, C, F, Dm, Gm, C,
               Bb, C, Am, Dm, Gm, Am, Bb, C,
               F, Dm, Bb, C, F, Dm, Gm, C,
               Dm, Bb, F, C, Dm, Bb, Gm, C]

A_TUNE = [
    [(0, 72, 1), (1, 69, .5), (1.5, 72, .5), (2, 77, 1.5), (3.5, 76, .5)],
    [(0, 74, 1), (1, 72, .5), (1.5, 69, .5), (2, 74, 2)],
    [(0, 70, .5), (.5, 72, .5), (1, 74, 1), (2, 77, 1), (3, 74, 1)],
    [(0, 72, 1.5), (1.5, 70, .5), (2, 67, 1), (3, 72, 1)],
    [(0, 72, 1), (1, 69, .5), (1.5, 72, .5), (2, 77, 1), (3, 79, 1)],
    [(0, 81, 1.5), (1.5, 79, .5), (2, 77, 1), (3, 74, 1)],
    [(0, 74, .5), (.5, 77, .5), (1, 79, 1), (2, 76, 1), (3, 74, .5), (3.5, 76, .5)],
    [(0, 72, 3)],
]
B_TUNE = [
    [(0, 74, .5), (.5, 74, .5), (1, 72, .5), (1.5, 70, .5), (2, 72, 1), (3, 74, 1)],
    [(0, 76, .5), (.5, 76, .5), (1, 74, .5), (1.5, 72, .5), (2, 79, 2)],
    [(0, 76, 1), (1, 72, .5), (1.5, 76, .5), (2, 81, 1.5), (3.5, 79, .5)],
    [(0, 77, 1), (1, 76, .5), (1.5, 74, .5), (2, 74, 2)],
    [(0, 70, .5), (.5, 74, .5), (1, 79, 1), (2, 77, .5), (2.5, 74, .5), (3, 70, 1)],
    [(0, 72, .5), (.5, 76, .5), (1, 81, 1), (2, 79, .5), (2.5, 76, .5), (3, 72, 1)],
    [(0, 74, 1), (1, 77, 1), (2, 82, 1), (3, 81, .5), (3.5, 79, .5)],
    [(0, 79, 1), (1, 76, 1), (2, 72, 1), (3, 67, 1)],
]
C_TUNE = [
    [(0, 74, 2), (2, 77, 2)],
    [(0, 77, 1.5), (1.5, 74, .5), (2, 70, 2)],
    [(0, 72, 2), (2, 77, 2)],
    [(0, 76, 3), (3, 72, 1)],
    [(0, 74, 1), (1, 77, 1), (2, 81, 2)],
    [(0, 82, 1), (1, 81, 1), (2, 77, 2)],
    [(0, 79, 1), (1, 77, 1), (2, 74, 1), (3, 76, 1)],
    [(0, 79, 2), (2, 76, 1), (3, 74, .5), (3.5, 76, .5)],
]


def tune(tr, bar0, bars, vel, bells=False):
    for b, notes in enumerate(bars):
        for beat, note, length in notes:
            t = tr.at(bar0 + b, beat)
            marimba(tr, t, note, vel)
            if length >= 2:                       # a long note: struck again, softer, half way (a marimba cannot hold)
                marimba(tr, t + length * tr.beat / 2, note, vel * 0.55)
            if bells and beat == 0:
                glock(tr, t, note + 12, 0.18)


def menu():
    tr = Track(104, 32)
    for b, ch in enumerate(MENU_CHORDS):
        root, voicing, tones = CH[ch]
        nxt = CH[MENU_CHORDS[(b + 1) % 32]][0]
        section = b // 8                          # 0 A, 1 B, 2 A', 3 C
        pad(tr, tr.at(b, 0), voicing, tr.bar, 0.10 if section < 3 else 0.14)
        # the bass: root, fifth, root, and a step towards the next chord
        bass(tr, tr.at(b, 0), root, tr.beat * 1.4, 0.5)
        bass(tr, tr.at(b, 2), root + 7, tr.beat * 0.9, 0.38)
        bass(tr, tr.at(b, 3), root + 12 if root < 40 else root, tr.beat * 0.45, 0.32)
        bass(tr, tr.at(b, 3.5), nxt + (1 if nxt < root else -1), tr.beat * 0.45, 0.3)
        # offbeat chords on the marimba (lighter in the bridge)
        if section != 3:
            for k, off in enumerate((0.5, 1.5, 2.5, 3.5)):
                for tone in tones[k % 2:k % 2 + 2]:
                    marimba(tr, tr.at(b, off), tone, 0.16)
        # percussion: kick on 1 and 3, woodblock on 2 and 4, shaker on the eighths (the offbeats louder)
        for beat in range(4):
            if section != 3 and beat in (0, 2):
                kick(tr, tr.at(b, beat), 0.55)
            if beat in (1, 3):
                woodblock(tr, tr.at(b, beat), 0.16 if section != 3 else 0.1)
        for e in range(8):
            shaker(tr, tr.at(b, e / 2), 0.09 if e % 2 else 0.05, seed=b * 8 + e)
    tune(tr, 0, A_TUNE, 0.55)
    tune(tr, 8, B_TUNE, 0.5)
    tune(tr, 16, A_TUNE, 0.55, bells=True)
    tune(tr, 24, C_TUNE, 0.5, bells=True)
    return tr


# ---------------- the match: calmer ----------------

GAME_CHORDS = [
    # (root, pad voicing, arpeggio)
    (36, [59, 64, 67], [60, 64, 67, 71]),   # Cmaj7
    (45, [60, 64, 67], [57, 60, 64, 67]),   # Am7
    (41, [60, 64, 69], [53, 57, 60, 64]),   # Fmaj7
    (43, [62, 65, 67], [55, 59, 62, 65]),   # G7
]
GAME_B = [
    (38, [60, 65, 69], [50, 57, 60, 65]),   # Dm7
    (43, [59, 62, 67], [55, 59, 62, 67]),   # G
    (40, [59, 62, 67], [52, 55, 59, 62]),   # Em7
    (45, [60, 64, 67], [57, 60, 64, 67]),   # Am7
    (41, [60, 64, 69], [53, 57, 60, 64]),   # Fmaj7
    (43, [62, 65, 67], [55, 59, 62, 65]),   # G7
    (36, [59, 64, 67], [60, 64, 67, 71]),   # Cmaj7
    (43, [59, 62, 65], [55, 59, 62, 65]),   # G7 (back to the top)
]
GAME_BELLS = {  # bar -> [(beat, note)]
    16: [(0, 76), (2, 79)], 17: [(0, 76), (3, 72)], 18: [(0, 77), (2, 76)], 19: [(0, 74)],
    20: [(0, 76), (2, 79)], 21: [(0, 81), (3, 79)], 22: [(0, 77), (2, 74)], 23: [(0, 74), (2, 71)],
}


def game():
    tr = Track(84, 24)
    seq = GAME_CHORDS * 2 + GAME_B + GAME_CHORDS * 2
    for b, (root, voicing, arp) in enumerate(seq):
        pad(tr, tr.at(b, 0), voicing, tr.bar, 0.13)
        bass(tr, tr.at(b, 0), root, tr.beat * 2.6, 0.42)
        bass(tr, tr.at(b, 3), root + 7, tr.beat * 0.9, 0.22)
        # the arpeggio: up and down the chord in eighths, the first of each beat a little stronger
        order = [0, 1, 2, 3, 2, 1, 2, 3] if b % 2 == 0 else [0, 2, 1, 3, 2, 3, 1, 2]
        for e, k in enumerate(order):
            marimba(tr, tr.at(b, e / 2), arp[k] + 12, 0.2 if e % 2 == 0 else 0.13)
        for e in (1, 3, 5, 7):
            shaker(tr, tr.at(b, e / 2), 0.035, seed=1000 + b * 8 + e)
        if b % 2 == 1:
            woodblock(tr, tr.at(b, 2), 0.06)
        for beat, note in GAME_BELLS.get(b, []):
            glock(tr, tr.at(b, beat), note + 12, 0.22)
    return tr


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else 'Assets/Resources/Music'
    for name, make, peak in (('menu', menu, 0.8), ('game', game, 0.7)):
        tr = make()
        buf = master(reverb_loop(tr.buf, 0.22 if name == 'menu' else 0.3), peak)
        write(os.path.join(out, name + '.wav'), buf)
        print(name, round(len(buf) / RATE, 1), 's')
